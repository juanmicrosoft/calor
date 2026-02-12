#!/usr/bin/env bash
# Verify: Convert C# idiom to Calor pattern
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for TryParse function (any variation)
grep -qE "TryParse" "$CALR_FILE" || { echo "TryParse function not found"; exit 1; }

# Check for Option return type
grep -qE "§O\{Option<" "$CALR_FILE" || { echo "Option return type not found"; exit 1; }

# Check for Some (§SM) usage
grep -q "§SM" "$CALR_FILE" || { echo "Some (§SM) not found"; exit 1; }

# Check for None (§NN) usage
grep -q "§NN" "$CALR_FILE" || { echo "None (§NN) not found"; exit 1; }

echo "Verification passed: C# idiom converted to Calor Option pattern"
exit 0
