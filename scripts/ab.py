#!/usr/bin/env python3
"""A/B batch harness over mode-selector conditions (issue #136).

    scripts/ab.py <model.onnx> [--conditions conditions.json] [--matches N]
                  [--seed S] [--subject policy|heuristic|random|flee]
                  [--opponent policy|heuristic] [--confidence 0.95]
                  [--out DIR] [--force]

One command runs N real-time matches per selector condition through
scripts/eval.sh and renders the comparison table (§4.3): win/loss/draw,
damage, accuracy, time to kill, survival, per-mode compliance and
visibility, mode distribution and switch rate, decision latency and
fallback rate, plus a pairwise win-rate-difference confidence interval per
condition pair (Newcombe's hybrid Wilson-score method — a plain
percentage difference overstates precision at the match counts this is
run at).

Conditions differ only in who writes the NPC's mode; everything else
(model, subject, opponent) is shared. Default conditions, absent
`--conditions`, are the three arms the thesis compares (§4.4): the shipped
LLM selector (anthropic/Haiku/v4/bank-v1 x8, #156), the FSM and the random
baseline. `--conditions` takes a JSON file of
`[{"name": ..., "args": [eval.sh flags appended to every match]}, ...]`.

Match `i` uses seed `--seed + i - 1` under every condition, so the same
arena and spawn are fought by each selector — the comparison is paired,
not just averaged. The non-LLM conditions are then exactly reproducible
(aim spread is the only unseeded draw, and it is a control the subject
plays, not the mode).

**Resumable per match:** each match is its own `scripts/eval.sh --episodes
1` run under `<out>/<condition>/match-NNNN/`, skipped if that directory
already has a `summary.json` — a crash partway through the campaign loses
nothing already written, and re-running this same command resumes it.
Matches that fail outright are reported and left for the next run to
retry rather than aborting the rest of the campaign.
"""

import argparse
import json
import math
import os
import statistics
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import eval_summary  # noqa: E402  (path has to be set up first)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
EVAL_SH = os.path.join(SCRIPT_DIR, "eval.sh")

# The thesis's own three-arm comparison (implementation-plan.md §4.4), on the
# shipped cloud selector config (#156) — the defaults a caller gets without
# writing a conditions file for the common case.
DEFAULT_CONDITIONS = [
    {"name": "llm", "args": [
        "--selector", "llm", "--llm-backend", "anthropic",
        "--llm-model", "claude-haiku-4-5-20251001", "--llm-prompt", "v4",
        "--llm-exemplars", "bank-v1", "--llm-shots", "8",
    ]},
    {"name": "fsm", "args": ["--selector", "fsm"]},
    {"name": "random", "args": ["--selector", "random"]},
]


# Injected into every match's eval.sh call (model, --episodes, seed, subject,
# opponent, --out) — a condition that also names one would silently overrule
# the pairwise seed matching or the per-match output layout instead of erroring.
RESERVED_FLAGS = {"--episodes", "--seed", "--subject", "--opponent", "--out"}


def load_conditions(path):
    if path is None:
        return DEFAULT_CONDITIONS
    with open(path, encoding="utf-8") as handle:
        conditions = json.load(handle)
    names = []
    for condition in conditions:
        name, condition_args = condition.get("name"), condition.get("args")
        if not isinstance(name, str) or not name:
            raise ValueError(f"{path}: every condition needs a non-empty 'name'")
        if not isinstance(condition_args, list):
            raise ValueError(f"{path}: condition '{name}' needs an 'args' list")
        reserved_hit = RESERVED_FLAGS.intersection(condition_args)
        if reserved_hit:
            raise ValueError(f"{path}: condition '{name}' sets {sorted(reserved_hit)}, "
                              "which this harness already controls")
        names.append(name)
    if len(names) != len(set(names)):
        raise ValueError(f"{path}: duplicate condition name")
    return conditions


def match_dir(out, condition_name, index):
    return os.path.join(out, condition_name, f"match-{index:04d}")


def run_match(model, directory, seed, subject, opponent, condition_args):
    """One eval.sh episode. Returns 'skip', 'ok' or 'fail'."""
    if os.path.exists(os.path.join(directory, "summary.json")):
        return "skip"
    command = [EVAL_SH, model, "--episodes", "1", "--seed", str(seed),
               "--subject", subject, "--opponent", opponent,
               "--out", directory, *condition_args]
    result = subprocess.run(command)
    return "ok" if result.returncode == 0 else "fail"


def run_campaign(args, conditions):
    failures = []
    for match_index in range(1, args.matches + 1):
        seed = args.seed + match_index - 1
        for condition in conditions:
            tag = f"{condition['name']} match {match_index}/{args.matches}"
            directory = match_dir(args.out, condition["name"], match_index)
            status = run_match(args.model, directory, seed, args.subject,
                                args.opponent, condition["args"])
            if status == "skip":
                print(f"[{tag}] already done, skipping", file=sys.stderr)
            elif status == "fail":
                print(f"[{tag}] FAILED — rerun this command to retry it",
                      file=sys.stderr)
                failures.append({"condition": condition["name"], "match": match_index})
    return failures


