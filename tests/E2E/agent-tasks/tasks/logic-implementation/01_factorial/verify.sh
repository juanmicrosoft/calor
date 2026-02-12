#!/usr/bin/env bash
# Verify: Implement Factorial
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Factorial function
grep -q "Factorial" "$CALR_FILE" || { echo "Factorial function not found"; exit 1; }

# Check for precondition
grep -q "§Q" "$CALR_FILE" || { echo "Precondition not found"; exit 1; }

# Check for postcondition ensuring result >= 1
grep -qE "§S.*(>=.*result.*1|result.*>=.*1)" "$CALR_FILE" || { echo "Postcondition result >= 1 not found"; exit 1; }

# Check that it handles some factorial values (2, 6, 24, or 120)
grep -qE "(120|24|6)" "$CALR_FILE" || { echo "Factorial values not found"; exit 1; }

echo "Verification passed: Factorial function found with contracts"
exit 0
