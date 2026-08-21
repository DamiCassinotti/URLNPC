# URLNPC Thesis — ML Layer Plan (LLM + RL hybrid NPC)

> **Recovered 2026-07-09.** The original file was lost. Reconstructed from this session's transcript (where it was first written), the project memory files, and the current repo/issue state. The design content below is the approved original; **status annotations and the as-built values were added during recovery** so the document reflects reality rather than the state at approval time. Anything marked *(as-built)* was verified against the repo today.

## Context

The thesis ("Comportamiento de NPC mediante Reinforcement Learning", FIUBA, tutor Dr. Hernán Merlino) proposes a **hybrid NPC architecture** for a 1v1 Unity FPS:
- **High level:** an LLM selects the NPC's *behavior mode* (chase, seek cover, flee, explore) from the overall match state.
- **Low level:** an RL agent executes concrete low-latency combat actions *within* the commanded mode.
- **Evaluation:** two-stage — RL judged on combat performance; LLM judged on situational coherence + perceived humanness by real players. Training must be automated agent-vs-agent.

**Decisions locked in with Damián (still binding):**
- The NPC does **not** know the player's HP — ever.
- The NPC only knows the player's position/distance **while it can see them**; otherwise it works from memory (last-seen position).
- The "Player" side must be interchangeable: Human (StarterAssets controller) or Agent (policy-driven), on the same body.
- Game code is *not* frozen — changes that simplify the ML phases are welcome.
- Round timeout = **draw** with a small negative terminal reward for both sides (no forced winner).

---

## 0. Status overview (added at recovery)

| Phase | Status |
|---|---|
| Foundations (Quick TODOs, §6) — issues #9–#16 | ✅ **All closed** (2026-08-06) |
| RL infrastructure — issues #40–#48 (modes, primitives, frozen brain spec, reward table, compliance tracking, headless build, train scripts, runbook) | ✅ **All closed** |
| Combat/arena bug fixes found while training — #56, #71–#74, #79, #80, #82 | ✅ Closed |
| Training runs — #49 (slice gate), #50 (full 4-mode compliance gate) | 🟡 **Open.** Runs `slice-01…04`, `full-01…04` executed; gates not yet met |
| Self-play run + ELO gate — #51 | 🟡 Open (config `config/URLNPC-selfplay.yaml` exists, run not done) |
| Headless eval harness `scripts/eval.sh` — #52 | 🟡 Open |
| **LLM layer** (`GameStateSnapshot`, `IModeSelector`, Ollama selector, baselines) | 🔴 **Not started, no issues filed** — the remaining frontier |
| Real-time A/B, scenario battery, human study | 🔴 Not started |

**Where the effort actually sits now:** the RL executor and all of its plumbing exist and train end-to-end; what's unfinished is (a) getting a policy that *passes the compliance gate*, and (b) the entire LLM tier. Current blocker on (a) is reward balance, not engineering — see memory `rl-run-observations`.

---

## 1. Glossary

| Term | Meaning |
|---|---|
| **RL** | Reinforcement Learning — learning by trial-and-error from rewards |
| **PPO** | Proximal Policy Optimization — the RL training algorithm ML-Agents uses (stable, industry default) |
| **SAC / POCA** | Alternative ML-Agents algorithms (Soft Actor-Critic; Posthumous Credit Assignment for teams) |
| **Policy / brain / model** | The trained neural network mapping observations → actions |
| **ONNX** | Open Neural Network Exchange — the file format the trained policy is exported to and that Unity loads for in-game inference |
| **Inference** | Running a trained model (no learning), in-game via Unity Inference Engine |
| **ELO** | Chess-style skill rating; ML-Agents computes it automatically during self-play to show the agent is genuinely improving |
| **Self-play** | Training where the agent's opponent is (a past copy of) itself |
| **Observation (obs)** | The numeric inputs the policy sees each decision step |
| **Discrete branch** | One independent action "slot" in ML-Agents; each step the policy picks one value per branch (§3.2) |
| **One-hot** | Encoding a category as a 0/1 vector, e.g. mode 2 of 4 → `[0,0,1,0]` |
| **Reward shaping** | Auxiliary rewards that guide learning toward desired behavior |
| **Entropy** | How random the policy still is; falling entropy = the policy is committing to choices |
| **LLM** | Large Language Model (Claude, GPT, Llama, Qwen…) |
| **GGUF** | Quantized model file format used by llama.cpp/Ollama for running LLMs locally |
| **Ollama** | Local server that runs open LLMs behind a simple HTTP API |
| **Temp 0 / seed** | LLM sampling settings that make outputs (near-)deterministic for reproducibility |
| **FSM** | Finite State Machine — hand-written if/else mode logic; our non-LLM baseline |
| **LOS** | Line of sight |
| **FOV** | Field of view |
| **NavMesh** | Unity's walkable-surface graph used for pathfinding |
| **TensorBoard** | Dashboard that plots training curves from ML-Agents runs |
| **Likert** | 1–7 agreement scale used in questionnaires |
| **Latin square** | Counter-balanced ordering of study conditions to cancel learning effects |
| **BotPrize** | 2008–2012 competition where judges rated Unreal Tournament bots' "humanness" — template for our study |

