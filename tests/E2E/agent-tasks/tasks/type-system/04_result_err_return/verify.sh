#!/usr/bin/env bash
# Verify: Result Err return
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for AlwaysFail function
grep -q "AlwaysFail" "$CALR_FILE" || { echo "AlwaysFail function not found"; exit 1; }

# Check for Result return type
grep -qE "§O\{Result<i32" "$CALR_FILE" || { echo "Result return type not found"; exit 1; }

# Check for Err return §ERR
grep -q "§ERR" "$CALR_FILE" || { echo "Err return (§ERR) not found"; exit 1; }

echo "Verification passed: AlwaysFail function found with Result Err return"
exit 0
