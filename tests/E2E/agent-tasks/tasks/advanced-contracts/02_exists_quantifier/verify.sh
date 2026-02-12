#!/usr/bin/env bash
# Verify: Exists quantifier
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Contracts.calr"

[[ -f "$CALR_FILE" ]] || { echo "Contracts.calr not found"; exit 1; }

# Check for HasNegative function
grep -q "HasNegative" "$CALR_FILE" || { echo "HasNegative function not found"; exit 1; }

# Check for exists quantifier
grep -qE "\(exists " "$CALR_FILE" || { echo "Exists quantifier not found"; exit 1; }

# Check for array parameter
grep -qE "i32\[\]" "$CALR_FILE" || { echo "Array parameter type (i32[]) not found"; exit 1; }

echo "Verification passed: HasNegative function found with exists quantifier"
exit 0
