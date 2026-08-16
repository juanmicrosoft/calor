#!/usr/bin/env bash
# D-S1.5 fixture-registry check (PP-S4's instrument; A-1.5.7, roadmap v0.13 §2.5 gate 6).
#
# For the diff BASE..HEAD, asserts:
#   1. every SupportLevel promotion toward Full in Migration/FeatureSupport.cs (including a
#      new entry born above NotSupported) has a registered fixture naming that featureKey;
#   2. every NET removal of a ConversionLossKind.<Kind> reference from Migration/ sources
#      (counted tree-wide at each ref, comments stripped — not per diff line, so file moves
#      and same-line refactors cannot false-fire) has a fixture certifying that kind absent;
#   3. FAIL-CLOSED: every key present at BASE must be extractable at HEAD. A key that
#      vanishes or becomes unparseable (e.g. `Support = SomeConst` instead of the literal)
#      fails the check — evading the parser is indistinguishable from a hidden promotion.
#      The extractor must also account for every FeatureInfo entry at HEAD (count check).
#
# Fixture GREENNESS is asserted by DS15FixtureRegistryTests, run by the same CI job.
# Indeterminate counts as failing there.
#
# Usage:
#   check-d-s1.5-fixtures.sh <BASE_SHA> [HEAD_SHA]      # real check (HEAD default: HEAD)
#   check-d-s1.5-fixtures.sh --self-test                # discriminating pins, every CI run
set -euo pipefail

REGISTRY="bench/phase0-agent-native/fixtures/d-s1.5"
FEATURE_FILE="src/Calor.Compiler/Migration/FeatureSupport.cs"
MIGRATION_DIR="src/Calor.Compiler/Migration"

# Extract "rank<TAB>key" pairs from FeatureSupport.cs content on stdin. Keys may contain
# spaces and parentheses (two such keys exist today), so TAB is the only safe separator.
# Line comments are stripped first, so commented-out entries neither fire nor parse.
# Rank: NotSupported=0, Partial=1, Full=2. POSIX awk only (CI runners default to mawk).
extract_levels() {
  awk '
    { sub(/\/\/.*/, "") }
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
        printf "%d\t%s\n", rank, key
        key = ""
      }
    }'
}

# Count FeatureInfo entries in content on stdin (comments stripped) — the extractor's
# fail-closed denominator: every entry must yield an extracted pair.
count_entries() {
  awk '{ sub(/\/\/.*/, "") } /= new FeatureInfo/ { n++ } END { print n + 0 }'
}

# Promoted keys: rank increased, or key born above NotSupported (base rank 0).
find_promotions() {
  local base_map="$1" head_map="$2"
  awk -F'\t' 'NR == FNR { b[$2] = $1; next }
       { base = ($2 in b) ? b[$2] : 0; if ($1 + 0 > base + 0) print $2 }' \
    "$base_map" "$head_map"
}

# FAIL-CLOSED leg: keys present at BASE but absent from the HEAD map. A legitimate
# feature-entry removal is a denominator change and needs a supersession note plus a
# temporary allowlist entry here — never a silent disappearance.
find_vanished() {
  local base_map="$1" head_map="$2"
  awk -F'\t' 'NR == FNR { h[$2] = 1; next } !($2 in h) { print $2 }' \
    "$head_map" "$base_map"
}

# The fixture is located by exact featureKey match across ALL fixture.json files (grep -F:
# keys contain parens), not by directory name — dirnames are sanitized (see README).
fixture_has_key() {
  local key="$1" f
  for f in "$REGISTRY"/*/fixture.json; do
    [ -f "$f" ] || continue
    grep -qF "\"featureKey\": \"$key\"" "$f" && return 0
  done
  return 1
}

fixture_certifies_loss_kind() {
  local kind="$1" f
  for f in "$REGISTRY"/*/fixture.json; do
    [ -f "$f" ] || continue
    grep -qF "\"lossKindCertifiedAbsent\": \"$kind\"" "$f" && return 0
  done
  return 1
}

