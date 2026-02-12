#!/usr/bin/env bash
# Verify: Push/Put operations
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for BuildCollection function
grep -q "BuildCollection" "$CALR_FILE" || { echo "BuildCollection function not found"; exit 1; }

# Check for both §PUSH and §PUT
grep -q "§PUSH{" "$CALR_FILE" || { echo "Push operation (§PUSH) not found"; exit 1; }
grep -q "§PUT{" "$CALR_FILE" || { echo "Put operation (§PUT) not found"; exit 1; }

# Check for both list and dictionary
grep -q "§LIST{" "$CALR_FILE" || { echo "List creation not found"; exit 1; }
grep -q "§DICT{" "$CALR_FILE" || { echo "Dictionary creation not found"; exit 1; }

echo "Verification passed: BuildCollection function found with PUSH and PUT"
exit 0
