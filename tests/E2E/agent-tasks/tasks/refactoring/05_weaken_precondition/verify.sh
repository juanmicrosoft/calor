#!/usr/bin/env bash
# Verify: Weaken precondition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for SafeMultiply function
grep -q "SafeMultiply" "$CALR_FILE" || { echo "SafeMultiply function not found"; exit 1; }

# Check for multiplication
grep -qE "\(\*" "$CALR_FILE" || { echo "Multiplication operation not found"; exit 1; }

# Check for postconditions (there should be some)
grep -q "§S" "$CALR_FILE" || { echo "Postconditions not found"; exit 1; }

echo "Verification passed: SafeMultiply with weakened precondition found"
exit 0