# Count ConversionLossKind.<Kind> references tree-wide under Migration/ at a ref,
# comments stripped. Output: "count<TAB>kind" lines.
count_loss_kinds_at_ref() {
  local ref="$1" f
  git ls-tree -r --name-only "$ref" -- "$MIGRATION_DIR" 2>/dev/null \
    | grep '\.cs$' \
    | while read -r f; do git show "$ref:$f" 2>/dev/null || true; done \
    | awk '
        { sub(/\/\/.*/, "")
          s = $0
          while (match(s, /ConversionLossKind\.[A-Za-z0-9]+/)) {
            n[substr(s, RSTART + 19, RLENGTH - 19)]++
            s = substr(s, RSTART + RLENGTH)
          } }
        END { for (k in n) printf "%d\t%s\n", n[k], k }'
}

check_maps() {
  # Args: base-map head-map removed-kinds-file head-entry-count
  local base_map="$1" head_map="$2" removed_kinds="$3" head_entries="${4:-}" fail=0

  if [ -n "$head_entries" ]; then
    local extracted; extracted=$(wc -l < "$head_map" | tr -d ' ')
    if [ "$extracted" -ne "$head_entries" ]; then
      echo "FAIL (fail-closed): extracted $extracted level pairs but HEAD has" \
           "$head_entries FeatureInfo entries — an entry's Support is not the plain" \
           "SupportLevel literal, which is indistinguishable from a hidden promotion." >&2
      fail=1
    fi
  fi

  while IFS= read -r key; do
    [ -n "$key" ] || continue
    echo "FAIL (fail-closed): key '$key' present at BASE is missing/unparseable at HEAD." \
         "Removing or rewriting a FeatureSupport entry is a denominator change that" \
         "needs an explicit supersession, never a silent disappearance." >&2
    fail=1
  done < <(find_vanished "$base_map" "$head_map")

  while IFS= read -r key; do
    [ -n "$key" ] || continue
    if ! fixture_has_key "$key"; then
      echo "FAIL: SupportLevel promotion for '$key' has no registered fixture" \
           "(no $REGISTRY/*/fixture.json with featureKey '$key'). PP-S4: every" \
           "support increase must be demonstrated on values, not declared." >&2
      fail=1
    fi
  done < <(find_promotions "$base_map" "$head_map")

  while IFS= read -r kind; do
    [ -n "$kind" ] || continue
    if ! fixture_certifies_loss_kind "$kind"; then
      echo "FAIL: net removal of ConversionLossKind.$kind references under Migration/" \
           "has no fixture certifying that loss absent." >&2
      fail=1
    fi
  done < "$removed_kinds"

  return "$fail"
}

