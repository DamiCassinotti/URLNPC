# A/B batch — URLNPC_policy-vs-policy_seed1001_n30_d84914be

30 matches/condition, seed 1001, subject policy, opponent policy, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| v4 | 30/30 | 9 | 20 | 1 | 30.0% | 65.5 | 91.0 | 81.9% | 18.9 s | 21.5 s |
| v5 | 30/30 | 9 | 20 | 1 | 30.0% | 62.5 | 91.0 | 80.1% | 20.8 s | 20.6 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| v4 | 90.0% / 39.7% | 0.0% / 16.7% | 3.9% / 9.2% | 38.7% / 5.1% |
| v5 | 88.8% / 24.5% | 23.6% / 9.6% | 33.9% / 2.9% | 40.7% / 5.3% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| v4 | llm (claude-haiku-4-5-20251001) | 5.5 | 28.3% | 934 ms | 1120 ms | 0.0% | 0.0% |
| v5 | llm (claude-haiku-4-5-20251001) | 5.7 | 36.8% | 956 ms | 1154 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| v4 | v5 | 30.0% | 30.0% | +0.0 pp | [-22.3, +22.3] pp |
