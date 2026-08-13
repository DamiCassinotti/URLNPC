#!/usr/bin/env bash
# Headless standalone build for URLNPC.
#
#   scripts/build-player.sh
#
# Builds the FPS scene into a StandaloneLinux64 player at
# Builds/Linux/URLNPC.x86_64 (gitignored), for ML-Agents --env training runs.
# Uses the editor version pinned in ProjectSettings/ProjectVersion.txt from the
# Unity Hub install location; override with UNITY_BIN=/path/to/Unity.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT_ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
UNITY_BIN="${UNITY_BIN:-$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity}"

if [[ ! -x "$UNITY_BIN" ]]; then
    echo "error: Unity $UNITY_VERSION not found at $UNITY_BIN (override with UNITY_BIN=...)" >&2
    exit 1
fi

# Unity allows a single instance per project — a batch build against an open
# project fails with an opaque error, so catch it early.
if [[ -f "$PROJECT_ROOT/Temp/UnityLockfile" ]]; then
    echo "error: the project looks open in the Unity editor (Temp/UnityLockfile exists)." >&2
    echo "Close the editor and retry." >&2
    exit 1
fi

LOG="$PROJECT_ROOT/Builds/build.log"
mkdir -p "$PROJECT_ROOT/Builds"

echo "==> Building StandaloneLinux64 player..."
if "$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$PROJECT_ROOT" \
    -executeMethod BuildPlayer.BuildLinux \
    -logFile "$LOG"; then
    echo "    PASSED — player: $PROJECT_ROOT/Builds/Linux/URLNPC.x86_64"
else
    echo "    FAILED — see $LOG" >&2
    exit 1
fi