def load_condition_events(out, condition_name, matches):
    events = []
    completed = 0
    for index in range(1, matches + 1):
        telemetry = os.path.join(match_dir(out, condition_name, index), "telemetry.jsonl")
        if os.path.exists(telemetry):
            events.extend(eval_summary.read_events(telemetry))
            completed += 1
    return events, completed


def summarize_conditions(out, conditions, matches, entity="NPC"):
    summaries, completed = {}, {}
    for condition in conditions:
        events, done = load_condition_events(out, condition["name"], matches)
        completed[condition["name"]] = done
        summaries[condition["name"]] = eval_summary.summarize(events, entity) if events else None
    return summaries, completed


def wilson_interval(wins, n, z):
    """Wilson score interval for one proportion. (point, lower, upper)."""
    if n == 0:
        return 0.0, 0.0, 1.0
    phat = wins / n
    denom = 1 + z * z / n
    center = (phat + z * z / (2 * n)) / denom
    half = (z / denom) * math.sqrt(phat * (1 - phat) / n + z * z / (4 * n * n))
    return phat, max(0.0, center - half), min(1.0, center + half)


def newcombe_diff_interval(wins_a, n_a, wins_b, n_b, z):
    """CI on p_a - p_b for two independent proportions (Newcombe 1998, method 10)."""
    p_a, lower_a, upper_a = wilson_interval(wins_a, n_a, z)
    p_b, lower_b, upper_b = wilson_interval(wins_b, n_b, z)
    diff = p_a - p_b
    lower = diff - math.sqrt((p_a - lower_a) ** 2 + (upper_b - p_b) ** 2)
    upper = diff + math.sqrt((upper_a - p_a) ** 2 + (p_b - lower_b) ** 2)
    return diff, lower, upper


def pairwise_rows(conditions, summaries, confidence):
    z = statistics.NormalDist().inv_cdf((1 + confidence) / 2)
    rows = []
    for i, a in enumerate(conditions):
        for b in conditions[i + 1:]:
            summary_a, summary_b = summaries.get(a["name"]), summaries.get(b["name"])
            if not summary_a or not summary_b:
                print(f"note: no completed matches for {a['name']} or {b['name']} "
                      f"— omitted from the pairwise table", file=sys.stderr)
                continue
            diff, lower, upper = newcombe_diff_interval(
                summary_a["wins"], summary_a["episodes"],
                summary_b["wins"], summary_b["episodes"], z)
            rows.append({
                "a": a["name"], "b": b["name"],
                "winRateA": summary_a["winRate"], "winRateB": summary_b["winRate"],
                "diff": diff, "ciLow": lower, "ciHigh": upper,
            })
    return rows


