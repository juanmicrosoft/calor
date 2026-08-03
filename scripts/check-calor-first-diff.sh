#!/usr/bin/env bash
# Reject new C# implementation files anywhere in the repository.
# Runtime Codex hooks are best-effort; this is the repository-level backstop.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/check-calor-first-diff.sh [--working-tree] [BASE]

Checks changed C# files against BASE...HEAD. With --working-tree, also checks
untracked files in the current checkout. CALOR_BASE_SHA or GITHUB_BASE_SHA may
provide the base revision when BASE is omitted.
EOF
}

working_tree=false
base=""
while (($# > 0)); do
  case "$1" in
    --working-tree) working_tree=true ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    *)
      if [[ -n "$base" ]]; then
        echo "Only one base revision may be supplied" >&2
        exit 2
      fi
      base="$1"
      ;;
  esac
  shift
done

root=$(git rev-parse --show-toplevel)
cd "$root"

if [[ -z "$base" ]]; then
  base="${CALOR_BASE_SHA:-${GITHUB_BASE_SHA:-}}"
fi
if [[ -z "$base" ]]; then
  base="HEAD^"
fi
if [[ "$base" =~ ^0+$ ]]; then
  base="HEAD^"
fi

if ! git rev-parse --verify "$base^{commit}" >/dev/null 2>&1; then
  echo "Cannot resolve base revision '$base'" >&2
  exit 2
fi

mapfile -t changed < <(git diff --name-only --diff-filter=ACMR "$base"...HEAD)
if [[ "$working_tree" == true ]]; then
  mapfile -t unstaged < <(git diff --name-only --diff-filter=ACMR)
  mapfile -t staged < <(git diff --cached --name-only --diff-filter=ACMR)
  mapfile -t untracked < <(git ls-files --others --exclude-standard)
  changed+=("${unstaged[@]}")
  changed+=("${staged[@]}")
  changed+=("${untracked[@]}")
fi

is_base_allowlisted() {
  # Read only the merge-base allowlist. A PR cannot add an allowlist entry and
  # use it to exempt a new C# file in that same PR.
  git cat-file -e "${base}:.calor-csharp-allowlist" 2>/dev/null || return 1
  local pattern
  while IFS= read -r pattern || [[ -n "$pattern" ]]; do
    [[ -z "$pattern" || "$pattern" == \#* ]] && continue
    if [[ "$1" == $pattern ]]; then return 0; fi
  done < <(git show "${base}:.calor-csharp-allowlist")
  return 1
}

exists_in_base() {
  git cat-file -e "${base}:$1" 2>/dev/null
}

is_test_source() {
  # The Calor-first rule governs PRODUCT source (the compiler/runtime/SDK that
  # ships), not the xUnit test suites — those are C# by nature and every new test
  # is a new .cs file. Exempt the tests/ tree structurally so adding a test never
  # needs an allowlist entry or an admin merge. Product code under src/, tools/,
  # etc. stays fully governed.
  [[ "$1" == tests/* ]]
}

is_bench_source() {
  # Benchmark fixtures and harness support are likewise not product source:
  # the two-arm benchmark pairs are DEFINITIONALLY half C# (every pair ships a
  # csharp/ arm, shims, and test suites — that is the comparison being run),
  # and the ~700 pre-existing bench/**/*.cs were only ever passing via the
  # exists_in_base grandfather. Exempt the tree structurally (loop plan M4b,
  # WS5 probe pairs; review of #808 finding 1) so authoring a new pair never
  # needs an allowlist entry. Nothing under bench/ ships in the product.
  [[ "$1" == bench/* ]]
}

is_harness_source() {
  # The round-trip / real-scale benchmark harness is benchmark SUPPORT, not
  # product source — identical rationale to is_bench_source. It measures the
  # converter (mines corpus history, drives `dotnet test`, manipulates Roslyn),
  # which cannot be authored in Calor, and NOTHING under it ships in the
  # product (verified: no src/ project references Calor.RoundTrip.Harness). The
  # pre-existing harness .cs only ever passed via the exists_in_base grandfather;
  # exempt the tree structurally (wedge plan v0.11 WS-W4 Slice C — the task-gen
  # machinery under TaskGen/) so authoring new harness support never needs an
  # allowlist entry. Scoped to this one tool tree, not tools/ at large — other
  # tools/ product code stays fully governed.
  [[ "$1" == tools/Calor.RoundTrip.Harness/* ]]
}

violations=()
for path in "${changed[@]}"; do
  [[ "$path" == *.cs ]] || continue
  # Existing tracked C# is grandfathered for compiler/runtime maintenance.
  # New paths must be Calor or be pre-approved on the protected base branch.
  exists_in_base "$path" && continue
  is_test_source "$path" && continue
  is_bench_source "$path" && continue
  is_harness_source "$path" && continue
  is_base_allowlisted "$path" && continue
  [[ -f "$path" ]] || continue
  violations+=("$path")
done

if ((${#violations[@]} > 0)); then
  echo "Calor-first guard failed: new C# paths are not permitted:" >&2
  printf '  %s\n' "${violations[@]}" >&2
  echo "Create the Calor source, or obtain a reviewed base-branch allowlist entry before adding generated C# output." >&2
  exit 1
fi

echo "Calor-first guard passed (${#changed[@]} changed/untracked paths inspected; new C# is base-allowlist controlled)."
