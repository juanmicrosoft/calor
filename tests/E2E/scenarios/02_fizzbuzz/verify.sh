#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace FizzBuzz" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }
grep -q "for.*var i = 1" "$OUTPUT_FILE" || { echo "Missing for loop"; exit 1; }
grep -q "i % 15" "$OUTPUT_FILE" || { echo "Missing modulo 15 check"; exit 1; }
grep -q "i % 3" "$OUTPUT_FILE" || { echo "Missing modulo 3 check"; exit 1; }
grep -q "i % 5" "$OUTPUT_FILE" || { echo "Missing modulo 5 check"; exit 1; }
grep -q '"FizzBuzz"' "$OUTPUT_FILE" || { echo "Missing FizzBuzz string"; exit 1; }
grep -q '"Fizz"' "$OUTPUT_FILE" || { echo "Missing Fizz string"; exit 1; }
grep -q '"Buzz"' "$OUTPUT_FILE" || { echo "Missing Buzz string"; exit 1; }

exit 0
