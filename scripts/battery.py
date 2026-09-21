#!/usr/bin/env python3
"""Run a mode selector over the labeled snapshot battery (issue #128).

    scripts/battery.py [--selector fsm|random|llm] [--snapshots battery/snapshots.json]
                       [--repeats K] [--temps 0.0,0.7] [--seed S] [--json out.json]

The offline inner loop for prompt iteration: a real-time match costs a minute
and can't be time-scaled with an LLM in the loop, so the selector is scored
against a fixed set of `GameStateSnapshot`s instead (battery/snapshots.json,
harvested from match telemetry and labelled by hand with the acceptable mode(s)
per state). Reports, per temperature:

  accuracy         fraction of non-ambiguous snapshots answered with an
                   acceptable mode (the ambiguous subset is excluded — it has
                   more than one defensible answer, so it measures consistency)
  consistency      mean over snapshots of the modal answer's share of K repeats
  invalid          fraction of calls that named no mode
  latency          mean / p50 / p95 / max in ms

The FSM and random baselines run as Python twins of the C# selectors (see the
runbook for why a twin rather than driving the Unity build); the `llm` selector
is the seam #130 plugs its Ollama call into.
"""

import argparse
import json
import math
import random
import statistics
import sys
import time

MODES = ("Hunt", "HoldCover", "Retreat", "Patrol")

# HeuristicModeSelector defaults (Assets/Scripts/HeuristicModeSelector.cs).
FSM_LOW_HEALTH_PERCENT = 35
FSM_UNSEEN_SECONDS_FOR_PATROL = 6


def fsm_decide(s, rng, temp):
    """Python twin of HeuristicModeSelector.Decide — priority-ordered rules."""
    if s["hpPercent"] <= FSM_LOW_HEALTH_PERCENT:
        return "Retreat"
    if s["targetVisible"]:
        return "Hunt"
    if s["recentlyDamaged"]:
        return "HoldCover"
    if s["targetDistance"] == "None" or s["secondsSinceSeen"] >= FSM_UNSEEN_SECONDS_FOR_PATROL:
        return "Patrol"
    return "Hunt"


def random_decide(s, rng, temp):
    """Twin of RandomModeSelector: uniform draw over the four modes."""
    return rng.choice(MODES)


def llm_decide(s, rng, temp):
    """The LLM selector plugs in here (issue #130): build the prompt from the
    snapshot, call Ollama at the given temperature, parse a mode from the reply.
    Not wired until the prompt lands."""
    raise NotImplementedError(
        "llm selector arrives with the Ollama prompt (#130); "
        "run --selector fsm or random for now")


SELECTORS = {
    "fsm": fsm_decide,
    "random": random_decide,
    "llm": llm_decide,
}


def load_battery(path):
    with open(path, encoding="utf-8") as handle:
        entries = json.load(handle)
    for entry in entries:
        acceptable = entry.get("acceptable") or []
        if not entry.get("ambiguous") and not acceptable:
            raise ValueError(f"snapshot {entry.get('id')} has no acceptable modes")
        for mode in acceptable:
            if mode not in MODES:
                raise ValueError(f"snapshot {entry.get('id')}: unknown mode {mode!r}")
    return entries


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    rank = max(1, math.ceil(fraction * len(ordered)))
    return ordered[rank - 1]


