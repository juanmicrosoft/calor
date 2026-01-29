#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace TypeSystem" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }
grep -q "TestSome" "$OUTPUT_FILE" || { echo "Missing TestSome function"; exit 1; }
grep -q "TestNone" "$OUTPUT_FILE" || { echo "Missing TestNone function"; exit 1; }
grep -q "TestOk" "$OUTPUT_FILE" || { echo "Missing TestOk function"; exit 1; }
grep -q "TestErr" "$OUTPUT_FILE" || { echo "Missing TestErr function"; exit 1; }

# Check for Option type usage (Some/None)
grep -q "Option" "$OUTPUT_FILE" || { echo "Missing Option type"; exit 1; }

# Check for Result type usage (Ok/Err)
grep -q "Result" "$OUTPUT_FILE" || { echo "Missing Result type"; exit 1; }

exit 0
