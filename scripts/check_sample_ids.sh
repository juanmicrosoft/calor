#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "--self-test" ]]; then
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT
  mkdir -p "$work/samples/failing"
  cat >"$work/failing-dotnet" <<'EOF'
#!/usr/bin/env bash
exit 17
EOF
  chmod +x "$work/failing-dotnet"
  if CALOR_DOTNET="$work/failing-dotnet" CALOR_SAMPLES_ROOT="$work/samples" "$0"; then
    echo "ERROR: sample ID gate masked a failing validator" >&2
    exit 1
  fi
  echo "Sample ID negative self-test passed."
  exit 0
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
samples_root="${CALOR_SAMPLES_ROOT:-$repo_root/samples}"
dotnet_command="${CALOR_DOTNET:-dotnet}"

found=0
for sample_dir in "$samples_root"/*/; do
  [[ -d "$sample_dir" ]] || continue
  found=1
  echo "== ids check $sample_dir =="
  "$dotnet_command" run -c Release --no-build \
    --project "$repo_root/src/Calor.Compiler" -- \
    ids check "$sample_dir" --allow-test-ids
done

if [[ "$found" -eq 0 ]]; then
  echo "ERROR: no sample directories found under $samples_root" >&2
  exit 1
fi
