# URLNPC

First-person combat arena where an AI enemy NPC is trained with Unity ML-Agents (reinforcement learning) to fight against the player.

## Requirements

- **Unity 6000.0 LTS** (install via Unity Hub).
- **Python 3.10.12** for ML training.

## Running the game

1. Open the project in Unity 6000.0 LTS.
2. Load `Assets/Scenes/FPS.unity`.
3. Press Play.

To play against a trained model rather than the heuristic, drag the `.onnx` produced by training into the **Behavior Parameters → Model** field on the Enemy prefab and set **Behavior Type = Inference Only**.

## Arenas

The arena is generated procedurally at startup by `Assets/Scripts/ArenaManager.cs`. On every scene load it removes the static scene arena (the `Arena` root), picks **one of 5 layouts at random**, builds it from primitive geometry with contrasting URP materials, bakes a fresh NavMesh, and drops the player at a random point on it — so both the arena and your starting position re-roll every round.

The five arenas vary in size and cover:

| # | Name | Size (X×Z) | Theme |
|---|------|-----------|-------|
| 0 | Courtyard   | 40×40 | Central building with a doorway, corner crate stacks, low flanking walls |
| 1 | The Pit     | 28×28 | Tight; two raised corner platforms reached by stairs, central pillars |
| 2 | Twin Towers | 60×60 | Two tall tower buildings, long mid-field sightline barriers |
| 3 | Maze        | 44×44 | Flat maze of offset wall segments and pillars |
| 4 | Ramparts    | 56×36 | Rectangular; raised side walkways via stairs, central divider with gaps |

### Scene wiring (optional)

`ArenaManager` auto-bootstraps itself on **every** scene load (it hooks `SceneManager.sceneLoaded`), so arenas work — and re-roll each round — with no scene setup. The game restarts rounds via `SceneManager.LoadScene` after a win or a draw, and each reload builds a fresh random arena.

Placing the component in the scene by hand is only needed if you want non-default Inspector settings:

1. In `Assets/Scenes/FPS.unity`, create an empty GameObject named `ArenaManager`.
2. **Add Component → Arena Manager**.
3. Set **Forced Arena Index** to `0`–`4` to always build a specific arena (leave `-1` for random); toggle **Reposition Player On Start**; tune wall height/thickness.

No NavMesh needs to be baked by hand — `ArenaManager` bakes one at runtime via `NavMeshSurface` (`com.unity.ai.navigation`). The static arena baked into the scene is removed automatically at startup, so you can leave it in place.

## Training the agent

For the end-to-end sequence of running a session and checking it — build the player, run the
slice, read TensorBoard, load an `.onnx` for inference, force a mode, run self-play — see
[`docs/rl-runbook.md`](docs/rl-runbook.md).

### One-time setup

```bash
# Python 3.10 is required (mlagents 1.1.0 is pinned to the 3.10 series).
python3.10 -m venv .venv
source .venv/bin/activate
pip install --upgrade pip
# --ignore-requires-python: mlagents 1.1.0 caps at 3.10.12, but newer 3.10.x
# patch releases (e.g. Ubuntu 24.04 ships 3.10.20) are runtime-compatible.
pip install --ignore-requires-python -r requirements.txt
```

### Required Enemy prefab setup (verify before first training run)

The `Assets/Prefabs/Characters/Enemy.prefab` GameObject must have these components configured. Without them, ML-Agents will not drive the agent (it will stand still in Play mode):

1. **Behavior Parameters** (`Unity.MLAgents.Policies.BehaviorParameters`)
   - **Behavior Name:** `URLNPC` (must match the key in `config/URLNPC.yaml`)
   - **Vector Observation → Space Size:** `18` (see `NpcObservations` for the slot-by-slot layout)
   - **Vector Observation → Stacked Vectors:** `1`
   - **Actions → Continuous Actions:** `0`
   - **Actions → Discrete Branches:** `2`, **Branch 0 Size:** `7` (the `MovementAction` primitives), **Branch 1 Size:** `2` (hold fire / fire)
   - **Behavior Type:** `Default` while training, `Inference Only` to play vs a trained `.onnx`, `Heuristic Only` to test the scripted fallback
   - **Model:** the trained `.onnx` once you have one (leave empty for first training)
