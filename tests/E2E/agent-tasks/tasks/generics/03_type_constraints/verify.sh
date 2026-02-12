#!/usr/bin/env bash
# Verify: Type constraints
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Generic.calr"

[[ -f "$CALR_FILE" ]] || { echo "Generic.calr not found"; exit 1; }

# Check for Repository class
grep -q "Repository" "$CALR_FILE" || { echo "Repository class not found"; exit 1; }

# Check for generic class
grep -qE "§CL\{.*\}<T>" "$CALR_FILE" || { echo "Generic class not found"; exit 1; }

# Check for type constraint §WHERE
grep -q "§WHERE" "$CALR_FILE" || { echo "Type constraint (§WHERE) not found"; exit 1; }

# Check for class constraint
grep -qE "(class|struct|new\(\))" "$CALR_FILE" || { echo "Type constraint value (class/struct/new()) not found"; exit 1; }

echo "Verification passed: Repository generic class with type constraint found"
exit 0
