# A/B batch — URLNPC_policy-vs-heuristic_seed1001_n400_355aba14

400 matches/condition, seed 1001, subject policy, opponent heuristic, 95% CI on the pairwise rows.

| condition | episodes | win | loss | draw | win rate | dmg dealt | dmg taken | accuracy | TTK | survival |
|---|---|---|---|---|---|---|---|---|---|---|
| llm8 | 400/400 | 189 | 201 | 10 | 47.2% | 68.3 | 84.5 | 82.3% | 13.9 s | 15.5 s |
| llm13 | 400/400 | 195 | 194 | 11 | 48.8% | 68.9 | 83.4 | 81.9% | 14.2 s | 15.9 s |
| fsm | 400/400 | 204 | 183 | 13 | 51.0% | 69.5 | 83.4 | 81.9% | 13.8 s | 15.6 s |

## Per-mode compliance / visible

| condition | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| llm8 | 89.0% / 24.8% | 4.5% / 25.0% | 6.4% / 12.4% | 38.5% / 9.2% |
| llm13 | 89.8% / 23.7% | 10.3% / 26.1% | 23.0% / 7.4% | 37.5% / 9.4% |
| fsm | 93.7% / 45.5% | 26.0% / 6.4% | 16.4% / 20.8% | 31.5% / 5.6% |

## Selector

| condition | selector | decisions/ep | switch rate | latency mean | latency p95 | invalid | fallback |
|---|---|---|---|---|---|---|---|
| llm8 | llm (claude-haiku-4-5-20251001) | 6.0 | 42.1% | 885 ms | 1094 ms | 0.0% | 0.0% |
| llm13 | llm (claude-haiku-4-5-20251001) | 6.2 | 43.2% | 898 ms | 1100 ms | 0.0% | 0.0% |
| fsm | fsm | 6.5 | 25.2% | 2 ms | 12 ms | 0.0% | 0.0% |

## Pairwise win-rate difference (95% CI)

| A | B | win rate A | win rate B | A − B | CI |
|---|---|---|---|---|---|
| llm8 | llm13 | 47.2% | 48.8% | -1.5 pp | [-8.4, +5.4] pp |
| llm8 | fsm | 47.2% | 51.0% | -3.8 pp | [-10.6, +3.2] pp |
| llm13 | fsm | 48.8% | 51.0% | -2.3 pp | [-9.1, +4.7] pp |

## Paired damage differential, dealt − taken per match (95% CI)

| A | B | pairs | mean A | mean B | A − B | CI |
|---|---|---|---|---|---|---|
| llm8 | llm13 | 400 | -16.2 | -14.5 | -1.7 | [-4.3, +0.9] |
| llm8 | fsm | 400 | -16.2 | -14.0 | -2.2 | [-5.6, +1.2] |
| llm13 | fsm | 400 | -14.5 | -14.0 | -0.5 | [-4.4, +3.3] |
