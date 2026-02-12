#!/usr/bin/env bash
# Verify: Enum extension
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Enums.calr"

[[ -f "$CALR_FILE" ]] || { echo "Enums.calr not found"; exit 1; }

# Check for Priority enum
grep -q "Priority" "$CALR_FILE" || { echo "Priority enum not found"; exit 1; }

# Check for enum definition
grep -q "§EN{" "$CALR_FILE" || { echo "Enum definition not found"; exit 1; }

# Check for enum extension §EEXT{
grep -q "§EEXT{" "$CALR_FILE" || { echo "Enum extension (§EEXT) not found"; exit 1; }

# Check for IsUrgent method
grep -q "IsUrgent" "$CALR_FILE" || { echo "IsUrgent method not found"; exit 1; }

echo "Verification passed: Priority enum with extension found"
exit 0
