#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Check for expected patterns
grep -q "namespace QuantifierDemo" "$OUTPUT_FILE" || { echo "Missing namespace"; exit 1; }

# Check for all four functions
grep -q "AllNonNegative" "$OUTPUT_FILE" || { echo "Missing AllNonNegative function"; exit 1; }
grep -q "ExistsTarget" "$OUTPUT_FILE" || { echo "Missing ExistsTarget function"; exit 1; }
grep -q "ImplicationTest" "$OUTPUT_FILE" || { echo "Missing ImplicationTest function"; exit 1; }
grep -q "MatrixSymmetry" "$OUTPUT_FILE" || { echo "Missing MatrixSymmetry function"; exit 1; }

# Check for forall generating Enumerable.Range().All()
grep -q "Enumerable.Range" "$OUTPUT_FILE" || { echo "Missing Enumerable.Range for quantifier"; exit 1; }
grep -q "\.All(" "$OUTPUT_FILE" || { echo "Missing .All() for forall quantifier"; exit 1; }

# Check for exists generating Enumerable.Range().Any()
grep -q "\.Any(" "$OUTPUT_FILE" || { echo "Missing .Any() for exists quantifier"; exit 1; }

# Check for implication translated as !p || q (with nested parens)
grep -q "!((" "$OUTPUT_FILE" && grep -q ")) ||" "$OUTPUT_FILE" || { echo "Missing implication translation (!p || q)"; exit 1; }

# Check for nested quantifier (two variables in MatrixSymmetry)
# This should generate nested All calls
grep -E "\.All\([a-z]+ =>" "$OUTPUT_FILE" | wc -l | grep -q "[2-9]" || { echo "Expected multiple .All() calls for nested quantifiers"; exit 1; }

exit 0
