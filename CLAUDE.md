# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**URLNPC** is a Unity 6 LTS game project featuring a first-person combat arena where an AI enemy NPC is trained using Unity ML-Agents (reinforcement learning) to fight against the player.

## Build & Run

- **Play the game:** Open the project in **Unity 6000.0 LTS**, load `Assets/Scenes/FPS/FPS.unity`, press Play.
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
- `EnemyAgent.cs` — the ML-Agents `Agent` subclass. Collects observations (`canAttack`, `targetInSight`, `health`), receives discrete actions (0=Patrol, 1=Chase, 2=Attack) via the `ActionBuffers` API, and assigns rewards.
- `EnemyBehavior.cs` — executes the actual behavior: NavMesh patrolling within a 20-unit random range, chasing the player, and triggering weapon fire with a 0.5s cooldown.

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

## Known follow-ups

- **Enemy movement / training validation** — at time of upgrade, the enemy doesn't move in play mode. Needs investigation: confirm the `EnemyAgent` Behavior Parameters use *Inference Only* with a valid model, or run training and verify the policy learns. The unused `chaseRange`/`attackRange`/`distanceToTarget` fields in `EnemyAgent.cs` suggest a heuristic was started but not wired up.
