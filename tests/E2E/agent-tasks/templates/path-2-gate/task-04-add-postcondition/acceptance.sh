#!/usr/bin/env bash
# task-04 acceptance.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="${1:-${HERE}/setup}"

S="${WORK}/Score.calr"
B="${WORK}/Bounds.calr"
[ -f "$S" ] || { echo "missing: $S"; exit 1; }
[ -f "$B" ] || { echo "missing: $B"; exit 1; }

clamp_score_body="$(awk '/§F\{[^}]*:ClampScore:/,/§\/F/' "$S")"
clamp_lower_body="$(awk '/§F\{[^}]*:ClampLowerBound:/,/§\/F/' "$S")"

echo "$clamp_score_body" | grep -Fq '§Q (>= max 0)' \
  || { echo "ClampScore missing §Q (>= max 0)"; exit 1; }
echo "$clamp_score_body" | grep -Fq '§S (<= result max)' \
  || { echo "ClampScore missing §S (<= result max)"; exit 1; }
echo "$clamp_lower_body" | grep -Fq '§S (>= result 0)' \
  || { echo "ClampLowerBound missing §S (>= result 0)"; exit 1; }

# BoundsCheck must be untouched (contain the two §C calls).
grep -Fq '§C{ClampScore}' "$B" \
  || { echo "BoundsCheck no longer references ClampScore"; exit 1; }
grep -Fq '§C{ClampLowerBound}' "$B" \
  || { echo "BoundsCheck no longer references ClampLowerBound"; exit 1; }

echo "task-04 PASS"
