#!/usr/bin/env bash
# Verify: Variable assignment
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Accumulate function
grep -q "Accumulate" "$CALR_FILE" || { echo "Accumulate function not found"; exit 1; }

# Check for variable binding §B{
grep -q "§B{" "$CALR_FILE" || { echo "Variable binding (§B) not found"; exit 1; }

# Check for variable assignment §ASSIGN
grep -q "§ASSIGN" "$CALR_FILE" || { echo "Variable assignment (§ASSIGN) not found"; exit 1; }

echo "Verification passed: Accumulate function found with variable assignment"
exit 0
