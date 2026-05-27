#!/usr/bin/env bash
# task-02 acceptance: all three functions declare §E{db}; no other
# function picks up §E{db} accidentally.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="${1:-${HERE}/setup}"

declare -A required=(
  ["${WORK}/Db.calr"]="LoadUserSettings"
  ["${WORK}/Service.calr"]="PrepareDashboard"
  ["${WORK}/Web.calr"]="RenderHome"
)

for f in "${!required[@]}"; do
  name="${required[$f]}"
  [ -f "$f" ] || { echo "missing: $f"; exit 1; }
  body="$(awk "/§F\\{[^}]*:${name}:/,/§\\/F/" "$f")"
  if ! echo "$body" | grep -E '§E\{[^}]*\bdb\b[^}]*\}' > /dev/null; then
    echo "$name in $f is missing §E{db}"
    exit 1
  fi
done

# No db effect should appear in functions OTHER than the three above.
all_funcs_with_db="$(grep -h '§E{[^}]*\bdb\b[^}]*}' "${!required[@]}" \
                     | grep -cE '§E\{[^}]*\bdb\b[^}]*\}' || true)"
if [ "$all_funcs_with_db" -ne 3 ]; then
  echo "expected exactly 3 §E{db} declarations across the 3 files, got $all_funcs_with_db"
  exit 1
fi

echo "task-02 PASS"
