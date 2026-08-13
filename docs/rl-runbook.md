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

The slice is the two-mode run: `ModeDirector.enabledModes` ships as `Hunt | Retreat`, so the
director only ever commands those two and the policy learns the Hunt/Retreat split before the
full four-mode pool. From the repo root with the venv active:

```bash
mlagents-learn config/URLNPC.yaml --run-id=slice-01 --env=Builds/Linux/URLNPC --num-envs=4
```

- `--run-id` names the run; results land in `results/<run-id>/`. Reuse a name only with
  `--resume` (continue) or `--force` (overwrite).
- `--env` points at the standalone build; drop it and press Play to drive from the editor
  instead.
- Reproducible run: append `--env-args -runSeed 12345` (README → *Reproducible evaluation
  runs*). The seed is logged and recorded as `Run/Seed` per episode.

Trained model lands at `results/<run-id>/URLNPC.onnx`; checkpoints every 50k steps
(`checkpoint_interval`), `max_steps` 1M.

## 3. What to look for in TensorBoard

```bash
tensorboard --logdir results
```

- **`Environment/Cumulative Reward`** — the headline. Should trend up and stabilise; a flat
  line near zero means the policy isn't finding the kill/hit rewards.
- **`Compliance/Hunt`, `Compliance/Retreat`** — does the policy obey the commanded mode
  (Hunt closes distance or shoots, Retreat opens distance)? These are the point of the
  mode-conditioning: reward can climb while compliance stays flat if the policy ignores the
  command and just fights. Only the enabled modes report on the slice. Patrol's rate reads
  low by construction (a cell counts once) — compare between runs, not as a percentage.
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
live opponent instead of a static target. `CombatantRig` swaps the player between a Human
driver and an Agent driver:

```bash
mlagents-learn config/URLNPC.yaml --run-id=selfplay-01 --env=Builds/Linux/URLNPC \
    --num-envs=4 --env-args -playerDriver agent
```

The agent driver defaults to behavior name `URLNPC` / team id 1 — same behavior name as the
enemy, so both sides share one policy. In the editor, set **Driver = Agent** on `CombatantRig`
(or `CombatantRig.DriverOverride` from code) instead of the CLI arg. Both bodies carry a
`ModeDirector`, so each is independently commanded during the run. Give the agent driver a
different behavior name to train a separate player policy against the enemy.
