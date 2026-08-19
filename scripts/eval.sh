#!/usr/bin/env bash
# Headless evaluation of a trained policy (issue #52).
#
#   scripts/eval.sh <model.onnx> [--episodes N] [--seed S]
#                   [--opponent policy|heuristic] [--modes scripted|none|<Mode>]
#                   [--time-scale F] [--out DIR]
#                   [--rebuild | --no-build] [--timeout SEC]
#
# Runs N rounds against the standalone build with no trainer attached, then
# summarizes the telemetry JSONL: win rate, damage dealt/taken, accuracy, time
# to kill, survival time, and per-mode compliance and step counts.
#
#   --opponent policy     both sides run the model (self-play)
#   --opponent heuristic  the far side runs the scripted heuristic — the
#                         baseline the ≥70% win-rate gate (#50) is measured on
#   --modes               who commands the NPC's mode: the scripted director,
#                         nobody, or one mode pinned for the whole run
#   --seed                fixes arenas, spawns and the mode schedule; aim
#                         spread stays unseeded by design, so rounds still
#                         differ — run enough episodes for the average
#   --time-scale          game time per rendered frame, in physics steps; 1 is
#                         the most faithful, higher is faster and coarser. The
#                         run is never throttled to real time either way.
#   --rebuild             rebuild the player even if it already has this model
#
# The model is baked into the player: Inference Engine only imports ONNX in the
# editor, so <model.onnx> is copied into Assets/Resources and the player is
# rebuilt whenever it changes. --no-build reuses the existing build (only valid
# if it already carries this model).
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_BIN="$PROJECT_ROOT/Builds/Linux/URLNPC.x86_64"
MODEL_DIR="$PROJECT_ROOT/Assets/Resources/EvalModels"
MODEL_RESOURCE="EvalModels/eval"
MODEL_DEST="$MODEL_DIR/eval.onnx"
STAMP_FILE="$PROJECT_ROOT/Builds/Linux/.eval-model.sha256"

usage() {
    sed -n '2,29p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    exit "${1:-1}"
}

[[ $# -ge 1 ]] || usage
[[ "$1" != "-h" && "$1" != "--help" ]] || usage 0
[[ "$1" != -* ]] || usage
MODEL="$1"; shift
[[ -f "$MODEL" ]] || { echo "error: model '$MODEL' not found" >&2; exit 1; }

EPISODES=100
SEED=1001
OPPONENT="policy"
MODES="scripted"
TIME_SCALE=1
OUT=""
BUILD=1
TIMEOUT=""
REBUILD=0
while [[ $# -ge 1 ]]; do
    case "$1" in
        --episodes) EPISODES="$2"; shift 2 ;;
        --seed) SEED="$2"; shift 2 ;;
        --opponent) OPPONENT="$2"; shift 2 ;;
        --modes) MODES="$2"; shift 2 ;;
        --time-scale) TIME_SCALE="$2"; shift 2 ;;
        --out) OUT="$2"; shift 2 ;;
        --no-build) BUILD=0; shift ;;
        --rebuild) REBUILD=1; shift ;;
        --timeout) TIMEOUT="$2"; shift 2 ;;
        -h|--help) usage 0 ;;
        *) echo "error: unknown argument '$1'" >&2; usage ;;
    esac
done

# The player rejects these too, but only after a rebuild that can take minutes.
case "$OPPONENT" in policy|heuristic) ;; *) echo "error: --opponent takes policy|heuristic" >&2; exit 1 ;; esac
case "${MODES,,}" in scripted|none|hunt|holdcover|retreat|patrol) ;; *) echo "error: --modes takes scripted|none|Hunt|HoldCover|Retreat|Patrol" >&2; exit 1 ;; esac
[[ "$EPISODES" =~ ^[1-9][0-9]*$ ]] || { echo "error: --episodes takes a positive integer" >&2; exit 1; }
[[ "$SEED" =~ ^-?[0-9]+$ ]] || { echo "error: --seed takes an integer" >&2; exit 1; }

