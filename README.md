# URLNPC

First-person combat arena where an AI enemy NPC is trained with Unity ML-Agents (reinforcement learning) to fight against the player.

## Requirements

- **Unity 6000.0 LTS** (install via Unity Hub).
- **Python 3.10.12** for ML training.

## Running the game

1. Open the project in Unity 6000.0 LTS.
2. Load `Assets/Scenes/FPS/FPS.unity`.
3. Press Play.

## Training the agent

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
mlagents-learn config/URLNPC.yaml --run-id=URLNPC
```

Then press Play in the Unity editor to start a training episode. Trained models are written to `results/URLNPC/URLNPC.onnx` and can be referenced from the `EnemyAgent`'s **Behavior Parameters → Model** field.

## Tech stack

- Unity 6 LTS, ML-Agents Release 22 (`com.unity.ml-agents` 3.0.0 — embedded under `Packages/` and locally patched for the renamed Inference Engine package).
- New Input System with Starter Assets first-person controller (Cinemachine-driven camera).
- PPO trainer, Python `mlagents 1.1.0` / `torch 2.2.1`.

See `CLAUDE.md` for architecture notes.
