#!/usr/bin/env bash
# Verify: StringBuilder operations
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Check for BuildGreeting function
grep -q "BuildGreeting" "$CALR_FILE" || { echo "BuildGreeting function not found"; exit 1; }

# Check for sb-new operation
grep -qE "\(sb-new\)" "$CALR_FILE" || { echo "sb-new operation not found"; exit 1; }

# Check for sb-append operation
grep -qE "\(sb-append " "$CALR_FILE" || { echo "sb-append operation not found"; exit 1; }

echo "Verification passed: BuildGreeting function found with StringBuilder operations"
exit 0
