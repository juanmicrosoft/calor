#!/usr/bin/env bash
# Verify: Method in class
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for Calculator class
grep -q "Calculator" "$CALR_FILE" || { echo "Calculator class not found"; exit 1; }

# Check for class definition
grep -q "§CL{" "$CALR_FILE" || { echo "Class definition not found"; exit 1; }

# Check for method definition §MT{
grep -q "§MT{" "$CALR_FILE" || { echo "Method definition (§MT) not found"; exit 1; }

# Check for Add method
grep -q "Add" "$CALR_FILE" || { echo "Add method not found"; exit 1; }

echo "Verification passed: Calculator class with Add method found"
exit 0
