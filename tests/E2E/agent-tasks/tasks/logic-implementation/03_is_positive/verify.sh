#!/usr/bin/env bash
# Verify: Implement IsPositive with postcondition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for IsPositive function
grep -q "IsPositive" "$CALR_FILE" || { echo "IsPositive function not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

# Check for postcondition (§S)
grep -q "§S" "$CALR_FILE" || { echo "Postcondition (§S) not found"; exit 1; }

# Check for comparison with 0
grep -qE "(>.*0|>.*x.*0)" "$CALR_FILE" || { echo "Comparison > 0 not found"; exit 1; }

echo "Verification passed: IsPositive function found with postcondition"
exit 0
