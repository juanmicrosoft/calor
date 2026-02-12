#!/usr/bin/env bash
# Verify: Add Sign function with nested ternary
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/MathUtils.calr"

[[ -f "$CALR_FILE" ]] || { echo "MathUtils.calr not found"; exit 1; }

# Check for Sign function
grep -q "Sign" "$CALR_FILE" || { echo "Sign function not found"; exit 1; }

# Check for i32 return type
grep -q "§O{i32}" "$CALR_FILE" || { echo "i32 return type not found"; exit 1; }

# Check for ternary operators (nested conditionals)
grep -qE "\\?" "$CALR_FILE" || { echo "Ternary operator not found"; exit 1; }

# Check for -1 representation (either (- 1) or -1)
grep -qE "(-.*1|\(- 1\))" "$CALR_FILE" || { echo "Negative 1 not found"; exit 1; }

echo "Verification passed: Sign function found"
exit 0
