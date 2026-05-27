#!/usr/bin/env bash
# task-06 acceptance.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="${1:-${HERE}/setup}"

P="${WORK}/Pipeline.calr"
R="${WORK}/Remote.calr"
[ -f "$P" ] || { echo "missing: $P"; exit 1; }
[ -f "$R" ] || { echo "missing: $R"; exit 1; }

body="$(awk '/§F\{[^}]*:FetchAndStore:/,/§\/F/' "$P")"

echo "$body" | grep -qE '§TR\{' || { echo "missing §TR{...} block"; exit 1; }
echo "$body" | grep -Fq '§C{FetchRemote}' \
  || { echo "FetchRemote call removed from try body"; exit 1; }
echo "$body" | grep -Fq '§C{WriteRecord}' \
  || { echo "WriteRecord call removed from try body"; exit 1; }
echo "$body" | grep -qE '§CA\{NetworkError' \
  || { echo "missing §CA{NetworkError:...}"; exit 1; }
echo "$body" | grep -qE '§CA\{IoError' \
  || { echo "missing §CA{IoError:...}"; exit 1; }
echo "$body" | grep -qE '§FI\b' || { echo "missing §FI clause"; exit 1; }

# In the finally block, must assign ~closed to BOOL:true.
finally_body="$(echo "$body" | awk '/§FI/,/§\/TR/')"
echo "$finally_body" | grep -Fq '§B{~closed} BOOL:true' \
  || { echo "§FI must contain §B{~closed} BOOL:true"; exit 1; }

# Remote.calr must be byte-identical to its setup version (untouched).
expected_remote_hash="$(sha1sum "${HERE}/setup/Remote.calr" | awk '{print $1}')"
actual_remote_hash="$(sha1sum "$R" | awk '{print $1}')"
if [ "$expected_remote_hash" != "$actual_remote_hash" ]; then
  echo "Remote.calr was modified; it should remain untouched"
  exit 1
fi

echo "task-06 PASS"
