#!/bin/sh
# Compiles the AppKit-free logic files together with the checks and runs them.
set -e
cd "$(dirname "$0")/.."
out="${TMPDIR:-/tmp}/screentune-logic-tests"
swiftc -swift-version 5 \
    ScreenTune/DisplaySettings.swift \
    ScreenTune/SettingsStore.swift \
    Tests/main.swift \
    -o "$out"
"$out"
