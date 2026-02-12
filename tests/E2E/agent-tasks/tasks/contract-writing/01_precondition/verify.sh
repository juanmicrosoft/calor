#!/usr/bin/env bash
# Verify: Add precondition with REQ
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for SquareRoot function
grep -q "SquareRoot" "$CALR_FILE" || { echo "SquareRoot function not found"; exit 1; }

# Check for precondition (§Q)
grep -q "§Q" "$CALR_FILE" || { echo "Precondition (§Q) not found"; exit 1; }

# Check for >= 0 or > -1 condition on x
grep -qE "§Q.*((>=.*0)|(>.*-1)|(>=.*x.*0))" "$CALR_FILE" || { echo "Precondition x >= 0 not found"; exit 1; }

echo "Verification passed: SquareRoot function found with precondition"
exit 0
