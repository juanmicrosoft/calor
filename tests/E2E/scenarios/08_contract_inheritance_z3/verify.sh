#!/bin/bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT="$SCENARIO_DIR/output.g.cs"

# Verify output generated
[[ -f "$OUTPUT" ]] || { echo "No output file generated"; exit 1; }

# Check ValidService compiles (contract checks present)
grep -q "class ValidService" "$OUTPUT" || { echo "ValidService missing"; exit 1; }

# Check AnotherValidService compiles
grep -q "class AnotherValidService" "$OUTPUT" || { echo "AnotherValidService missing"; exit 1; }

# Check for ContractViolationException in generated code (contract enforcement)
grep -q "ContractViolationException" "$OUTPUT" || { echo "No contract checks generated"; exit 1; }

# Check that the interface was generated
grep -q "interface IService" "$OUTPUT" || { echo "IService interface missing"; exit 1; }

echo "E2E verification passed"
exit 0
