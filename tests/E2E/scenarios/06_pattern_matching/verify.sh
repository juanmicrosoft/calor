#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace PatternDemo" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }
grep -q "public static string GetGrade" "$OUTPUT_FILE" || { echo "Missing GetGrade method"; exit 1; }
grep -q "public static string GetHttpStatus" "$OUTPUT_FILE" || { echo "Missing GetHttpStatus method"; exit 1; }
grep -q "public static string Describe" "$OUTPUT_FILE" || { echo "Missing Describe method"; exit 1; }

# Check for switch expressions
grep -q "switch" "$OUTPUT_FILE" || { echo "Missing switch expressions"; exit 1; }

# Check for relational patterns
grep -q ">= 90" "$OUTPUT_FILE" || { echo "Missing >= 90 pattern"; exit 1; }
grep -q ">= 80" "$OUTPUT_FILE" || { echo "Missing >= 80 pattern"; exit 1; }

# Check for guards
grep -q "when" "$OUTPUT_FILE" || { echo "Missing when clause"; exit 1; }

# Check for literal patterns
grep -q "200 =>" "$OUTPUT_FILE" || { echo "Missing 200 literal pattern"; exit 1; }
grep -q "404 =>" "$OUTPUT_FILE" || { echo "Missing 404 literal pattern"; exit 1; }

# Check for wildcard
grep -q '_ =>' "$OUTPUT_FILE" || { echo "Missing wildcard pattern"; exit 1; }

exit 0
