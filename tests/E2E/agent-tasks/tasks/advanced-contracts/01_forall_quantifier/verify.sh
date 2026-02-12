#!/usr/bin/env bash
# Verify: Forall quantifier
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Contracts.calr"

[[ -f "$CALR_FILE" ]] || { echo "Contracts.calr not found"; exit 1; }

# Check for AllPositive function
grep -q "AllPositive" "$CALR_FILE" || { echo "AllPositive function not found"; exit 1; }

# Check for forall quantifier
grep -qE "\(forall " "$CALR_FILE" || { echo "Forall quantifier not found"; exit 1; }

# Check for array parameter
grep -qE "i32\[\]" "$CALR_FILE" || { echo "Array parameter type (i32[]) not found"; exit 1; }

echo "Verification passed: AllPositive function found with forall quantifier"
exit 0
