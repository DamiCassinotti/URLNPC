# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**URLNPC** is a Unity 6 LTS game project featuring a first-person combat arena where an AI enemy NPC is trained using Unity ML-Agents (reinforcement learning) to fight against the player.

## Build & Run

- **Play the game:** Open the project in **Unity 6000.0 LTS**, load `Assets/Scenes/FPS.unity`, press Play.
- **ML training:** Create a Python 3.10 venv, install `requirements.txt`, then from the repo root:
  ```bash
  python -m venv .venv && source .venv/bin/activate
  pip install -r requirements.txt
  mlagents-learn config/URLNPC.yaml --run-id=URLNPC
  ```
  Trained models (`.onnx` files) are saved to `results/URLNPC/`.

## Tech Stack

- **Unity:** 6000.0 LTS
- **ML-Agents:** package `3.0.0` (Release 22), embedded at `Packages/com.unity.ml-agents/` and locally patched to use `Unity.InferenceEngine` instead of the deprecated `Unity.Sentis` namespace. Sentis was rebranded to "Inference Engine" (`com.unity.ai.inference`) in Unity 6, but ML-Agents 3.0.0 was authored against the old namespace — the embedded patch bridges that gap.
- **Inference Engine:** `com.unity.ai.inference` 2.6.1 (formerly Sentis)
- **Input System:** new (`com.unity.inputsystem`), legacy Input class disabled. Player controller is **Starter Assets — First Person Character Controller**, with Cinemachine driving the camera via a virtual camera following `PlayerCapsule/PlayerCameraRoot`.
- **Python (training):** 3.10.12, `mlagents 1.1.0`, `torch 2.2.1` — see `requirements.txt`.

## Architecture

### Component Relationships

**Game state** is managed by `GameManager.cs`, which tracks win/loss conditions, controls the end-of-round UI, and calls `CounterData` (static class) to persist scores across scene reloads. `GameManager` holds a generic `[SerializeField] Behaviour playerController` reference that is disabled at end-of-round — wire whichever player controller component you're using (currently StarterAssets `FirstPersonController`) to that slot in the Inspector.

**Enemy AI** is split across two scripts:
- `EnemyAgent.cs` — the ML-Agents `Agent` subclass (the **only** `Agent` on the Enemy GameObject). Collects observations (`canAttack`, `targetInSight`, `normalizedHealth`), receives discrete actions (0=Patrol, 1=Chase, 2=Attack) via `ActionBuffers`, and assigns rewards. Subscribes to `Health.OnDamaged` / `OnDied` on both itself and the player target to emit hit/kill/death rewards. Calls `EndEpisode()` on either death and resets state in `OnEpisodeBegin()`.
- `EnemyBehavior.cs` — plain `MonoBehaviour`. Executes the actual behavior: NavMesh patrolling within a random range, chasing the player, triggering weapon fire with cooldown, and providing observation primitives (`IsTargetInSight`, `ReadCanAttack`, etc.) plus `ResetState()` for episode resets.

`Health.cs` exposes `OnDamaged(float)` and `OnDied` events that `EnemyAgent` listens to for reward shaping. `GameManager.ProcessDeath` early-exits when `Academy.Instance.IsCommunicatorOn` so it doesn't freeze the scene during training.

**Weapons** use a raycast-based hit system defined in abstract class `Weapon.cs`, with `PlayerWeapon.cs` (new Input System: `Mouse.current.leftButton.wasPressedThisFrame`) and `EnemyWeapon.cs` (called by `EnemyBehavior`) as concrete implementations. Hits call `Health.DecreaseHealth()` on the target.

**Health & death:** `Health.cs` tracks HP for both Player and NPC. On death it calls `GameManager.ProcessDeath()`, which reads the entity's tag (`"Player"` or `"NPC"`) to determine the winner.

**UI:** `Counter.cs` reads from static `CounterData` each frame to display win counts. TextMesh Pro is used for all UI text.

### Tags
- `"Player"` — the first-person character (Starter Assets `PlayerCapsule`)
- `"NPC"` — the enemy agent

### ML-Agents Configuration (`config/URLNPC.yaml`)
- Trainer: PPO
- `max_steps`: 500,000
- `batch_size`: 1024
- `learning_rate`: 0.0003
- `time_horizon`: 64

The pre-trained `.nn` model in `results/URLNPC/` was produced under the old ML-Agents 1.0.8 stack and is not guaranteed to load under Release 22 / Inference Engine — retrain from scratch with `mlagents-learn` and reference the new `.onnx` output from the `EnemyAgent`'s **Behavior Parameters → Model** field.

## Required Enemy prefab components

The Enemy GameObject **must** carry `BehaviorParameters` + `DecisionRequester` for ML-Agents to drive it at all. The prefab in `Assets/Prefabs/Characters/Enemy.prefab` is missing both at time of writing — see the "Required Enemy prefab setup" section in `README.md` for the exact Inspector configuration (Behavior Name `URLNPC`, vector obs size 3, one discrete branch of size 3, decision period 5).

## Reward shape (in `EnemyAgent.cs`, all serialized for Inspector tweaking)

- `aliveRewardPerStep` `+0.001`
- `hitTargetReward` `+0.5`
- `gotHitPenalty` `-0.5`
- `killTargetReward` `+1.0` (ends episode)
- `diedPenalty` `-1.0` (ends episode)
- `wastedShotPenalty` `-0.05` (attack action while target not in sight)

## Known follow-ups

- **NavMesh bake** — confirm a NavMesh exists for the FPS scene; the enemy uses `NavMeshAgent` for patrol/chase and won't move without one.
- **Player position reset on episode begin** — `EnemyAgent.OnEpisodeBegin` resets the *enemy's* position and both healths but leaves the player where they are. For self-play / scripted-player training, add a player reset hook.
