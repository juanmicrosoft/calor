#!/usr/bin/env bash
# Verify: File write effect
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for WriteFile function
grep -q "WriteFile" "$CALR_FILE" || { echo "WriteFile function not found"; exit 1; }

# Check for file system write effect §E{fs:w} or §E{fs}
grep -qE "§E\{fs(:w)?\}" "$CALR_FILE" || { echo "File system write effect (§E{fs:w}) not found"; exit 1; }

# Check for file write call
grep -qE "(File.WriteAllText|File.WriteAllLines|File.Write)" "$CALR_FILE" || { echo "File write call not found"; exit 1; }

echo "Verification passed: WriteFile function found with file write effect"
exit 0
