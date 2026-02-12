#!/usr/bin/env bash
# Verify: Wildcard default
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for IsSingleDigit function
grep -q "IsSingleDigit" "$CALR_FILE" || { echo "IsSingleDigit function not found"; exit 1; }

# Check for switch
grep -q "§W{" "$CALR_FILE" || { echo "Switch statement not found"; exit 1; }

# Check for wildcard pattern (§K _ or §K _)
grep -qE "§K[[:space:]]+_" "$CALR_FILE" || { echo "Wildcard pattern (§K _) not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

echo "Verification passed: IsSingleDigit function found with wildcard default"
exit 0
