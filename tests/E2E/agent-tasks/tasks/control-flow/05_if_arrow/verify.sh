#!/usr/bin/env bash
# Verify: If with arrow syntax
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Max function
grep -q "Max" "$CALR_FILE" || { echo "Max function not found"; exit 1; }

# Check for arrow if syntax (§IF ... →) - allow both arrow character and ->
grep -qE "§IF.*[→>-]" "$CALR_FILE" || { echo "Arrow if syntax (§IF ... →) not found"; exit 1; }

# Check for i32 parameters a and b
grep -q "i32:a" "$CALR_FILE" || { echo "Parameter a not found"; exit 1; }
grep -q "i32:b" "$CALR_FILE" || { echo "Parameter b not found"; exit 1; }

echo "Verification passed: Max function found with arrow if syntax"
exit 0
