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
sends the same prompt asset the game sends (Assets/Resources/Prompts/<id>.txt,
rendered here with an empty history — a battery snapshot is a single decision)
to Ollama. One attempt per call, no retry: the retry ladder is the game's
robustness, while what the battery measures is how often the raw answer is
usable.
"""

import argparse
import json
import math
import os
import random
import re
import statistics
import sys
import time
import urllib.error
import urllib.request

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


PROMPT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "Resources", "Prompts")

# ModeDecisionRecord.SnapshotObject's field order, which is what the game sends
# and therefore what the prompt has to carry here too.
SNAPSHOT_FIELDS = (
    "hpPercent", "targetVisible", "targetDistance", "secondsSinceSeen",
    "recentlyDamaged", "damagedFrom", "roundSecondsRemaining",
    "playerWins", "npcWins", "draws", "arenaIndex", "arenaName",
    "coverDensity", "observedSeconds", "meanEngagementDistanceMetres",
    "shotsHeardPer10Seconds", "observedMeanSpeed", "mode", "secondsInMode",
)

# LlmModeResponse.Schema()
SCHEMA = {
    "type": "object",
    "properties": {
        "mode": {"type": "string", "enum": list(MODES)},
        "reason": {"type": "string"},
    },
    "required": ["mode", "reason"],
}


def snapshot_json(s):
    """JsonLine's formatting, field for field: floats to three decimals with the
    trailing zeros dropped, bools lower-case. A prompt that differs from the
    game's by whitespace is a different prompt."""
    parts = []
    for key in SNAPSHOT_FIELDS:
        value = s[key]
        if isinstance(value, bool):
            rendered = "true" if value else "false"
        elif isinstance(value, float):
            rendered = f"{value:.3f}".rstrip("0").rstrip(".") or "0"
        elif isinstance(value, int):
            rendered = str(value)
        else:
            rendered = json.dumps(str(value))
        parts.append(f'"{key}":{rendered}')
    return "{" + ",".join(parts) + "}"


def load_prompt(prompt_id):
    path = os.path.join(PROMPT_DIR, prompt_id + ".txt")
    with open(path, encoding="utf-8") as handle:
        template = handle.read()
    if "{{STATE}}" not in template:
        raise ValueError(f"prompt {prompt_id} has no {{{{STATE}}}} placeholder")
    return template


def parse_mode(text):
    """The readable part of LlmModeResponse.TryParse: the "mode" key when there
    is one, otherwise a whole-word scan that answers only when exactly one mode
    is named and nothing is negated."""
    match = re.search(r'"mode"\s*:\s*"([^"]*)"', text)
    if match:
        named = match.group(1).strip().lower()
        return next((m for m in MODES if m.lower() == named), None)
    if re.search(r"\bnot\b|n't|\bnever\b|\bavoid\b|\binstead\b|rather than",
                 text, re.IGNORECASE):
        return None
    found = [m for m in MODES if re.search(rf"\b{m}\b", text, re.IGNORECASE)]
    return found[0] if len(found) == 1 else None


def make_llm_decide(endpoint, model, prompt_id, seed, timeout):
    """The in-game side is LlmModeSelector (#130) with prompt v1 (#131); this
    shares the prompt text with it rather than restating it."""
    template = load_prompt(prompt_id)
    url = endpoint.rstrip("/") + "/api/generate"

    def decide(s, rng, temp):
        prompt = template.replace("{{STATE}}", snapshot_json(s))
        # A battery snapshot stands alone, so there is no history to show.
        prompt = prompt.replace(
            "{{HISTORY}}", "(none — this is the first decision of the round)")
        body = json.dumps({
            "model": model,
            "prompt": prompt,
            "stream": False,
            "format": SCHEMA,
            "keep_alive": "30m",
            "options": {"temperature": temp, "seed": seed},
        }).encode("utf-8")
        request = urllib.request.Request(
            url, data=body, headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=timeout) as response:
            payload = json.load(response)
        return parse_mode(payload.get("response", "") or "")

    return decide


SELECTORS = ("fsm", "random", "llm")


def build_selector(name, args):
    if name == "fsm":
        return fsm_decide
    if name == "random":
        return random_decide
    return make_llm_decide(args.endpoint, args.model, args.prompt,
                           args.decode_seed, args.timeout)


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
    parser.add_argument("--prompt", default="v1",
                        help="prompt variant under Assets/Resources/Prompts (default v1)")
    parser.add_argument("--model", default="llama3.1:8b", help="Ollama model tag")
    parser.add_argument("--endpoint", default="http://localhost:11434",
                        help="Ollama base URL")
    parser.add_argument("--decode-seed", type=int, default=1,
                        help="decode seed sent to the model (LlmSelectorConfig.Seed)")
    parser.add_argument("--timeout", type=float, default=120.0,
                        help="seconds per call; generous on purpose — this is the "
                             "offline loop, and the first call pays a cold model load")
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
    try:
        decide = build_selector(args.selector, args)
    except (OSError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    temps = [float(t) for t in args.temps.split(",") if t.strip() != ""]
    rng = random.Random(args.seed)

    try:
        results = [run_temp(decide, entries, args.repeats, temp, rng) for temp in temps]
    except (urllib.error.URLError, OSError) as error:
        print(f"error: {args.endpoint} unreachable: {error}", file=sys.stderr)
        return 1
    label = args.selector
    if args.selector == "llm":
        label = f"{args.selector} ({args.model}, prompt {args.prompt})"
    print(render(label, entries, results))

    if args.json:
        payload = {"selector": args.selector, "repeats": args.repeats,
                   "snapshots": len(entries), "results": results}
        if args.selector == "llm":
            payload.update({"model": args.model, "prompt": args.prompt,
                            "endpoint": args.endpoint})
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
