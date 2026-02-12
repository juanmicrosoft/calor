#!/usr/bin/env bash
# Verify: Add postcondition with ENS
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Abs function
grep -q "Abs" "$CALR_FILE" || { echo "Abs function not found"; exit 1; }

# Check for postcondition (§S)
grep -q "§S" "$CALR_FILE" || { echo "Postcondition (§S) not found"; exit 1; }

# Check for result >= 0 postcondition
grep -qE "§S.*(>=.*result.*0|>=.*0.*result|result.*>=.*0)" "$CALR_FILE" || { echo "Postcondition result >= 0 not found"; exit 1; }

# Check for conditional return (ternary or if)
grep -qE "(\\?.*<|§IF)" "$CALR_FILE" || { echo "Conditional logic not found"; exit 1; }

echo "Verification passed: Abs function found with postcondition"
exit 0
