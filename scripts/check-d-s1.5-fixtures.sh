#!/usr/bin/env bash
# D-S1.5 fixture-registry check (PP-S4's instrument; A-1.5.7, roadmap v0.13 §2.5 gate 6).
#
# For the diff BASE..HEAD, asserts:
#   1. every SupportLevel promotion toward Full in Migration/FeatureSupport.cs (including a
#      new entry born above NotSupported) has a fixture at
#      bench/phase0-agent-native/fixtures/d-s1.5/<key>/fixture.json naming that featureKey;
#   2. every NET removal of a ConversionLossKind.<Kind> reference from Migration/ sources has
#      a fixture naming that kind as lossKindCertifiedAbsent.
# Fixture GREENNESS (build/conversion match) is asserted by DS15FixtureRegistryTests, run by
# the same CI job after this script. Indeterminate counts as failing there.
#
# Usage:
#   check-d-s1.5-fixtures.sh <BASE_SHA> [HEAD_SHA]      # real check (HEAD default: HEAD)
#   check-d-s1.5-fixtures.sh --self-test                # discriminating pin, run every CI pass
set -euo pipefail

REGISTRY="bench/phase0-agent-native/fixtures/d-s1.5"
FEATURE_FILE="src/Calor.Compiler/Migration/FeatureSupport.cs"

# Extract "key level-rank" pairs from a FeatureSupport.cs content stream.
# Rank: NotSupported=0, Partial=1, Full=2. Pairing rule: the most recent ["key"] =
# line owns the next Support = SupportLevel.<X> line. POSIX awk only (no gawk
# extensions — CI runners default to mawk, macOS to BSD awk).
extract_levels() {
  awk '
    /\["[^"]+"\] = new FeatureInfo/ {
      line = $0
      sub(/^[^"]*"/, "", line); sub(/".*/, "", line)
      key = line
    }
    /Support = SupportLevel\.(NotSupported|Partial|Full)/ {
      if (key != "") {
        rank = 0
        if ($0 ~ /SupportLevel\.Full/) rank = 2
        else if ($0 ~ /SupportLevel\.Partial/) rank = 1
        print key, rank
        key = ""
      }
    }'
}

# Compare two level maps (files of "key rank"); print promoted keys (rank increased,
# or new key born above NotSupported — treated as promotion from rank 0).
find_promotions() {
  local base_map="$1" head_map="$2"
  awk 'NR == FNR { b[$1] = $2; next }
       { base = ($1 in b) ? b[$1] : 0; if ($2 + 0 > base + 0) print $1 }' \
    "$base_map" "$head_map"
}

fixture_has_key() {
  local key="$1"
  [ -f "$REGISTRY/$key/fixture.json" ] \
    && grep -q "\"featureKey\"[[:space:]]*:[[:space:]]*\"$key\"" "$REGISTRY/$key/fixture.json"
}

fixture_certifies_loss_kind() {
  local kind="$1" f
  for f in "$REGISTRY"/*/fixture.json; do
    [ -f "$f" ] || continue
    grep -q "\"lossKindCertifiedAbsent\"[[:space:]]*:[[:space:]]*\"$kind\"" "$f" && return 0
  done
  return 1
}

check_maps() {
  # Args: base-map-file head-map-file removed-loss-kinds-file(one per line, may be empty)
  local base_map="$1" head_map="$2" removed_kinds="$3" fail=0

  while read -r key; do
    [ -n "$key" ] || continue
    if ! fixture_has_key "$key"; then
      echo "FAIL: SupportLevel promotion for '$key' has no registered fixture" \
           "($REGISTRY/$key/fixture.json with featureKey '$key'). PP-S4: a promotion" \
           "toward Full must be demonstrated on values, not declared." >&2
      fail=1
    fi
  done < <(find_promotions "$base_map" "$head_map")

  while read -r kind; do
    [ -n "$kind" ] || continue
    if ! fixture_certifies_loss_kind "$kind"; then
      echo "FAIL: net removal of ConversionLossKind.$kind has no fixture certifying that" \
           "loss absent (no fixture.json with lossKindCertifiedAbsent '$kind')." >&2
      fail=1
    fi
  done < "$removed_kinds"

  return "$fail"
}

self_test() {
  local tmp; tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' RETURN

  cat > "$tmp/base.cs" <<'EOF'
        ["record"] = new FeatureInfo
        {
            Name = "record",
            Support = SupportLevel.Partial,
        },
        ["class"] = new FeatureInfo
        {
            Name = "class",
            Support = SupportLevel.Full,
        },
EOF
  sed 's/SupportLevel.Partial/SupportLevel.Full/' "$tmp/base.cs" > "$tmp/head.cs"

  extract_levels < "$tmp/base.cs" > "$tmp/base.map"
  extract_levels < "$tmp/head.cs" > "$tmp/head.map"
  : > "$tmp/no-kinds"

  # Pin 1 (discriminating): a fixture-less promotion MUST fail.
  if check_maps "$tmp/base.map" "$tmp/head.map" "$tmp/no-kinds" 2>/dev/null; then
    echo "SELF-TEST FAILED: fixture-less promotion did not fail the check" >&2
    return 1
  fi
  # Pin 2 (negative control): an unchanged map MUST pass.
  if ! check_maps "$tmp/base.map" "$tmp/base.map" "$tmp/no-kinds" 2>/dev/null; then
    echo "SELF-TEST FAILED: no-change diff did not pass the check" >&2
    return 1
  fi
  # Pin 3 (discriminating, loss leg): an uncertified loss-kind removal MUST fail.
  echo "InteropPreserved" > "$tmp/kinds"
  if check_maps "$tmp/base.map" "$tmp/base.map" "$tmp/kinds" 2>/dev/null; then
    echo "SELF-TEST FAILED: uncertified loss-kind removal did not fail the check" >&2
    return 1
  fi
  echo "self-test: all three pins hold (promotion fails, no-change passes, loss-removal fails)"
}

if [ "${1:-}" = "--self-test" ]; then
  self_test
  exit $?
fi

BASE="${1:?usage: $0 <BASE_SHA>|--self-test}"
HEAD_REF="${2:-HEAD}"

tmpd=$(mktemp -d); trap 'rm -rf "$tmpd"' EXIT

git show "$BASE:$FEATURE_FILE" 2>/dev/null | extract_levels > "$tmpd/base.map" || : > "$tmpd/base.map"
git show "$HEAD_REF:$FEATURE_FILE" | extract_levels > "$tmpd/head.map"

# Net loss-kind removals across Migration/ sources: kinds whose removed-line count
# exceeds their added-line count in the diff.
git diff "$BASE".."$HEAD_REF" -- 'src/Calor.Compiler/Migration/*.cs' \
  | awk '
      # "ConversionLossKind." is 19 characters; the kind name follows it.
      /^-[^-]/ { s = $0; while (match(s, /ConversionLossKind\.[A-Za-z]+/)) {
                   rem[substr(s, RSTART + 19, RLENGTH - 19)]++; s = substr(s, RSTART + RLENGTH) } }
      /^\+[^+]/ { s = $0; while (match(s, /ConversionLossKind\.[A-Za-z]+/)) {
                   add[substr(s, RSTART + 19, RLENGTH - 19)]++; s = substr(s, RSTART + RLENGTH) } }
      END { for (k in rem) if (rem[k] > add[k] + 0) print k }
    ' > "$tmpd/removed-kinds"

if check_maps "$tmpd/base.map" "$tmpd/head.map" "$tmpd/removed-kinds"; then
  echo "d-s1.5 check: no unregistered promotions or loss-kind removals in $BASE..$HEAD_REF"
else
  exit 1
fi
