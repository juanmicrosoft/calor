#!/usr/bin/env bash
# Verify: Collection count
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for GetListLength function
grep -q "GetListLength" "$CALR_FILE" || { echo "GetListLength function not found"; exit 1; }

# Check for count operation §CNT{
grep -q "§CNT{" "$CALR_FILE" || { echo "Count operation (§CNT) not found"; exit 1; }

# Check for list creation
grep -q "§LIST{" "$CALR_FILE" || { echo "List creation not found"; exit 1; }

echo "Verification passed: GetListLength function found with count operation"
exit 0
