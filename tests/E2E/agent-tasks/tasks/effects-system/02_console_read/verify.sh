#!/usr/bin/env bash
# Verify: Console read effect
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for ReadInput function
grep -q "ReadInput" "$CALR_FILE" || { echo "ReadInput function not found"; exit 1; }

# Check for console read effect §E{cr}
grep -q "§E{cr}" "$CALR_FILE" || { echo "Console read effect (§E{cr}) not found"; exit 1; }

# Check for Console.ReadLine call
grep -qE "Console.ReadLine" "$CALR_FILE" || { echo "Console.ReadLine call not found"; exit 1; }

echo "Verification passed: ReadInput function found with console read effect"
exit 0
