#!/usr/bin/env bash
# Verify: List creation and iteration
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for SumList function
grep -q "SumList" "$CALR_FILE" || { echo "SumList function not found"; exit 1; }

# Check for list creation §LIST{
grep -q "§LIST{" "$CALR_FILE" || { echo "List creation (§LIST) not found"; exit 1; }

# Check for push operation
grep -q "§PUSH{" "$CALR_FILE" || { echo "Push operation (§PUSH) not found"; exit 1; }

# Check for iteration (§EACH or §L)
grep -qE "(§EACH\{|§L\{)" "$CALR_FILE" || { echo "Iteration (§EACH or §L) not found"; exit 1; }

echo "Verification passed: SumList function found with list operations"
exit 0
