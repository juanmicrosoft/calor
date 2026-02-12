#!/usr/bin/env bash
# Verify: Console write effect
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for SayHello function
grep -q "SayHello" "$CALR_FILE" || { echo "SayHello function not found"; exit 1; }

# Check for console write effect §E{cw}
grep -q "§E{cw}" "$CALR_FILE" || { echo "Console write effect (§E{cw}) not found"; exit 1; }

# Check for method call to Console.WriteLine
grep -qE "(Console.WriteLine|Console.Write)" "$CALR_FILE" || { echo "Console write call not found"; exit 1; }

echo "Verification passed: SayHello function found with console write effect"
exit 0
