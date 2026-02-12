#!/usr/bin/env bash
# Verify: Network effect
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for FetchUrl function
grep -q "FetchUrl" "$CALR_FILE" || { echo "FetchUrl function not found"; exit 1; }

# Check for network effect §E{net...}
grep -qE "§E\{net" "$CALR_FILE" || { echo "Network effect (§E{net}) not found"; exit 1; }

# Check for url parameter
grep -q "str:url" "$CALR_FILE" || { echo "url parameter not found"; exit 1; }

echo "Verification passed: FetchUrl function found with network effect"
exit 0
