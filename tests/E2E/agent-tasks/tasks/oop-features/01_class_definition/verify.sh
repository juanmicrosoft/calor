#!/usr/bin/env bash
# Verify: Class definition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for Person class
grep -q "Person" "$CALR_FILE" || { echo "Person class not found"; exit 1; }

# Check for class definition §CL{
grep -q "§CL{" "$CALR_FILE" || { echo "Class definition (§CL) not found"; exit 1; }

# Check for class closing tag
grep -q "§/CL{" "$CALR_FILE" || { echo "Class closing tag not found"; exit 1; }

echo "Verification passed: Person class definition found"
exit 0
