#!/usr/bin/env bash
# Verify: Switch with literals
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for DayName function
grep -q "DayName" "$CALR_FILE" || { echo "DayName function not found"; exit 1; }

# Check for switch syntax §W{
grep -q "§W{" "$CALR_FILE" || { echo "Switch statement (§W) not found"; exit 1; }

# Check for case syntax §K
grep -q "§K" "$CALR_FILE" || { echo "Case pattern (§K) not found"; exit 1; }

# Check for switch closing tag
grep -q "§/W{" "$CALR_FILE" || { echo "Switch closing tag not found"; exit 1; }

echo "Verification passed: DayName function found with switch"
exit 0
