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

**Round clock:** `GameManager` also runs a configurable round timer (`roundDurationSeconds`, default 120 s game time — it scales with the trainer's `time_scale`; ≤ 0 disables it). Timeout is a **draw**: no winner is forced and `CounterData.Draw()` records the outcome. In human play a draw ends the round exactly like a win — freeze, "Draw!" banner, and the end-of-round button reloads the scene into a completely new round (fresh arena, fresh spawns); agents are *not* reset in place on this path. During training (communicator on), every `EnemyAgent` instead gets `OnRoundTimeout()` (small negative terminal reward `timeoutPenalty`, default `-0.2`, then `EndEpisode()`), the clock is rearmed and the scene keeps running — `EnemyAgent.OnEpisodeBegin` calls `GameManager.ResetRoundClock()` so every episode gets a full budget without a scene reload. **`EnemyAgent.Initialize` forces `Agent.MaxStep = 0`:** the round clock is the single owner of time-based episode termination. The binary FPS scene carries a stale serialized `MaxStep` of 5000 (~100 s, shorter than the round), which used to end episodes silently before the clock fired — timeouts restarted the round in place and draws were never counted. Remaining time is queryable via `GameManager.RemainingRoundTime` (the "tiempo restante" input for the future GameStateSnapshot/LLM context) and shown on a runtime-created HUD text (the scene is binary, so the label is built in code like the Reset Score button).

**Enemy AI** is split across three scripts:
- `EnemyAgent.cs` — the ML-Agents `Agent` subclass (the **only** `Agent` on the Enemy GameObject). Collects observations (`canAttack`, `targetInSight`, `normalizedHealth`), receives discrete actions (0=Patrol, 1=Chase, 2=Attack) via `ActionBuffers`, and assigns rewards. Subscribes to `Health.OnDamaged` / `OnDied` on both itself and the player target to emit hit/kill/death rewards. Calls `EndEpisode()` on either death and resets state in `OnEpisodeBegin()`.
- `EnemyBehavior.cs` — plain `MonoBehaviour`. Executes the actual behavior: NavMesh patrolling within a random range, chasing toward the last-seen player position, triggering weapon fire with cooldown, and providing observation primitives (`IsTargetInSight`, `ReadCanAttack`, etc.) plus `ResetState()` for episode resets.
- `PerceptionMemory.cs` — enforces the **sensory contract**: the NPC never knows the player's HP and knows their position only while visible (`CurrentlyVisible`, `LastSeenPosition`, `TimeSinceSeen`, updated from `IsTargetInSight()`). It is the *only* source of target info for the NPC brain — `Chase()` navigates to and `Attack()` aims at `LastSeenPosition`, and the `targetInSight` observation reads `CurrentlyVisible`. Environment code (spawn separation, reward computation like `DistanceToTarget()`, and the sight check itself) may still read true state; only policy inputs/actions are restricted. Auto-added at runtime by `EnemyBehavior.Awake` (binary prefab), memory wiped by `ResetState()` on episode begin.

`Health.cs` exposes `OnDamaged(float)` and `OnDied` events that `EnemyAgent` listens to for reward shaping. `GameManager.ProcessDeath` early-exits when `Academy.Instance.IsCommunicatorOn` so it doesn't freeze the scene during training.

**Weapons** use a raycast-based hit system defined in abstract class `Weapon.cs`, with `PlayerWeapon.cs` (new Input System: `Mouse.current.leftButton.wasPressedThisFrame`) and `EnemyWeapon.cs` (called by `EnemyBehavior`) as concrete implementations. Hits call `Health.DecreaseHealth()` on the target.

**Health & death:** `Health.cs` tracks HP for both Player and NPC. On death it calls `GameManager.ProcessDeath()`, which reads the entity's tag (`"Player"` or `"NPC"`) to determine the winner.

**Arenas:** `ArenaManager.cs` procedurally builds the level at runtime. Because the scene is force-binary serialized, level geometry is generated in code rather than authored in the scene. On `Awake` (it runs at `[DefaultExecutionOrder(-10000)]` so the NavMesh exists before the enemy spawns) it destroys any static `Arena` root, picks 1 of 5 layouts (random, or `forcedArenaIndex`), builds floor/walls/cover from primitive cubes with contrasting URP/Lit materials, and bakes a fresh NavMesh via a runtime `NavMeshSurface` (`Unity.AI.Navigation`). It exposes `ArenaManager.Current` so `EnemyBehavior.GetRandomPositionInMap` samples spawn points from the active arena's NavMesh instead of hard-coded ±60 bounds, and repositions the player to a **random** NavMesh point on `Start` (seeded via `RunRng`, every round, all modes). The auto-bootstrap re-checks on **every** scene load (`SceneManager.sceneLoaded`), not just the first: the manager and its generated arena die with each `LoadScene`, and without the re-check rounds 2+ had no manager — no arena re-roll, and the old static `Arena` root baked into the scene was left standing. Placing the component in the scene still works and takes precedence. See "Arenas" in `README.md`.

**Reproducibility:** `RunRng.cs` is a static, process-wide seedable RNG that all evaluation-relevant randomness routes through — arena selection, spawn-point sampling (`ArenaManager.RandomGroundPoint`, `EnemyBehavior` fallback) and patrol/wander waypoints — each on an independent sub-stream so a variable number of draws in one domain can't shift the others. Seed priority: `-runSeed <int>` command-line arg > non-zero `runSeed` Inspector field on `ArenaManager` > time-derived random (still logged, so any run is replayable after the fact). The seed is logged at startup (`[RunRng]` line) and recorded per episode as the `Run/Seed` stat in TensorBoard. `EnemyWeapon` aim spread deliberately stays on `UnityEngine.Random`: it's drawn a policy-dependent number of times, so routing it through the deterministic streams would desynchronise them between runs.

**UI:** `Counter.cs` reads from static `CounterData` each frame to display win counts. TextMesh Pro is used for all UI text.

### Tags
- `"Player"` — the first-person character (Starter Assets `PlayerCapsule`)
- `"NPC"` — the enemy agent

### ML-Agents Configuration (`config/URLNPC.yaml`)
- Trainer: PPO
- `max_steps`: 1,000,000
- `batch_size`: 1024
- `learning_rate`: 0.0003
- `time_horizon`: 64

The pre-trained `.nn` model in `results/URLNPC/` was produced under the old ML-Agents 1.0.8 stack and is not guaranteed to load under Release 22 / Inference Engine — retrain from scratch with `mlagents-learn` and reference the new `.onnx` output from the `EnemyAgent`'s **Behavior Parameters → Model** field.

## Required Enemy prefab components

The Enemy GameObject **must** carry `BehaviorParameters` + `DecisionRequester` for ML-Agents to drive it at all — without a `DecisionRequester` no decisions are requested, `OnActionReceived` never fires, and the enemy stands still in every mode (heuristic, inference, and training). Both are present on `Assets/Prefabs/Characters/Enemy.prefab` (Behavior Name `URLNPC`, vector obs size 3, one discrete branch of size 3, decision period 5, Behavior Type Default, no model assigned). The prefab is text-serialized YAML (only the scene is force-binary), so it can be edited directly.

## Reward shape (in `EnemyAgent.cs`, all serialized for Inspector tweaking)

- `aliveRewardPerStep` `+0.001`
- `hitTargetReward` `+0.5`
- `gotHitPenalty` `-0.5`
- `killTargetReward` `+1.0` (ends episode)
- `diedPenalty` `-1.0` (ends episode)
- `wastedShotPenalty` `-0.05` (attack action while target not in sight)
- `timeoutPenalty` `-0.2` (round clock ran out — draw, ends episode)

## Known follow-ups

- **NavMesh bake** — now handled at runtime: `ArenaManager` bakes a `NavMeshSurface` over the generated arena on `Awake`, so no hand-baked NavMesh is required. (A hand-baked NavMesh in the scene is removed at startup.)
- **Player position reset on episode begin** — handled: during training (`Academy.Instance.IsCommunicatorOn`), `EnemyAgent.OnEpisodeBegin` teleports the player to a random NavMesh point (`ArenaManager.RepositionPlayerAtRandomPoint`) *before* the enemy respawns, so `EnemyBehavior.InitAtRandomPosition`'s min-spawn-separation check measures against the player's fresh position. Toggle via `repositionPlayerOnEpisodeBegin` on `EnemyAgent`. In human play the randomizer runs at round start instead: each scene reload, `ArenaManager.Start` places the player at a random NavMesh point (not per episode-begin, which would also fire on mid-round `MaxStep` resets and teleport a live player mid-fight).
