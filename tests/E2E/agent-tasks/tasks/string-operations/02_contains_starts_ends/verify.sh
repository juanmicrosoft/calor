#!/usr/bin/env bash
# Verify: String contains/starts/ends
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for StringContains function
grep -q "StringContains" "$CALR_FILE" || { echo "StringContains function not found"; exit 1; }

# Check for contains operation
grep -qE "\(contains " "$CALR_FILE" || { echo "contains operation not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

echo "Verification passed: StringContains function found with contains operation"
exit 0
