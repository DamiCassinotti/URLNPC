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

Trained models land at `results/URLNPC/URLNPC.onnx`. Resume an interrupted run with `--resume` instead of `--force`.

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

## Tech stack

- Unity 6 LTS, ML-Agents Release 22 (`com.unity.ml-agents` 3.0.0 — embedded under `Packages/` and locally patched for the renamed Inference Engine package).
- New Input System with Starter Assets first-person controller (Cinemachine-driven camera).
- PPO trainer, Python `mlagents 1.1.0` / `torch 2.2.1`.

See `CLAUDE.md` for architecture notes.
