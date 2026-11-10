# A/B batch — masked-02_vs-heuristic_n100_period3s

100 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm | 100/100 | 53 | 42 | 5 | 53.0% | 70.8 | 83.4 | 84.4% | 13.7 s | 14.6 s |
| fsm | 100/100 | 53 | 41 | 6 | 53.0% | 71.5 | 82.8 | 82.2% | 14.3 s | 17.6 s |
| random | 100/100 | 49 | 46 | 5 | 49.0% | 68.7 | 81.2 | 84.3% | 12.7 s | 13.9 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm | 96.1% / 37.9% | 35.3% / 16.7% | 11.1% / 7.6% | 37.9% / 7.6% |
| fsm | 95.1% / 47.5% | 9.5% / 5.7% | 26.4% / 14.0% | 31.8% / 4.0% |
| random | 92.4% / 13.8% | 35.4% / 10.5% | 41.9% / 13.2% | 33.8% / 10.9% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm | llm (claude-haiku-4-5-20251001) | 6.8 | 34.4% | 826 ms | 1054 ms | 0.0% | 0.0% |
| fsm | fsm | 7.7 | 23.5% | 2 ms | 11 ms | 0.0% | 0.0% |
| random | random | 6.7 | 74.9% | 2 ms | 11 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm | fsm | 53.0% | 53.0% | +0.0 pp | [-13.6, +13.6] pp |
| llm | random | 53.0% | 49.0% | +4.0 pp | [-9.7, +17.5] pp |
| fsm | random | 53.0% | 49.0% | +4.0 pp | [-9.7, +17.5] pp |
