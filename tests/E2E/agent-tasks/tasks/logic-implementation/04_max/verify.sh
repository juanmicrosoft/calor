#!/usr/bin/env bash
# Verify: Implement Max with postcondition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Max function
grep -q "Max" "$CALR_FILE" || { echo "Max function not found"; exit 1; }

# Check for postconditions
postcond_count=$(grep -c "§S" "$CALR_FILE" || echo "0")
if [[ "$postcond_count" -lt 2 ]]; then
    echo "Need at least 2 postconditions for Max (result >= a, result >= b)"
    exit 1
fi

# Check for ternary or conditional
grep -qE "(\\?|§IF)" "$CALR_FILE" || { echo "Conditional logic not found"; exit 1; }

echo "Verification passed: Max function found with postconditions"
exit 0
