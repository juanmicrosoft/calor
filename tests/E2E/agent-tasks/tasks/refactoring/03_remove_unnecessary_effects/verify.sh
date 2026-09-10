#!/usr/bin/env bash
# Verify: Remove unnecessary effects
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Impure.calr"

[[ -f "$CALR_FILE" ]] || { echo "Impure.calr not found"; exit 1; }

source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-lib.sh"

# JustCompute must exist and must declare no side effects.
#
# Three ways this check has been fooled, all now covered by calor_body:
#   - it used to bound the body with `grep -B 5 "§/F"`, a closer removed in Phase 4d, so
#     it matched nothing and the check passed for ANY input, including an impure one;
#   - an unanchored name match latched onto `JustComputeMore` and reported on it instead;
#   - a bare mention in a comment satisfied the "function exists" grep.
#
# Match a NON-EMPTY row only: `§E{}` is the explicit declaration of purity and must pass,
# so a bare "§E{" test would reject the very answer the task asks for.
BODY=$(calor_body "$CALR_FILE" JustCompute) || {
    echo "JustCompute is not declared (a mention in a comment does not count)"; exit 1; }

if grep -qE "§E\{[^}]" <<<"$BODY"; then
    echo "JustCompute should be pure (no side-effect row; §E{} or no §E is correct)"
    exit 1
fi

echo "Verification passed: JustCompute pure function found"
exit 0