def render_report(args, conditions, summaries, completed, failures, pairwise):
    lines = [
        f"# A/B batch — {os.path.basename(args.out)}",
        "",
        f"{args.matches} matches/condition, seed {args.seed}, subject {args.subject}, "
        f"opponent {args.opponent}, {int(args.confidence * 100)}% CI on the pairwise rows.",
        "",
    ]
    if failures:
        lines += [f"**{len(failures)} match(es) failed outright — rerun this command "
                  "to retry them.**", ""]
    lines += [
        "| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken "
        "| accuracy | TTK | survival |",
        "|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for condition in conditions:
        name = condition["name"]
        summary = summaries.get(name)
        done = completed.get(name, 0)
        if not summary:
            lines.append(f"| {name} | 0/{args.matches} | - | - | - | - | - | - | - | - | - |")
            continue
        lines.append(
            f"| {name} | {done}/{args.matches} | {summary['wins']} | {summary['losses']} "
            f"| {summary['draws']} | {summary['winRate'] * 100:.1f}% "
            f"| {summary['damageDealtPerEpisode']:.1f} "
            f"| {summary['damageTakenPerEpisode']:.1f} "
            f"| {summary['accuracy'] * 100:.1f}% "
            f"| {eval_summary.seconds(summary['timeToKillSeconds'])} "
            f"| {eval_summary.seconds(summary['survivalSeconds'])} |")

    lines += [
        "",
        "## Per-mode compliance / visible",
        "",
        "| condition | " + " | ".join(eval_summary.MODES) + " |",
        "|---|" + "---|" * len(eval_summary.MODES),
    ]
    for condition in conditions:
        summary = summaries.get(condition["name"])
        if not summary:
            continue
        cells = []
        for mode in eval_summary.MODES:
            row = summary["modes"].get(mode)
            cells.append("-" if row is None else
                         f"{eval_summary.rate(row['compliance']).strip()} / "
                         f"{eval_summary.rate(row['visible']).strip()}")
        lines.append(f"| {condition['name']} | " + " | ".join(cells) + " |")

    selector_rows = [(c["name"], summaries[c["name"]]["selector"]) for c in conditions
                      if summaries.get(c["name"]) and summaries[c["name"]]["selector"]]
    if selector_rows:
        lines += [
            "",
            "## Selector",
            "",
            "| condition | selector | decisions/ep | switch rate | latency mean "
            "| latency p95 | invalid | fallback |",
            "|---|---|---|---|---|---|---|---|",
        ]
        for name, selector in selector_rows:
            label = selector["selector"]
            if selector["model"]:
                label += f" ({selector['model']})"
            lines.append(
                f"| {name} | {label} | {selector['decisionsPerEpisode']:.1f} "
                f"| {eval_summary.rate(selector['switchRate']).strip()} "
                f"| {selector['latencyMsMean']:.0f} ms | {selector['latencyMsP95']} ms "
                f"| {eval_summary.rate(selector['invalidRate']).strip()} "
                f"| {eval_summary.rate(selector['fallbackRate']).strip()} |")

    if pairwise:
        lines += [
            "",
            f"## Pairwise win-rate difference ({int(args.confidence * 100)}% CI)",
            "",
            "| A | B | win rate A | win rate B | A − B | CI |",
            "|---|---|---|---|---|---|",
        ]
        for row in pairwise:
            lines.append(
                f"| {row['a']} | {row['b']} | {row['winRateA'] * 100:.1f}% "
                f"| {row['winRateB'] * 100:.1f}% | {row['diff'] * 100:+.1f} pp "
                f"| [{row['ciLow'] * 100:+.1f}, {row['ciHigh'] * 100:+.1f}] pp |")

    return "\n".join(lines) + "\n"


def commit_hash():
    result = subprocess.run(["git", "-C", PROJECT_ROOT, "rev-parse", "--short", "HEAD"],
                             capture_output=True, text=True, check=False)
    return result.stdout.strip() if result.returncode == 0 else "unknown"


def main():
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("model", help="path to the .onnx model, as scripts/eval.sh takes it")
    parser.add_argument("--conditions", default=None,
                         help="JSON file of [{'name':.., 'args':[eval.sh flags]}, ...]; "
                              "defaults to the shipped llm/fsm/random comparison")
    parser.add_argument("--matches", type=int, default=100, help="matches per condition")
    parser.add_argument("--seed", type=int, default=1001,
                         help="base seed; match i uses seed + i - 1 under every "
                              "condition, so arenas and spawns match pairwise")
    parser.add_argument("--subject", default="policy",
                         choices=["policy", "heuristic", "random", "flee"])
    parser.add_argument("--opponent", default="policy", choices=["policy", "heuristic"])
    parser.add_argument("--confidence", type=float, default=0.95,
                         help="confidence level for the pairwise win-rate CI")
    parser.add_argument("--out", default=None, help="campaign directory")
    parser.add_argument("--force", action="store_true",
                         help="re-run matches that already have a summary.json")
    args = parser.parse_args()

    args.model = os.path.abspath(args.model)
    if not os.path.isfile(args.model):
        print(f"error: model '{args.model}' not found", file=sys.stderr)
        return 1
    if args.matches <= 0:
        print("error: --matches takes a positive integer", file=sys.stderr)
        return 1
    if not 0 < args.confidence < 1:
        print("error: --confidence takes a value between 0 and 1 (e.g. 0.95)",
              file=sys.stderr)
        return 1
    try:
        conditions = load_conditions(args.conditions)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    stem = os.path.splitext(os.path.basename(args.model))[0]
    args.out = os.path.abspath(args.out) if args.out else os.path.join(
        PROJECT_ROOT, "results", "ab",
        f"{stem}_{args.subject}-vs-{args.opponent}_{time.strftime('%Y%m%d_%H%M%S')}")
    os.makedirs(args.out, exist_ok=True)

    if args.force:
        for condition in conditions:
            for match_index in range(1, args.matches + 1):
                summary_path = os.path.join(
                    match_dir(args.out, condition["name"], match_index), "summary.json")
                if os.path.exists(summary_path):
                    os.remove(summary_path)

    with open(os.path.join(args.out, "config.json"), "w", encoding="utf-8") as handle:
        json.dump({
            "model": args.model, "matches": args.matches, "seed": args.seed,
            "subject": args.subject, "opponent": args.opponent,
            "confidence": args.confidence, "conditions": conditions,
            "commit": commit_hash(),
            "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }, handle, indent=2)

    failures = run_campaign(args, conditions)
    summaries, completed = summarize_conditions(args.out, conditions, args.matches)
    pairwise = pairwise_rows(conditions, summaries, args.confidence)

    report = render_report(args, conditions, summaries, completed, failures, pairwise)
    with open(os.path.join(args.out, "report.md"), "w", encoding="utf-8") as handle:
        handle.write(report)
    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as handle:
        json.dump({"conditions": summaries, "pairwise": pairwise, "failures": failures},
                   handle, indent=2)
    print(report)
    print(f"==> results in {args.out}", file=sys.stderr)

    if failures:
        print(f"error: {len(failures)} match(es) failed outright — rerun this command "
              "to retry them", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
