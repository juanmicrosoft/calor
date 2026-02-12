#!/usr/bin/env bash
# Verify: String length/upper/lower
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for StringLength function
grep -q "StringLength" "$CALR_FILE" || { echo "StringLength function not found"; exit 1; }

# Check for len operation
grep -qE "\(len " "$CALR_FILE" || { echo "len operation not found"; exit 1; }

# Check for str parameter type
grep -q "str:" "$CALR_FILE" || { echo "str parameter type not found"; exit 1; }

echo "Verification passed: StringLength function found with len operation"
exit 0
