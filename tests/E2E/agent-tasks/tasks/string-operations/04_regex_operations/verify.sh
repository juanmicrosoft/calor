#!/usr/bin/env bash
# Verify: Regex operations
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for IsValidEmail function
grep -q "IsValidEmail" "$CALR_FILE" || { echo "IsValidEmail function not found"; exit 1; }

# Check for regex-test operation
grep -qE "\(regex-test " "$CALR_FILE" || { echo "regex-test operation not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

echo "Verification passed: IsValidEmail function found with regex-test operation"
exit 0
