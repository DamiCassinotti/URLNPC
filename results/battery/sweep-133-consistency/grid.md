# Selector sweep (#133)

86 snapshots — 72 scored for accuracy, 14 ambiguous.

| selector | prompt | shots | temp | accuracy | consistency | ambig-acc | invalid | lat mean | lat p95 |
|---|---|---|---|---|---|---|---|---|---|
| llama3.2:3b | v4 | 8 | 0.0 | 57.9% | 95.8% | 61.9% | 0.0% | 7.7 s | 14.3 s |
| llama3.2:3b | v4 | 8 | 0.7 | 47.2% | 93.5% | 66.7% | 0.0% | 7.4 s | 14.3 s |

## Per-mode, at temperature 0.0

How often each mode was answered on the scored states it is an acceptable answer for, and that mode's share of the answers given. Answering Hunt to a Hunt-or-Patrol state is a hit for Hunt and a miss for Patrol.

| selector | prompt | shots | Hunt answered / chosen | HoldCover answered / chosen | Retreat answered / chosen | Patrol answered / chosen |
|---|---|---|---|---|---|---|
| llama3.2:3b | v4 | 8 | 62.1% / 37.2% | 6.9% / 4.7% | 52.2% / 18.6% | 56.2% / 39.5% |
