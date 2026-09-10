#!/usr/bin/env bash
# Verify: If-ElseIf-Else block
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Scope every check to the declaration the task asked for. A file-global grep passes a
# wrong answer where the construct sits in some unrelated function and the target is an
# empty stub -- demonstrated against this verifier.
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-lib.sh"

require_in_body "$CALR_FILE" 'Classify' '-q' '§IF{' -- 'If statement (§IF) not found in Classify'
require_in_body "$CALR_FILE" 'Classify' '-qE' '(§EI |§EL)' -- 'Else-if (§EI) or else (§EL) not found in Classify'

echo "Verification passed: Classify function found with if-elseif-else"
exit 0
