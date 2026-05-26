#!/usr/bin/env bash
# task-05 acceptance.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="${1:-${HERE}/setup}"

F="${WORK}/Grid.calr"
[ -f "$F" ] || { echo "missing: $F"; exit 1; }

# SumPlane must exist with the right signature.
grep -E '§F\{[^}]*:SumPlane:' "$F" > /dev/null \
  || { echo "SumPlane not found"; exit 1; }
sumplane_body="$(awk '/§F\{[^}]*:SumPlane:/,/§\/F/' "$F")"
echo "$sumplane_body" | grep -Fq '§I{i32:x}' \
  || { echo "SumPlane missing §I{i32:x}"; exit 1; }
echo "$sumplane_body" | grep -Fq '§I{i32:size}' \
  || { echo "SumPlane missing §I{i32:size}"; exit 1; }
echo "$sumplane_body" | grep -Fq '§O{i32}' \
  || { echo "SumPlane missing §O{i32}"; exit 1; }

# SumGrid must have exactly one §L{...} (the outer x loop).
sumgrid_body="$(awk '/§F\{[^}]*:SumGrid:/,/§\/F/' "$F")"
loop_count="$(echo "$sumgrid_body" | grep -cE '§L\{')"
if [ "$loop_count" -ne 1 ]; then
  echo "expected exactly 1 §L in SumGrid, got $loop_count"
  exit 1
fi

# SumGrid must invoke SumPlane.
echo "$sumgrid_body" | grep -Fq '§C{SumPlane}' \
  || { echo "SumGrid does not invoke §C{SumPlane}"; exit 1; }

echo "task-05 PASS"
