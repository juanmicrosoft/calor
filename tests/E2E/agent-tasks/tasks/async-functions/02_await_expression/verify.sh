#!/usr/bin/env bash
# Verify: Await expression
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/AsyncOps.calr"

[[ -f "$CALR_FILE" ]] || { echo "AsyncOps.calr not found"; exit 1; }

# Check for ProcessAsync function
grep -q "ProcessAsync" "$CALR_FILE" || { echo "ProcessAsync function not found"; exit 1; }

# Check for await syntax §AWAIT
grep -q "§AWAIT" "$CALR_FILE" || { echo "Await expression (§AWAIT) not found"; exit 1; }

# Check for async function definition
grep -q "§AF{" "$CALR_FILE" || { echo "Async function (§AF) not found"; exit 1; }

echo "Verification passed: ProcessAsync function found with await"
exit 0
