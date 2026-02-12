#!/usr/bin/env bash
# Verify: Delegate definition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for MathOperation delegate
grep -q "MathOperation" "$CALR_FILE" || { echo "MathOperation delegate not found"; exit 1; }

# Check for delegate definition §DEL{
grep -q "§DEL{" "$CALR_FILE" || { echo "Delegate definition (§DEL) not found"; exit 1; }

# Check for delegate closing tag
grep -q "§/DEL{" "$CALR_FILE" || { echo "Delegate closing tag not found"; exit 1; }

echo "Verification passed: MathOperation delegate definition found"
exit 0
