#!/usr/bin/env bash
# Verify: HashSet creation
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for UniqueCount function
grep -q "UniqueCount" "$CALR_FILE" || { echo "UniqueCount function not found"; exit 1; }

# Check for HashSet creation §HSET{
grep -q "§HSET{" "$CALR_FILE" || { echo "HashSet creation (§HSET) not found"; exit 1; }

# Check for add operation §ADD{
grep -q "§ADD{" "$CALR_FILE" || { echo "Add operation (§ADD) not found"; exit 1; }

echo "Verification passed: UniqueCount function found with HashSet operations"
exit 0
