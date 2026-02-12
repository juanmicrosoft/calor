#!/usr/bin/env bash
# Verify: Add function with two i32 parameters
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Multiply function
grep -q "Multiply" "$CALR_FILE" || { echo "Multiply function not found"; exit 1; }

# Check for two input parameters
grep -c "§I{i32:" "$CALR_FILE" | grep -q "[2-9]" || { echo "Need at least 2 i32 inputs"; exit 1; }

# Check for multiplication in return
grep -q "§R.*(\\*" "$CALR_FILE" || { echo "Multiplication not found in return"; exit 1; }

echo "Verification passed: Multiply function found with two parameters"
exit 0
