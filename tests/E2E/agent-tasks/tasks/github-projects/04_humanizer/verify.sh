#!/usr/bin/env bash
# Verify: Add DivMod functions with division contracts
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/MathUtils.calr"

[[ -f "$CALR_FILE" ]] || { echo "MathUtils.calr not found"; exit 1; }

# Check for SafeDiv function
grep -q "SafeDiv" "$CALR_FILE" || { echo "SafeDiv function not found"; exit 1; }

# Check for SafeMod function
grep -q "SafeMod" "$CALR_FILE" || { echo "SafeMod function not found"; exit 1; }

# Check for precondition b != 0
grep -qE "§Q.*(!=.*b.*0|!=.*0.*b)" "$CALR_FILE" || { echo "Precondition b != 0 not found"; exit 1; }

# Check for division operator
grep -qE "§R.*/.*" "$CALR_FILE" || { echo "Division operator not found"; exit 1; }

# Check for modulo operator
grep -qE "§R.*%.*" "$CALR_FILE" || { echo "Modulo operator not found"; exit 1; }

echo "Verification passed: SafeDiv and SafeMod functions found"
exit 0
