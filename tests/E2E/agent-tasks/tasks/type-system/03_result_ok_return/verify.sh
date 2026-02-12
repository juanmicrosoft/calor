#!/usr/bin/env bash
# Verify: Result Ok return
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for SafeDivide function
grep -q "SafeDivide" "$CALR_FILE" || { echo "SafeDivide function not found"; exit 1; }

# Check for Result return type
grep -qE "§O\{Result<i32" "$CALR_FILE" || { echo "Result return type not found"; exit 1; }

# Check for Ok return §OK
grep -q "§OK" "$CALR_FILE" || { echo "Ok return (§OK) not found"; exit 1; }

echo "Verification passed: SafeDivide function found with Result Ok return"
exit 0
