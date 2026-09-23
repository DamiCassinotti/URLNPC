#!/usr/bin/env python3
"""Sweep the battery across models x prompt variants x shot counts (issue #133).

    scripts/battery_sweep.py [--models llama3.2:3b,llama3.1:8b] [--prompts v2,v3]
                             [--shots 0,8] [--exemplars bank-v1]
                             [--temps 0.0] [--repeats 1]
                             [--out results/battery/sweep-133] [--baselines]

One cell is one `scripts/battery.py` run, and this is the loop around it: it
runs every combination, writes each cell's full result next to the others and
then renders the grid the selector's configuration is picked from.

**Cells are resumable.** A cell costs the battery times the model's latency —
tens of minutes on CPU — so a finished cell is skipped if its JSON is already
in `--out`, and a sweep interrupted halfway resumes by being re-run. `--force`
re-runs them anyway.

The grid is written as `grid.md` (the table to publish) and `grid.json` (the
same numbers for anything that wants to read them back). `--baselines` adds the
FSM and random rows, which are what the LLM cells are read against and cost
nothing to compute.
"""

import argparse
import json
import os
import random
import sys
import time
import urllib.error

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import battery  # noqa: E402  (path has to be set up first)


def cell_name(model, prompt, shots, exemplars):
    shown = f"{exemplars}x{shots}" if shots else "zero-shot"
    return f"{model.replace(':', '-').replace('/', '-')}_{prompt}_{shown}"


class CellArgs:
    """battery.build_selector reads its configuration off an argparse namespace;
    this is that namespace for one cell."""

    def __init__(self, args, model, prompt, shots):
        self.selector = "llm"
        self.model = model
        self.prompt = prompt
        self.shots = shots
        self.exemplars = "none" if shots == 0 else args.exemplars
        self.endpoint = args.endpoint
        self.decode_seed = args.decode_seed
        self.timeout = args.timeout


def run_cell(args, entries, model, prompt, shots, temps):
    decide, used_shots = battery.build_selector(
        "llm", CellArgs(args, model, prompt, shots), entries)
    rng = random.Random(args.seed)
    results = [battery.run_temp(decide, entries, args.repeats, temp, rng)
               for temp in temps]
    return {
        "selector": "llm",
        "model": model,
        "prompt": prompt,
        "exemplars": args.exemplars if used_shots else "none",
        "shots": used_shots,
        "endpoint": args.endpoint,
        "repeats": args.repeats,
        "snapshots": len(entries),
        "results": results,
    }


def run_baseline(args, entries, name, temps):
    decide, _ = battery.build_selector(name, args, entries)
    rng = random.Random(args.seed)
    return {
        "selector": name,
        "model": "-",
        "prompt": "-",
        "exemplars": "none",
        "shots": 0,
        "repeats": args.repeats,
        "snapshots": len(entries),
        "results": [battery.run_temp(decide, entries, args.repeats, temp, rng)
                    for temp in temps],
    }


def label(cell):
    if cell["selector"] != "llm":
        return cell["selector"]
    shown = f"{cell['exemplars']} x{cell['shots']}" if cell["shots"] else "zero-shot"
    return f"{cell['model']}, {cell['prompt']}, {shown}"


