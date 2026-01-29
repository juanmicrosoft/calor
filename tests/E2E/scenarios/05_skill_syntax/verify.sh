#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace SkillSyntax" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }

# Check arithmetic operators are generated
grep -q "Add" "$OUTPUT_FILE" || { echo "Missing Add function"; exit 1; }
grep -q "Subtract" "$OUTPUT_FILE" || { echo "Missing Subtract function"; exit 1; }
grep -q "Multiply" "$OUTPUT_FILE" || { echo "Missing Multiply function"; exit 1; }

# Check that operators compile to C# expressions
grep -q "a + b" "$OUTPUT_FILE" || { echo "Missing addition expression"; exit 1; }
grep -q "a - b" "$OUTPUT_FILE" || { echo "Missing subtraction expression"; exit 1; }
grep -q 'a \* b' "$OUTPUT_FILE" || { echo "Missing multiplication expression"; exit 1; }

# Check comparison operators
grep -q "IsPositive" "$OUTPUT_FILE" || { echo "Missing IsPositive function"; exit 1; }
grep -q "IsZero" "$OUTPUT_FILE" || { echo "Missing IsZero function"; exit 1; }
grep -q "x > 0" "$OUTPUT_FILE" || { echo "Missing greater-than comparison"; exit 1; }
grep -q "x == 0" "$OUTPUT_FILE" || { echo "Missing equality comparison"; exit 1; }

# Check control flow
grep -q "for.*var i = 1" "$OUTPUT_FILE" || { echo "Missing for loop"; exit 1; }

exit 0
