#!/usr/bin/env bash
# Verify: Add InRange function with boundary contracts
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/MathUtils.calr"

[[ -f "$CALR_FILE" ]] || { echo "MathUtils.calr not found"; exit 1; }

# Check for InRange function
grep -q "InRange" "$CALR_FILE" || { echo "InRange function not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

# Check for precondition (min <= max)
grep -q "§Q" "$CALR_FILE" || { echo "Precondition not found"; exit 1; }

# Check for postcondition
grep -q "§S" "$CALR_FILE" || { echo "Postcondition not found"; exit 1; }

echo "Verification passed: InRange function found with contracts"
exit 0
