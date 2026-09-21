#!/usr/bin/env python3
"""Harvest candidate GameStateSnapshots from match telemetry (issue #128).

    scripts/battery_harvest.py <telemetry.jsonl...> [--out candidates.json]
                               [--per-category N] [--seed S]

Pulls the `snapshot` object off every `mode_decision` line, dedupes on the
fields a mode decision actually turns on, sorts each snapshot into a coverage
category (never-seen, just-lost-sight, low-hp with/without contact, clock
running out, and one per visible band) and samples up to N per category. The
point is that the battery set is drawn from the distribution the selector meets
in play rather than invented; the sampled candidates are then labelled by hand
into battery/snapshots.json (acceptable modes + rationale), which is the file
the runner scores against.

The emitted candidates carry an empty `acceptable`/`rationale` for the labeller
to fill; `category` is a hint, not a label.
"""

import argparse
import json
import random
import sys

# The subset of snapshot fields a mode decision turns on. Two lines with the
# same tuple are the same decision problem however much the score or arena
# differ, so we keep one.
KEY_FIELDS = (
    "hpPercent",
    "targetVisible",
    "targetDistance",
    "secondsSinceSeen",
    "recentlyDamaged",
    "damagedFrom",
)

LOW_HP = 35        # HeuristicModeSelector.LowHealthPercent
CLOCK_LOW = 20     # "about to run out" — a few decision periods left


def category(s):
    hp = s["hpPercent"]
    visible = s["targetVisible"]
    known = s["targetDistance"] != "None"
    contact = visible or known or s["recentlyDamaged"]

    if hp <= LOW_HP:
        return "lowhp-contact" if contact else "lowhp-nocontact"
    if s["roundSecondsRemaining"] <= CLOCK_LOW:
        return "clock-low"
    if not known:
        return "never-seen"
    if not visible:
        return "just-lost-sight"
    return f"visible-{s['targetDistance'].lower()}"


def key(s):
    return tuple(s[f] for f in KEY_FIELDS)


def harvest(paths):
    by_category = {}
    seen = set()
    for path in paths:
        with open(path, encoding="utf-8-sig") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    event = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if event.get("type") != "mode_decision":
                    continue
                snapshot = event.get("snapshot")
                if not snapshot:
                    continue
                k = key(snapshot)
                if k in seen:
                    continue
                seen.add(k)
                by_category.setdefault(category(snapshot), []).append(snapshot)
    return by_category


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("telemetry", nargs="+", help="session_*.jsonl file(s)")
    parser.add_argument("--out", help="write candidates JSON here (default stdout)")
    parser.add_argument("--per-category", type=int, default=6,
                        help="max candidates sampled per category (default 6)")
    parser.add_argument("--seed", type=int, default=0, help="sampling seed")
    args = parser.parse_args()

    by_category = harvest(args.telemetry)
    rng = random.Random(args.seed)
    candidates = []
    for cat in sorted(by_category):
        pool = by_category[cat]
        rng.shuffle(pool)
        for snapshot in pool[: args.per_category]:
            candidates.append({
                "category": cat,
                "ambiguous": False,
                "acceptable": [],
                "rationale": "",
                "snapshot": snapshot,
            })

    counts = {c: len(v) for c, v in sorted(by_category.items())}
    print(f"harvested {len(candidates)} candidates across "
          f"{len(by_category)} categories: {counts}", file=sys.stderr)

    text = json.dumps(candidates, indent=2)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(text + "\n")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