2. **Decision Requester** (`Unity.MLAgents.DecisionRequester`)
   - **Decision Period:** `5` (one decision every 5 fixed-update ticks)
   - **Take Actions Between Decisions:** on
3. **EnemyAgent** — Max Step: `0`; the round clock owns episode timeout (`EnemyAgent.Initialize` forces this).
4. **EnemyBehavior** — wire **Target** to the `PlayerCapsule` (or leave empty; it auto-finds the `Player` tag at Start).
5. **No Ray Perception Sensor.** It used to be on the prefab; it fed the policy live target detections outside `PerceptionMemory` and outside the declared observation width, so it was removed. Don't add one back.
6. **NavMeshAgent** is required for movement. Bake a NavMesh for the FPS scene if you haven't.

### Run training

From the repo root with the venv active, `scripts/train.sh` is the one entry point for both
paths (it wraps `mlagents-learn`):

```bash
scripts/train.sh editor <run-id> [config] [extra mlagents args...]   # press Play to feed it
scripts/train.sh build  <run-id> [config] [--num-envs N] [--seed S]  # headless standalone
```

`config` defaults to `config/URLNPC.yaml`; the slice/full/self-play configs and a step-by-step
walkthrough are in [`docs/rl-runbook.md`](docs/rl-runbook.md). In `editor` mode the trainer
prints "Listening on port 5004…" — open the FPS scene and press **Play**. Episodes auto-reset
on death (the agent calls `EndEpisode()` and `OnEpisodeBegin` re-rolls position and health).

To speed training, in the Editor: **Edit → Project Settings → Time → Time Scale** can be increased, or run multiple parallel envs by duplicating the Enemy/Player setup into separate Training Areas (recommended for longer runs).

Trained models land at `results/<run-id>/URLNPC.onnx`.

### Headless standalone build (`--env` runs)

Pressing Play is only one way to feed the trainer. Build a standalone Linux player once and `mlagents-learn` can launch it itself — no editor, parallel environments, unattended overnight runs:

```bash
scripts/build-player.sh   # close the editor first (single instance per project)
```

This runs `BuildPlayer.BuildLinux` (`Assets/Editor/BuildPlayer.cs`) in batch mode and writes `Builds/Linux/URLNPC.x86_64` (gitignored). Point the trainer at it with `scripts/train.sh build`:

```bash
scripts/train.sh build <run-id> [config] --num-envs=4 --seed=12345
```

`build` runs against the standalone player and drives the player side with the shared agent policy (`-playerDriver agent`) for self-play; `--seed` becomes the build's `-runSeed`. Both are passed through the trainer's `--env-args`.

### Stopping and resuming training

You can quit the game and pick training back up later **on the same semi-trained model** — the network is checkpointed entirely on the Python side (`results/URLNPC/`, see `checkpoint_interval` in `config/URLNPC.yaml`), not stored in the Unity project.

1. Stop whenever: exit Play mode in the Editor and/or `Ctrl+C` the trainer.
2. To continue, run with **`--resume`** instead of `--force` (both pass straight through `train.sh`), then press **Play** again:

   ```bash
   scripts/train.sh editor <run-id> config/URLNPC.yaml --resume
   ```

   `--resume` reloads the network weights, optimizer state, and step count from the last checkpoint. (To instead *fork* a finished model into a brand-new run, use `--initialize-from=URLNPC` with a different `--run-id`.)

The on-screen win/loss tally also survives quitting: `CounterData` persists the score to `PlayerPrefs`, so it carries across Editor Play sessions and standalone builds. Use the **Reset Score** button on the end-of-round canvas (or call `CounterData.ResetScores()`) to clear it.

### Reproducible evaluation runs

Arena selection, spawn sampling, wander waypoints and commanded-mode sampling are driven by a single seedable RNG (`RunRng`). To make two runs comparable, fix the seed either way:

- **Inspector:** set **Run Seed** on the `ArenaManager` component (0 = random each run).
- **Command line (standalone build / batch mode):** pass `-runSeed <int>` — this overrides the Inspector value.

