#!/usr/bin/env bash
# Verify: Foreach over dictionary
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Collections.calr"

[[ -f "$CALR_FILE" ]] || { echo "Collections.calr not found"; exit 1; }

# Check for Dictionary parameter type
grep -qE "(Dictionary|dict)" "$CALR_FILE" || { echo "Dictionary parameter not found"; exit 1; }

# Scope every check to the declaration the task asked for. A file-global grep passes a
# wrong answer where the construct sits in some unrelated function and the target is an
# empty stub -- demonstrated against this verifier.
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-lib.sh"

require_in_body "$CALR_FILE" 'PrintDictionary' '-q' '§EACHKV{' -- 'Foreach-kv (§EACHKV) not found in PrintDictionary'
require_in_body "$CALR_FILE" 'PrintDictionary' '-qE' '(Dictionary|dict)' -- 'Dictionary parameter not found on PrintDictionary'

echo "Verification passed: PrintDictionary function found with foreach-kv"
exit 0
