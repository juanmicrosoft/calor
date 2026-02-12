#!/usr/bin/env bash
# Verify: Enum definition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Enums.calr"

[[ -f "$CALR_FILE" ]] || { echo "Enums.calr not found"; exit 1; }

# Check for Status enum
grep -q "Status" "$CALR_FILE" || { echo "Status enum not found"; exit 1; }

# Check for enum definition §EN{
grep -q "§EN{" "$CALR_FILE" || { echo "Enum definition (§EN) not found"; exit 1; }

# Check for enum closing tag
grep -q "§/EN{" "$CALR_FILE" || { echo "Enum closing tag not found"; exit 1; }

# Check for at least some enum values
grep -qE "(Pending|InProgress|Completed)" "$CALR_FILE" || { echo "Enum values not found"; exit 1; }

echo "Verification passed: Status enum definition found"
exit 0
