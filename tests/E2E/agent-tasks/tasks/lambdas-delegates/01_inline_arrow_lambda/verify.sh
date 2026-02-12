#!/usr/bin/env bash
# Verify: Inline arrow lambda
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for ApplyDouble function
grep -q "ApplyDouble" "$CALR_FILE" || { echo "ApplyDouble function not found"; exit 1; }

# Check for arrow syntax → or ->
grep -qE "[→>-]" "$CALR_FILE" || { echo "Arrow lambda syntax (→) not found"; exit 1; }

# Check that it's using multiplication (doubling)
grep -qE "\*" "$CALR_FILE" || { echo "Multiplication operator not found"; exit 1; }

echo "Verification passed: ApplyDouble function found with arrow lambda"
exit 0
