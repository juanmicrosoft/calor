#!/usr/bin/env bash
# Verify: If-ElseIf-Else block
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for Classify function
grep -q "Classify" "$CALR_FILE" || { echo "Classify function not found"; exit 1; }

# Check for if statement syntax §IF{
grep -q "§IF{" "$CALR_FILE" || { echo "If statement (§IF) not found"; exit 1; }

# Check for else-if or else (§EI and §EL don't take attributes/braces)
grep -qE "(§EI |§EL)" "$CALR_FILE" || { echo "Else-if (§EI) or else (§EL) not found"; exit 1; }

# Check for if closing tag
grep -q "§/I{" "$CALR_FILE" || { echo "If closing tag not found"; exit 1; }

echo "Verification passed: Classify function found with if-elseif-else"
exit 0
