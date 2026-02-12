#!/usr/bin/env bash
# Verify: Implication in contract
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Contracts.calr"

[[ -f "$CALR_FILE" ]] || { echo "Contracts.calr not found"; exit 1; }

# Check for SafeIncrement function
grep -q "SafeIncrement" "$CALR_FILE" || { echo "SafeIncrement function not found"; exit 1; }

# Check for implication syntax (->
grep -qE "\(-> " "$CALR_FILE" || { echo "Implication syntax (->) not found"; exit 1; }

# Check for postcondition §S
grep -q "§S" "$CALR_FILE" || { echo "Postcondition (§S) not found"; exit 1; }

echo "Verification passed: SafeIncrement function found with implication"
exit 0
