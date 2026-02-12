#!/usr/bin/env bash
# Verify: Nested conditionals
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for GradeToLetter function
grep -q "GradeToLetter" "$CALR_FILE" || { echo "GradeToLetter function not found"; exit 1; }

# Check for multiple if statements (at least 2 different IDs or ternary operators)
if_count=$(grep -c "§IF{" "$CALR_FILE" 2>/dev/null || echo "0")
ternary_count=$(grep -c "(?" "$CALR_FILE" 2>/dev/null || echo "0")

if [[ $if_count -lt 2 && $ternary_count -lt 2 ]]; then
    echo "Not enough conditional structures (need nested ifs or ternaries)"
    exit 1
fi

# Check for grade parameter
grep -q "i32:grade" "$CALR_FILE" || { echo "grade parameter not found"; exit 1; }

echo "Verification passed: GradeToLetter function found with nested conditionals"
exit 0
