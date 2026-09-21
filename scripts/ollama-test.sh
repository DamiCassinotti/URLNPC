#!/usr/bin/env bash
# Run the live-Ollama checks against a local model server.
#
#   scripts/ollama-test.sh [--model TAG] [--endpoint URL] [--probe-only]
#
# The PlayMode suite's live check (LlmSelectorRuntimeTests) is gated on
# URLNPC_OLLAMA / URLNPC_OLLAMA_MODEL and skips without them, so CI never
# reaches a server. This wrapper is the other half: it makes sure a server is
# up and carrying the model, times one schema-constrained call against the
# selector's own decision budget, then runs the PlayMode suite with the two
# variables set. A server it started itself is stopped on the way out; one that
# was already running is left alone.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENDPOINT="http://localhost:11434"
MODEL="llama3.2:3b"
PROBE_ONLY=0
# The driver cancels a call that outlives its decision period, so a model that
# can't answer inside the budget is a selector that never commands anything.
BUDGET_SECONDS=4

while [[ $# -gt 0 ]]; do
    case "$1" in
        --model)      MODEL="$2"; shift 2 ;;
        --endpoint)   ENDPOINT="${2%/}"; shift 2 ;;
        --budget)     BUDGET_SECONDS="$2"; shift 2 ;;
        --probe-only) PROBE_ONLY=1; shift ;;
        -h|--help)    sed -n '2,13p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *)            echo "error: unknown argument '$1'" >&2; exit 2 ;;
    esac
done

command -v curl >/dev/null || { echo "error: curl is required" >&2; exit 1; }

STARTED_SERVER=""
cleanup() {
    [[ -n "$STARTED_SERVER" ]] && kill "$STARTED_SERVER" 2>/dev/null || true
}
trap cleanup EXIT

server_up() { curl -fsS -m 2 "$ENDPOINT/api/tags" >/dev/null 2>&1; }

if server_up; then
    echo "==> server already up at $ENDPOINT"
else
    if [[ "$ENDPOINT" != http://localhost:* && "$ENDPOINT" != http://127.0.0.1:* ]]; then
        echo "error: nothing answering at $ENDPOINT, and it isn't ours to start" >&2
        exit 1
    fi
    command -v ollama >/dev/null || {
        echo "error: no server at $ENDPOINT and no ollama binary to start one." >&2
        echo "Install it with: curl -fsSL https://ollama.com/install.sh | sh" >&2
        exit 1
    }
    echo "==> starting ollama serve"
    ollama serve >/tmp/ollama-serve.log 2>&1 &
    STARTED_SERVER=$!
    for _ in $(seq 20); do
        server_up && break
        sleep 0.5
    done
    server_up || { echo "error: ollama serve did not come up — see /tmp/ollama-serve.log" >&2; exit 1; }
fi

if ! curl -fsS -m 5 "$ENDPOINT/api/tags" | grep -q "\"$MODEL\""; then
    echo "==> pulling $MODEL (first run only)"
    OLLAMA_HOST="$ENDPOINT" ollama pull "$MODEL"
fi

# The same shape the selector sends: schema-constrained, temp 0, seeded. A model
# that free-texts its way around the schema shows up here rather than as an
# invalid-output rate halfway through a battery run.
echo "==> probing $MODEL"
REQUEST=$(cat <<JSON
{"model":"$MODEL","stream":false,
 "prompt":"You are the tactical commander of an NPC in a first-person shooter duel. Pick the mode it should be in right now.\nModes: Hunt, HoldCover, Retreat, Patrol.\nState: hp 40%, target visible at mid range, just took fire from the front.\nAnswer with JSON only.",
 "format":{"type":"object","properties":{"mode":{"type":"string","enum":["Hunt","HoldCover","Retreat","Patrol"]},"reason":{"type":"string"}},"required":["mode","reason"]},
 "options":{"temperature":0,"seed":1}}
JSON
)
START=$(date +%s.%N)
REPLY=$(curl -fsS -m 120 "$ENDPOINT/api/generate" -d "$REQUEST") || {
    echo "error: the probe call failed — see /tmp/ollama-serve.log" >&2; exit 1; }
ELAPSED=$(echo "$(date +%s.%N) - $START" | bc)

# Unloaded weights make the first call much slower than the steady state, so the
# budget is judged on a second one.
START=$(date +%s.%N)
REPLY=$(curl -fsS -m 120 "$ENDPOINT/api/generate" -d "$REQUEST")
WARM=$(echo "$(date +%s.%N) - $START" | bc)

ANSWER=$(echo "$REPLY" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("response",""))')
printf '    answer: %s\n' "$ANSWER"
printf '    latency: %.1fs cold, %.1fs warm (budget %ss)\n' "$ELAPSED" "$WARM" "$BUDGET_SECONDS"

if ! echo "$ANSWER" | grep -qE '"mode"[[:space:]]*:[[:space:]]*"(Hunt|HoldCover|Retreat|Patrol)"'; then
    echo "warning: the answer names no mode — the selector would retry, then hand over to its fallback" >&2
fi
if (( $(echo "$WARM > $BUDGET_SECONDS" | bc -l) )); then
    echo "warning: $MODEL is slower than the ${BUDGET_SECONDS}s decision budget — every call would time out." >&2
    echo "         Try a smaller model, or raise -llmTimeout and the driver's decisionPeriodSeconds together." >&2
fi

[[ "$PROBE_ONLY" == 1 ]] && exit 0

echo "==> PlayMode suite against $MODEL"
URLNPC_OLLAMA="$ENDPOINT" URLNPC_OLLAMA_MODEL="$MODEL" "$PROJECT_ROOT/scripts/run-tests.sh" playmode
