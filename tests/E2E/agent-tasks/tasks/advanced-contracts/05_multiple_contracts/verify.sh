#!/usr/bin/env bash
# Verify: Multiple pre/post contracts
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Contracts.calr"

[[ -f "$CALR_FILE" ]] || { echo "Contracts.calr not found"; exit 1; }

# Check for ClampValue function
grep -q "ClampValue" "$CALR_FILE" || { echo "ClampValue function not found"; exit 1; }

# Count preconditions
precount=$(grep -c "§Q" "$CALR_FILE" 2>/dev/null || echo "0")
if [[ $precount -lt 1 ]]; then
    echo "At least one precondition expected"
    exit 1
fi

# Count postconditions
postcount=$(grep -c "§S" "$CALR_FILE" 2>/dev/null || echo "0")
if [[ $postcount -lt 2 ]]; then
    echo "At least two postconditions expected"
    exit 1
fi

echo "Verification passed: ClampValue function found with multiple contracts"
exit 0
