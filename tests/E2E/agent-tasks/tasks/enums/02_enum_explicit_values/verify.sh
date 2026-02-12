#!/usr/bin/env bash
# Verify: Enum with explicit values
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Enums.calr"

[[ -f "$CALR_FILE" ]] || { echo "Enums.calr not found"; exit 1; }

# Check for Color enum
grep -q "Color" "$CALR_FILE" || { echo "Color enum not found"; exit 1; }

# Check for enum definition
grep -q "§EN{" "$CALR_FILE" || { echo "Enum definition not found"; exit 1; }

# Check for explicit value assignment (= number)
grep -qE "= [0-9]+" "$CALR_FILE" || { echo "Explicit enum values (= N) not found"; exit 1; }

# Check for Red, Green, Blue values
grep -q "Red" "$CALR_FILE" || { echo "Red value not found"; exit 1; }
grep -q "Green" "$CALR_FILE" || { echo "Green value not found"; exit 1; }
grep -q "Blue" "$CALR_FILE" || { echo "Blue value not found"; exit 1; }

echo "Verification passed: Color enum with explicit values found"
exit 0
