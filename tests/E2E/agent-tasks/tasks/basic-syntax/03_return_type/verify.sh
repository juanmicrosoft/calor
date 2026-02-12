#!/usr/bin/env bash
# Verify: Add function with explicit return type
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for IsPositive function
grep -q "IsPositive" "$CALR_FILE" || { echo "IsPositive function not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

# Check for comparison with 0 (> x 0) or similar
grep -qE "(>|<|>=|<=).*0" "$CALR_FILE" || { echo "Comparison with 0 not found"; exit 1; }

echo "Verification passed: IsPositive function found with bool return type"
exit 0
