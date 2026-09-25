# CLAUDE.md

Guidance for Claude Code (claude.ai/code) in this repository. This file is the operational core; the deep per-system design lives in [docs/architecture.md](docs/architecture.md) — read the relevant section there before working on that system.

## Project Overview

**URLNPC** is a Unity 6 LTS game project featuring a first-person combat arena where an AI enemy NPC is trained using Unity ML-Agents (reinforcement learning) to fight against the player. On top of the RL policy sits an LLM tier that commands a high-level `NpcMode` per decision period.

## Build & Run

- **Play the game:** Open the project in **Unity 6000.0 LTS**, load `Assets/Scenes/FPS.unity`, press Play.
- **ML training:** Create a Python 3.10 venv, install `requirements.txt`, then drive training through `scripts/train.sh` (wraps `mlagents-learn`) from the repo root:
  ```bash
  python -m venv .venv && source .venv/bin/activate
  pip install -r requirements.txt
  scripts/train.sh editor <run-id> [config]   # press Play in the editor to feed it
  scripts/train.sh build  <run-id> [config]   # headless standalone build, live opponent
  ```
  `config` defaults to `config/URLNPC.yaml` (slice/full/self-play configs and the full walkthrough are in `docs/rl-runbook.md`). Trained models (`.onnx`) are saved to `results/<run-id>/`.
