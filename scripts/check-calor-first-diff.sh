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

violations=()
for path in "${changed[@]}"; do
  [[ "$path" == *.cs ]] || continue
  # Existing tracked C# is grandfathered for compiler/runtime maintenance.
  # New paths must be Calor or be pre-approved on the protected base branch.
  exists_in_base "$path" && continue
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
