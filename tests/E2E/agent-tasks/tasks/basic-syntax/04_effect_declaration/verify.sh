#!/usr/bin/env bash
# Verify: Add function with effect declaration
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for PrintNumber or PrintResult function
grep -qE "(PrintNumber|PrintResult)" "$CALR_FILE" || { echo "Print function not found"; exit 1; }

# Check for void return type
grep -q "§O{void}" "$CALR_FILE" || { echo "void return type not found"; exit 1; }

# Check for effect declaration {cw} - be lenient about exact format
grep -qE "§E\{.*cw" "$CALR_FILE" || { echo "Console write effect {cw} not found"; exit 1; }

echo "Verification passed: Print function found with effect declaration"
exit 0
