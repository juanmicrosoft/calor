#!/usr/bin/env bash
# Verify: Multiple combined effects
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for LogToFileAndConsole function
grep -q "LogToFileAndConsole" "$CALR_FILE" || { echo "LogToFileAndConsole function not found"; exit 1; }

# Check for effects declaration with comma (multiple effects)
grep -qE "§E\{[^}]*,[^}]*\}" "$CALR_FILE" || { echo "Multiple effects (comma-separated) not found"; exit 1; }

# Check for console write effect (cw)
grep -qE "§E\{[^}]*cw" "$CALR_FILE" || { echo "Console write effect not found in §E"; exit 1; }

# Check for file system effect (fs)
grep -qE "§E\{[^}]*fs" "$CALR_FILE" || { echo "File system effect not found in §E"; exit 1; }

echo "Verification passed: LogToFileAndConsole function found with multiple effects"
exit 0
