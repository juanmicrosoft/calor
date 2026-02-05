#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace Contracts" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }
grep -q "Square" "$OUTPUT_FILE" || { echo "Missing Square function"; exit 1; }
grep -q "Divide" "$OUTPUT_FILE" || { echo "Missing Divide function"; exit 1; }

# Check for precondition enforcement (ContractViolationException)
grep -q "ContractViolationException" "$OUTPUT_FILE" || { echo "Missing precondition check"; exit 1; }

# Check for custom error message
grep -q "divisor must not be zero" "$OUTPUT_FILE" || { echo "Missing custom error message"; exit 1; }

exit 0
