#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace TypeSystem" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }
grep -q "TestPresent" "$OUTPUT_FILE" || { echo "Missing TestPresent function"; exit 1; }
grep -q "TestMissing" "$OUTPUT_FILE" || { echo "Missing TestMissing function"; exit 1; }
grep -q "TestSuccess" "$OUTPUT_FILE" || { echo "Missing TestSuccess function"; exit 1; }
grep -q "TestFailure" "$OUTPUT_FILE" || { echo "Missing TestFailure function"; exit 1; }

# Check nullable values and fallback expressions.
grep -q "int?" "$OUTPUT_FILE" || { echo "Missing nullable type"; exit 1; }
grep -q '??' "$OUTPUT_FILE" || { echo "Missing nullable fallback"; exit 1; }

# Check exception construction and handling, without removed runtime wrappers.
grep -q "throw new InvalidOperationException" "$OUTPUT_FILE" || { echo "Missing throw"; exit 1; }
grep -q "catch (InvalidOperationException" "$OUTPUT_FILE" || { echo "Missing catch"; exit 1; }
if grep -Eq 'Calor\.Runtime\.(Option|Result)' "$OUTPUT_FILE"; then
    echo "Removed runtime wrapper emitted"
    exit 1
fi

exit 0
