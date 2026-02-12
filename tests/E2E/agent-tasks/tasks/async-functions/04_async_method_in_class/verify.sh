#!/usr/bin/env bash
# Verify: Async method in class
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for DataService class
grep -q "DataService" "$CALR_FILE" || { echo "DataService class not found"; exit 1; }

# Check for class definition §CL{
grep -q "§CL{" "$CALR_FILE" || { echo "Class definition (§CL) not found"; exit 1; }

# Check for async method §AMT{
grep -q "§AMT{" "$CALR_FILE" || { echo "Async method (§AMT) not found"; exit 1; }

# Check for LoadAsync method
grep -q "LoadAsync" "$CALR_FILE" || { echo "LoadAsync method not found"; exit 1; }

echo "Verification passed: DataService class found with async method"
exit 0
