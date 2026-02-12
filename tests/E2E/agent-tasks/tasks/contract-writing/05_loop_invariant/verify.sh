#!/usr/bin/env bash
# Verify: Add loop invariant to function
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for SumToN function
grep -q "SumToN" "$CALR_FILE" || { echo "SumToN function not found"; exit 1; }

# Check for precondition n >= 0
grep -qE "§Q.*(>=.*n.*0|>=.*0|n.*>=)" "$CALR_FILE" || { echo "Precondition n >= 0 not found"; exit 1; }

# Check for postcondition result >= 0
grep -qE "§S.*(>=.*result.*0|result.*>=.*0)" "$CALR_FILE" || { echo "Postcondition result >= 0 not found"; exit 1; }

echo "Verification passed: SumToN function found with contracts"
exit 0
