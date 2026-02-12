#!/usr/bin/env bash
# Verify: Remove unnecessary effects
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Impure.calr"

[[ -f "$CALR_FILE" ]] || { echo "Impure.calr not found"; exit 1; }

# Check for JustCompute function
grep -q "JustCompute" "$CALR_FILE" || { echo "JustCompute function not found"; exit 1; }

# Verify JustCompute does NOT have effects (check lines between function start and return)
# Extract JustCompute function content and check for absence of §E
if grep -A 10 "JustCompute" "$CALR_FILE" | grep -B 5 "§/F" | grep -q "§E{"; then
    echo "JustCompute should be pure (no §E declaration)"
    exit 1
fi

echo "Verification passed: JustCompute pure function found"
exit 0
