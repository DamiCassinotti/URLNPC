#!/usr/bin/env bash
# Headless evaluation of a trained policy (issue #52).
#
#   scripts/eval.sh <model.onnx> [--episodes N] [--seed S]
#                   [--subject policy|heuristic|random|flee]
#                   [--opponent policy|heuristic] [--modes scripted|none|<Mode>]
#                   [--selector none|fixed:<Mode>|random|fsm|llm]
#                   [--llm-prompt ID] [--llm-exemplars BANK] [--llm-shots N]
#                   [--decision-period SECONDS]
#                   [--time-scale F] [--out DIR]
#                   [--rebuild | --no-build] [--timeout SEC]
#
# Runs N rounds against the standalone build with no trainer attached, then
# summarizes the telemetry JSONL: win rate, damage dealt/taken, accuracy, time
# to kill, survival time, and per-mode compliance and step counts.
#
#   --subject policy      the NPC side runs the model — what is being scored
#   --subject heuristic|random|flee
#                         put the NPC side on a control instead: the scripted
#                         heuristic, uniform draws over the action branches, or
#                         a scripted retreat that backs off, takes cover and
#                         never fires. A model is still required — it is what
#                         the build carries — but nothing on the NPC side runs
#                         it.
#   --opponent policy     both sides run the model (self-play)
#   --opponent heuristic  the far side runs the scripted heuristic — the
#                         baseline the ≥70% win-rate gate (#50) is measured on
#   --modes               who commands the NPC's mode: the scripted director,
#                         nobody, or one mode pinned for the whole run
#   --selector            a mode selector commands the modes instead (#127):
#                         the FSM, uniform random draws, one pinned mode, or the
#                         LLM (#130 — the run ignores --time-scale: a model call
#                         takes wall-clock seconds). Exactly one writer: a
#                         selector forces --modes none, and naming both is an
#                         error
#   --llm-model           which model answers, plus --llm-endpoint,
#                         --llm-temperature, --llm-timeout, --llm-retries,
#                         --llm-seed, --llm-prompt (the prompt variant, an
#                         id under Assets/Resources/Prompts) and
#                         --llm-exemplars/--llm-shots (the few-shot bank under
#                         Assets/Resources/Exemplars and how many of it to show;
#                         no bank is the zero-shot arm): forwarded as -llm*, so a
#                         batch sweeps models or temperatures off one build.
#                         Only with --selector llm
#   --decision-period     seconds between periodic selector decisions. The
#                         default (20 s) is set by the chosen model's latency
#                         (#133); a call still unanswered when the next
#                         decision comes due is cancelled, so this has to
#                         exceed --llm-timeout. Pass 5 to score the FSM and
#                         random baselines on the cadence #129 measured
#   --seed                fixes arenas, spawns and the mode schedule; aim
#                         spread stays unseeded by design, so rounds still
#                         differ — run enough episodes for the average
#   --time-scale          game time per rendered frame, in physics steps; 1 is
#                         the most faithful, higher is faster and coarser. The
#                         run is never throttled to real time either way —
#                         except under --selector llm, which ignores this and
#                         runs at wall clock, since a model call costs real
#                         seconds
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
    # Through the last option line, not a fixed 43: the range already cut
    # --seed, --time-scale and --rebuild before #133 added a flag below them.
    sed -n '2,60p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    exit "${1:-1}"
}

