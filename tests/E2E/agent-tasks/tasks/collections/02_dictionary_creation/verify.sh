#!/usr/bin/env bash
# Verify: Dictionary with KV pairs
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for CreateScores function
grep -q "CreateScores" "$CALR_FILE" || { echo "CreateScores function not found"; exit 1; }

# Check for dictionary creation §DICT{
grep -q "§DICT{" "$CALR_FILE" || { echo "Dictionary creation (§DICT) not found"; exit 1; }

# Check for put operation §PUT{
grep -q "§PUT{" "$CALR_FILE" || { echo "Put operation (§PUT) not found"; exit 1; }

echo "Verification passed: CreateScores function found with dictionary operations"
exit 0
