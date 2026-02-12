#!/usr/bin/env bash
# Verify: Add contracts to uncontracted code
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Impure.calr"

[[ -f "$CALR_FILE" ]] || { echo "Impure.calr not found"; exit 1; }

# Check for NoContractAdd function
grep -q "NoContractAdd" "$CALR_FILE" || { echo "NoContractAdd function not found"; exit 1; }

# Check that contracts were added - look in the function body
# Get the function content
func_content=$(sed -n '/NoContractAdd/,/§\/F/p' "$CALR_FILE")

# Check for at least one precondition or postcondition in the function
if echo "$func_content" | grep -qE "(§Q|§S)"; then
    echo "Verification passed: Contracts added to NoContractAdd"
    exit 0
else
    echo "No contracts (§Q or §S) found in NoContractAdd function"
    exit 1
fi
