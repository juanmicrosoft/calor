#!/usr/bin/env bash
# Run Codex, then audit the resulting worktree even if Codex used an
# uncovered file-change path. Use from a clean repository baseline.
set -uo pipefail

if (($# == 0)); then
  echo "Usage: scripts/codex-with-calor-check.sh codex [args...]" >&2
  exit 2
fi

repo_root=$(git rev-parse --show-toplevel)
base=$(git rev-parse HEAD)
guard="$repo_root/scripts/check-calor-first-diff.sh"

"$@"
command_status=$?

if bash "$guard" --working-tree "$base"; then
  guard_status=0
else
  guard_status=$?
fi

if ((command_status != 0)); then
  exit "$command_status"
fi
exit "$guard_status"
