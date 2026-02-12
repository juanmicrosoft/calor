#!/usr/bin/env bash
# Verify: Block lambda
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for ApplyComplex function
grep -q "ApplyComplex" "$CALR_FILE" || { echo "ApplyComplex function not found"; exit 1; }

# Check for block lambda syntax §LAM{
grep -q "§LAM{" "$CALR_FILE" || { echo "Block lambda (§LAM) not found"; exit 1; }

# Check for lambda closing tag
grep -q "§/LAM{" "$CALR_FILE" || { echo "Block lambda closing tag not found"; exit 1; }

echo "Verification passed: ApplyComplex function found with block lambda"
exit 0
