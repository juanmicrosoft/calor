#!/usr/bin/env bash
# Verify: Implement Clamp with contracts
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Clamp function
grep -q "Clamp" "$CALR_FILE" || { echo "Clamp function not found"; exit 1; }

# Check for three parameters (value, min, max)
# Count i32 inputs - should have at least 3 more than original (Add and Subtract have 4 total)
input_count=$(grep -c "§I{i32:" "$CALR_FILE" || echo "0")
if [[ "$input_count" -lt 5 ]]; then
    echo "Clamp needs 3 parameters (value, min, max)"
    exit 1
fi

# Check for precondition min <= max
grep -qE "§Q.*(<=.*min.*max|min.*<=.*max)" "$CALR_FILE" || { echo "Precondition min <= max not found"; exit 1; }

# Check for postconditions (result >= min and result <= max)
postcond_count=$(grep -c "§S" "$CALR_FILE" || echo "0")
if [[ "$postcond_count" -lt 2 ]]; then
    echo "Need at least 2 postconditions for Clamp"
    exit 1
fi

echo "Verification passed: Clamp function found with contracts"
exit 0
