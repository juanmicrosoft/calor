#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace Hello" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }
grep -q "public static void Main" "$OUTPUT_FILE" || { echo "Missing Main method"; exit 1; }
grep -q "Console.WriteLine" "$OUTPUT_FILE" || { echo "Missing Console.WriteLine"; exit 1; }
grep -q "Hello from OPAL E2E Test!" "$OUTPUT_FILE" || { echo "Missing message"; exit 1; }

exit 0
