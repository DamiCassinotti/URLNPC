# A/B batch — masked-02_v5-vs-v6_n100

100 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| v5 | 100/100 | 55 | 40 | 5 | 55.0% | 70.8 | 81.9 | 84.7% | 14.4 s | 17.8 s |
| v6 | 100/100 | 53 | 42 | 5 | 53.0% | 70.0 | 82.0 | 84.0% | 13.6 s | 15.8 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| v5 | 91.9% / 30.8% | 9.1% / 23.0% | 11.2% / 5.2% | 37.2% / 7.5% |
| v6 | 93.7% / 25.2% | 25.1% / 19.4% | 34.1% / 5.8% | 38.8% / 7.5% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| v5 | llm (claude-haiku-4-5-20251001) | 7.1 | 36.6% | 835 ms | 1059 ms | 0.0% | 0.0% |
| v6 | llm (claude-haiku-4-5-20251001) | 6.8 | 45.2% | 857 ms | 1079 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| v5 | v6 | 55.0% | 53.0% | +2.0 pp | [-11.6, +15.5] pp |