def render_grid(cells, entries):
    ambiguous = sum(1 for e in entries if e.get("ambiguous", False))
    lines = [
        "# Selector sweep (#133)",
        "",
        f"{len(entries)} snapshots — {len(entries) - ambiguous} scored for accuracy, "
        f"{ambiguous} ambiguous.",
        "",
        "| selector | prompt | shots | temp | accuracy | consistency | ambig-acc | "
        "invalid | lat mean | lat p95 |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for cell in cells:
        name = cell["model"] if cell["selector"] == "llm" else cell["selector"]
        shots = str(cell["shots"]) if cell["selector"] == "llm" else "-"
        for r in cell["results"]:
            lines.append(
                f"| {name} | {cell['prompt']} | {shots} | {r['temp']:.1f} "
                f"| {battery.rate(r['accuracy']).strip()} "
                f"| {battery.rate(r['consistency']).strip()} "
                f"| {battery.rate(r['ambiguousAccuracy']).strip()} "
                f"| {battery.rate(r['invalidRate']).strip()} "
                f"| {r['latencyMsMean'] / 1000:.1f} s "
                f"| {r['latencyMsP95'] / 1000:.1f} s |")

    lines += [
        "",
        "## Per-mode, at the lowest temperature",
        "",
        "Accuracy over the scored states each mode is an acceptable answer for, "
        "and that mode's share of the answers given.",
        "",
        "| selector | prompt | shots | " +
        " | ".join(f"{m} acc / chosen" for m in battery.MODES) + " |",
        "|---|---|---|" + "---|" * len(battery.MODES),
    ]
    for cell in cells:
        name = cell["model"] if cell["selector"] == "llm" else cell["selector"]
        shots = str(cell["shots"]) if cell["selector"] == "llm" else "-"
        by_mode = cell["results"][0]["byMode"]
        cols = " | ".join(
            f"{battery.rate(by_mode[m]['accuracy']).strip()} / "
            f"{battery.rate(by_mode[m]['chosenShare']).strip()}"
            for m in battery.MODES)
        lines.append(f"| {name} | {cell['prompt']} | {shots} | {cols} |")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--models", default="llama3.2:3b,llama3.1:8b",
                        help="comma-separated Ollama tags (default a 3B and an 8B)")
    parser.add_argument("--prompts", default="v2,v3",
                        help="comma-separated prompt variants; each must carry the "
                             "{{EXEMPLARS}} slot if any shot count is non-zero")
    parser.add_argument("--shots", default="0,8",
                        help="comma-separated shot counts; 0 is the zero-shot arm")
    parser.add_argument("--exemplars", default="bank-v1",
                        help="bank the non-zero shot counts draw from")
    parser.add_argument("--temps", default="0.0",
                        help="comma-separated temperatures per cell (default 0.0)")
    parser.add_argument("--repeats", type=int, default=1,
                        help="calls per snapshot per temperature; 1 is enough at "
                             "temperature 0, where the decode is greedy")
    parser.add_argument("--baselines", action="store_true",
                        help="also score the FSM and random twins into the grid")
    parser.add_argument("--endpoint", default="http://localhost:11434",
                        help="Ollama base URL")
    parser.add_argument("--decode-seed", type=int, default=1,
                        help="decode seed sent to the model")
    parser.add_argument("--timeout", type=float, default=300.0,
                        help="seconds per call")
    parser.add_argument("--snapshots", default="battery/snapshots.json",
                        help="labeled battery JSON")
    parser.add_argument("--seed", type=int, default=0, help="RNG seed for the run")
    parser.add_argument("--out", default="results/battery/sweep-133",
                        help="directory the cells and the grid are written to")
    parser.add_argument("--force", action="store_true",
                        help="re-run cells that already have a result on disk")
    args = parser.parse_args()

    entries = battery.load_battery(args.snapshots)
    temps = [float(t) for t in args.temps.split(",") if t.strip() != ""]
    models = [m.strip() for m in args.models.split(",") if m.strip()]
    prompts = [p.strip() for p in args.prompts.split(",") if p.strip()]
    shot_counts = [int(s) for s in args.shots.split(",") if s.strip() != ""]
    os.makedirs(args.out, exist_ok=True)

    planned = [(m, p, s) for m in models for p in prompts for s in shot_counts]
    for index, (model, prompt, shots) in enumerate(planned, start=1):
        path = os.path.join(args.out, cell_name(model, prompt, shots, args.exemplars) + ".json")
        if os.path.exists(path) and not args.force:
            print(f"[{index}/{len(planned)}] {os.path.basename(path)} done, skipping",
                  file=sys.stderr)
            continue
        print(f"[{index}/{len(planned)}] {model} {prompt} {shots} shots — "
              f"{len(entries) * args.repeats * len(temps)} calls", file=sys.stderr)
        started = time.perf_counter()
        try:
            cell = run_cell(args, entries, model, prompt, shots, temps)
        except (ValueError, OSError) as error:
            # A bad pairing or a dead endpoint stops the sweep rather than
            # leaving a hole in the grid; the finished cells are already on disk.
            if isinstance(error, urllib.error.URLError):
                print(f"error: {args.endpoint} unreachable: {error}", file=sys.stderr)
            else:
                print(f"error: {error}", file=sys.stderr)
            return 1
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(cell, handle, indent=2)
        print(f"    {(time.perf_counter() - started) / 60:.1f} min, "
              f"accuracy {battery.rate(cell['results'][0]['accuracy']).strip()}",
              file=sys.stderr)

    # The grid is every cell in the directory, not just this invocation's: the
    # sweep is run in several passes (a model at a time, and resumed after an
    # interrupt), and one table over all of them is the thing being published.
    cells = []
    if args.baselines:
        cells = [run_baseline(args, entries, name, temps) for name in ("fsm", "random")]
    for name in sorted(os.listdir(args.out)):
        if not name.endswith(".json") or name == "grid.json":
            continue
        with open(os.path.join(args.out, name), encoding="utf-8") as handle:
            cells.append(json.load(handle))

    grid = render_grid(cells, entries)
    with open(os.path.join(args.out, "grid.md"), "w", encoding="utf-8") as handle:
        handle.write(grid)
    with open(os.path.join(args.out, "grid.json"), "w", encoding="utf-8") as handle:
        json.dump({"snapshots": len(entries), "repeats": args.repeats,
                   "cells": [{"label": label(c), **c} for c in cells]}, handle, indent=2)
    print(grid)
    return 0


if __name__ == "__main__":
    sys.exit(main())
