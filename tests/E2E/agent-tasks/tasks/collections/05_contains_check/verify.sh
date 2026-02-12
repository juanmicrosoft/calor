#!/usr/bin/env bash
# Verify: Contains check
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for HasValue function
grep -q "HasValue" "$CALR_FILE" || { echo "HasValue function not found"; exit 1; }

# Check for contains check §HAS{
grep -q "§HAS{" "$CALR_FILE" || { echo "Contains check (§HAS) not found"; exit 1; }

# Check for bool return type
grep -q "§O{bool}" "$CALR_FILE" || { echo "bool return type not found"; exit 1; }

echo "Verification passed: HasValue function found with contains check"
exit 0