def run_temp(decide, entries, repeats, temp, rng):
    """One pass at a fixed temperature: K repeats per snapshot."""
    latencies = []
    invalid = 0
    calls = 0
    acc_hits = acc_total = 0          # non-ambiguous only
    consistencies = []               # per snapshot, over all snapshots
    ambig_consistencies = []
    per_id = []

    for entry in entries:
        snapshot = entry["snapshot"]
        acceptable = set(entry.get("acceptable") or [])
        answers = []
        for _ in range(repeats):
            start = time.perf_counter()
            answer = decide(snapshot, rng, temp)
            latencies.append((time.perf_counter() - start) * 1000.0)
            calls += 1
            if answer not in MODES:
                invalid += 1
                answer = None
            answers.append(answer)

        valid = [a for a in answers if a is not None]
        modal = statistics.mode(valid) if valid else None
        consistency = valid.count(modal) / len(answers) if valid else 0.0
        (ambig_consistencies if entry.get("ambiguous", False) else consistencies).append(consistency)

        if not entry.get("ambiguous", False):
            acc_hits += sum(1 for a in answers if a in acceptable)
            acc_total += len(answers)

        per_id.append({
            "id": entry["id"],
            "category": entry["category"],
            "ambiguous": entry.get("ambiguous", False),
            "acceptable": sorted(acceptable),
            "modal": modal,
            "consistency": consistency,
            "accuracy": (None if entry.get("ambiguous", False)
                         else sum(1 for a in answers if a in acceptable) / len(answers)),
        })

    return {
        "temp": temp,
        "accuracy": acc_hits / acc_total if acc_total else None,
        "consistency": statistics.fmean(consistencies) if consistencies else None,
        "ambiguousConsistency": (statistics.fmean(ambig_consistencies)
                                 if ambig_consistencies else None),
        "invalidRate": invalid / calls if calls else 0.0,
        "latencyMsMean": statistics.fmean(latencies) if latencies else 0.0,
        "latencyMsP50": percentile(latencies, 0.5),
        "latencyMsP95": percentile(latencies, 0.95),
        "latencyMsMax": max(latencies) if latencies else None,
        "perSnapshot": per_id,
    }


def rate(value):
    return "   -" if value is None else f"{value * 100:.1f}%"


def render(selector, entries, results):
    ambiguous = sum(1 for e in entries if e.get("ambiguous", False))
    lines = [
        f"selector      {selector}",
        f"snapshots     {len(entries)}  ({ambiguous} ambiguous, "
        f"{len(entries) - ambiguous} scored for accuracy)",
        "",
        f"{'temp':>5}{'accuracy':>11}{'consistency':>13}{'ambig-consist':>15}"
        f"{'invalid':>9}{'lat.mean':>10}{'lat.p95':>9}",
    ]
    for r in results:
        lines.append(
            f"{r['temp']:>5.1f}{rate(r['accuracy']):>11}{rate(r['consistency']):>13}"
            f"{rate(r['ambiguousConsistency']):>15}{rate(r['invalidRate']):>9}"
            f"{r['latencyMsMean']:>9.1f}m{r['latencyMsP95']:>8.1f}"
        )
    # Misses at the first temperature, so a wrong-looking selector is debuggable.
    misses = [p for p in results[0]["perSnapshot"]
              if p["accuracy"] is not None and p["accuracy"] < 1.0]
    if misses:
        lines.append("")
        lines.append(f"misses at temp {results[0]['temp']:.1f}:")
        for p in misses:
            lines.append(f"  {p['id']:<32} chose {p['modal']}, "
                         f"acceptable {p['acceptable']}")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--selector", default="fsm", choices=sorted(SELECTORS),
                        help="which selector to score (default fsm)")
    parser.add_argument("--snapshots", default="battery/snapshots.json",
                        help="labeled battery JSON")
    parser.add_argument("--repeats", type=int, default=5,
                        help="calls per snapshot per temperature (default 5)")
    parser.add_argument("--temps", default="0.0,0.7",
                        help="comma-separated temperatures (default 0.0,0.7)")
    parser.add_argument("--seed", type=int, default=0, help="RNG seed for the run")
    parser.add_argument("--json", help="also write full results here")
    args = parser.parse_args()

    entries = load_battery(args.snapshots)
    decide = SELECTORS[args.selector]
    temps = [float(t) for t in args.temps.split(",") if t.strip() != ""]
    rng = random.Random(args.seed)

    try:
        results = [run_temp(decide, entries, args.repeats, temp, rng) for temp in temps]
    except NotImplementedError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(render(args.selector, entries, results))

    if args.json:
        payload = {"selector": args.selector, "repeats": args.repeats,
                   "snapshots": len(entries), "results": results}
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
