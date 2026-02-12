#!/usr/bin/env bash
# Verify: While loop countdown
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for Countdown function
grep -q "Countdown" "$CALR_FILE" || { echo "Countdown function not found"; exit 1; }

# Check for while loop syntax §WH{
grep -q "§WH{" "$CALR_FILE" || { echo "While loop (§WH) not found"; exit 1; }

# Check for while loop closing tag
grep -q "§/WH{" "$CALR_FILE" || { echo "While loop closing tag not found"; exit 1; }

# Check for variable binding or assignment
grep -qE "(§B\{|§ASSIGN)" "$CALR_FILE" || { echo "Variable binding/assignment not found"; exit 1; }

echo "Verification passed: Countdown function found with while loop"
exit 0