Unseeded runs draw a random seed and still log it (look for the `[RunRng] Run seed: …` line in the Console/Player log), so any run can be replayed after the fact. The seed is also recorded per episode as the `Run/Seed` stat in TensorBoard. Note the seed governs arena/spawn/waypoint *sequences*, not frame-exact gameplay (physics, input timing and aim spread still vary).

### Reward shape

Defined in `EnemyAgent.cs` (tunable in Inspector). The rows the commanded mode doesn't change:

| Event | Reward |
|---|---|
| Per decision step while alive | `+0.001` |
| Killed player | `+1.0` (ends episode) |
| Died | `-1.0` (ends episode) |
| Round clock ran out (draw) | `-0.2` (ends episode) |
| Shot while target out of sight | `-0.05` |
| Per step closer to the player than 6 m | `-0.005` |

The rest is one column per commanded mode (`NpcMode`), so the same policy is pulled toward different behavior depending on what it was told to do:

| Event | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| Dealt damage to player | `+0.5` | `+0.5` | `+0.1` | `+0.1` |
| Took damage | `-0.3` | `-0.6` | `-0.6` | `-0.5` |
| Per metre closed on the player | `+0.01` | `0` | `-0.01` | `0` |
| Per step with the player's line of sight to it broken | `0` | `+0.005` | `+0.002` | `0` |
| Each patch of the arena first entered this episode | `0` | `0` | `0` | `+0.002` |

### Mode compliance

Whether the policy actually obeys the mode it is commanded. Every decision step is scored against its mode, and the per-episode fraction is reported as `Compliance/Hunt`, `Compliance/HoldCover`, `Compliance/Retreat` and `Compliance/Patrol` in TensorBoard, next to reward and entropy:

| Mode | A step counts as compliant when it |
|---|---|
| Hunt | closed distance on the player, or got a shot off |
| HoldCover | kept the player's line of sight to it broken |
| Retreat | opened distance |
| Patrol | entered a patch of the arena it had not been in this episode |

The same numbers go to the telemetry log as a `mode_compliance` line per episode (steps, compliant steps and rate per mode), alongside a `mode_change` line for every switch — that pair is the mode timeline the offline analysis reads. Patrol's rate is low by construction: a patch only counts the first time, so compare it between runs rather than reading it as a percentage.

## Testing

The project carries a Unity Test Framework suite covering the load-bearing behaviors: seeded reproducibility (`RunRng`, arena/spawn replays), the round clock and draw path, win/loss/draw counters, health/death events, episode resets, and the NPC's sensory contract (`PerceptionMemory`).

- **In the editor:** *Window ▸ General ▸ Test Runner*, then run the `URLNPC.Tests.EditMode` / `URLNPC.Tests.PlayMode` assemblies.
- **Headless (CLI):** close the editor (Unity is single-instance per project) and run:

  ```bash
  scripts/run-tests.sh            # EditMode only (fast)
  scripts/run-tests.sh playmode   # PlayMode integration tests
  scripts/run-tests.sh all        # both
  ```

  Results (NUnit XML + log) are written to `results/tests/`. The script auto-detects the editor version pinned in `ProjectSettings/ProjectVersion.txt` under `~/Unity/Hub/Editor/`; override with `UNITY_BIN=/path/to/Unity`.

Test scaffolding restores real state on teardown: your persisted score tally (PlayerPrefs) is snapshotted and put back, and `Time.timeScale`/NavMesh/RNG state are reset per test.

**CI:** `.github/workflows/tests.yml` runs both suites on every PR and on pushes to `main`, via [GameCI](https://game.ci/) (`unity-test-runner` in the `unityci/editor` Docker image matching `ProjectVersion.txt`). It needs three repository secrets — `UNITY_LICENSE` (contents of a manually activated `.ulf`), `UNITY_EMAIL` and `UNITY_PASSWORD`. Results are published as check runs and uploaded as artifacts.

## Tech stack

- Unity 6 LTS, ML-Agents Release 22 (`com.unity.ml-agents` 3.0.0 — embedded under `Packages/` and locally patched for the renamed Inference Engine package).
- New Input System with Starter Assets first-person controller (Cinemachine-driven camera).
- PPO trainer, Python `mlagents 1.1.0` / `torch 2.2.1`.

See `CLAUDE.md` for architecture notes.
