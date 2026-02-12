#!/usr/bin/env bash
# Verify: Extract pure function from impure
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Impure.calr"

[[ -f "$CALR_FILE" ]] || { echo "Impure.calr not found"; exit 1; }

# Check for DoubleValue function (the new pure function)
grep -q "DoubleValue" "$CALR_FILE" || { echo "DoubleValue function not found"; exit 1; }

# Check that LogAndDouble still exists
grep -q "LogAndDouble" "$CALR_FILE" || { echo "LogAndDouble function not found"; exit 1; }

# Check that LogAndDouble has effects
grep -A 5 "LogAndDouble" "$CALR_FILE" | grep -q "§E{cw}" || { echo "LogAndDouble should still have §E{cw} effect"; exit 1; }

# Count total functions - should have at least 2
func_count=$(grep -c "§F{" "$CALR_FILE" 2>/dev/null || echo "0")
if [[ $func_count -lt 2 ]]; then
    echo "Expected at least 2 functions after extraction"
    exit 1
fi

echo "Verification passed: Pure function extracted successfully"
exit 0
