# Training runbook

The end-to-end sequence for running a training session and checking it did what you
wanted: build the standalone player, run the slice, read the result in TensorBoard, then
load the `.onnx` back for inference, force a mode to confirm behavior changes, and run
self-play.

Background on each system is in [`../README.md`](../README.md) (setup, reward shape, mode
compliance) and [`../CLAUDE.md`](../CLAUDE.md) (architecture). This file is the checklist.

## 0. Prerequisites

- Unity 6000.0 LTS installed, project opening cleanly.
- Python venv from `requirements.txt` (see README → *Training the agent → One-time setup*).
- Enemy prefab shape verified once (README → *Required Enemy prefab setup*): 18 obs, discrete
  branches `7 × 2`, no ray sensor.

## 1. Build the standalone player

You can press Play in the editor to feed the trainer, but a headless build runs faster,
unattended, and in parallel. Close the editor first (single instance per project):

```bash
scripts/build-player.sh
```

Writes `Builds/Linux/URLNPC.x86_64` (gitignored); build log at `Builds/build.log`. Rebuild
after any script or prefab change you want the trainer to see — the player is a frozen
snapshot, not live like editor Play.

## 2. Run the slice

`scripts/train.sh` is the one entry point for both training paths; it wraps `mlagents-learn`
so you don't assemble the `--env` / `--env-args` line by hand:

```bash
scripts/train.sh editor <run-id> [config] [extra mlagents args...]
scripts/train.sh build  <run-id> [config] [--num-envs N] [--seed S]
                       [--train-modes all|<Mode>[,<Mode>...]]
                       [--heuristic-opponent F] [extra...]
```

- **`editor`** starts the trainer and waits for you to press Play — for watching behavior and
  quick checks.
- **`build`** runs against the standalone player from step 1, and always drives the player
  side with the shared agent policy (`-playerDriver agent`) so both bodies fight a live
  opponent.
- `config` defaults to `config/URLNPC.yaml`. `--resume` / `--force` and any other flag pass
  straight through to `mlagents-learn`.

The slice is the short early run: `config/URLNPC-slice.yaml` (50k steps, `summary_freq` 2000),
enough to see reward move and the compliance columns appear before the full 1M-step run.
Training commands all four modes by default — the set is owned by code (`NpcModes.AllMask`),
so the prefab, the scene's instance override and `CombatantRig`'s composed director can't
train different pools; `ModeDirector.enabledModes` only narrows it outside training. To train
a restricted pool, pass `--train-modes` on a `build` run (`--train-modes Hunt`,
`--train-modes Hunt,Retreat`, `--train-modes all`): it becomes `-trainModes` in the env-args,
so the one argument reaches both self-play bodies and the pool stays symmetric. Smoke the
slice in the editor first:

```bash
scripts/train.sh editor slice-01 config/URLNPC-slice.yaml
```

The three configs:

| Config | Steps | Use |
|---|---|---|
| `config/URLNPC-slice.yaml` | 50k | slice / smoke run |
| `config/URLNPC.yaml` | 1M | full run |
| `config/URLNPC-selfplay.yaml` | 1M | full run plus the self-play block (see step 6) |

For a longer unattended run, point `build` at the standalone player with parallel envs and a
fixed seed for reproducibility (`--seed` becomes `-runSeed` in the build's env-args; README →
*Reproducible evaluation runs*):

```bash
scripts/train.sh build slice-02 config/URLNPC-slice.yaml --num-envs=4 --seed=12345
```

Results land in `results/<run-id>/`; reuse a `<run-id>` only with `--resume` (continue) or
`--force` (overwrite). Trained model at `results/<run-id>/URLNPC.onnx`; the slice checkpoints
every 25k steps, the full run every 50k (`checkpoint_interval`).

Only three of the files a run leaves behind are version-controlled: `configuration.yaml`, the
final `URLNPC.onnx` and the TensorBoard `events.out.tfevents.*`. The checkpoints under
`URLNPC/` and `run_logs/` are gitignored — they stay on disk, where `--resume` needs them, but
they don't enter the repo.

## 3. What to look for in TensorBoard

```bash
tensorboard --logdir results
```

- **`Environment/Cumulative Reward`** — the headline. Should trend up and stabilise; a flat
  line near zero means the policy isn't finding the kill/hit rewards.
