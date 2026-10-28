#!/usr/bin/env bash
# Thin entry point for the A/B batch harness (issue #136) — see ab.py for the
# orchestration, scripts/eval.sh for the per-match run it drives.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/ab.py" "$@"
