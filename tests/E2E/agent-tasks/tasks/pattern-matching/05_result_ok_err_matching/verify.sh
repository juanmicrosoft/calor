#!/usr/bin/env bash
# Verify: Result Ok/Err matching
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for HandleResult function
grep -q "HandleResult" "$CALR_FILE" || { echo "HandleResult function not found"; exit 1; }

# Check for Result parameter type
grep -qE "Result<i32" "$CALR_FILE" || { echo "Result parameter type not found"; exit 1; }

# Check for Ok pattern §OK
grep -q "§OK" "$CALR_FILE" || { echo "Ok pattern (§OK) not found"; exit 1; }

# Check for Err pattern §ERR
grep -q "§ERR" "$CALR_FILE" || { echo "Err pattern (§ERR) not found"; exit 1; }

echo "Verification passed: HandleResult function found with Result matching"
exit 0
