#!/usr/bin/env bash
# Verify: Switch with literals
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Calculator.calr"

[[ -f "$CALR_FILE" ]] || { echo "Calculator.calr not found"; exit 1; }

# Scope every check to the declaration the task asked for. A file-global grep passes a
# wrong answer where the construct sits in some unrelated function and the target is an
# empty stub -- demonstrated against this verifier.
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-lib.sh"

require_in_body "$CALR_FILE" 'DayName' '-q' '§W{' -- 'Switch statement (§W) not found in DayName'
require_in_body "$CALR_FILE" 'DayName' '-q' '§K' -- 'Case pattern (§K) not found in DayName'

echo "Verification passed: DayName function found with switch"
exit 0
