#!/usr/bin/env bash
# Verify: Option Some return
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for TryDouble function
grep -q "TryDouble" "$CALR_FILE" || { echo "TryDouble function not found"; exit 1; }

# Check for Option return type
grep -qE "§O\{Option<i32>\}" "$CALR_FILE" || { echo "Option<i32> return type not found"; exit 1; }

# Check for Some return §SM
grep -q "§SM" "$CALR_FILE" || { echo "Some return (§SM) not found"; exit 1; }

echo "Verification passed: TryDouble function found with Option Some return"
exit 0
