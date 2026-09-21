# Selector battery

`snapshots.json` — 40 `GameStateSnapshot`s for scoring a mode selector offline, so prompt
iteration doesn't need a live match (see `docs/rl-runbook.md` §9). Run it with
`scripts/battery.py`.

Each entry:

| field | meaning |
|---|---|
| `id` | stable identifier |
| `category` | coverage bucket (never-seen, just-lost-sight, low-HP, clock-low, …) |
| `ambiguous` | more than one defensible answer — scored for consistency, not accuracy |
| `acceptable` | the mode(s) that count as correct (a set, not one answer) |
| `rationale` | why those modes, in one line |
| `snapshot` | the state as `mode_decision` telemetry serializes it |

The snapshots are harvested from real match telemetry, not invented:
`scripts/battery_harvest.py <telemetry.jsonl> --out candidates.json` pulls and dedupes the
`mode_decision` snapshots into coverage categories; the sampled candidates are then labelled
by hand into this file. Regenerate the candidate pool the same way when new telemetry is
worth drawing from.