[[ $# -ge 1 ]] || usage
[[ "$1" != "-h" && "$1" != "--help" ]] || usage 0
[[ "$1" != -* ]] || usage
MODEL="$1"; shift
[[ -f "$MODEL" ]] || { echo "error: model '$MODEL' not found" >&2; exit 1; }

EPISODES=100
SEED=1001
SUBJECT="policy"
OPPONENT="policy"
MODES="scripted"
MODES_SET=0
SELECTOR="none"
TIME_SCALE=1
OUT=""
BUILD=1
TIMEOUT=""
REBUILD=0
LLM_ARGS=()
# Not an --llm-* knob: the period is shared by every selector kind, so it must
# stay out of the gate below that rejects LLM knobs on a non-LLM run.
DECISION_PERIOD=""
while [[ $# -ge 1 ]]; do
    case "$1" in
        --episodes) EPISODES="$2"; shift 2 ;;
        --seed) SEED="$2"; shift 2 ;;
        --subject) SUBJECT="$2"; shift 2 ;;
        --opponent) OPPONENT="$2"; shift 2 ;;
        --modes) MODES="$2"; MODES_SET=1; shift 2 ;;
        --selector) SELECTOR="$2"; shift 2 ;;
        --llm-endpoint)    LLM_ARGS+=(-llmEndpoint "$2"); shift 2 ;;
        --llm-model)       LLM_ARGS+=(-llmModel "$2"); shift 2 ;;
        --llm-timeout)     LLM_ARGS+=(-llmTimeout "$2"); shift 2 ;;
        --llm-retries)     LLM_ARGS+=(-llmRetries "$2"); shift 2 ;;
        --llm-temperature) LLM_ARGS+=(-llmTemperature "$2"); shift 2 ;;
        --llm-seed)        LLM_ARGS+=(-llmSeed "$2"); shift 2 ;;
        --llm-prompt)      LLM_ARGS+=(-llmPrompt "$2"); shift 2 ;;
        --llm-exemplars)   LLM_ARGS+=(-llmExemplars "$2"); shift 2 ;;
        --llm-shots)       LLM_ARGS+=(-llmShots "$2"); shift 2 ;;
        --decision-period) DECISION_PERIOD="$2"; shift 2 ;;
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
# Lower-cased first, because the player's own parsers are case-insensitive and
# this check must not reject a spelling the run would otherwise have accepted.
SUBJECT="${SUBJECT,,}"
OPPONENT="${OPPONENT,,}"
case "$SUBJECT" in policy|heuristic|random|flee) ;; *) echo "error: --subject takes policy|heuristic|random|flee" >&2; exit 1 ;; esac
case "$OPPONENT" in policy|heuristic) ;; *) echo "error: --opponent takes policy|heuristic" >&2; exit 1 ;; esac
case "${MODES,,}" in scripted|none|hunt|holdcover|retreat|patrol) ;; *) echo "error: --modes takes scripted|none|Hunt|HoldCover|Retreat|Patrol" >&2; exit 1 ;; esac
SELECTOR="${SELECTOR,,}"
case "$SELECTOR" in none|random|fsm|llm|fixed:hunt|fixed:holdcover|fixed:retreat|fixed:patrol) ;; *) echo "error: --selector takes none|fixed:<Mode>|random|fsm|llm" >&2; exit 1 ;; esac
if [[ ${#LLM_ARGS[@]} -gt 0 && "$SELECTOR" != "llm" ]]; then
    echo "error: the --llm-* knobs need --selector llm" >&2
    exit 1
fi
PERIOD_ARGS=()
if [[ -n "$DECISION_PERIOD" ]]; then
    # A typo would otherwise fall through to the serialized default and score
    # the run at a cadence its own config.json disagrees with.
    # Numeric compare, not a string one: '0.0' matches the shape but resolves
    # to zero, which the driver ignores — leaving config.json claiming a period
    # the run never used.
    if ! [[ "$DECISION_PERIOD" =~ ^[0-9]+(\.[0-9]+)?$ ]] \
       || ! awk -v v="$DECISION_PERIOD" 'BEGIN { exit !(v + 0 > 0) }'; then
        echo "error: --decision-period takes a positive number of seconds" >&2
        exit 1
    fi
    PERIOD_ARGS=(-decisionPeriod "$DECISION_PERIOD")
fi
# One writer on the mode channel: a selector run stands the scripted director
# down. Naming both is a condition mix-up, not a run.
if [[ "$SELECTOR" != "none" ]]; then
    if [[ $MODES_SET -eq 1 && "${MODES,,}" != "none" ]]; then
        echo "error: --selector and --modes both command the mode channel — drop one" >&2
        exit 1
    fi
    MODES="none"
fi
[[ "$EPISODES" =~ ^[1-9][0-9]*$ ]] || { echo "error: --episodes takes a positive integer" >&2; exit 1; }
[[ "$SEED" =~ ^-?[0-9]+$ ]] || { echo "error: --seed takes an integer" >&2; exit 1; }

OUT="${OUT:-$PROJECT_ROOT/results/eval/$(basename "${MODEL%.onnx}")_${SUBJECT}-vs-${OPPONENT}_$(date +%Y%m%d_%H%M%S)}"
mkdir -p "$OUT"

# What was scored, next to the numbers: a summary is only comparable against
# another run if the condition it was produced under is on record.
cat > "$OUT/config.json" <<JSON
{
  "model": "$MODEL",
  "episodes": $EPISODES,
  "seed": $SEED,
  "subject": "$SUBJECT",
  "opponent": "$OPPONENT",
  "modes": "$MODES",
  "selector": "$SELECTOR",
  "llmArgs": "${LLM_ARGS[*]-}",
  "decisionPeriod": "${DECISION_PERIOD:-default}",
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
# forever is a startup failure that never starts one. This is a loose wall-clock
# bound, not the expected duration; --timeout 0 disables the guard. An LLM run
# is the one that actually spends real time per episode — up to a full round
# each — so it gets a budget built on the round length instead.
if [[ "$SELECTOR" == "llm" ]]; then
    TIMEOUT="${TIMEOUT:-$((EPISODES * 180 + 300))}"
else
    TIMEOUT="${TIMEOUT:-$((EPISODES * 60 + 300))}"
fi
echo "==> $EPISODES episodes, seed $SEED, subject $SUBJECT, opponent $OPPONENT, modes $MODES, selector $SELECTOR, timeScale $TIME_SCALE"
echo "    log: $UNITY_LOG"
set +e
timeout "$TIMEOUT" "$ENV_BIN" -batchmode -nographics -logFile "$UNITY_LOG" \
    -playerDriver agent \
    -runSeed "$SEED" \
    -evalEpisodes "$EPISODES" \
    -evalModel "$MODEL_RESOURCE" \
    -evalSubject "$SUBJECT" \
    -evalOpponent "$OPPONENT" \
    -evalModes "$MODES" \
    -modeSelector "$SELECTOR" \
    -evalTimeScale "$TIME_SCALE" \
    ${LLM_ARGS[@]+"${LLM_ARGS[@]}"} \
    ${PERIOD_ARGS[@]+"${PERIOD_ARGS[@]}"}
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
