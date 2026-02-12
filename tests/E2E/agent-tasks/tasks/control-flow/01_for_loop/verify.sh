#!/usr/bin/env bash
# Verify: For loop printing numbers
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for PrintNumbers function
grep -q "PrintNumbers" "$CALR_FILE" || { echo "PrintNumbers function not found"; exit 1; }

# Check for for loop syntax §L{
grep -q "§L{" "$CALR_FILE" || { echo "For loop (§L) not found"; exit 1; }

# Check for console write effect
grep -q "§E{cw}" "$CALR_FILE" || { echo "Console write effect not found"; exit 1; }

# Check for loop closing tag
grep -q "§/L{" "$CALR_FILE" || { echo "For loop closing tag not found"; exit 1; }

echo "Verification passed: PrintNumbers function found with for loop"
exit 0
