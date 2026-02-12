#!/usr/bin/env bash
# Verify: Option None return
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for GetNothing function
grep -q "GetNothing" "$CALR_FILE" || { echo "GetNothing function not found"; exit 1; }

# Check for Option return type
grep -qE "§O\{Option<i32>\}" "$CALR_FILE" || { echo "Option<i32> return type not found"; exit 1; }

# Check for None return §NN
grep -q "§NN" "$CALR_FILE" || { echo "None return (§NN) not found"; exit 1; }

echo "Verification passed: GetNothing function found with Option None return"
exit 0
