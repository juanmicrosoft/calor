#!/usr/bin/env bash
# Verify: Loop with conditional
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for PrintEvenNumbers function
grep -q "PrintEvenNumbers" "$CALR_FILE" || { echo "PrintEvenNumbers function not found"; exit 1; }

# Check for a loop (for, while, or foreach)
grep -qE "(§L\{|§WH\{|§EACH)" "$CALR_FILE" || { echo "Loop structure not found"; exit 1; }

# Check for conditional (if or ternary)
grep -qE "(§IF|%.*2)" "$CALR_FILE" || { echo "Conditional or modulo check not found"; exit 1; }

# Check for console write effect
grep -q "§E{cw}" "$CALR_FILE" || { echo "Console write effect not found"; exit 1; }

echo "Verification passed: PrintEvenNumbers function found with loop and conditional"
exit 0
