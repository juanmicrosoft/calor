#!/usr/bin/env bash
# Verify: Strengthen postcondition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for AbsoluteValue function
grep -q "AbsoluteValue" "$CALR_FILE" || { echo "AbsoluteValue function not found"; exit 1; }

# Count postconditions - should have at least 2
postcount=$(grep -c "§S" "$CALR_FILE" 2>/dev/null || echo "0")
if [[ $postcount -lt 2 ]]; then
    echo "Expected at least 2 postconditions (strengthened)"
    exit 1
fi

# Check for non-negative postcondition
grep -qE "§S.*(>=.*result.*0|result.*>=.*0)" "$CALR_FILE" || { echo "Non-negative postcondition not found"; exit 1; }

echo "Verification passed: AbsoluteValue with strengthened postconditions found"
exit 0
