#!/usr/bin/env bash
# Verify: Implement Clamp function with contracts
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Clamp function
grep -q "Clamp" "$CALR_FILE" || { echo "Clamp function not found"; exit 1; }

# Check for i32 return type
grep -q "§O{i32}" "$CALR_FILE" || { echo "i32 return type not found"; exit 1; }

# Check for min and max parameters
grep -q "i32:min" "$CALR_FILE" || { echo "min parameter not found"; exit 1; }
grep -q "i32:max" "$CALR_FILE" || { echo "max parameter not found"; exit 1; }
grep -q "i32:value" "$CALR_FILE" || { echo "value parameter not found"; exit 1; }

# Check for ternary operator (nested conditional)
grep -qE "\?" "$CALR_FILE" || { echo "Ternary operator not found"; exit 1; }

echo "Verification passed: Clamp function found with parameters"
exit 0
