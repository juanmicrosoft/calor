#!/usr/bin/env bash
# Verify: Variable with guard
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for DescribeNumber function
grep -q "DescribeNumber" "$CALR_FILE" || { echo "DescribeNumber function not found"; exit 1; }

# Check for switch
grep -q "§W{" "$CALR_FILE" || { echo "Switch statement not found"; exit 1; }

# Check for variable pattern §VAR
grep -q "§VAR{" "$CALR_FILE" || { echo "Variable pattern (§VAR) not found"; exit 1; }

# Check for guard §WHEN
grep -q "§WHEN" "$CALR_FILE" || { echo "Guard clause (§WHEN) not found"; exit 1; }

echo "Verification passed: DescribeNumber function found with variable pattern and guard"
exit 0
