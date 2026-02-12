#!/usr/bin/env bash
# Verify: ConfigureAwait false
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/AsyncOps.calr"

[[ -f "$CALR_FILE" ]] || { echo "AsyncOps.calr not found"; exit 1; }

# Check for BackgroundProcessAsync function
grep -q "BackgroundProcessAsync" "$CALR_FILE" || { echo "BackgroundProcessAsync function not found"; exit 1; }

# Check for ConfigureAwait false syntax §AWAIT{false}
grep -q "§AWAIT{false}" "$CALR_FILE" || { echo "ConfigureAwait false (§AWAIT{false}) not found"; exit 1; }

echo "Verification passed: BackgroundProcessAsync function found with ConfigureAwait false"
exit 0
