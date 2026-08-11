#!/usr/bin/env bash
# Dogfood utility guard (roadmap-v0.13-v0.15 §2.2).
#
# The utility's claim is that a real in-repo tool is built from Calor and
# nothing else. That claim decays silently: someone adds a .cs "just for this
# one thing", or commits the generated output, and the utility keeps building
# while no longer being dogfood. This enforces the claim mechanically.
#
# What is checked:
#   1. No .cs is tracked under the utility — hand-written or generated.
#   2. No build output (obj/, bin/) is tracked under the utility.
#   3. At least one .calr exists, so the utility cannot be emptied and still pass.
#
# Note on scope: §2.2 words the rule as "PRs touching the utility touch only
# .calr". Enforced literally that would reject the commit adding the .csproj
# itself, so the enforceable form is the one above — no C# and no build output,
# ever, with the project file and docs allowed to change. The property that
# matters (the tool's SOURCE is Calor) is fully covered.
set -euo pipefail

root=$(git rev-parse --show-toplevel)
cd "$root"

utility="tools/calor-allowlist-audit"
violations=()

mapfile -t tracked < <(git ls-files "$utility")

if ((${#tracked[@]} == 0)); then
  echo "Dogfood guard failed: no tracked files under $utility." >&2
  exit 1
fi

for path in "${tracked[@]}"; do
  case "$path" in
    *.cs)
      violations+=("$path (C# under the dogfood utility — its source must be .calr only)")
      ;;
    "$utility"/obj/*|"$utility"/bin/*)
      violations+=("$path (committed build output — generated C# must stay untracked)")
      ;;
  esac
done

calr_count=$(git ls-files "$utility/*.calr" | wc -l | tr -d ' ')
if [[ "$calr_count" -eq 0 ]]; then
  violations+=("no .calr source under $utility — the utility must be written in Calor")
fi

if ((${#violations[@]} > 0)); then
  echo "Dogfood guard failed:" >&2
  printf '  %s\n' "${violations[@]}" >&2
  echo "The §2.2 utility is dogfood: its source is Calor, and its generated C# is never committed." >&2
  exit 1
fi

echo "Dogfood guard passed ($calr_count .calr source file(s); no C# and no build output tracked)."
