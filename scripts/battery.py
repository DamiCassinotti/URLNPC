#!/usr/bin/env python3
"""Run a mode selector over the labeled snapshot battery (issue #128).

    scripts/battery.py [--selector fsm|random|llm] [--snapshots battery/snapshots.json]
                       [--prompt v1] [--exemplars none|<bank>] [--shots N]
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

`--exemplars <bank>` fills the prompt's {{EXEMPLARS}} slot from
Assets/Resources/Exemplars/<bank>.txt and `--shots N` caps how many are shown;
the two arms of the few-shot ablation (issue #132) are the same prompt with and
without them. The bank has to be disjoint from the battery on the fields a mode
decision turns on, and this refuses to run otherwise: an exemplar that is also a
battery item is an answer the model was handed.
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


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROMPT_DIR = os.path.join(REPO_ROOT, "Assets", "Resources", "Prompts")
EXEMPLAR_DIR = os.path.join(REPO_ROOT, "Assets", "Resources", "Exemplars")

# The subset of snapshot fields a mode decision turns on — battery_harvest.py
# dedupes on it, and disjointness between bank and battery is defined on it too:
# two states differing only in the arena name are the same decision problem.
KEY_FIELDS = (
    "hpPercent", "targetVisible", "targetDistance", "secondsSinceSeen",
    "recentlyDamaged", "damagedFrom",
)

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
            # ensure_ascii off: JsonLine leaves non-ASCII as-is, and a prompt
            # that differs from the game's is a different prompt.
            rendered = json.dumps(str(value), ensure_ascii=False)
        parts.append(f'"{key}":{rendered}')
    return "{" + ",".join(parts) + "}"


def load_prompt(prompt_id):
    path = os.path.join(PROMPT_DIR, prompt_id + ".txt")
    with open(path, encoding="utf-8") as handle:
        template = handle.read()
    if "{{STATE}}" not in template:
        raise ValueError(f"prompt {prompt_id} has no {{{{STATE}}}} placeholder")
    return template


# Twin of ModeExemplars (Assets/Scripts/ModeExemplars.cs), like fsm_decide is of
# HeuristicModeSelector: same file, same three-line entries, same round-robin
# and the same rendering, so the offline arm sends what the game would.
EXEMPLARS_HEADING = ("Worked examples — states of the kind below and the answer "
                     "each should get:")


def load_exemplars(bank_id):
    path = os.path.join(EXEMPLAR_DIR, bank_id + ".txt")
    with open(path, encoding="utf-8") as handle:
        lines = [line.strip() for line in handle]

    entries = []
    pending = []
    for line in lines:
        if not line or line.startswith("#"):
            continue
        if not line.startswith("{"):
            if pending:
                raise ValueError(f"bank {bank_id}: exemplar {pending[0]!r} is incomplete")
            pending = [line]
            continue
        if not pending:
            raise ValueError(f"bank {bank_id}: a JSON line with no exemplar id above it")
        pending.append(line)
        if len(pending) == 3:
            mode = parse_mode(pending[2])
            if mode is None:
                raise ValueError(f"bank {bank_id}: exemplar {pending[0]!r} names no mode")
            entries.append({"id": pending[0], "state": pending[1],
                            "answer": pending[2], "mode": mode})
            pending = []
    if pending:
        raise ValueError(f"bank {bank_id}: exemplar {pending[0]!r} is incomplete")
    if not entries:
        raise ValueError(f"bank {bank_id} holds no exemplars")
    return entries


def take_exemplars(entries, shots):
    """Round-robin over the modes in NpcModes order, each mode's entries in
    curated order — so four shots is one of each, not four Hunts."""
    by_mode = [[e for e in entries if e["mode"] == mode] for mode in MODES]
    wanted = len(entries) if shots <= 0 else min(shots, len(entries))
    picked = []
    for round_index in range(max((len(row) for row in by_mode), default=0)):
        for row in by_mode:
            if round_index < len(row) and len(picked) < wanted:
                picked.append(row[round_index])
    return picked


def render_exemplars(entries, shots):
    picked = take_exemplars(entries, shots)
    if not picked:
        return ""
    blocks = "".join(f"\n\nState: {e['state']}\nAnswer: {e['answer']}" for e in picked)
    return EXEMPLARS_HEADING + blocks


def fill_exemplars(template, block):
    """ModePrompt.Render's handling of the slot: filled, or dropped along with
    the blank line it sat on so the zero-shot arm is the text without it."""
    if block:
        return template.replace("{{EXEMPLARS}}", block)
    return re.sub(r"\{\{EXEMPLARS\}\}(\r?\n){0,2}", "", template)


def check_disjoint(entries, battery):
    """An exemplar that is also a battery item makes the accuracy number
    meaningless, so an overlap stops the run rather than footnoting it."""
    battery_keys = {tuple(e["snapshot"][f] for f in KEY_FIELDS) for e in battery}
    overlap = [e["id"] for e in entries
               if tuple(json.loads(e["state"])[f] for f in KEY_FIELDS) in battery_keys]
    if overlap:
        raise ValueError("exemplars also in the battery: " + ", ".join(overlap))


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


def make_llm_decide(endpoint, model, prompt_id, seed, timeout, exemplars=""):
    """The in-game side is LlmModeSelector (#130) with prompt v1 (#131); this
    shares the prompt text with it rather than restating it."""
    template = fill_exemplars(load_prompt(prompt_id), exemplars)
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


def build_selector(name, args, battery):
    """Returns the decide function and how many exemplars it shows, the shot
    count being part of what a result is labelled with."""
    if name == "fsm":
        return fsm_decide, 0
    if name == "random":
        return random_decide, 0
    block = ""
    shots = 0
    if args.exemplars and args.exemplars.strip().lower() != "none":
        entries = load_exemplars(args.exemplars)
        check_disjoint(entries, battery)
        if "{{EXEMPLARS}}" not in load_prompt(args.prompt):
            # A few-shot run silently scored as the zero-shot arm it is being
            # compared against would be the one result worth nothing.
            raise ValueError(f"prompt {args.prompt} has no {{{{EXEMPLARS}}}} slot, "
                             f"so bank {args.exemplars} would never be shown")
        picked = take_exemplars(entries, args.shots)
        shots = len(picked)
        block = render_exemplars(entries, args.shots)
    return make_llm_decide(args.endpoint, args.model, args.prompt,
                           args.decode_seed, args.timeout, block), shots


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
    parser.add_argument("--exemplars", default="none",
                        help="few-shot bank under Assets/Resources/Exemplars, "
                             "or 'none' for the zero-shot arm (default none)")
    parser.add_argument("--shots", type=int, default=0,
                        help="how many of the bank's exemplars to show; 0 is all")
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
        decide, shots = build_selector(args.selector, args, entries)
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
        shown = f"{args.exemplars} x{shots}" if shots else "zero-shot"
        label = f"{args.selector} ({args.model}, prompt {args.prompt}, {shown})"
    print(render(label, entries, results))

    if args.json:
        payload = {"selector": args.selector, "repeats": args.repeats,
                   "snapshots": len(entries), "results": results}
        if args.selector == "llm":
            payload.update({"model": args.model, "prompt": args.prompt,
                            "endpoint": args.endpoint,
                            "exemplars": args.exemplars if shots else "none",
                            "shots": shots})
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
