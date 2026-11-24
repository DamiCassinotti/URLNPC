# A/B batch — URLNPC_policy-vs-policy_seed1001_n400_21243b50

400 matches/condition, seed 1001, subject policy, opponent policy, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm8 | 400/400 | 155 | 231 | 14 | 38.8% | 65.8 | 83.2 | 79.1% | 19.4 s | 20.8 s |
| fsm | 400/400 | 160 | 227 | 13 | 40.0% | 67.0 | 83.9 | 79.5% | 19.4 s | 20.5 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm8 | 87.5% / 19.8% | 27.4% / 14.7% | 50.2% / 12.0% | 38.1% / 5.6% |
| fsm | 87.9% / 41.0% | 33.0% / 8.5% | 43.5% / 18.9% | 34.9% / 4.1% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm8 | llm (claude-haiku-4-5-20251001) | 8.2 | 41.7% | 734 ms | 860 ms | 0.0% | 0.0% |
| fsm | fsm | 8.2 | 21.0% | 1 ms | 11 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm8 | fsm | 38.8% | 40.0% | -1.3 pp | [-8.0, +5.5] pp |

## Paired damage differential, dealt − taken per match (95% CI)

| A | B | pairs | mean A | mean B | A − B | CI |
|---|---|---|---|---|---|---|
| llm8 | fsm | 400 | -17.4 | -16.9 | -0.6 | [-5.0, +3.8] |
