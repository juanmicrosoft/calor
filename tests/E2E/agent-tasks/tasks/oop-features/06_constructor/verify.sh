#!/usr/bin/env bash
# Verify: Constructor
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for Point class
grep -q "Point" "$CALR_FILE" || { echo "Point class not found"; exit 1; }

# Check for constructor §CTOR
grep -q "§CTOR" "$CALR_FILE" || { echo "Constructor (§CTOR) not found"; exit 1; }

# Check for constructor closing tag
grep -q "§/CTOR" "$CALR_FILE" || { echo "Constructor closing tag not found"; exit 1; }

echo "Verification passed: Point class with constructor found"
exit 0
