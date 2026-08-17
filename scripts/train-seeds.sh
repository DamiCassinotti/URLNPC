#!/usr/bin/env bash
# Run the same config N times with different fixed seeds, back to back, unattended.
# Each run is fully reproducible: --seed feeds BOTH the ML-Agents learner seed and
# the env -runSeed (see train.sh). Using a *fixed* seed set means the whole batch
# is replayable and a later reward-change batch can reuse the same seeds for a
# paired comparison instead of comparing against uncontrolled noise.
#
#   scripts/train-seeds.sh <run-group> [config] [extra train.sh/mlagents args...]
#
# Example:  scripts/train-seeds.sh 03
#   -> full-03-seed-1001 .. full-03-seed-1005 on config/URLNPC.yaml, one after another.
#
# Override the seeds:   SEEDS="7 8 9" scripts/train-seeds.sh 03
# Re-run over old ids:  scripts/train-seeds.sh 03 config/URLNPC.yaml --force
# Leave it overnight:   nohup scripts/train-seeds.sh 03 >results/seed-batches/nohup.log 2>&1 &
#
# Needs the venv active (mlagents-learn on PATH) and the standalone build present
# (scripts/build-player.sh). One failed run is logged and the batch keeps going.
set -uo pipefail   # deliberately not -e: a single failed seed must not abort the night

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TRAIN="$PROJECT_ROOT/scripts/train.sh"

[[ $# -ge 1 && "$1" != -* ]] || { echo "usage: scripts/train-seeds.sh <run-group> [config] [extra...]" >&2; exit 1; }
GROUP="$1"; shift

CONFIG=""
if [[ $# -ge 1 && "$1" != -* ]]; then CONFIG="$1"; shift; fi
EXTRA=("$@")

SEEDS="${SEEDS:-1001 1002 1003 1004 1005}"

LOG_DIR="$PROJECT_ROOT/results/seed-batches"
mkdir -p "$LOG_DIR"
STAMP="$(date +%Y%m%d_%H%M%S)"
SUMMARY="$LOG_DIR/full-${GROUP}_${STAMP}.summary.log"

log() { echo "$@" | tee -a "$SUMMARY"; }

log "batch full-${GROUP}  config=${CONFIG:-config/URLNPC.yaml}  seeds: $SEEDS  ($STAMP)"

fail=0
for SEED in $SEEDS; do
    RUN_ID="full-${GROUP}-seed-${SEED}"
    RUN_LOG="$LOG_DIR/${RUN_ID}_${STAMP}.log"
    log "==> [$(date +%H:%M:%S)] start $RUN_ID (seed=$SEED)  log: $RUN_LOG"
    if bash "$TRAIN" build "$RUN_ID" ${CONFIG:+"$CONFIG"} --seed "$SEED" \
            ${EXTRA[@]+"${EXTRA[@]}"} >"$RUN_LOG" 2>&1; then
        log "    [$(date +%H:%M:%S)] done  $RUN_ID  -> results/$RUN_ID"
    else
        rc=$?
        log "    [$(date +%H:%M:%S)] FAIL  $RUN_ID  (exit $rc, see $RUN_LOG)"
        fail=$((fail + 1))
    fi
done

log "batch complete: $(echo "$SEEDS" | wc -w) runs, $fail failed. summary: $SUMMARY"
exit "$fail"