self_test() {
  local tmp; tmp=$(mktemp -d)
  local registry_before="$REGISTRY"
  REGISTRY="$tmp/registry"
  mkdir -p "$REGISTRY"
  trap 'REGISTRY="$registry_before"; rm -rf "$tmp"' RETURN

  cat > "$tmp/base.cs" <<'EOF'
        ["record"] = new FeatureInfo
        {
            Name = "record",
            Support = SupportLevel.Partial,
        },
        ["binary pattern (and/or)"] = new FeatureInfo
        {
            Name = "binary pattern",
            Support = SupportLevel.Partial,
        },
        ["class"] = new FeatureInfo
        {
            Name = "class",
            Support = SupportLevel.Full,
        },
EOF
  sed 's/SupportLevel.Partial/SupportLevel.Full/' "$tmp/base.cs" > "$tmp/head.cs"
  # Evasion variant: rewrite one entry's level as a non-literal.
  sed 's/Support = SupportLevel.Partial,/Support = PromotedConst,/' "$tmp/base.cs" > "$tmp/evade.cs"
  # Comment variant: the base plus a commented-out Full entry (must NOT fire).
  { cat "$tmp/base.cs"; echo '        // ["ghost"] = new FeatureInfo { Support = SupportLevel.Full },'; } > "$tmp/comment.cs"

  extract_levels < "$tmp/base.cs"    > "$tmp/base.map"
  extract_levels < "$tmp/head.cs"    > "$tmp/head.map"
  extract_levels < "$tmp/evade.cs"   > "$tmp/evade.map"
  extract_levels < "$tmp/comment.cs" > "$tmp/comment.map"
  : > "$tmp/no-kinds"

  # Pin 1: a fixture-less promotion MUST fail — including the space-bearing key.
  if check_maps "$tmp/base.map" "$tmp/head.map" "$tmp/no-kinds" 2>/dev/null; then
    echo "SELF-TEST FAILED: fixture-less promotion did not fail" >&2; return 1
  fi
  # Capture rather than pipe: with pipefail, check_maps' (correct) exit 1 would fail
  # the pipeline even when grep matches.
  local pin1_out
  pin1_out=$(check_maps "$tmp/base.map" "$tmp/head.map" "$tmp/no-kinds" 2>&1 || true)
  if ! grep -qF "binary pattern (and/or)" <<< "$pin1_out"; then
    echo "SELF-TEST FAILED: space-bearing key's promotion was not detected" >&2; return 1
  fi
  # Pin 2: no change MUST pass (incl. entry-count leg).
  if ! check_maps "$tmp/base.map" "$tmp/base.map" "$tmp/no-kinds" "$(count_entries < "$tmp/base.cs")" 2>/dev/null; then
    echo "SELF-TEST FAILED: no-change diff did not pass" >&2; return 1
  fi
  # Pin 3: an uncertified loss-kind removal MUST fail.
  echo "InteropPreserved" > "$tmp/kinds"
  if check_maps "$tmp/base.map" "$tmp/base.map" "$tmp/kinds" 2>/dev/null; then
    echo "SELF-TEST FAILED: uncertified loss-kind removal did not fail" >&2; return 1
  fi
  # Pin 4 (fail-closed): a key rewritten to a non-literal level MUST fail, two ways —
  # vanished-key leg and entry-count leg.
  if check_maps "$tmp/base.map" "$tmp/evade.map" "$tmp/no-kinds" "$(count_entries < "$tmp/evade.cs")" 2>/dev/null; then
    echo "SELF-TEST FAILED: non-literal Support rewrite did not fail closed" >&2; return 1
  fi
  # Pin 5 (negative control): a commented-out entry neither fires nor counts.
  if ! check_maps "$tmp/base.map" "$tmp/comment.map" "$tmp/no-kinds" "$(count_entries < "$tmp/comment.cs")" 2>/dev/null; then
    echo "SELF-TEST FAILED: commented-out entry false-fired" >&2; return 1
  fi
  echo "self-test: all five pins hold (promotion incl. space-key fails, no-change passes," \
       "loss-removal fails, non-literal rewrite fails closed, comment does not fire)"
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
head_entries=$(git show "$HEAD_REF:$FEATURE_FILE" | count_entries)

# Net loss-kind removals: tree-wide counts at each ref (comments stripped), not diff
# lines — a file moved out of Migration/ or a same-line refactor cannot false-fire.
count_loss_kinds_at_ref "$BASE"     > "$tmpd/base.kinds"
count_loss_kinds_at_ref "$HEAD_REF" > "$tmpd/head.kinds"
awk -F'\t' 'NR == FNR { h[$2] = $1; next }
     { if (($2 in h ? h[$2] : 0) + 0 < $1 + 0) print $2 }' \
  "$tmpd/head.kinds" "$tmpd/base.kinds" > "$tmpd/removed-kinds"

if check_maps "$tmpd/base.map" "$tmpd/head.map" "$tmpd/removed-kinds" "$head_entries"; then
  echo "d-s1.5 check: no unregistered promotions, vanished keys, or loss-kind removals in $BASE..$HEAD_REF"
else
  exit 1
fi
