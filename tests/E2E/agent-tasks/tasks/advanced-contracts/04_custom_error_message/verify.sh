#!/usr/bin/env bash
# Verify: Custom error message
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Contracts.calr"

[[ -f "$CALR_FILE" ]] || { echo "Contracts.calr not found"; exit 1; }

# Check for Divide function
grep -q "Divide" "$CALR_FILE" || { echo "Divide function not found"; exit 1; }

# Check for precondition with custom message §Q{"..."}
grep -qE '§Q\{".+"' "$CALR_FILE" || { echo "Precondition with custom message (§Q{\"...\"}) not found"; exit 1; }

# Check for division operation
grep -qE "\(/ " "$CALR_FILE" || { echo "Division operation not found"; exit 1; }

echo "Verification passed: Divide function found with custom error message"
exit 0