- **Score a model:** `scripts/eval.sh results/<run-id>/URLNPC.onnx --episodes 100 --seed 1001 --opponent heuristic` — headless rounds, no trainer, summary in `results/eval/`. Full flag set and behavior: "Headless evaluation" in [docs/architecture.md](docs/architecture.md).
- **Run tests:** `scripts/run-tests.sh [editmode|playmode|all]` (editor must be closed; results in `results/tests/`).
- **What of a run is version-controlled (#140):** a training run commits `configuration.yaml`, the final `URLNPC.onnx` and the TensorBoard `events.out.tfevents.*`; an eval run commits `config.json`, `summary.json`/`summary.txt` and `telemetry.jsonl`. Everything else (console `.log`, mlagents' `run_logs/`, PPO checkpoints `*.pt`, step-numbered ONNX snapshots) is gitignored — still written to disk for local debugging and `--resume`.

## Tech Stack

- **Unity:** 6000.0 LTS
- **ML-Agents:** package `3.0.0` (Release 22), embedded at `Packages/com.unity.ml-agents/` and locally patched to use `Unity.InferenceEngine` instead of the deprecated `Unity.Sentis` namespace (ML-Agents 3.0.0 was authored against the old namespace; the embedded patch bridges the Unity 6 rebrand).
- **Inference Engine:** `com.unity.ai.inference` 2.6.1 (formerly Sentis)
- **Input System:** new (`com.unity.inputsystem`), legacy Input disabled. Player controller is **Starter Assets — First Person Character Controller**, Cinemachine camera following `PlayerCapsule/PlayerCameraRoot`.
- **Python (training):** 3.10.12, `mlagents 1.1.0`, `torch 2.2.1` — see `requirements.txt`.

## Architecture — system map

Full detail per system is in [docs/architecture.md](docs/architecture.md). The map, so you know what exists and where to look:

- `GameManager.cs` — game state, win/loss, round clock (timeout = **draw**), end-of-round UI, `CounterData` score persistence.
- **Enemy AI** — `EnemyAgent.cs` (the only ML-Agents `Agent`; observations + rewards), `EnemyBehavior.cs` (executes primitives, provides observation primitives), `PerceptionMemory.cs` (the **sensory contract**: target position only while visible, never HP).
- `NpcBrainSpec.cs` — the **frozen brain interface** (18 obs, two discrete branches 7×2); `NpcObservations` is the slot layout.
- Movement primitives — the `MovementAction` enum dispatched by `EnemyBehavior.Move`; `Wander`/`SearchPlanner` is the search.
- Commanded mode — `NpcMode` (`Hunt`, `HoldCover`, `Retreat`, `Patrol`), held by `ModeChannel.cs`, written by `ModeDirector.cs` (training) or a mode selector (inference). Per-mode action masking in `EnemyAgent.WriteDiscreteActionMask` (`MovementMask`).
- `DamageMemory.cs` — recent-damage flag + hit direction (observations 8–12).
- Combat — `Weapon.cs`/`PlayerWeapon.cs`/`EnemyWeapon.cs`, `CombatBalance.cs` (per-shot damage/cooldown/spread/range, forced in code), `BodyMetrics` (the two body origins), `Health.cs`.
- `ArenaManager.cs` — procedural arena + runtime NavMesh bake; `RunRng.cs` — process-wide seedable RNG for reproducibility.
- `CombatantRig.cs` — swappable Human/Agent player driver (self-play).
- **LLM selector tier** — `GameStateSnapshot` (bucketed input, same sensory contract), `IModeSelector`/`ModeSelectorDriver` (the seam + cadence/failover), baseline selectors (`Fixed`/`Random`/`Heuristic`-FSM), `LlmModeSelector` (+`OllamaEndpoint`), prompt assets (`ModePrompt`, `Assets/Resources/Prompts/`) and few-shot exemplars (`ModeExemplars`, `Assets/Resources/Exemplars/`). Shipped config chosen in #133 (see runbook §11).
- Telemetry — `TelemetryLogger.cs` (one JSON line per event; `mode_decision`/`mode_change`/`mode_compliance`), `ModeComplianceTracker` (per-mode compliance/visibility/range-delta).
- `EvalSession.cs` — headless eval harness behind `eval.sh`.

### Tags
- `"Player"` — the first-person character (Starter Assets `PlayerCapsule`)
- `"NPC"` — the enemy agent

### ML-Agents Configuration (`config/URLNPC.yaml`)
- Trainer PPO, `max_steps` 1,000,000, `batch_size` 1024, `learning_rate` 0.0003, `time_horizon` 64.
- The pre-trained `.nn` in `results/URLNPC/` is from the old ML-Agents 1.0.8 stack and is not guaranteed to load under Release 22 — retrain from scratch and reference the new `.onnx`.

## Critical constraints

These are easy to violate without reading the detail. The **why** for each is in [docs/architecture.md](docs/architecture.md); do not break them without cause:

- **Frozen brain interface (#43):** 18 observations, two discrete branches 7×2. Changing the observation layout, the `MovementAction` enum order, the `NpcMode` enum order, the per-mode `MovementMask`, or `CombatBalance` values **invalidates every trained model** — retrain after.
- **No `RayPerceptionSensor3D` on the Enemy prefab** — it would feed the policy target detections outside `PerceptionMemory` and outside the declared observation width. Don't add one back.
- **Combat/config values are forced in code, not the Inspector** (`CombatBalance`, `MaxStep`, brain shape) — the binary FPS scene is a prefab instance with stale overrides that prefab edits can't reach. Keep `[SerializeField]` fields (renaming drops other scene overrides) but tune the constants.
- **Keep new game rules as pure POCO + thin MonoBehaviour adapter**, exhaustively unit-tested in EditMode. Do **not** fold serialized reward/config fields into POCOs or nested serializable classes — the binary scene may carry overrides a field move would silently drop.
- **Never pass `-runSeed` to the test process** — it outranks the inspector seeds the reproducibility tests set on purpose.
- **Required Enemy prefab components:** `BehaviorParameters` + `DecisionRequester` (without the requester, `OnActionReceived` never fires and the enemy stands still). Present on `Assets/Prefabs/Characters/Enemy.prefab` (Behavior Name `URLNPC`, obs 18, branches 7×2, decision period 5, no model assigned).

## Reward shape

The full table and rationale are in [docs/architecture.md](docs/architecture.md) ("Reward shape"). In brief: global rows (`aliveRewardPerStep`, `diedPenalty`, `wastedShotPenalty`, `timeoutPenalty`, `tooCloseDistance`) plus per-mode columns indexed in `NpcModes.All` order (Hunt, HoldCover, Retreat, Patrol) for kill/hit/got-hit/closing/cover/new-area/too-close. The columns are the `ModeRewardTable` POCO; `RewardComputer.StepReward` consumes a `StepRewardInput`. Changing what combat pays for means retraining.
