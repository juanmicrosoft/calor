#!/usr/bin/env bash
# Verify: Option Some matching
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for UnwrapOrDefault function
grep -q "UnwrapOrDefault" "$CALR_FILE" || { echo "UnwrapOrDefault function not found"; exit 1; }

# Check for Option parameter type
grep -qE "Option<i32>" "$CALR_FILE" || { echo "Option<i32> parameter not found"; exit 1; }

# Check for Some pattern §SM
grep -q "§SM" "$CALR_FILE" || { echo "Some pattern (§SM) not found"; exit 1; }

# Check for None pattern §NN
grep -q "§NN" "$CALR_FILE" || { echo "None pattern (§NN) not found"; exit 1; }

echo "Verification passed: UnwrapOrDefault function found with Option matching"
exit 0
