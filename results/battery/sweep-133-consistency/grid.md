# Selector sweep (#133)

86 snapshots — 72 scored for accuracy, 14 ambiguous.

| selector | prompt | shots | temp | accuracy | consistency | ambig-acc | invalid | lat mean | lat p95 |
|---|---|---|---|---|---|---|---|---|---|
| llama3.2:3b | v4 | 8 | 0.0 | 57.9% | 95.8% | 61.9% | 0.0% | 7.7 s | 14.3 s |
| llama3.2:3b | v4 | 8 | 0.7 | 47.2% | 93.5% | 66.7% | 0.0% | 7.4 s | 14.3 s |

## Per-mode, at the lowest temperature

Accuracy over the scored states each mode is an acceptable answer for, and that mode's share of the answers given.

| selector | prompt | shots | Hunt acc / chosen | HoldCover acc / chosen | Retreat acc / chosen | Patrol acc / chosen |
|---|---|---|---|---|---|---|
| llama3.2:3b | v4 | 8 | 75.9% / 37.2% | 42.5% / 4.7% | 63.8% / 19.8% | 68.8% / 38.4% |
