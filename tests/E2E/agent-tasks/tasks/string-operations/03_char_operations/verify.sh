#!/usr/bin/env bash
# Verify: Character operations
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for GetFirstChar function
grep -q "GetFirstChar" "$CALR_FILE" || { echo "GetFirstChar function not found"; exit 1; }

# Check for char-at operation
grep -qE "\(char-at " "$CALR_FILE" || { echo "char-at operation not found"; exit 1; }

# Check for char return type
grep -q "§O{char}" "$CALR_FILE" || { echo "char return type not found"; exit 1; }

echo "Verification passed: GetFirstChar function found with char-at operation"
exit 0
