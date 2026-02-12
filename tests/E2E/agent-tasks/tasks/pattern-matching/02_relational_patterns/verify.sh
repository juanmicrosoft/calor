#!/usr/bin/env bash
# Verify: Relational patterns
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for ScoreCategory function
grep -q "ScoreCategory" "$CALR_FILE" || { echo "ScoreCategory function not found"; exit 1; }

# Check for switch syntax
grep -q "§W{" "$CALR_FILE" || { echo "Switch statement (§W) not found"; exit 1; }

# Check for relational pattern syntax §PREL
grep -q "§PREL{" "$CALR_FILE" || { echo "Relational pattern (§PREL) not found"; exit 1; }

echo "Verification passed: ScoreCategory function found with relational patterns"
exit 0
