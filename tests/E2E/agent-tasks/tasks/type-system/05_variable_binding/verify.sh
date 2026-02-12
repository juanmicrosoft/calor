#!/usr/bin/env bash
# Verify: Variable binding
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for ComputeWithTemp function
grep -q "ComputeWithTemp" "$CALR_FILE" || { echo "ComputeWithTemp function not found"; exit 1; }

# Check for variable binding §B{
grep -q "§B{" "$CALR_FILE" || { echo "Variable binding (§B) not found"; exit 1; }

# Check for multiple bindings or at least one
binding_count=$(grep -c "§B{" "$CALR_FILE" 2>/dev/null || echo "0")
if [[ $binding_count -lt 1 ]]; then
    echo "At least one variable binding expected"
    exit 1
fi

echo "Verification passed: ComputeWithTemp function found with variable bindings"
exit 0
