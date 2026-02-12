#!/usr/bin/env bash
# Verify: Do-while loop
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for DoWhileDemo function
grep -q "DoWhileDemo" "$CALR_FILE" || { echo "DoWhileDemo function not found"; exit 1; }

# Check for do-while loop syntax §DO{
grep -q "§DO{" "$CALR_FILE" || { echo "Do-while loop (§DO) not found"; exit 1; }

# Check for do-while loop closing tag
grep -q "§/DO{" "$CALR_FILE" || { echo "Do-while loop closing tag not found"; exit 1; }

echo "Verification passed: DoWhileDemo function found with do-while loop"
exit 0
