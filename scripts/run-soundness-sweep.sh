#!/usr/bin/env bash
# #1589: run the soundness sweep (bench/Calor.Soundness.Sweep) against one build of the compiler and
# gate the result. Used by .github/workflows/soundness-sweep.yml and for local runs.
#
#   scripts/run-soundness-sweep.sh --out DIR [--compiler DIR] [--summary FILE] [--headline FILE] [--run-url URL]
#
# Build both projects in Release first (after the Z3 bootstrap):
#   dotnet build src/Calor.Compiler/Calor.Compiler.csproj -c Release
#   dotnet build bench/Calor.Soundness.Sweep/Calor.Soundness.Sweep.csproj -c Release
#
# All three baseline slots of the harness (B1, N1, P845) point at the same build, so the sweep runs
# main as a single baseline. The harness is macOS ARM64 only (BaselineHost and LoadedImageCapture).
# HOME, CALOR_HOME and DOTNET_CLI_HOME point at a fresh directory under --out for the run, so the
# sweep never reads or writes the real home directory. Exit status is the gate's: 0 pass, 1 fail.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="" compiler="$repo/src/Calor.Compiler/bin/Release/net10.0" summary="" headline="" run_url=""
while [ $# -gt 0 ]; do
  case "$1" in
    --out) out="$2"; shift 2 ;;
    --compiler) compiler="$2"; shift 2 ;;
    --summary) summary="$2"; shift 2 ;;
    --headline) headline="$2"; shift 2 ;;
    --run-url) run_url="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ -n "$out" ] || { echo "--out is required" >&2; exit 2; }

sweep="$repo/bench/Calor.Soundness.Sweep/bin/Release/net10.0/Calor.Soundness.Sweep.dll"
for f in "$sweep" "$compiler/calor.dll"; do
  [ -f "$f" ] || { echo "missing $f: build both projects in Release first" >&2; exit 2; }
done
if [ -e "$out" ] && [ -n "$(ls -A "$out")" ]; then
  echo "$out is not empty; the harness resumes from existing results, so use a fresh directory" >&2
  exit 2
fi
out="$(mkdir -p "$out" && cd "$out" && pwd)"
compiler="$(cd "$compiler" && pwd)"
results="$out/results"
home="$out/home"
mkdir -p "$home"
commit="$(git -C "$repo" rev-parse HEAD)"

isolated() {
  HOME="$home" CALOR_HOME="$home/.calor" DOTNET_CLI_HOME="$home" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$@"
}

set +e
isolated dotnet "$sweep" run --repo "$repo" --out "$results" \
  --b1 "$compiler" --n1 "$compiler" --p845 "$compiler" --registration-commit "$commit" 2>&1 | tee "$out/run.log"
run_exit=${PIPESTATUS[0]}
isolated dotnet "$sweep" report --repo "$repo" --out "$results" 2>&1 | tee "$out/report.log"
report_exit=${PIPESTATUS[0]}
set -e

gate=(python3 "$repo/scripts/soundness_sweep_gate.py" gate --repo "$repo" --out "$results"
  --run-exit "$run_exit" --report-exit "$report_exit")
[ -z "$summary" ] || gate+=(--summary "$summary")
[ -z "$headline" ] || gate+=(--headline "$headline")
[ -z "$run_url" ] || gate+=(--run-url "$run_url")
"${gate[@]}"
