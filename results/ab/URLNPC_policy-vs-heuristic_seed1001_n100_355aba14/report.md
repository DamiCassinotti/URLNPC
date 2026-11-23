# A/B batch — URLNPC_policy-vs-heuristic_seed1001_n100_355aba14

100 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm8 | 100/100 | 44 | 51 | 5 | 44.0% | 68.7 | 86.2 | 83.0% | 13.7 s | 15.7 s |
| llm13 | 100/100 | 49 | 46 | 5 | 49.0% | 70.2 | 85.5 | 84.5% | 13.4 s | 15.5 s |
| fsm | 100/100 | 50 | 44 | 6 | 50.0% | 69.8 | 84.0 | 81.7% | 13.4 s | 15.3 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm8 | 94.0% / 24.7% | 9.8% / 22.6% | 7.5% / 9.7% | 38.5% / 7.3% |
| llm13 | 97.7% / 22.3% | 14.7% / 29.3% | 18.2% / 7.0% | 39.4% / 8.2% |
| fsm | 95.0% / 48.3% | 10.5% / 6.0% | 3.6% / 18.7% | 32.0% / 4.0% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm8 | llm (claude-haiku-4-5-20251001) | 6.9 | 43.9% | 813 ms | 954 ms | 0.0% | 0.0% |
| llm13 | llm (claude-haiku-4-5-20251001) | 6.8 | 45.3% | 831 ms | 993 ms | 0.0% | 0.0% |
| fsm | fsm | 7.3 | 23.6% | 2 ms | 12 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm8 | llm13 | 44.0% | 49.0% | -5.0 pp | [-18.4, +8.7] pp |
| llm8 | fsm | 44.0% | 50.0% | -6.0 pp | [-19.4, +7.7] pp |
| llm13 | fsm | 49.0% | 50.0% | -1.0 pp | [-14.6, +12.6] pp |

## Paired damage differential, dealt − taken per match (95% CI)

| A | B | pairs | mean A | mean B | A − B | CI |
|---|---|---|---|---|---|---|
| llm8 | llm13 | 100 | -17.6 | -15.3 | -2.2 | [-7.7, +3.2] |
| llm8 | fsm | 100 | -17.6 | -14.2 | -3.3 | [-9.4, +2.8] |
| llm13 | fsm | 100 | -15.3 | -14.2 | -1.1 | [-8.1, +6.0] |
