# A/B batch — URLNPC_policy-vs-policy_seed1001_n100_a0b7fb79

100 matches/condition, seed 1001, subject policy, opponent policy, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm | 100/100 | 32 | 64 | 4 | 32.0% | 62.9 | 87.6 | 82.3% | 18.5 s | 19.5 s |
| fsm | 100/100 | 41 | 55 | 4 | 41.0% | 69.0 | 84.8 | 82.7% | 18.5 s | 20.6 s |
| random | 100/100 | 35 | 60 | 5 | 35.0% | 68.2 | 86.1 | 77.5% | 18.6 s | 18.2 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm | 94.5% / 35.6% | 13.6% / 12.6% | 43.6% / 6.9% | 40.5% / 5.4% |
| fsm | 94.2% / 39.2% | 73.4% / 2.7% | 34.8% / 25.3% | 35.9% / 2.9% |
| random | 94.5% / 9.3% | 59.2% / 7.9% | 52.5% / 9.1% | 37.2% / 9.8% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm | llm (claude-haiku-4-5-20251001) | 5.4 | 31.0% | 972 ms | 1200 ms | 0.0% | 0.0% |
| fsm | fsm | 5.8 | 28.8% | 2 ms | 11 ms | 0.0% | 0.0% |
| random | random | 6.0 | 74.8% | 2 ms | 11 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm | fsm | 32.0% | 41.0% | -9.0 pp | [-21.9, +4.3] pp |
| llm | random | 32.0% | 35.0% | -3.0 pp | [-15.8, +10.0] pp |
| fsm | random | 41.0% | 35.0% | +6.0 pp | [-7.4, +19.1] pp |
