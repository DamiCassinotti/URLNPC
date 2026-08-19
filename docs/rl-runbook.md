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
scripts/train.sh build  <run-id> [config] [--num-envs N] [--seed S] [extra...]
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
Training always commands all four modes — the set is owned by code (`NpcModes.AllMask`), so
the prefab, the scene's instance override and `CombatantRig`'s composed director can't train
different pools; `ModeDirector.enabledModes` only narrows it outside training. Smoke the slice
in the editor first:

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

## 3. What to look for in TensorBoard

```bash
tensorboard --logdir results
```

- **`Environment/Cumulative Reward`** — the headline. Should trend up and stabilise; a flat
  line near zero means the policy isn't finding the kill/hit rewards.
- **`Compliance/Hunt`, `Compliance/HoldCover`, `Compliance/Retreat`, `Compliance/Patrol`** —
  does the policy obey the commanded mode (Hunt closes distance or shoots, Retreat opens
  distance)? These are the point of the mode-conditioning: reward can climb while compliance
  stays flat if the policy ignores the command and just fights. The denominator is the steps
  the mode could act on — Hunt and Retreat only while the player is visible — so this reads as
  policy quality, not as how often the fight was joined. Patrol's rate reads low by
  construction (a cell counts once) — compare between runs, not as a percentage.
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

## 7. Score a model headlessly

`scripts/eval.sh` plays a fixed number of rounds against the standalone build with no trainer
attached and summarizes the telemetry:

```bash
scripts/eval.sh results/full-03-seed-1001/URLNPC.onnx --episodes 100 --seed 1001 \
    --opponent heuristic
```

Output lands in `results/eval/<model>_<opponent>_<stamp>/`: `unity.log`, the run's
`telemetry.jsonl`, and `summary.txt`/`summary.json` — win/loss/draw, damage dealt and taken,
accuracy, time to kill, survival time, and per-mode compliance, visibility and step counts.

- `--opponent policy` keeps both sides on the model (self-play); `--opponent heuristic` puts
  the far side on the scripted heuristic, which is what the ≥70% win-rate gate is measured on.
- `--modes scripted` (default) lets the `ModeDirector` sample as it does in training;
  `--modes Hunt` pins one mode for a per-mode baseline; `--modes none` leaves the channel on
  `initialMode`.
- `--seed` fixes arenas, spawns and the mode schedule, so a run is replayable.
- `--time-scale` trades fidelity for wall-clock time; 100 rounds at the default 1 can take
  hours, since a round runs to 120 s of game time before the clock draws it.

The model is copied into `Assets/Resources/EvalModels/` and the player rebuilt whenever it
changes: Inference Engine only imports ONNX in the editor, so the build has to carry it. Pass
`--no-build` to reuse a build that already does. `scripts/eval_summary.py` runs standalone on
any session JSONL, including one from a human-played round.
