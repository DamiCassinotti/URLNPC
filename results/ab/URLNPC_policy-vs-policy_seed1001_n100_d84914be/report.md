# A/B batch — URLNPC_policy-vs-policy_seed1001_n100_d84914be

100 matches/condition, seed 1001, subject policy, opponent policy, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| v4 | 100/100 | 38 | 57 | 5 | 38.0% | 63.3 | 86.2 | 83.7% | 18.2 s | 18.7 s |
| v5 | 100/100 | 32 | 63 | 5 | 32.0% | 62.4 | 87.0 | 81.7% | 19.4 s | 22.8 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| v4 | 93.6% / 35.2% | 39.5% / 10.2% | 6.9% / 11.8% | 39.5% / 4.5% |
| v5 | 93.6% / 28.0% | 46.0% / 13.0% | 12.7% / 9.0% | 39.3% / 4.6% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| v4 | llm (claude-haiku-4-5-20251001) | 5.8 | 34.4% | 753 ms | 859 ms | 0.0% | 0.0% |
| v5 | llm (claude-haiku-4-5-20251001) | 6.1 | 40.1% | 761 ms | 879 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| v4 | v5 | 38.0% | 32.0% | +6.0 pp | [-7.1, +18.9] pp |
