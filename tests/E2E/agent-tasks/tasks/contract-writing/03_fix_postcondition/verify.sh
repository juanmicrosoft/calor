#!/usr/bin/env bash
# Verify: Fix failing postcondition (Abs function)
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/BuggyContracts.calr"

[[ -f "$CALR_FILE" ]] || { echo "BuggyContracts.calr not found"; exit 1; }

# Check that the postcondition uses >= instead of >
# The bug was (> result 0) should be (>= result 0)
if grep -q "(> result 0)" "$CALR_FILE"; then
    echo "FAIL: Postcondition still uses (> result 0) - should be (>= result 0)"
    exit 1
fi

# Check for the correct postcondition
grep -q "(>= result 0)" "$CALR_FILE" || { echo "Correct postcondition (>= result 0) not found"; exit 1; }

echo "Verification passed: Postcondition fixed to (>= result 0)"
exit 0
