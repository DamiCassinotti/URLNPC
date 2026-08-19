#!/usr/bin/env python3
"""Summarize a telemetry JSONL into evaluation numbers (issue #52).

    scripts/eval_summary.py <session.jsonl> [--entity NPC] [--json out.json]

Reads the events TelemetryLogger writes -- episode_summary, death and
mode_compliance -- and reports, for one side of the fight: win rate, damage
dealt and taken, shot accuracy, time to kill, survival time, and the per-mode
compliance, visibility and step counts. scripts/eval.sh calls it; it is also
usable on any session file from a human-played or training run.
"""

import argparse
import json
import statistics
import sys

DRAW = "Draw"
MODES = ("Hunt", "HoldCover", "Retreat", "Patrol")


def read_events(path):
    events = []
    # utf-8-sig: TelemetryLogger's StreamWriter puts a BOM on the first line.
    with open(path, encoding="utf-8-sig") as handle:
        for number, line in enumerate(handle, 1):
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                # A run killed mid-write leaves a partial last line; everything
                # before it is still good data.
                print(f"warning: skipping unparseable line {number}", file=sys.stderr)
    return events


def mean(values):
    return statistics.fmean(values) if values else 0.0


def summarize(events, entity):
    rounds = [e for e in events if e.get("type") == "episode_summary"]
    deaths = [e for e in events if e.get("type") == "death"]
    compliance_events = [
        e for e in events
        if e.get("type") == "mode_compliance" and e.get("entity") == entity
    ]

    wins = sum(1 for r in rounds if r.get("winner") == entity)
    draws = sum(1 for r in rounds if r.get("winner") == DRAW)
    losses = len(rounds) - wins - draws
    decided = [r for r in rounds if r.get("winner") not in (None, DRAW)]

    def side(round_event, tag):
        return round_event.get("sides", {}).get(tag, {})

    dealt = [side(r, entity).get("damageDealt", 0.0) for r in rounds]
    taken = [side(r, entity).get("damageTaken", 0.0) for r in rounds]
    shots = sum(side(r, entity).get("shots", 0) for r in rounds)
    hits = sum(side(r, entity).get("hits", 0) for r in rounds)
    own_deaths = [d.get("survivalSeconds", 0.0) for d in deaths if d.get("entity") == entity]
    durations = [r.get("durationSeconds", 0.0) for r in decided]

    # Pooled over steps rather than averaged over episodes: a 10-step mode in
    # one round shouldn't weigh as much as a 500-step one in another.
    modes = {}
    for mode in MODES:
        steps = eligible = compliant = 0
        visible_eligible = visible_hits = 0
        for event in compliance_events:
            row = event.get("compliance", {}).get(mode)
            if row:
                steps += row.get("steps", 0)
                eligible += row.get("eligible", 0)
                compliant += row.get("compliant", 0)
            seen = event.get("visible", {}).get(mode)
            if seen:
                visible_eligible += seen.get("eligible", 0)
                visible_hits += seen.get("visible_steps", 0)
        if steps == 0:
            continue
        modes[mode] = {
            "steps": steps,
            "eligible": eligible,
            # None, not 0: a mode with no eligible step has no rate, the same
            # distinction the per-episode stat makes (#88).
            "compliance": compliant / eligible if eligible else None,
            "visible": visible_hits / visible_eligible if visible_eligible else None,
        }

    return {
        "entity": entity,
        "episodes": len(rounds),
        "wins": wins,
        "losses": losses,
        "draws": draws,
        "winRate": wins / len(rounds) if rounds else 0.0,
        "damageDealtPerEpisode": mean(dealt),
        "damageTakenPerEpisode": mean(taken),
        "shots": shots,
        "hits": hits,
        "accuracy": hits / shots if shots else 0.0,
        # Decided rounds only: a draw's duration is the round length, not a
        # time to kill, so an all-draw run reports none at all.
        "timeToKillSeconds": mean(durations) if durations else None,
        # None, not 0: a side that never died has no survival time, and a 0
        # would read as the opposite of what happened.
        "survivalSeconds": mean(own_deaths) if own_deaths else None,
        "modes": modes,
    }


def rate(value):
    return "     -" if value is None else f"{value * 100:5.1f}%"


def seconds(value):
    return "-" if value is None else f"{value:.1f} s"


def render(summary):
    lines = [
        f"entity            {summary['entity']}",
        f"episodes          {summary['episodes']}",
        f"win / loss / draw {summary['wins']} / {summary['losses']} / {summary['draws']}"
        f"   (win rate {summary['winRate'] * 100:.1f}%)",
        f"damage dealt      {summary['damageDealtPerEpisode']:.1f} per episode",
        f"damage taken      {summary['damageTakenPerEpisode']:.1f} per episode",
        f"accuracy          {summary['accuracy'] * 100:.1f}%"
        f"   ({summary['hits']}/{summary['shots']} shots)",
        f"time to kill      {seconds(summary['timeToKillSeconds'])} (decided rounds)",
        f"survival          {seconds(summary['survivalSeconds'])} (rounds it died in)",
    ]
    if summary["modes"]:
        lines.append("")
        lines.append(f"{'mode':<10}{'steps':>9}{'eligible':>10}{'compliance':>12}{'visible':>10}")
        for mode, row in summary["modes"].items():
            lines.append(
                f"{mode:<10}{row['steps']:>9}{row['eligible']:>10}"
                f"{rate(row['compliance']):>12}{rate(row['visible']):>10}"
            )
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("telemetry", help="session_*.jsonl written by TelemetryLogger")
    parser.add_argument("--entity", default="NPC", help="tag to summarize (default NPC)")
    parser.add_argument("--json", help="also write the summary as JSON to this path")
    args = parser.parse_args()

    summary = summarize(read_events(args.telemetry), args.entity)
    if summary["episodes"] == 0:
        print(f"error: no episodes in {args.telemetry}", file=sys.stderr)
        return 1
    print(render(summary))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(summary, handle, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
