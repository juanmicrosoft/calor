#!/bin/bash
# Compile all .calr files in a directory to .g.cs alongside, recursively
set -e

ROOT=${1:-.}
EXTRA_ARGS=${2:-}

failed=0
ok=0
total=0

while IFS= read -r f; do
  total=$((total + 1))
  out="${f%.calr}.g.cs"
  if calor --input "$f" --output "$out" $EXTRA_ARGS > /tmp/calor_compile.log 2>&1; then
    ok=$((ok + 1))
  else
    failed=$((failed + 1))
    echo "FAIL: $f"
    cat /tmp/calor_compile.log | head -10
    echo "---"
  fi
done < <(find "$ROOT" -name "*.calr" -type f)

echo ""
echo "Total: $total, OK: $ok, Failed: $failed"
exit $failed
