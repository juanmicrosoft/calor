#!/usr/bin/env bash
# task-01 acceptance: exit 0 iff the four required functions exist and
# Calculate's body invokes all three helpers.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="${1:-${HERE}/setup}"
F="${WORK}/Math.calr"

[ -f "$F" ] || { echo "missing: $F"; exit 1; }

for name in Calculate ValidateInput ScaleInput RunningTotal; do
  if ! grep -E "§F\{[^}]*:${name}:" "$F" > /dev/null; then
    echo "missing function: $name"
    exit 1
  fi
done

# Calculate body must invoke all three helpers via §C{Helper}.
calc_body="$(awk '/§F\{[^}]*:Calculate:/,/§\/F/' "$F")"
for helper in ValidateInput ScaleInput RunningTotal; do
  if ! echo "$calc_body" | grep -E "§C\{${helper}\}" > /dev/null; then
    echo "Calculate does not invoke helper: $helper"
    exit 1
  fi
done
echo "task-01 PASS"
