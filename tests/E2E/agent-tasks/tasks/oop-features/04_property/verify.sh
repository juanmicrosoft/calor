#!/usr/bin/env bash
# Verify: Property with getter/setter
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for Product class
grep -q "Product" "$CALR_FILE" || { echo "Product class not found"; exit 1; }

# Check for property definition §PROP{
grep -q "§PROP{" "$CALR_FILE" || { echo "Property definition (§PROP) not found"; exit 1; }

# Check for Price property
grep -q "Price" "$CALR_FILE" || { echo "Price property not found"; exit 1; }

echo "Verification passed: Product class with Price property found"
exit 0
