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
   - **Vector Observation → Space Size:** `3` (canAttack, targetInSight, normalizedHealth)
   - **Vector Observation → Stacked Vectors:** `1`
   - **Actions → Continuous Actions:** `0`
   - **Actions → Discrete Branches:** `1`, **Branch 0 Size:** `3` (Patrol, Chase, Attack)
   - **Behavior Type:** `Default` while training, `Inference Only` to play vs a trained `.onnx`, `Heuristic Only` to test the scripted fallback
   - **Model:** the trained `.onnx` once you have one (leave empty for first training)
2. **Decision Requester** (`Unity.MLAgents.DecisionRequester`)
   - **Decision Period:** `5` (one decision every 5 fixed-update ticks)
   - **Take Actions Between Decisions:** on
3. **EnemyAgent** — Max Step: `5000` (set in the Inspector on the Agent component itself)
4. **EnemyBehavior** — wire **Target** to the `PlayerCapsule` (or leave empty; it auto-finds the `Player` tag at Start).
5. **Ray Perception Sensor 3D** is already present and detects the `Player` tag — leave as-is.
6. **NavMeshAgent** is required for movement. Bake a NavMesh for the FPS scene if you haven't.

### Run training

From the repo root, with the venv active:

```bash
mlagents-learn config/URLNPC.yaml --run-id=URLNPC --force
```

`mlagents-learn` will print "Listening on port 5004. Start training by pressing the Play button in the Unity Editor." Open the FPS scene and press **Play**. Episodes auto-reset on death (the agent calls `EndEpisode()` and `OnEpisodeBegin` re-rolls position and health).

To speed training, in the Editor: **Edit → Project Settings → Time → Time Scale** can be increased, or run multiple parallel envs by duplicating the Enemy/Player setup into separate Training Areas (recommended for longer runs).

Trained models land at `results/URLNPC/URLNPC.onnx`.

### Stopping and resuming training

You can quit the game and pick training back up later **on the same semi-trained model** — the network is checkpointed entirely on the Python side (`results/URLNPC/`, see `checkpoint_interval` in `config/URLNPC.yaml`), not stored in the Unity project.

1. Stop whenever: exit Play mode in the Editor and/or `Ctrl+C` the trainer.
2. To continue, run with **`--resume`** instead of `--force`, then press **Play** again:

   ```bash
   mlagents-learn config/URLNPC.yaml --run-id=URLNPC --resume
   ```

   `--resume` reloads the network weights, optimizer state, and step count from the last checkpoint. (To instead *fork* a finished model into a brand-new run, use `--initialize-from=URLNPC` with a different `--run-id`.)

The on-screen win/loss tally also survives quitting: `CounterData` persists the score to `PlayerPrefs`, so it carries across Editor Play sessions and standalone builds. Use the **Reset Score** button on the end-of-round canvas (or call `CounterData.ResetScores()`) to clear it.

### Reproducible evaluation runs

Arena selection, spawn sampling, wander waypoints and commanded-mode sampling are driven by a single seedable RNG (`RunRng`). To make two runs comparable, fix the seed either way:

- **Inspector:** set **Run Seed** on the `ArenaManager` component (0 = random each run).
- **Command line (standalone build / batch mode):** pass `-runSeed <int>` — this overrides the Inspector value.

Unseeded runs draw a random seed and still log it (look for the `[RunRng] Run seed: …` line in the Console/Player log), so any run can be replayed after the fact. The seed is also recorded per episode as the `Run/Seed` stat in TensorBoard. Note the seed governs arena/spawn/waypoint *sequences*, not frame-exact gameplay (physics, input timing and aim spread still vary).

### Reward shape

Defined in `EnemyAgent.cs` (tunable in Inspector):

| Event | Reward |
|---|---|
| Per decision step while alive | `+0.001` |
| Dealt damage to player | `+0.5` |
| Took damage | `-0.5` |
| Killed player | `+1.0` (ends episode) |
| Died | `-1.0` (ends episode) |
| Shot while target out of sight | `-0.05` |

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
