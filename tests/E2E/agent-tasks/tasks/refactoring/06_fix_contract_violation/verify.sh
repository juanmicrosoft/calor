#!/usr/bin/env bash
# Verify: Fix contract violation
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/BuggyContracts.calr"

[[ -f "$CALR_FILE" ]] || { echo "BuggyContracts.calr not found"; exit 1; }

# Check for Abs function
grep -q "Abs" "$CALR_FILE" || { echo "Abs function not found"; exit 1; }

# Check that the postcondition uses >= not >
# The bug was (> result 0), the fix is (>= result 0)
if grep -q "§S.*(> result 0)" "$CALR_FILE"; then
    echo "Contract still has bug: (> result 0) should be (>= result 0)"
    exit 1
fi

# Check for the correct postcondition
grep -qE "§S.*(>=.*result.*0|result.*>=.*0)" "$CALR_FILE" || { echo "Fixed postcondition (>= result 0) not found"; exit 1; }

echo "Verification passed: Contract violation fixed"
exit 0
