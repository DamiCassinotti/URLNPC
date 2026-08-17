#!/usr/bin/env bash
# One entry point for the two training paths.
#
#   scripts/train.sh editor <run-id> [config] [extra mlagents args...]
#   scripts/train.sh build  <run-id> [config] [--num-envs N] [--seed S] [extra...]
#
# --seed S seeds both the ML-Agents learner and the env RNG (-runSeed), so the
# run is fully reproducible. build only; the editor path can't take env-args.
#
# editor — starts mlagents-learn and waits for you to press Play in the Editor.
#          For watching behavior and quick manual checks.
# build  — runs against the headless standalone build (scripts/build-player.sh),
#          driving the player side by the shared agent policy for self-play.
#
# config defaults to config/URLNPC.yaml. The slice/full/self-play configs are:
#   config/URLNPC-slice.yaml     50k-step smoke run
#   config/URLNPC.yaml           1M full run
#   config/URLNPC-selfplay.yaml  full run plus the self-play block
#
# --resume / --force (and any other flag) pass straight through to mlagents-learn.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_BIN="$PROJECT_ROOT/Builds/Linux/URLNPC.x86_64"

usage() {
    sed -n '2,17p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    exit "${1:-1}"
}

[[ $# -ge 1 ]] || usage
MODE="$1"; shift
[[ "$MODE" == "editor" || "$MODE" == "build" ]] || { echo "error: unknown mode '$MODE'" >&2; usage; }

[[ $# -ge 1 && "$1" != -* ]] || { echo "error: <run-id> is required" >&2; usage; }
RUN_ID="$1"; shift

CONFIG="config/URLNPC.yaml"
if [[ $# -ge 1 && "$1" != -* ]]; then
    CONFIG="$1"; shift
fi
[[ -f "$PROJECT_ROOT/$CONFIG" || -f "$CONFIG" ]] || { echo "error: config '$CONFIG' not found" >&2; exit 1; }

NUM_ENVS=""
SEED=""
PASSTHROUGH=()
while [[ $# -ge 1 ]]; do
    case "$1" in
        --num-envs) NUM_ENVS="$2"; shift 2 ;;
        --num-envs=*) NUM_ENVS="${1#*=}"; shift ;;
        --seed) SEED="$2"; shift 2 ;;
        --seed=*) SEED="${1#*=}"; shift ;;
        *) PASSTHROUGH+=("$1"); shift ;;
    esac
done

CMD=(mlagents-learn "$CONFIG" "--run-id=$RUN_ID")

if [[ "$MODE" == "build" ]]; then
    if [[ ! -x "$ENV_BIN" ]]; then
        echo "error: standalone build not found at $ENV_BIN" >&2
        echo "Run scripts/build-player.sh first." >&2
        exit 1
    fi
    CMD+=("--env=$ENV_BIN" --no-graphics)
    [[ -n "$NUM_ENVS" ]] && CMD+=("--num-envs=$NUM_ENVS")
    # --seed drives both the learner (mlagents) and the env (-runSeed below) so a
    # seeded run is fully reproducible, not just its arena/spawn RNG.
    [[ -n "$SEED" ]] && CMD+=("--seed=$SEED")
    [[ ${#PASSTHROUGH[@]} -gt 0 ]] && CMD+=("${PASSTHROUGH[@]}")
    # The player side is driven by the shared agent policy so both bodies train
    # against a live opponent; env-args must come last (they end the arg list).
    ENV_ARGS=(-playerDriver agent)
    [[ -n "$SEED" ]] && ENV_ARGS+=(-runSeed "$SEED")
    CMD+=(--env-args "${ENV_ARGS[@]}")
else
    [[ -z "$NUM_ENVS" ]] || { echo "error: --num-envs applies to 'build', not 'editor'" >&2; exit 1; }
    [[ -z "$SEED" ]] || { echo "error: --seed applies to 'build' env-args; not available in editor mode" >&2; exit 1; }
    [[ ${#PASSTHROUGH[@]} -gt 0 ]] && CMD+=("${PASSTHROUGH[@]}")
    echo "==> Starting trainer; press Play in the Unity editor when it says 'Listening on port'..."
fi

cd "$PROJECT_ROOT"
echo "==> ${CMD[*]}"
exec "${CMD[@]}"