---

## 2. Library choices

### 2a. RL library — **decided: keep ML-Agents** ✅ *(as-built)*

| Option | Pros | Cons |
|---|---|---|
| **A. Keep ML-Agents 3.0.0 (embedded)** ✅ | Already integrated & patched; PPO + built-in **self-play with ELO** (exactly what the thesis promises); ONNX in-engine inference; thesis refs [15],[16] already benchmark it — academically defensible | Unity maintenance has slowed; local Inference Engine patch must be maintained; limited algorithm menu |
| B. ML-Agents env + Stable-Baselines3 via `mlagents_envs` Gym wrapper | More algorithms, better experiment tooling | Lose built-in self-play/ELO; two stacks to debug |
| C. ViZDoom / Godot RL / AMD Schola (Unreal) | — | Wrong engine — discards the finished game. Reject |
| D. Custom socket bridge to PyTorch | Total freedom | Reinvents ML-Agents. Reject |

Fall back to B (it sits *on top of* ML-Agents, nothing lost) only if PPO stalls irrecoverably.

### 2b. LLM integration — **decided: Ollama behind an interface** (not yet built)

| Option | Pros | Cons |
|---|---|---|
| A. LLMUnity (llama.cpp in-process, GGUF) | Free, offline, ships with the game, fixed-seed reproducibility | Small-model reasoning; competes with Unity for GPU; package risk |
| **B. Local HTTP server (Ollama) + `UnityWebRequest`** ✅ | Decoupled; **swap any model by name** (3B ↔ 8B ↔ cloud) behind one code path; JSON-schema constrained outputs; temp 0 + seed; free | One extra process; local hardware bound |
| C. Cloud API (Claude/GPT) | Best reasoning; strongest coherence | Latency 0.5–3 s, cost, network, weaker reproducibility |
| D. LLM inside Unity Inference Engine | — | Impractical today. Reject |

**B primary, C reachable through the same seam:**

```csharp
public interface IModeSelector {          // async, never blocks a frame
    Task<NpcMode> SelectModeAsync(GameStateSnapshot s);
}
```

Implementations: `LlmModeSelector` (Ollama or cloud), `HeuristicModeSelector` (FSM), `RandomModeSelector`, `FixedModeSelector`. The last three are **fallbacks and experimental baselines at once** — this one interface funds the entire ablation study. Comparing local-3B vs 8B vs cloud on the same battery becomes a thesis results table for free.

---

## 3. ML modelling

### 3.1 Sensory contract & game state

**Sensory contract (locked):** the NPC's brain (LLM *and* RL) may only use its own state, what it currently sees, and its own memory of what it saw. No player HP, no through-wall distance. Enforced in **one place**.

*(as-built)* `PerceptionMemory.cs` owns `CurrentlyVisible` / `LastSeenPosition` / `TimeSinceSeen` with a 10 s horizon, and is the only source of target info for the brain — every movement primitive navigates relative to `LastSeenPosition` and `Attack()` aims at it (firing gated on `SeenWithin(FiringGraceSeconds)` = 1 s). Environment code (spawn separation, reward computation, the sight check itself) may still read true state; that's standard "privileged critic/environment" and only *policy inputs* are restricted — **say exactly that in the thesis, reviewers will ask.**

