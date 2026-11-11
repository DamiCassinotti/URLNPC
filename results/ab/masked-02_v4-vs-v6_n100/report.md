# A/B batch — masked-02_v4-vs-v6_n100

100 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| v4 | 100/100 | 51 | 44 | 5 | 51.0% | 71.8 | 83.1 | 85.2% | 14.7 s | 17.0 s |
| v6 | 100/100 | 47 | 48 | 5 | 47.0% | 69.9 | 84.3 | 83.5% | 13.3 s | 15.6 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| v4 | 96.0% / 41.0% | 42.1% / 20.8% | 0.0% / 5.1% | 36.5% / 7.7% |
| v6 | 92.0% / 24.7% | 0.6% / 27.8% | 0.0% / 9.1% | 38.6% / 7.6% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| v4 | llm (claude-haiku-4-5-20251001) | 7.2 | 32.4% | 836 ms | 1039 ms | 0.0% | 0.0% |
| v6 | llm (claude-haiku-4-5-20251001) | 6.7 | 42.6% | 853 ms | 1079 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| v4 | v6 | 51.0% | 47.0% | +4.0 pp | [-9.7, +17.5] pp |
