#!/usr/bin/env bash
# Verify: While loop countdown
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Scope every check to the declaration the task asked for. A file-global grep passes a
# wrong answer where the construct sits in some unrelated function and the target is an
# empty stub -- demonstrated against this verifier.
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-lib.sh"

require_in_body "$CALR_FILE" 'Countdown' '-q' '§WH{' -- 'While loop (§WH) not found in Countdown'
require_in_body "$CALR_FILE" 'Countdown' '-qE' '(§B\{|§ASSIGN)' -- 'Variable binding/assignment not found in Countdown'

echo "Verification passed: Countdown function found with while loop"
exit 0
