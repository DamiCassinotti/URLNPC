# A/B batch — URLNPC_policy-vs-heuristic_seed1001_n100_a0b7fb79

100 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm | 100/100 | 57 | 37 | 6 | 57.0% | 74.0 | 80.2 | 84.1% | 13.7 s | 14.9 s |
| fsm | 100/100 | 55 | 40 | 5 | 55.0% | 71.2 | 82.7 | 82.5% | 14.6 s | 19.6 s |
| random | 100/100 | 43 | 52 | 5 | 43.0% | 69.6 | 84.6 | 81.7% | 13.3 s | 13.3 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm | 95.0% / 41.3% | 34.9% / 14.0% | 33.9% / 7.8% | 37.0% / 6.5% |
| fsm | 95.6% / 42.1% | 0.0% / 6.9% | 22.8% / 17.0% | 32.5% / 3.8% |
| random | 96.2% / 11.4% | 39.8% / 11.2% | 54.9% / 11.9% | 35.7% / 13.0% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm | llm (claude-haiku-4-5-20251001) | 5.2 | 41.3% | 769 ms | 920 ms | 0.0% | 0.0% |
| fsm | fsm | 5.3 | 31.2% | 2 ms | 12 ms | 0.0% | 0.0% |
| random | random | 5.0 | 75.1% | 2 ms | 12 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm | fsm | 57.0% | 55.0% | +2.0 pp | [-11.6, +15.5] pp |
| llm | random | 57.0% | 43.0% | +14.0 pp | [+0.2, +27.1] pp |
| fsm | random | 55.0% | 43.0% | +12.0 pp | [-1.8, +25.2] pp |
