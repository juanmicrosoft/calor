#!/usr/bin/env bash
# Verify: Add simple function with no parameters
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

# Check that the file exists
[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for GetZero function
grep -q "GetZero" "$CALR_FILE" || { echo "GetZero function not found"; exit 1; }

# Check for i32 return type
grep -q "§O{i32}" "$CALR_FILE" || { echo "i32 return type not found"; exit 1; }

# Check for return statement with 0
grep -q "§R.*0" "$CALR_FILE" || { echo "Return 0 not found"; exit 1; }

echo "Verification passed: GetZero function found with correct structure"
exit 0