**Tier 1 — `GameStateSnapshot` → LLM** (every 3–5 s or on events: took damage, LOS gained/lost, own-HP threshold crossed, clock milestones). Compact JSON prompt; response constrained to `{"mode": ..., "reason": ...}`.

- Own HP %
- `targetVisible`; if visible: distance bucket (near/mid/far); if not: last-seen distance bucket + `secondsSinceSeen`
- Took-damage-recently + coarse direction (front/back/left/right) — you know where you were hit from even without seeing the shooter; keeps `Retreat`/`HoldCover` sensible when blind
- Round time remaining *(as-built: `GameManager.RemainingRoundTime` already exposes this)*, current score
- Arena descriptor: layout index, cover density
- Rolling observed-player-style stats **computed only from visible intervals**: avg engagement distance when seen, shots heard per 10 s, observed avg speed — the "adapts to the player" selling point, kept honest
- Current mode + time in mode (prevents thrashing)

**Deliberately excluded:** player HP (contract) and **weapon cooldown** — cooldown is 0.5 s, the LLM period is 3–5 s, so it's stale before the answer arrives and is execution-level information anyway. General rule worth stating in the thesis: *the LLM tier only receives state whose timescale is ≥ its own decision period.*

**Tier 2 — RL observation vector.** Plan called for ~16 floats; **as-built the frozen spec is 18** (`NpcBrainSpec.cs`, `NpcObservations.cs`), slot by slot: 0 canAttack, 1 cooldown remaining, 2 target visible, 3 distance to *perceived* position / sightRange, 4–5 bearing sin/cos, 6 time since seen / 10 s, 7 own HP, 8 recently damaged, 9–12 hit-direction one-hot, 13–16 commanded-mode one-hot, 17 own speed. Never-seen is a single fixed point (distance 1, staleness 1, no bearing) rather than whatever a zeroed position would imply.

*Ray sensors:* the plan said "start without a `RayPerceptionSensor3D`, add only if cover behavior fails." **As-built the prefab's existing ray sensor was deliberately removed (#43)** — it fed live target detections outside `PerceptionMemory` (contract leak) and outside the declared observation width, and would have given the agent-driven player a different obs shape under the same behavior name. If ever re-added, it must go on **both** sides.

### 3.2 Enemy action space — **Option B, 2 discrete branches** ✅ *(as-built)*

| Option | Description | Verdict |
|---|---|---|
| A. Keep the old 3 macro-actions (Patrol/Chase/Attack) | Trivial training | **Redundant with the LLM's modes** — both layers would decide the same thing |
| **B. Tactical primitives, 2 discrete branches** ✅ | Detail below | Learnable (NavMesh does pathing), varied tactics, clean layer separation |
| C. Low-level continuous control | Highest humanlike ceiling | Aim-from-scratch blows the budget; future work |

"Branches" are independent action slots decided *simultaneously* — every decision step (~10 Hz) the policy outputs one choice from **each** branch, so it's movement **and** fire each step, not either/or.

