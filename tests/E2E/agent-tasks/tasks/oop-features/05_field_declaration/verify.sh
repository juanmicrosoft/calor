#!/usr/bin/env bash
# Verify: Field declaration
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for Counter class
grep -q "Counter" "$CALR_FILE" || { echo "Counter class not found"; exit 1; }

# Check for field definition §FLD{
grep -q "§FLD{" "$CALR_FILE" || { echo "Field definition (§FLD) not found"; exit 1; }

# Check for _count field
grep -qE "_count" "$CALR_FILE" || { echo "_count field not found"; exit 1; }

echo "Verification passed: Counter class with _count field found"
exit 0
