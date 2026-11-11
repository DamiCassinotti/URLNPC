# A/B batch — URLNPC_policy-vs-heuristic_seed1001_n100_20781736

100 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm | 100/100 | 53 | 43 | 4 | 53.0% | 71.1 | 84.5 | 83.2% | 14.5 s | 19.0 s |
| fsm | 100/100 | 52 | 42 | 6 | 52.0% | 71.4 | 83.0 | 83.1% | 14.1 s | 16.9 s |
| random | 100/100 | 47 | 48 | 5 | 47.0% | 68.4 | 82.5 | 84.9% | 12.7 s | 14.1 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm | 95.7% / 23.8% | 8.7% / 30.3% | 23.2% / 2.4% | 40.4% / 8.4% |
| fsm | 95.6% / 49.4% | 10.5% / 5.8% | 20.7% / 13.8% | 31.7% / 4.0% |
| random | 92.4% / 13.0% | 35.2% / 10.3% | 40.8% / 13.5% | 33.9% / 11.1% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm | llm (claude-haiku-4-5-20251001) | 6.7 | 44.0% | 955 ms | 1294 ms | 0.0% | 0.0% |
| fsm | fsm | 7.6 | 22.6% | 2 ms | 12 ms | 0.0% | 0.0% |
| random | random | 6.7 | 74.8% | 2 ms | 12 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm | fsm | 53.0% | 52.0% | +1.0 pp | [-12.6, +14.6] pp |
| llm | random | 53.0% | 47.0% | +6.0 pp | [-7.7, +19.4] pp |
| fsm | random | 52.0% | 47.0% | +5.0 pp | [-8.7, +18.5] pp |