- **Branch 0 — movement (7):** `Hold`, `Advance`, `Retreat`, `StrafeLeft`, `StrafeRight`, `MoveToCover`, `Wander` *(as-built: `MovementAction` enum; declaration order **is** the action index order, so reordering invalidates trained models)*
- **Branch 1 — fire (2):** `DontFire` / `Fire` (respects cooldown; firing while the target isn't visible incurs `wastedShotPenalty`)

14 combos/step. Emergent tactics: `StrafeLeft+Fire` = circle-strafing; `Retreat+Fire` = fighting withdrawal; `MoveToCover+DontFire` = disengage. `Attack()`'s auto-aim stays — defensible as "the RL decides *when* to fire; aiming is a game mechanic" — but aims only at the *perceived* position.

### 3.3 LLM/RL coupling — how many models, and what the LLM actually changes

**One RL model.** One ONNX file, one training run — not one per mode.

| Option | Pros | Cons |
|---|---|---|
| **A. Single mode-conditioned policy** ✅ | One training run; skills shared across modes; LLM switches behavior by flipping an observation; standard goal-conditioned RL, easy to cite | Per-mode reward table needs care (Risk 4) |
| B. One policy per mode (4 ONNX, swapped at runtime) | Simple rewards, clean attribution | 4× training + tuning; runtime swapping awkward. **Fallback** if mode interference appears |
| C. LLM emits continuous style params | Richer surface | Compliance unmeasurable; hard to defend |

**How the mode influences the RL agent — two channels, one per phase.** The mode one-hot is a permanent obs slot present in **both** phases; what changes is only *who writes it*:

1. **Training (LLM absent):** a scripted `ModeDirector` writes the slot. The mode (a) appears in the observation vector and (b) selects which **reward-table column** is live. The policy is thereby *taught* "when obs says Retreat, opening distance pays; when obs says Hunt, closing and hitting pays."
2. **Inference (LLM active):** **rewards no longer exist** — nothing is training. The LLM simply replaces the `ModeDirector` as the writer of that same already-trained slot. The policy is agnostic to who writes it.

**"What is the goal of the LLM if it's only at inference?"** — *Inference is the product.* Every real match (playtests, automated A/B, human study, the defense demo) runs at inference. Division of labor:

- **RL training** = *skill acquisition*: how to execute each mode competently. Done once, offline.
- **LLM at play time** = *live strategic judgment*: which mode fits *this* moment against *this* player. The thesis's adaptation claim lives entirely here.

Football analogy: the player runs drills for months (RL training); the coach calls plays during the match (LLM). The coach adds nothing to the drills — his judgment matters only when there's a real opponent.

**What the LLM receives per call:** (1) static system prompt — role/personality, the **mode catalog** with one line on when each applies, and the JSON output contract; (2) the current `GameStateSnapshot`; (3) a short history of the last ~3 snapshots + chosen modes, so it reasons about trends ("player keeps closing despite my retreats") — this history is what makes style adaptation possible. ≈400–700 tokens/call, fine for a local 3B–8B at 3–5 s cadence.

**Could we train at 1× with the LLM in the loop?** Tempting symmetry, but worse on every axis — not just slower:

| Axis | LLM-in-training @1× | ModeDirector @20× |
|---|---|---|
| Wall-clock per 1M-step run | ~28 h + LLM stalls (~100 k calls) → days | ~1.5–2 h |
| Reward-tuning iterations affordable (the real bottleneck) | 2–3 | 15–20 |
| Mode coverage | **Biased** — the LLM only picks "sensible" modes, so rare-but-needed conditioning (Retreat while healthy) is undertrained; the policy gets *worse* at obeying unusual commands | Uniform over all modes × situations |
| Reproducibility | Non-deterministic external dependency inside every run | Seeded, replayable |
| What the RL gains | Nothing — the executor's job is obeying *any* commanded mode; goal-conditioned RL wants broad goal coverage, not realistic goal frequency | — |

**Decided: no.** Clean middle path if you later want realistic mode frequencies: run pilot matches with the LLM, **log its mode-transition statistics, and have `ModeDirector` sample from that fitted distribution** (mixed ~50/50 with uniform to preserve coverage). LLM realism, 20× speed, full reproducibility — and a good methodology paragraph.

**Mode set (4):** `Hunt`, `HoldCover`, `Retreat`, `Patrol`.

**Per-mode reward table** (training only). Design held; values below are *(as-built, post-#79/#80 rebalance)*:

| Event | Hunt | HoldCover | Retreat | Patrol |
|---|---|---|---|---|
| hit target | +0.5 | +0.5 | +0.1 | +0.1 |
| got hit | −0.3 | −0.6 | −0.6 | −0.5 |
| closing per metre | +0.01 | 0 | −0.01 | 0 |
| in cover, per step | 0 | +0.005 | +0.002 | 0 |
| new area visited | 0 | 0 | 0 | +0.002 |
| too close (<6 m), per step | **0** | −0.004 | −0.008 | −0.002 |

Global rows *(as-built)*: alive **+0.0002**/step, kill **+1.0**, died **−1.0**, wasted shot **−0.05**, timeout **−0.6**. Hunt's too-close column is 0 deliberately: as one global row the penalty taxed the one mode whose job is to close (#79/#80).

### 3.4 Interchangeable Player: Human or Agent ✅ *(as-built)*

One body (capsule + `Health` + weapon + camera anchor), swappable driver. *(as-built: `CombatantRig.cs` + `DriverSelector.cs`; priority `-playerDriver <human|agent>` CLI > static override > Inspector. The Agent driver disables the input components and runtime-composes the enemy combat stack onto the body with `PlayerAgent`.)*

| Player driver | Enemy driver | Used for |
|---|---|---|
| Agent (learning) | Agent (learning, mirrored) | **Self-play training** (20×, ModeDirector) |
| Agent (frozen policy or scripted) | Trained policy + `IModeSelector` | **Automated evaluation**, incl. real-time LLM A/B (§3.5) |
| Human | Trained policy + `IModeSelector` | Human study + playtesting |

### 3.5 Real-time self-play as the LLM test harness (1:1)

Before any human plays, run **real-time (1×) agent-vs-agent matches**: Enemy = trained policy + live `IModeSelector`; Player side = frozen policy + FSM selector (stationary, fair opponent). Cheapest end-to-end test of the LLM layer:

- **A/B at scale, no humans:** 100 matches LLM-selector vs 100 FSM-selector overnight; compare win rate, mode-switch patterns, compliance, latency — the LLM's *marginal contribution*, measured objectively before the subjective study.
- **Integration soak test:** surfaces what training can't — timeouts, malformed JSON, mode thrashing, async races — under true latency.
- **Qualitative material:** record matches (video + prompt/response logs side by side). "At 0:42 HP dropped below 20% and the LLM switched to Retreat *because* (logged reason)" is strong defense material.
- **Constraint:** runs at 1× only (the LLM can't be time-scaled). ~100 matches × ~1 min ≈ 2 h per condition — fine overnight, but it can't be iterated fast. Keep the scenario battery (§4.2) as the fast inner loop and this as the slow outer loop.

---

## 4. Testing & analysis

### 4.1 RL component (objective, automated)

- **During training:** TensorBoard — cumulative reward, episode length, ELO (self-play), policy entropy. Entropy collapse = premature convergence; entropy *plateau above max−0.2* = reward signal too weak (this is exactly what runs full-01…03 showed).
- **Post-training eval harness** *(#52, still open)*: headless Unity `-batchmode`, ≥100 episodes/condition, seeded arenas. Per-episode telemetry: winner, damage dealt/taken, accuracy, time-to-kill, survival time, per-mode step counts, distance timeline.
- **Mode-compliance ratio** — the metric bridging both layers and the strongest defensible number *(as-built: `ModeComplianceTracker`, logged to `Compliance/<Mode>` + telemetry)*. Hunt: closes distance, lands a shot, or holds inside 20 m of a visible player without backing off. Retreat: opens distance or keeps the eye-line broken. HoldCover: keeps the eye-line broken, scored only while the player is known about. Patrol: walks through ground it hasn't covered this episode. One caveat to report: the closing rules use a deadband so jitter counts as neither. The rules are calibrated against controls, not intuition — a rule a random walk scores as well as the scripted bot measures the arena rather than the policy, which is what #105 found and reworked.

### 4.2 LLM component (fast inner loop, offline)

- **Scenario battery:** 30–50 curated `GameStateSnapshot`s with expert-labeled acceptable mode(s) (e.g. "HP 15%, player at 8 m, cover nearby → Retreat or HoldCover"). Measure accuracy vs labels, self-consistency (k repeats at temp 0 and 0.7), latency distribution, invalid-output rate. Run across model sizes (3B/8B/cloud), prompt variants, and the FSM/random baselines.
- Log every prompt/response during play for post-hoc audit — this is the qualitative-analysis chapter's raw material.

### 4.3 Real-time agent-vs-agent A/B (§3.5) — objective LLM-vs-FSM comparison at scale, before humans.

### 4.4 Humanness study (headline evaluation)

- BotPrize-style blinded study: 12–20 participants, each plays vs each condition, Latin-square order. Conditions: **(1) LLM+RL, (2) FSM+RL, (3) Random-modes+RL**, optionally (4) classic scripted bot. Without a human-vs-human condition this measures *relative* humanness between architectures — cheaper, and it still answers the research question ("does the LLM layer add perceived humanness over an FSM?").
- Per round: Likert 1–7 humanness / difficulty / enjoyment + binary "human or bot?" + free text; record participant FPS experience.
- **Analysis:** within-subject non-parametric — Friedman across conditions, Wilcoxon signed-rank pairwise with Holm correction, effect sizes (r) alongside p-values. Correlate subjective ratings with telemetry (mode-switch frequency, accuracy) to explain *why* a condition felt human.

### 4.5 What "good" looks like

1. **RL:** rising reward + ELO; beats the scripted bot ≥70% (full-07 is at 87.5%); per-mode compliance thresholds (#50) — **pending re-measurement.** #91's numbers were calibrated on rules #105 replaced: the random control read Hunt 76% / HoldCover 70% / Retreat 12% / Patrol 0.4% against the heuristic bot's 99 / 63 / 6 / 0.6, so only Hunt and Retreat separated at all and HoldCover ran backwards. One number across all four modes never meant anything — the ceilings differ. Re-run the control batch (`--subject random|heuristic|flee`, 200 forced-mode episodes per mode) under the new rules and set each gate clear of its own controls before the gate run.
2. **LLM:** ≥ FSM accuracy on the battery; ~100% valid JSON; p95 latency < mode period (≤3 s).
3. **Joint:** LLM+RL rated above random-modes (sanity floor) and measurably different from FSM (the research question — a **null result here is still a valid thesis finding** if the pipeline is sound; say so explicitly in the writeup).

---

## 5. Risks

1. **LLM in the training loop** — schedule killer. Designed out via `ModeDirector` (§3.3). ✅ avoided
2. **LLM latency at play time** — async only, min dwell time, timeout → keep current mode or fall back to FSM. Never block a frame.
3. **LLM non-determinism / malformed output** — temp 0 + seed, JSON-schema constrained decoding, retry-once-then-fallback, log every call.
4. **Reward interference in the conditioned policy** — global shaping fights specific modes. **This risk materialized twice** (#79 stall-to-draw: alive reward out-paid winning; #80 global too-close penalty taxed Hunt). Both fixed by moving shaping per-mode. Watch for the next instance.
5. **Self-play exploit collapse** (thesis ref [11]) — opponent pool of past checkpoints (`save_steps`/`swap_steps`/`play_against_latest_model_ratio`), periodic eval vs a fixed scripted bot to catch degenerate strategies.
6. **Slice runs mislead** *(learned the hard way)*: slice-02 looked decisive → full-02 stalled; slice-03 looked passive → full-03 was decisive. A complete inversion. **Judge on full runs only**; treat 50 k slices as smoke tests for plumbing, not behavior.
7. **Sensory-contract leaks** — safe only because enforcement is centralized in `PerceptionMemory`; any direct `target.position` read from brain code is a leak reviewers can find. The removed ray sensor was exactly this.
8. **Eval reproducibility vs arena randomness** — seed everything *(as-built: `RunRng` with per-domain sub-streams; seed logged and recorded as the `Run/Seed` stat)*; report per-arena breakdowns.
9. **n=1 run conclusions** — full-03's success is a single seed and could be luck; confirm reward changes with the 5-seed batch (`scripts/train-seeds.sh`) before writing them up.
10. **Human-study validity** — small n, ordering effects, expertise variance; check FIUBA requirements for studies with human participants; pre-register the questionnaire.
11. **Version fragility** — the embedded ML-Agents + Inference Engine patch is bespoke; pin Unity/package/Python versions in a thesis appendix or the jury can't reproduce anything.
12. **Frozen-interface debt** — changing the obs layout, the `MovementAction` order, the `NpcMode` order, or `CombatBalance.SightRange` invalidates **every** trained model. Any such change means rerunning the whole training campaign.

---

## 6. De-risking playbook

1. **Vertical slice first.** *(Done — with a deliberate deviation: the slice shipped the **final** 18-float obs vector and 7×2 branches, cutting only the mode set to Hunt/Retreat, so slice→full became a values-only change and the slice runs weren't invalidated by a later interface change. Keep this discipline.)*
2. **Freeze interfaces, iterate values.** Locked in `NpcBrainSpec`. Afterwards tune only reward numbers and prompts.
3. **Baselines before the star.** Get FSM-selector + trained policy working end-to-end *before* the first LLM call. The LLM then drops into a proven pipeline, and you always have a demo that works.
4. **Smoke-test protocol per run:** short run → check reward slope, entropy, one watched episode — before committing to a 1M-step run. But per Risk 6, don't read *behavior* off a slice.
5. **Milestone gates:** (a) slice trains → (b) 4-mode policy complies under the scripted director → (c) FSM selector plays a full match → (d) LLM selector passes the battery → (e) real-time A/B → (f) human study. Each gate is also a thesis-chapter artifact, so partial completion still yields writable results.
6. **Log from day one.** *(Done: `TelemetryLogger`, one JSON line per event, works in batchmode.)*
7. **Pilot everything human:** 1 pilot participant + 1 full dry run before recruiting; questionnaire bugs are unfixable after collection starts.
8. **Keep a known-good checkpoint chain:** archive every ONNX + config + git SHA together; the thesis must answer "which model produced figure X" a year later.
9. **Manual play validation** *(still pending as of 2026-08-17)* — watch the trained policy with your own eyes each milestone; TensorBoard curves hide behaviors like camping that are obvious in 30 seconds of play.

---

## 7. Quick TODOs — foundations (historical; all closed 2026-08-06)

Filed as GitHub issues #9–#16, label `ml-layer`:

| # | Item | Delivered as |
|---|---|---|
| 9 | `PerceptionMemory` enforcing the sensory contract | `PerceptionMemory.cs` + `PerceptionState` POCO |
| 10 | `CombatantRig` with Human/Agent drivers | `CombatantRig.cs`, `DriverSelector.cs`, `PlayerAgent.cs` |
| 11 | Round timer; timeout = draw + negative reward | `RoundClock` POCO + `GameManager` |
| 12 | Telemetry event bus | `TelemetryLogger.cs`, `EpisodeLog`, `JsonLine` |
| 13 | Seedable RNG | `RunRng.cs` (per-domain sub-streams) |
| 14 | `ArenaManager.NearestCoverPoint` | `ArenaManager` + `EyeLine.cs` |
| 15 | Player-reset gap on episode begin | `EnemyAgent.OnEpisodeBegin` repositions both |
| 16 | Doc drift (`max_steps`) | Docs aligned |

---

## 8. Remaining sequence

1. **Close the RL gates (#49, #50).** Confirm the #79/#80 rebalance across the 5-seed batch rather than n=1; if Hunt compliance stays low, apply the secondary boosts (kill +1→+2, `closingRewardPerMeterByMode[Hunt]` 0.01→0.03) and re-measure. **Establish a measured compliance baseline and recalibrate the ">80%" target** (§4.5). Validate by watching real play, not only TensorBoard.
2. **Self-play run + ELO gate (#51)** using `config/URLNPC-selfplay.yaml` — both bodies already command modes, so the shared policy trains symmetrically.
3. **Eval harness (#52):** `scripts/eval.sh`, ≥100 seeded headless episodes per condition, telemetry → summary tables.
4. **LLM layer — file issues first, none exist yet:** `GameStateSnapshot` builder (§3.1), `IModeSelector` + FSM/random/fixed baselines, then `LlmModeSelector` (Ollama HTTP, JSON-schema output, async with dwell time and fallback).
5. **Evaluation campaign:** scenario battery → real-time A/B batches → human-study protocol + pilot → study.

**Verification per step:** full (not slice) run shows mode-dependent behavior and rising compliance; self-play shows a rising ELO curve; eval harness reproduces identical results for a fixed seed; FSM selector plays a clean full match before any LLM call; LLM battery passes latency and validity bars; pilot study completes without protocol changes.
