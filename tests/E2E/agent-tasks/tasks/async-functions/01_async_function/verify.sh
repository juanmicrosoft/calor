#!/usr/bin/env bash
# Verify: Async function definition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/AsyncOps.calr"

[[ -f "$CALR_FILE" ]] || { echo "AsyncOps.calr not found"; exit 1; }

# Check for GetDataAsync function
grep -q "GetDataAsync" "$CALR_FILE" || { echo "GetDataAsync function not found"; exit 1; }

# Check for async function syntax §AF{
grep -q "§AF{" "$CALR_FILE" || { echo "Async function (§AF) not found"; exit 1; }

# Check for Task return type
grep -qE "§O\{Task<" "$CALR_FILE" || { echo "Task<> return type not found"; exit 1; }

echo "Verification passed: GetDataAsync async function found"
exit 0
