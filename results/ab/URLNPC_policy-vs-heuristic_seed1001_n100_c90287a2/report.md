# A/B batch — URLNPC_policy-vs-heuristic_seed1001_n100_c90287a2

100 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm | 100/100 | 50 | 44 | 6 | 50.0% | 71.4 | 83.0 | 85.2% | 12.3 s | 14.1 s |
| fsm | 100/100 | 54 | 41 | 5 | 54.0% | 71.2 | 82.3 | 82.3% | 14.6 s | 19.6 s |
| random | 100/100 | 45 | 50 | 5 | 45.0% | 70.0 | 83.5 | 82.5% | 13.0 s | 12.6 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm | 94.5% / 23.1% | 14.8% / 16.5% | 22.5% / 5.4% | 38.9% / 7.6% |
| fsm | 95.4% / 43.1% | 0.0% / 8.2% | 21.4% / 14.6% | 33.1% / 4.0% |
| random | 96.7% / 11.2% | 39.6% / 11.1% | 55.4% / 11.8% | 35.7% / 13.4% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm | llm (claude-haiku-4-5-20251001) | 4.7 | 51.4% | 956 ms | 1140 ms | 0.0% | 0.0% |
| fsm | fsm | 5.3 | 30.8% | 2 ms | 11 ms | 0.0% | 0.0% |
| random | random | 5.0 | 74.8% | 2 ms | 12 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm | fsm | 50.0% | 54.0% | -4.0 pp | [-17.5, +9.7] pp |
| llm | random | 50.0% | 45.0% | +5.0 pp | [-8.7, +18.4] pp |
| fsm | random | 54.0% | 45.0% | +9.0 pp | [-4.8, +22.3] pp |
