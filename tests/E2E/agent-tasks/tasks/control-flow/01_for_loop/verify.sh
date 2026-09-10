#!/usr/bin/env bash
# Verify: For loop printing numbers
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Scope every check to the declaration the task asked for. A file-global grep passes a
# wrong answer where the construct sits in some unrelated function and the target is an
# empty stub -- demonstrated against this verifier.
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-lib.sh"

require_in_body "$CALR_FILE" 'PrintNumbers' '-q' '§L{' -- 'For loop (§L) not found in PrintNumbers'
require_in_body "$CALR_FILE" 'PrintNumbers' '-q' '§E{cw}' -- 'Console write effect not found on PrintNumbers'

echo "Verification passed: PrintNumbers function found with for loop"
exit 0
