#!/usr/bin/env bash
# Headless Unity Test Framework runner for URLNPC.
#
#   scripts/run-tests.sh [editmode|playmode|all]     (default: editmode)
#
# Uses the editor version pinned in ProjectSettings/ProjectVersion.txt from
# the Unity Hub install location; override with UNITY_BIN=/path/to/Unity.
# Results (NUnit XML + editor log) land in results/tests/ (gitignored).
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT_ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
UNITY_BIN="${UNITY_BIN:-$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity}"

if [[ ! -x "$UNITY_BIN" ]]; then
    echo "error: Unity $UNITY_VERSION not found at $UNITY_BIN (override with UNITY_BIN=...)" >&2
    exit 1
fi

# Unity allows a single instance per project — a batch run against an open
# project fails with an opaque error, so catch it early.
if [[ -f "$PROJECT_ROOT/Temp/UnityLockfile" ]]; then
    echo "error: the project looks open in the Unity editor (Temp/UnityLockfile exists)." >&2
    echo "Close the editor and retry." >&2
    exit 1
fi

RESULTS_DIR="$PROJECT_ROOT/results/tests"
mkdir -p "$RESULTS_DIR"

run_platform() {
    local platform="$1" # EditMode | PlayMode
    local assembly="URLNPC.Tests.$platform"
    local out="$RESULTS_DIR/$(echo "$platform" | tr '[:upper:]' '[:lower:]')"
    echo "==> $platform tests ($assembly)..."
    # -assemblyNames keeps the embedded ML-Agents package tests out of the run.
    # Never pass -runSeed here: it outranks the inspector seeds that the
    # reproducibility tests set up on purpose.
    if "$UNITY_BIN" -batchmode -projectPath "$PROJECT_ROOT" \
        -runTests -testPlatform "$platform" \
        -assemblyNames "$assembly" \
        -testResults "$out.xml" \
        -logFile "$out.log"; then
        echo "    PASSED — results: $out.xml"
    else
        echo "    FAILED — see $out.xml and $out.log" >&2
        return 1
    fi
}

case "${1:-editmode}" in
    editmode) run_platform EditMode ;;
    playmode) run_platform PlayMode ;;
    all)      run_platform EditMode && run_platform PlayMode ;;
    *)        echo "usage: $0 [editmode|playmode|all]" >&2; exit 2 ;;
esac
