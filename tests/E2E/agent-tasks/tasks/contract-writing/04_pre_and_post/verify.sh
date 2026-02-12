#!/usr/bin/env bash
# Verify: Add both precondition and postcondition to SafeDivide
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for SafeDivide function
grep -q "SafeDivide" "$CALR_FILE" || { echo "SafeDivide function not found"; exit 1; }

# Check for precondition (§Q)
grep -q "§Q" "$CALR_FILE" || { echo "Precondition (§Q) not found"; exit 1; }

# Check for b != 0 precondition
grep -qE "§Q.*(!=.*b.*0|!=.*0.*b|!= b 0)" "$CALR_FILE" || { echo "Precondition b != 0 not found"; exit 1; }

# Check for postcondition (§S)
grep -q "§S" "$CALR_FILE" || { echo "Postcondition (§S) not found"; exit 1; }

# Check for division in return
grep -qE "§R.*/.*" "$CALR_FILE" || { echo "Division not found in return"; exit 1; }

echo "Verification passed: SafeDivide function found with pre and postcondition"
exit 0
