#!/usr/bin/env bash
# Verify: Implement IsEven with boolean postcondition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for IsEven function
grep -q "IsEven" "$CALR_FILE" || { echo "IsEven function not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

# Check for modulo operator
grep -qE "%" "$CALR_FILE" || { echo "Modulo operator not found"; exit 1; }

# Check for comparison with 0 or 2
grep -qE "(== .* 0|== .* 2)" "$CALR_FILE" || { echo "Comparison with 0 or 2 not found"; exit 1; }

echo "Verification passed: IsEven function found"
exit 0
