#!/usr/bin/env bash
# Verify: Foreach over dictionary
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for PrintDictionary function
grep -q "PrintDictionary" "$CALR_FILE" || { echo "PrintDictionary function not found"; exit 1; }

# Check for foreach-kv syntax §EACHKV{
grep -q "§EACHKV{" "$CALR_FILE" || { echo "Foreach-kv (§EACHKV) not found"; exit 1; }

# Check for Dictionary parameter type
grep -qE "(Dictionary|dict)" "$CALR_FILE" || { echo "Dictionary parameter not found"; exit 1; }

# Check for closing tag
grep -q "§/EACHKV{" "$CALR_FILE" || { echo "Foreach-kv closing tag not found"; exit 1; }

echo "Verification passed: PrintDictionary function found with foreach-kv"
exit 0