OUT="${OUT:-$PROJECT_ROOT/results/eval/$(basename "${MODEL%.onnx}")_${OPPONENT}_$(date +%Y%m%d_%H%M%S)}"
mkdir -p "$OUT"

# What was scored, next to the numbers: a summary is only comparable against
# another run if the condition it was produced under is on record.
cat > "$OUT/config.json" <<JSON
{
  "model": "$MODEL",
  "episodes": $EPISODES,
  "seed": $SEED,
  "opponent": "$OPPONENT",
  "modes": "$MODES",
  "timeScale": $TIME_SCALE,
  "commit": "$(git -C "$PROJECT_ROOT" rev-parse --short HEAD 2>/dev/null || echo unknown)",
  "startedUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
JSON

# ------------------------------------------------------------------- build

MODEL_HASH="$(sha256sum "$MODEL" | cut -d' ' -f1)"
if [[ $BUILD -eq 1 ]]; then
    mkdir -p "$MODEL_DIR"
    cp "$MODEL" "$MODEL_DEST"
    if [[ $REBUILD -eq 0 && -x "$ENV_BIN" && -f "$STAMP_FILE" && "$(cat "$STAMP_FILE")" == "$MODEL_HASH" ]]; then
        echo "==> Build already carries this model, skipping the rebuild."
    else
        rm -f "$STAMP_FILE"   # a failed build must not look like a good one
        bash "$PROJECT_ROOT/scripts/build-player.sh"
        echo "$MODEL_HASH" > "$STAMP_FILE"
    fi
elif [[ ! -x "$ENV_BIN" ]]; then
    echo "error: no build at $ENV_BIN — drop --no-build" >&2
    exit 1
elif [[ ! -f "$STAMP_FILE" || "$(cat "$STAMP_FILE")" != "$MODEL_HASH" ]]; then
    echo "warning: the existing build was made from a different model — results describe whatever it carries" >&2
fi

# --------------------------------------------------------------------- run

UNITY_LOG="$OUT/unity.log"
# Rounds always end (the clock is a draw), so the only way the player runs
# forever is a startup failure that never starts one. The run is not throttled
# to real time, so this is a loose wall-clock bound, not the expected duration;
# --timeout 0 disables the guard.
TIMEOUT="${TIMEOUT:-$((EPISODES * 60 + 300))}"
echo "==> $EPISODES episodes, seed $SEED, opponent $OPPONENT, modes $MODES, timeScale $TIME_SCALE"
echo "    log: $UNITY_LOG"
set +e
timeout "$TIMEOUT" "$ENV_BIN" -batchmode -nographics -logFile "$UNITY_LOG" \
    -playerDriver agent \
    -runSeed "$SEED" \
    -evalEpisodes "$EPISODES" \
    -evalModel "$MODEL_RESOURCE" \
    -evalOpponent "$OPPONENT" \
    -evalModes "$MODES" \
    -evalTimeScale "$TIME_SCALE"
RC=$?
set -e
if [[ $RC -eq 124 ]]; then
    echo "error: the player ran past the ${TIMEOUT}s budget — see $UNITY_LOG" >&2
    exit 124
elif [[ $RC -ne 0 ]]; then
    echo "error: the player exited $RC — see $UNITY_LOG" >&2
    exit "$RC"
fi

# ----------------------------------------------------------------- summary

# The logger prints its path at startup; taking it from there beats guessing
# persistentDataPath.
TELEMETRY="$(sed -n 's/.*\[Telemetry\] Logging to //p' "$UNITY_LOG" | tail -1 | tr -d '\r')"
if [[ -z "$TELEMETRY" || ! -f "$TELEMETRY" ]]; then
    echo "error: no telemetry file recorded in $UNITY_LOG" >&2
    exit 1
fi
cp "$TELEMETRY" "$OUT/telemetry.jsonl"

python3 "$PROJECT_ROOT/scripts/eval_summary.py" "$OUT/telemetry.jsonl" \
    --json "$OUT/summary.json" | tee "$OUT/summary.txt"
echo "==> results in $OUT"
