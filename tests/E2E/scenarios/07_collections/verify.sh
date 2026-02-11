#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace Collections" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }
grep -q "Main" "$OUTPUT_FILE" || { echo "Missing Main function"; exit 1; }

# Check for List<int> type usage
grep -q "List<int>" "$OUTPUT_FILE" || { echo "Missing List<int> type"; exit 1; }

# Check for Dictionary<string, int> type usage
grep -q "Dictionary<string, int>" "$OUTPUT_FILE" || { echo "Missing Dictionary<string, int> type"; exit 1; }

# Check for HashSet<string> type usage
grep -q "HashSet<string>" "$OUTPUT_FILE" || { echo "Missing HashSet<string> type"; exit 1; }

# Check for collection operations
grep -q "\.Add(" "$OUTPUT_FILE" || { echo "Missing Add operation"; exit 1; }
grep -q "\.Insert(" "$OUTPUT_FILE" || { echo "Missing Insert operation"; exit 1; }
grep -q "\.Remove(" "$OUTPUT_FILE" || { echo "Missing Remove operation"; exit 1; }
grep -q "\.Clear(" "$OUTPUT_FILE" || { echo "Missing Clear operation"; exit 1; }

exit 0