- **`Compliance/Hunt`, `Compliance/HoldCover`, `Compliance/Retreat`, `Compliance/Patrol`** —
  does the policy obey the commanded mode (Hunt closes, shoots or holds inside 20 m of a
  visible player; Retreat opens distance or stays out of the player's eye-line)? These are the
  point of the mode-conditioning: reward can climb while compliance stays flat if the policy
  ignores the command and just fights. The denominator is the steps the mode could act on —
  Hunt only while the player is visible, Retreat while there is contact to break — so this
  reads as policy quality, not as how often the fight was joined. Patrol's rate reads low by
  construction (a cell counts once) — compare between runs, not as a percentage. As of Run 12
  this is a **secondary** signal: see §8 for what to report mode differentiation on.
- **`Visible/<Mode>`** — the fraction of each mode's steps the player was in sight for, i.e.
  how much of the round Hunt's and Retreat's rates were scored on. A mode that never saw the
  player logs no compliance rate that episode, and shows up here as a flat zero instead.
- **`Losses/Policy Loss`, `Losses/Value Loss`** — should settle, not diverge.
- **`Run/Seed`** — confirms which seed each episode ran under.

Cross-check against the telemetry log (`persistentDataPath/Telemetry/session_*.jsonl`, path
printed at startup): `episode_summary` for win/damage/accuracy, `mode_compliance` and
`mode_change` for the mode timeline.

## 4. Load an `.onnx` for inference

To watch a trained model play instead of training:

1. In the editor, select `Assets/Prefabs/Characters/Enemy.prefab`.
2. Drag `results/<run-id>/URLNPC.onnx` into **Behavior Parameters → Model**.
3. Set **Behavior Parameters → Behavior Type = Inference Only**.
4. Open `Assets/Scenes/FPS.unity` and press Play — no trainer needed.

At inference the `ModeDirector` stands down (`trainingOnly` on, no communicator), so the
channel reports `initialMode` until the LLM selector commands one. To drive modes by hand
without the selector, use the next step.

## 5. Force a mode and confirm it changes behavior

`ModeDirector` on the Enemy pins one mode for inspection:

1. On the Enemy's `ModeDirector`, tick **Use Forced Mode** and pick **Forced Mode**.
2. Untick **Training Only** — outside training the director won't write the channel
   otherwise.
3. Press Play and watch: **Hunt** should close and shoot; **Retreat** should open distance;
   **HoldCover** should break the player's line of sight; **Patrol** should roam.

Confirm from data, not just eyeballing: the `mode_change` telemetry line shows the commanded
mode, and per-mode `Compliance/*` (in a training run) or the `mode_compliance` summary line
tells you the forced mode actually drove the expected behavior. Re-tick **Training Only**
before your next training run so the director resumes sampling.

## 6. Self-play

The player body can be driven by the same policy as the enemy, so both sides train against a
live opponent instead of a static target. `train.sh build` already drives the player with the
agent policy, so any `build` run has both bodies fighting. Add the `config/URLNPC-selfplay.yaml`
config to also turn on ML-Agents' self-play block (snapshot opponents, team swaps, ELO):

```bash
scripts/train.sh build selfplay-01 config/URLNPC-selfplay.yaml --num-envs=4
```

The agent driver defaults to behavior name `URLNPC` / team id 1 — same behavior name as the
enemy, so both sides share one policy. In the editor, set **Driver = Agent** on `CombatantRig`
(or `CombatantRig.DriverOverride` from code); `editor` runs don't pass `-playerDriver`. Both
bodies carry a `ModeDirector`, so each is independently commanded during the run. Give the
agent driver a different behavior name to train a separate player policy against the enemy.

Pure self-play alone is not enough: `selfplay-02` scored ~50/50 against itself and ~1283 ELO,
then lost ~2:1 to the scripted heuristic in every mode including Hunt (#109) — two mutually
weak agents, neither aggressive enough to teach the other to punish aggression. Mix the
heuristic in for a share of the episodes so the policy also has a competent aggressor to beat:

```bash
scripts/train.sh build selfplay-03 config/URLNPC-selfplay.yaml --num-envs=4 \
    --heuristic-opponent 0.3
```

It becomes `-heuristicOpponent` in the env-args and puts the **player** side (the opponent;
the enemy is what the eval scores) on `BehaviorType.HeuristicOnly` for that fraction of
episodes, spread evenly and swapped only between episodes. Those episodes train the enemy
alone — a heuristic body sends the trainer nothing. `Run/HeuristicOpponent` in TensorBoard
averages to the realized fraction. The bot ignores the commanded mode, so on those episodes
the player body reports no `Compliance/*`, `Visible/*` or `mode_change` rows at all — the
per-mode numbers stay about bodies the mode actually drove. Read the result off `eval.sh --opponent heuristic`, not
ELO, which is self-referential: the gate is beating the heuristic in Hunt.

## 7. Score a model headlessly

`scripts/eval.sh` plays a fixed number of rounds against the standalone build with no trainer
attached and summarizes the telemetry:

```bash
scripts/eval.sh results/full-03-seed-1001/URLNPC.onnx --episodes 100 --seed 1001 \
    --opponent heuristic
```

Output lands in `results/eval/<model>_<subject>-vs-<opponent>_<stamp>/`: `config.json` (what was
scored), the run's `telemetry.jsonl`, and `summary.txt`/`summary.json` — win/loss/draw, damage
dealt and taken, accuracy, time to kill, survival time, and per-mode compliance, visibility and
step counts. The player's `unity.log` sits alongside them and is gitignored — `eval.sh` reads the
telemetry path out of it and it is what a failed run leaves to diagnose, but it is local only.

- `--opponent policy` keeps both sides on the model (self-play); `--opponent heuristic` puts
  the far side on the scripted heuristic, which is what the ≥70% win-rate gate is measured on.
- `--subject` picks what drives the NPC side — the side the summary scores. `policy` (default)
  is the model; `heuristic`, `random` and `flee` are the controls its numbers are read against:
  the scripted baseline, uniform draws over the 7x2 action branches, and a scripted retreat
  that backs off, takes cover and never fires. A model is still required either way (it is what
  the build carries); nothing on the NPC side runs it. A compliance rule is only worth reading
  if the controls separate on it — `heuristic` should top Hunt, `flee` should top Retreat and
  HoldCover, and `random` should be under both. Worth running the control batch against
  `--opponent policy` as well: training is self-play, so that is the opponent the rates the
  gate is read on were produced against.
- `--modes scripted` (default) lets the `ModeDirector` sample as it does in training;
  `--modes Hunt` pins one mode for a per-mode baseline; `--modes none` leaves the channel on
  `initialMode`.
- `--seed` fixes arenas, spawns and the mode schedule. It does *not* make two runs identical:
  aim spread is deliberately unseeded (see `CLAUDE.md`), so the fights still diverge — read
  the numbers as an average over enough episodes, not as a replay.
- `--time-scale` is game time per rendered frame in physics steps; 1 is the most faithful.
  The run is never throttled to real time, so a 120 s round takes far less than that.

The model is copied into `Assets/Resources/EvalModels/` and the player rebuilt whenever it
changes: Inference Engine only imports ONNX in the editor, so the build has to carry it. Pass
`--no-build` to reuse a build that already does. `scripts/eval_summary.py` runs standalone on
any session JSONL, including one from a human-played round.

## 8. Reporting mode differentiation

**Report per-mode target-visible fraction and time-to-kill. Compliance is a diagnostic.**

Compliance saturated once the modes started working. Measured against a self-play opponent
that mostly stays hidden, the `--subject random` control reads ~80% on HoldCover and Retreat
— a random walk rarely sees anyone either, so "kept the eye-line broken" comes nearly free.
The rule still describes the right behavior, but with the floor that high there is no room
left between a random walk and a policy, which is the separation the number exists to show.
That is a property of the opponent, not a bug in the rule: the low-contact fights the policy
now produces are exactly the ones the eligible-steps denominator (§3) makes cheap to satisfy.

Visible% and TTK have no rule behind them to saturate — they are what the modes actually do
differently. Run 12 reads HoldCover 2.8% visible / 57 s TTK, Retreat 5.0% / 40 s, Hunt 10.6%
/ 17 s: one hides, one engages and breaks off, one rushes. Read them together with win rate,
so a mode that merely stopped fighting doesn't look like a working defensive mode.

How to report a run:

- Per mode: visible fraction, TTK, win/loss/draw. All of it comes out of
  `scripts/eval.sh --modes <Mode>` and the `summary.json` it writes.
- **Always state the opponent.** `--opponent policy` is the standard — training is self-play,
  so that is what the modes were shaped against; heuristic numbers are not comparable to it.
- Run `--subject random`, `flee` and `heuristic` as anchors on the same opponent. They are
  what says whether a spread between modes is real: random is the floor, flee the defensive
  ceiling, heuristic the aggressive one.
- Compliance and `Visible/<Mode>` in TensorBoard still tell you *why* a mode moved — keep
  reading them during a run, just don't gate on compliance.

## 9. Iterate the selector offline (the battery)

A live match costs about a minute and can't be time-scaled with an LLM in the loop, so
prompt iteration happens offline against a fixed set of states rather than in-game.

`battery/snapshots.json` is 86 `GameStateSnapshot`s harvested from real match telemetry
(`scripts/battery_harvest.py` pulls and dedupes them off the `mode_decision` lines, then
they are labelled by hand), each with the acceptable mode(s) for that state and a one-line
rationale. Acceptable is a *set*: "25% HP, target at mid, cover near" is defensibly Retreat
or HoldCover, and scoring it as one answer would punish the right one. Fourteen of the 86 are
marked `ambiguous` — kept apart because they have more than one defensible answer, so they
measure consistency, not accuracy. Coverage is by construction: every mode is the right
answer several times, plus the never-seen, just-lost-sight, low-HP with and without contact,
and clock-about-to-run-out cases.

```bash
scripts/battery.py --selector fsm         # the FSM baseline
scripts/battery.py --selector random      # the sanity floor
scripts/battery.py --selector llm --model llama3.1:8b --prompt v1 --repeats 5 --temps 0.0,0.7
```

It reports, per temperature: accuracy over the non-ambiguous snapshots, self-consistency
over K repeats, invalid-output rate and the latency distribution. The FSM scores 95.8% and
random ~35% — that gap is the check that the labels discriminate and aren't broken. The FSM
is not perfect by construction and must not be made so (#153): its three misses are where
its rule ordering is too coarse — it hunts at 40% HP because its low-HP threshold is 35, and
its damage rule fires ahead of its pursuit rule. A battery whose labels always contain the
FSM's answer would make "beat the FSM" mean "score 100%".

The `llm` selector sends the same prompt the game sends: `--prompt <id>` names a text asset
under `Assets/Resources/Prompts/`, which `battery.py` reads directly and `ModePromptLibrary`
loads at runtime — one text, so an offline comparison of two variants says something about
the in-game run. A battery snapshot stands alone, so the history section renders empty;
`--llm-prompt <id>` on `scripts/eval.sh` is the same knob for a live run, and every
`mode_decision` line records the id it used. The twin sends one attempt per call and no
retry: the game's retry is robustness, while the battery's invalid rate is about how often
the raw answer is usable.

**FSM and random run as Python twins, not the Unity build.** The battery has to run any
selector, including the two baselines. Driving the standalone build in `-batchmode` for each
would drag the arena, NavMesh and Academy into what is a pure function of the snapshot, and
couple offline prompt iteration to a rebuild. The baselines are a handful of rules
(`HeuristicModeSelector`, `RandomModeSelector`), so `scripts/battery.py` carries a direct
twin of each — `fsm_decide` mirrors `HeuristicModeSelector.Decide` rule for rule, thresholds
and all. Keep the twin in step if those rules change.

## 10. The few-shot exemplar bank and the zero-shot ablation

No weights move in the LLM tier, so the only thing there is to tune is what the model is
shown. `Assets/Resources/Exemplars/bank-v1.txt` is that artifact: twelve curated
state -> answer pairs, three per mode, each state a real snapshot harvested from match
telemetry and written in the serialization the prompt's state slot carries. It is versioned
like a prompt and **disjoint** from `battery/snapshots.json` — `scripts/battery.py` compares
the two on the fields a decision turns on and refuses to run on an overlap.

Prompt `v2` is `v1` with a `{{EXEMPLARS}}` slot added and nothing else changed; rendered with
an empty bank it is `v1` byte for byte (`ModePromptTests.TheShippedV2_IsV1PlusTheExemplarSlot`
holds it there), so the two arms of the ablation differ by the examples alone.

```bash
scripts/battery.py --selector llm --prompt v2 --exemplars none              --repeats 1 --temps 0.0
scripts/battery.py --selector llm --prompt v2 --exemplars bank-v1 --shots 4 --repeats 1 --temps 0.0
scripts/battery.py --selector llm --prompt v2 --exemplars bank-v1 --shots 8 --repeats 1 --temps 0.0
scripts/battery.py --selector llm --prompt v2 --exemplars bank-v1           --repeats 1 --temps 0.0   # all 12
```

`battery.py` still defaults to the base prompt with no bank, so each arm names itself
rather than inheriting whatever the game ships. `--shots N` takes the bank round-robin over
the modes, so four shots is one of each rather than four Hunts. One repeat was assumed to be enough at temperature 0, on the
grounds that a greedy decode with a fixed seed reproduces the answer. **It does not** —
#133 measured 95.8% self-consistency at temp 0 (§11), so single-pass numbers carry a
couple of points of resampling noise. Live, the same knobs are `scripts/eval.sh --llm-prompt v2 --llm-exemplars
bank-v1 --llm-shots N`, and every `mode_decision` line records the pair as
`prompt: "v2+bank-v1x4"` — a few-shot run can't be read back as the zero-shot one.

### The ablation

llama3.1:8b, prompt v2, one greedy pass per arm (temperature 0 with a fixed seed, so repeats
would only reproduce the answer). **Measured on the 40-snapshot battery as it stood before
#153**, i.e. over 32 non-ambiguous items; the numbers are not comparable to a run against
the expanded set, and re-running the sweep is #133's job.

| shots | accuracy | invalid | latency mean | p95 |
|---|---|---|---|---|
| 0 (zero-shot) | 56.2% | 0% | 30.3 s | 43.2 s |
| 4 | 62.5% | 0% | 38.2 s | 43.3 s |
| **8** | **65.6%** | 0% | 37.8 s | 44.1 s |
| 12 (whole bank) | 62.5% | 0% | 40.2 s | 48.9 s |

Eight shots wins and is what the selector ships on (`ModeSelectorDriver`'s serialized
defaults and `Enemy.prefab`: prompt `v2`, bank `bank-v1`, 8 shots). Read the gaps as what
they are — 32 items, so each one is 3.1 points, and 8 shots is three items clear of
zero-shot. The shape is the usual one: examples help, and past a point the extra tokens
dilute more than they teach. What every arm gets wrong is the same thing, and few-shot
doesn't fix it: the model disengages when the labels say press (`hunt-visible-near-hurt`,
`hunt-lostsight-*`) and presses when they say break off (`retreat-lowhp-visible-*`) — the
mode catalog's "Retreat will not win a fight" seems to read as advice against retreating.
That is a prompt problem, not an exemplar-count one.

The latency column is the offline loop's, not the game's: this box runs the 8B on CPU at
tens of seconds per call, far over the 5 s decision period.

**Read the relative cost here with care — §11 supersedes it.** These means are over one
greedy pass each, so a single cold-prefix first call (up to 187 s on the 8B) dominates
them, and the "8 shots is ~25% slower" gap is mostly that artifact. Measured at steady
state with the prefix warm, prefill is about two thirds of every call and eight shots cost
only ~2 s more than zero-shot. The expensive axes are prompt and output length; shots are
cheap. The accuracy ordering below is also superseded — it was measured on 32 items, where
adjacent cells are indistinguishable (#153), and the #133 grid over 86 finds the shot count
to be the *only* axis with a significant effect.

Retrieval (nearest exemplars by feature distance) is deliberately not implemented: it adds
per-call latency and a second thing to tune, and the issue's own rule was to reach for it
only if the fixed set underperforms.

Raw results are in `results/battery/ablation-132/`.

## 11. The model and prompt sweep (#133)

The point where the selector's model, prompt and exemplar bank are chosen.
`scripts/battery_sweep.py` runs the grid — one `battery.py` cell per combination,
resumable, rendering `grid.md` from every cell on disk so it can be run in passes:

```bash
scripts/battery_sweep.py --models llama3.2:1b,llama3.2:3b --prompts v2,v3,v4 \
  --shots 0,8 --exemplars bank-v1 --timeout 600 --out results/battery/sweep-133
scripts/battery_sweep.py --models llama3.1:8b --prompts v2,v3 --shots 0,8 \
  --exemplars bank-v1 --baselines --timeout 600 --out results/battery/sweep-133
```

Sixteen cells over the 86-snapshot battery, temperature 0, one greedy pass each.
Full results in `results/battery/sweep-133/`.

| model | prompt | shots | accuracy | invalid | lat p95 |
|---|---|---|---|---|---|
| *FSM baseline* | — | — | *95.8%* | *0%* | *~0 s* |
| *random baseline* | — | — | *27.8%* | *0%* | *~0 s* |
| llama3.1:8b | v3 | 8 | **66.7%** | 0% | 39.2 s |
| llama3.1:8b | v2 | 8 | 58.3% | 0% | 40.5 s |
| **llama3.2:3b** | **v4** | **8** | **59.7%** | 0% | **14.8 s** |
| llama3.2:3b | v3 | 8 | 58.3% | 0% | 20.7 s |
| llama3.2:3b | v2 | 0 | 54.2% | 0% | 11.1 s |
| llama3.2:1b | v4 | 8 | 43.1% | 0% | 7.8 s |
| llama3.1:8b | v3 | 0 | 40.3% | 0% | 30.8 s |

### What the grid says

**One of the three bars passes.** Valid JSON is 100% in all sixteen cells — the
schema-constrained decode holds, and the defensive parsing in `LlmModeResponse`
was never exercised. Accuracy tops out at 66.7% against the FSM's 95.8%, and the
best p95 is 39.2 s against a 3 s bar. The contingency ladder is exhausted: the
catalog was revised twice (v3, v4), exemplars are in, and the model was stepped
1B → 3B → 8B. **The largest local model does not tie the FSM, so this is the
reportable null result §4.5.3 allows, and the cloud arm is now warranted.**

**Only one axis in the grid is statistically real.** McNemar, exact two-sided,
over the 72 scored snapshots:

| comparison | Δ | p |
|---|---|---|
| 8B v3: zero-shot → 8 shots | 40.3 → 66.7 | **0.002** |
| 3B v4×8 → 8B v3×8 (model size) | 59.7 → 66.7 | 0.458 |
| 8B ×8: v2 → v3 (catalog rewrite) | 58.3 → 66.7 | 0.180 |
| 3B ×8: v3 → v4 (terse output) | 58.3 → 59.7 | 1.000 |

Model size and prompt wording are both null at this sample size; exemplars are
not. This supersedes §10's reading of the #132 ablation, which was measured on
32 items and could not separate adjacent cells at all.

**What the exemplars actually buy is mode coverage, not judgement.** Six of the
sixteen cells answer one mode to nearly every state: 8B v3 zero-shot and 3B v4
zero-shot are 100% Hunt across all 86 snapshots, and four 1B cells are 93–98%
Patrol. Every collapsed cell is zero-shot or 1B. That is the failure shape #118
named, and the per-mode accuracy and chosen-share columns are the only place it
shows — a cell answering Hunt everywhere still scores 40.3%, comfortably above
the random floor.

**The misses have not moved since #132.** Of the 21 states the FSM gets right
and the best cell misses, 15 are `just-lost-sight` (8) and `lowhp-contact` (7) —
the press-versus-break-off calls. Two catalog rewrites and a 4× model-size
increase left them where they were, which is evidence that they are not a
wording problem.

### The chosen configuration

**Best in the grid: `llama3.1:8b` + `v3` + `bank-v1` ×8 (66.7%).** It also has by
far the best mode balance — 35/23/21/21 across Hunt/HoldCover/Retreat/Patrol,
the only cell that exercises all four.

**What the selector ships on: `llama3.2:3b` + `v4` + `bank-v1` ×8 (59.7%)** —
`ModeSelectorDriver`'s serialized defaults and `Enemy.prefab`. The split is
deliberate. The 8B's extra accuracy is not significant (p=0.46) and costs 2.5×
the latency; at a p95 of 39.2 s a 120 s round fits two or three decisions, with
event triggers mostly cancelled, so the first decision would set the mode for a
third of the round. That is not a mode selector. The 3B supports a 20 s period,
i.e. about six decisions a round plus events.

The cost is mode coverage: the 3B cell picks HoldCover on 4.7% of decisions, so
one of the four modes is effectively never commanded. Record it as a limitation
of the shipped tier — and note that the arm that fixes it (the 8B) is the one
that cannot run in time, which is the clearest single argument for the cloud arm.

### Latency is prefill, not model size

Measured on the study machine (i7-7500U, 4 cores, **no GPU**), steady state with
the prefix warm:

| | per call | of which prefill | output tokens |
|---|---|---|---|
| 3B, v3, zero-shot | ~14.8 s | ~9.9 s | 22.7 |
| 3B, v3, 8 shots | ~16.7 s | ~9.4 s | 25.5 |

Prefill is about two thirds of every call and eight shots cost only ~2 s more
than zero-shot, so **the expensive axis is prompt and output length, not shot
count** — the opposite of what §10 concluded from wall-clock means that were
dominated by one cold first call. Capping the `reason` field to four words (v4)
cut generation from 5.5 s to 2.9 s, which is the whole reason v4 ships over v3.

The first call against a cold prefix costs far more — up to 80 s on the 3B, 187 s
on the 8B — because the whole prefix is prefilled once. In a match that is one
timeout at session start, which the 3-consecutive-failure latch absorbs.
Pre-warming the prefix before the round starts is an obvious follow-up.

### Decision period

`decisionPeriodSeconds` is raised from 5 s to 20 s, and `llmTimeoutSeconds` from
4 s to 16 s. At the old values every LLM call was cancelled before it landed —
a call unanswered when the next decision comes due is cancelled, and the chosen
model's p95 is 14.8 s — so the run would have scored an uncommanded policy.

The period is shared by every selector kind, so the FSM and random baselines now
decide on the same cadence. `-decisionPeriod 5` (`scripts/eval.sh
--decision-period 5`) puts them back on the one #129 measured them at. Both are
worth reporting: matched cadence isolates decision quality, native cadence is
the deployment comparison.

Lengthening the period does **not** require a retrain. `ModeDirector` may redraw
the mode it is already on and simply extend the dwell, so mode intervals well
past 5 s are already in the policy's training distribution; it is shortening the
period that would be out of distribution.

### Self-consistency, and what temperature 0 does not guarantee

Three repeats per snapshot at each temperature, on the shipped cell
(`results/battery/sweep-133-consistency/`):

```bash
scripts/battery_sweep.py --models llama3.2:3b --prompts v4 --shots 8 \
  --exemplars bank-v1 --temps 0.0,0.7 --repeats 3 \
  --timeout 600 --out results/battery/sweep-133-consistency
```

| temp | accuracy | consistency | ambig-acc | invalid | lat p95 |
|---|---|---|---|---|---|
| 0.0 | 57.9% | 95.8% | 61.9% | 0% | 14.3 s |
| 0.7 | 47.2% | 93.5% | 66.7% | 0% | 14.3 s |

Temperature 0.7 costs 10.7 points of accuracy for no gain anywhere, so the
selector ships at temperature 0 (`llmTemperature`).

**Temperature 0 is not deterministic here, and the repo used to assume it was.**
At temp 0 with a fixed decode seed, **13 of 86 snapshots gave a non-unanimous
answer over three repeats** — 95.8%, not 100%. Ollama's CPU backend does not
guarantee a reproducible greedy decode; reduction order varies between runs and
near-ties in the argmax break differently. Three consequences:

- **Every single-pass number in §11 carries run-to-run noise.** The shipped cell
  scored 59.7% in the grid (one repeat) and 57.9% here (three) — about two
  points, from nothing but resampling. That *strengthens* the significance
  reading: the 7-point model-size gap is barely over the noise floor, which is
  what p=0.46 already said, while the 26-point exemplar effect is far outside it.
- **A fixed seed does not make a battery run replay.** Treat these as averages
  over repeats, the way `eval.sh` runs are (aim spread is deliberately unseeded
  there for the same kind of reason).
- **A plain retry at temp 0 is not necessarily wasted budget** — re-sending the
  same prompt can produce different text. `LlmModeSelector` quotes the
  unreadable answer back anyway, which is still the better retry.

**Latency from a repeats > 1 run is not comparable to the grid's.** Mean 7.7 s
and p50 4.4 s here are deflated: consecutive repeats send a byte-identical
prompt, which hits Ollama's exact-prefix KV cache and costs ~0.2 s of prefill
instead of ~10 s. A match never repeats a prompt. **p95 (14.3 s) is the honest
figure**, and it agrees with the grid's 14.8 s because it lands on the first,
uncached call of each triplet. The 126 s max at temp 0 is the cold-prefix first
call of the run.
