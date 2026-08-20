#!/usr/bin/env bash
# Score one model under every commanded mode, back to back, unattended.
#
#   scripts/eval-modes.sh <model.onnx> [extra eval.sh args...]
#
# Example:  scripts/eval-modes.sh results/full-06/URLNPC.onnx \
#               --episodes 200 --seed 1234 --opponent heuristic
#   -> one eval.sh run per mode, all under results/eval/<model>_<stamp>/<mode>/.
#
# Override the list:  MODES="Hunt Patrol" scripts/eval-modes.sh results/...
# Leave it running:   nohup scripts/eval-modes.sh results/... >/dev/null 2>&1 &
#
# Every run gets the same --seed, so the modes are compared over the same arenas
# and spawns. Do not pass --modes or --out; this script owns both. One failed
# mode is logged and the batch keeps going.
set -uo pipefail   # deliberately not -e: a single failed mode must not abort the batch

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EVAL="$PROJECT_ROOT/scripts/eval.sh"

[[ $# -ge 1 && "$1" != -* ]] || { echo "usage: scripts/eval-modes.sh <model.onnx> [extra eval.sh args...]" >&2; exit 1; }
MODEL="$1"; shift
for arg in "$@"; do
    case "$arg" in
        --modes|--out) echo "error: eval-modes.sh sets $arg itself" >&2; exit 1 ;;
    esac
done

MODES="${MODES:-Hunt HoldCover Retreat Patrol scripted}"

OUT_ROOT="$PROJECT_ROOT/results/eval/$(basename "${MODEL%.onnx}")_modes_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$OUT_ROOT"
SUMMARY="$OUT_ROOT/batch.log"

log() { echo "$@" | tee -a "$SUMMARY"; }

log "batch $(basename "$MODEL")  modes: $MODES  args: ${*:-none}"

fail=0
for MODE in $MODES; do
    log "==> [$(date +%H:%M:%S)] start $MODE  -> $OUT_ROOT/$MODE"
    if bash "$EVAL" "$MODEL" --modes "$MODE" --out "$OUT_ROOT/$MODE" "$@" \
            >"$OUT_ROOT/$MODE.log" 2>&1; then
        log "    [$(date +%H:%M:%S)] done  $MODE"
    else
        rc=$?
        log "    [$(date +%H:%M:%S)] FAIL  $MODE (exit $rc, see $OUT_ROOT/$MODE.log)"
        fail=$((fail + 1))
    fi
done

# The per-mode summaries side by side, so the batch is readable without opening
# five directories.
for MODE in $MODES; do
    [[ -f "$OUT_ROOT/$MODE/summary.txt" ]] || continue
    log ""
    log "----- $MODE -----"
    cat "$OUT_ROOT/$MODE/summary.txt" | tee -a "$SUMMARY"
done

log ""
log "batch complete: $(echo "$MODES" | wc -w) runs, $fail failed. results in $OUT_ROOT"
exit "$fail"
