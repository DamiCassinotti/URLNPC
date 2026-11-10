#!/usr/bin/env python3
"""Per-condition stall-streak summary over an ab campaign's telemetry.

A stall-streak is a run of consecutive mode_decision events that named the
same mode — the shape that showed up in #137's disagreement analysis as "15
Patrol calls over 65 s". The longest streak per episode is what the stall
discussion turns on; everything else (mean streak length, decisions/ep) is
there to contextualise that tail.

    scripts/stall_streaks.py <campaign-dir> [<campaign-dir> ...]

Prints one row per condition. Each row's `max-streak` columns are in
*decision count*; multiply by the campaign's decision period to get
wall-clock seconds.
"""
import json
import os
import statistics
import sys


def streaks(telemetry_path):
    modes = []
    with open(telemetry_path, encoding="utf-8-sig") as handle:
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
            mode = event.get("chosen") or event.get("mode")
            if mode is not None:
                modes.append(mode)
    if not modes:
        return []
    runs = []
    current = 1
    for i in range(1, len(modes)):
        if modes[i] == modes[i - 1]:
            current += 1
        else:
            runs.append(current)
            current = 1
    runs.append(current)
    return runs


def summarise(campaign_dir):
    print(f"\n=== {os.path.basename(campaign_dir)} ===")
    for condition in sorted(os.listdir(campaign_dir)):
        condition_dir = os.path.join(campaign_dir, condition)
        if not os.path.isdir(condition_dir):
            continue
        max_per_ep = []
        decisions_per_ep = []
        ge4 = ge6 = total = 0
        for match in sorted(os.listdir(condition_dir)):
            path = os.path.join(condition_dir, match, "telemetry.jsonl")
            if not os.path.exists(path):
                continue
            runs = streaks(path)
            if not runs:
                continue
            max_per_ep.append(max(runs))
            decisions_per_ep.append(sum(runs))
            total += len(runs)
            ge4 += sum(1 for run in runs if run >= 4)
            ge6 += sum(1 for run in runs if run >= 6)
        if not max_per_ep:
            continue
        max_per_ep.sort()
        p95_index = max(0, int(0.95 * len(max_per_ep)) - 1)
        print(
            f"  {condition}: n={len(max_per_ep)} ep | "
            f"max-streak mean {statistics.mean(max_per_ep):.2f} "
            f"p50 {statistics.median(max_per_ep):.0f} "
            f"p95 {max_per_ep[p95_index]} "
            f"max {max_per_ep[-1]} | "
            f"decisions/ep mean {statistics.mean(decisions_per_ep):.1f} | "
            f"streaks>=4: {ge4}/{total} ({100 * ge4 / total:.1f}%) | "
            f">=6: {ge6}/{total} ({100 * ge6 / total:.1f}%)"
        )


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__, file=sys.stderr)
        sys.exit(2)
    for directory in sys.argv[1:]:
        summarise(directory)
