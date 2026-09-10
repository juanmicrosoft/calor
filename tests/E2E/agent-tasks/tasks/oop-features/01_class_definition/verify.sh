#!/usr/bin/env bash
# Verify: Class definition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Scope the check to the declaration the task asked for. A file-global grep passed a
# class named Widget with a field PersonCount as "Person class definition found".
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-lib.sh"

calor_has_decl "$CALR_FILE" 'CL' 'Person' || { echo 'Person class (§CL{...:Person...}) not declared'; exit 1; }

echo "Verification passed: Person class definition found"
exit 0
