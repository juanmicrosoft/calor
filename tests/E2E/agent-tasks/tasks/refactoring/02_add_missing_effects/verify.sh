#!/usr/bin/env bash
# Verify: Add missing effects to function
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for ProcessAndLog function
grep -q "ProcessAndLog" "$CALR_FILE" || { echo "ProcessAndLog function not found"; exit 1; }

# Check for effects declaration with multiple effects
grep -qE "§E\{[^}]*,[^}]*\}" "$CALR_FILE" || { echo "Multiple effects declaration not found"; exit 1; }

# Check for file system effect
grep -qE "§E\{[^}]*fs" "$CALR_FILE" || { echo "File system effect not found"; exit 1; }

# Check for console write effect
grep -qE "§E\{[^}]*cw" "$CALR_FILE" || { echo "Console write effect not found"; exit 1; }

echo "Verification passed: ProcessAndLog with proper effects found"
exit 0
