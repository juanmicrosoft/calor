#!/usr/bin/env bash
# task-03 acceptance.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="${1:-${HERE}/setup}"

T="${WORK}/TaxCalc.calr"
O="${WORK}/Order.calr"
I="${WORK}/Invoice.calr"
R="${WORK}/Receipt.calr"
for f in "$T" "$O" "$I" "$R"; do
  [ -f "$f" ] || { echo "missing: $f"; exit 1; }
done

# ComputeTax must be pub.
if ! grep -E '§F\{[^}]*:ComputeTax:pub\b' "$T" > /dev/null; then
  echo "ComputeTax not declared pub in TaxCalc.calr"
  exit 1
fi

# ComputeTaxWrapper must be gone.
if grep -E '§F\{[^}]*:ComputeTaxWrapper:' "$T" > /dev/null; then
  echo "ComputeTaxWrapper still present in TaxCalc.calr"
  exit 1
fi

# Each caller file: contains §C{ComputeTax}, contains NO §C{ComputeTaxWrapper}.
for f in "$O" "$I" "$R"; do
  if ! grep -E '§C\{ComputeTax\}' "$f" > /dev/null; then
    echo "no §C{ComputeTax} in $f"
    exit 1
  fi
  if grep -E '§C\{ComputeTaxWrapper\}' "$f" > /dev/null; then
    echo "still references §C{ComputeTaxWrapper} in $f"
    exit 1
  fi
done

# Total ComputeTax call sites across callers == 5.
total="$(grep -hcE '§C\{ComputeTax\}' "$O" "$I" "$R" | paste -sd+ - | bc 2>/dev/null \
         || grep -hcE '§C\{ComputeTax\}' "$O" "$I" "$R" | awk '{s+=$1} END{print s}')"
if [ "$total" -ne 5 ]; then
  echo "expected 5 §C{ComputeTax} calls across callers, got $total"
  exit 1
fi

echo "task-03 PASS"
