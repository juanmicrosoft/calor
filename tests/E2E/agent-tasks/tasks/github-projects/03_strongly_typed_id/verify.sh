#!/usr/bin/env bash
# Verify: Add Min function with symmetric contracts
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/MathUtils.calr"

[[ -f "$CALR_FILE" ]] || { echo "MathUtils.calr not found"; exit 1; }

# Check for Min function
grep -q "Min" "$CALR_FILE" || { echo "Min function not found"; exit 1; }

# Check for multiple postconditions (at least 2)
postcond_count=$(grep -c "§S" "$CALR_FILE" || echo "0")
if [[ "$postcond_count" -lt 2 ]]; then
    echo "Need at least 2 postconditions for Min"
    exit 1
fi

# Check for ternary operator
grep -qE "\\?" "$CALR_FILE" || { echo "Ternary operator not found"; exit 1; }

echo "Verification passed: Min function found with contracts"
exit 0
