#!/usr/bin/env bash
# Roadmap §2.5 gate 7 — clean-consumer install, CLI leg (#790).
#
# The Sdk leg of gate 7 is `sdk-package-consumer` / test-sdk-package.sh. This is
# its counterpart for the `calor` global tool, which until now was only ever
# verified by hand after a publish.
#
# The bar is deliberately NOT exit-0 install. The pre-closure osx-x64 state was
# precisely "installs successfully, silently loses verification" — a consumer
# whose Z3 native is missing still installs, still exits 0 on `calor verify`,
# and reports every contract Skipped. So this script requires a *solver verdict*:
# contracts Proven, none Skipped, no Calor0710.
#
# It then runs the gate's 2026-08-11 amendment (#916 review F3) as a test rather
# than a promise. #916 dropped the mislabeled osx-x64 RID from the package, and
# the roadmap requires that an Intel-mac consumer get *documented degradation* —
# a loud Calor0710, no crash, no silent pass. GitHub does not offer an Intel-mac
# runner we can rely on, so the condition is reproduced exactly on whatever
# runner we have: remove the native for the runner's own RID from the installed
# tool, which is the same state an osx-x64 consumer is in, and assert the
# documented degradation.
#
# Env:
#   CALOR_CLI_VERSION   override the version to pack/install (default: Directory.Build.props)
#   CALOR_EXPECTED_RID  require the runner RID to match (CI pins this per matrix leg)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

VERSION="${CALOR_CLI_VERSION:-$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$REPO_ROOT/Directory.Build.props" | head -1)}"
if [ -z "$VERSION" ]; then
  echo "ERROR: could not read <Version> from Directory.Build.props" >&2
  exit 1
fi
echo "== gate 7 CLI clean-consumer check (calor $VERSION) =="

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
FEED="$WORK/feed"
TOOLDIR="$WORK/tools"
CONSUMER="$WORK/consumer"
mkdir -p "$FEED" "$CONSUMER"

RID="$(dotnet --info | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -1 | tr -d '\r')"
if [ -z "$RID" ]; then
  echo "ERROR: could not determine the runner RID from 'dotnet --info'" >&2
  exit 1
fi
if [ -n "${CALOR_EXPECTED_RID:-}" ] && [ "$RID" != "$CALOR_EXPECTED_RID" ]; then
  echo "ERROR: expected runner RID $CALOR_EXPECTED_RID, actual RID $RID" >&2
  exit 1
fi
echo "runner RID: $RID"

case "$RID" in
  linux-x64|linux-arm64) NATIVE_NAME="libz3.so" ;;
  osx-arm64)             NATIVE_NAME="libz3.dylib" ;;
  win-x64|win-arm64)     NATIVE_NAME="libz3.dll" ;;
  *)
    echo "ERROR: gate 7's frozen RID matrix does not include $RID" >&2
    exit 1
    ;;
esac
NATIVE_ENTRY="tools/net10.0/any/runtimes/$RID/native/$NATIVE_NAME"

echo "== 1. pack calor -> local feed =="
dotnet pack "$REPO_ROOT/src/Calor.Compiler/Calor.Compiler.csproj" -c Release -o "$FEED" -p:Version="$VERSION"

NUPKG="$FEED/calor.$VERSION.nupkg"
if [ ! -f "$NUPKG" ]; then
  echo "ERROR: expected package not produced: $NUPKG" >&2
  ls -la "$FEED" >&2
  exit 1
fi

echo "== 2. package-content inspection =="
ENTRIES="$WORK/entries.txt"
unzip -Z1 "$NUPKG" >"$ENTRIES"

if ! grep -qxF "$NATIVE_ENTRY" "$ENTRIES"; then
  echo "ERROR: package is missing the native for the runner RID: $NATIVE_ENTRY" >&2
  grep "runtimes/" "$ENTRIES" >&2 || true
  exit 1
fi
echo "OK: package carries $NATIVE_ENTRY"

# #916 dropped osx-x64 because upstream ships an arm64 binary under the x64
# label. Asserting its absence keeps the drop a tested fact: if it silently
# returns, the degradation oracle in step 5 stops describing real consumers.
if grep -q "runtimes/osx-x64/" "$ENTRIES"; then
  echo "ERROR: package carries an osx-x64 native; #916 dropped that RID" >&2
  grep "runtimes/osx-x64/" "$ENTRIES" >&2
  exit 1
fi
echo "OK: no osx-x64 native (#916 drop holds)"

echo "== 3. clean-consumer install from the feed =="
export NUGET_PACKAGES="$WORK/packages"   # cold cache: the install must come from the feed
dotnet tool install calor \
  --version "$VERSION" \
  --add-source "$FEED" \
  --tool-path "$TOOLDIR" \
  --ignore-failed-sources

if [ -f "$TOOLDIR/calor.exe" ]; then
  TOOL="$TOOLDIR/calor.exe"
elif [ -f "$TOOLDIR/calor" ]; then
  TOOL="$TOOLDIR/calor"
else
  echo "ERROR: tool install produced no calor executable in $TOOLDIR" >&2
  ls -la "$TOOLDIR" >&2
  exit 1
fi

INSTALLED_VERSION="$("$TOOL" --version | tr -d '\r' | head -1)"
if [ "$INSTALLED_VERSION" != "$VERSION" ]; then
  echo "ERROR: installed tool reports '$INSTALLED_VERSION', expected '$VERSION'" >&2
  exit 1
fi
echo "OK: installed calor reports $INSTALLED_VERSION"

# A clean consumer works on its own files, outside the repo tree.
cp "$REPO_ROOT/samples/Verification/proven-contracts.calr" "$CONSUMER/proven-contracts.calr"

echo "== 4. solver verdict (the gate's actual bar) =="
VERIFY_LOG="$WORK/verify.log"
set +e
(cd "$CONSUMER" && "$TOOL" verify proven-contracts.calr) >"$VERIFY_LOG" 2>&1
VERIFY_EXIT=$?
set -e
cat "$VERIFY_LOG"

if [ "$VERIFY_EXIT" -ne 0 ]; then
  echo "ERROR: 'calor verify' exited $VERIFY_EXIT on a fixture whose contracts all prove" >&2
  exit 1
fi
if grep -q "Calor0710" "$VERIFY_LOG"; then
  echo "ERROR: Calor0710 (Z3 unavailable) from a clean tool install — the packaged solver did not load" >&2
  exit 1
fi

PROVEN="$(sed -n 's/^[[:space:]]*Proven:[[:space:]]*\([0-9][0-9]*\).*/\1/p' "$VERIFY_LOG" | head -1)"
SKIPPED="$(sed -n 's/^[[:space:]]*Skipped:[[:space:]]*\([0-9][0-9]*\).*/\1/p' "$VERIFY_LOG" | head -1)"
if [ -z "$PROVEN" ] || [ -z "$SKIPPED" ]; then
  echo "ERROR: could not read a Proven/Skipped summary out of 'calor verify' output" >&2
  exit 1
fi
if [ "$PROVEN" -lt 1 ]; then
  echo "ERROR: 0 contracts Proven — install succeeded but verification did not happen" >&2
  exit 1
fi
if [ "$SKIPPED" -ne 0 ]; then
  echo "ERROR: $SKIPPED contracts Skipped — this is the silent-degradation state gate 7 exists to catch" >&2
  exit 1
fi
echo "OK: solver verdict on a clean install — Proven $PROVEN, Skipped $SKIPPED"

echo "== 5. degradation oracle: the RID whose native we do not ship =="
# Reproduce an osx-x64 consumer's state: the tool is installed, and there is no
# Z3 native for its RID.
rm -rf "$TOOLDIR/.store/calor/$VERSION/calor/$VERSION/tools/net10.0/any/runtimes/$RID"
if find "$TOOLDIR" -name "$NATIVE_NAME" -print -quit | grep -q .; then
  echo "ERROR: $NATIVE_NAME still present after removal — the oracle would not be testing degradation" >&2
  find "$TOOLDIR" -name "$NATIVE_NAME" >&2
  exit 1
fi

DEGRADED_LOG="$WORK/degraded.log"
set +e
(cd "$CONSUMER" && "$TOOL" verify proven-contracts.calr) >"$DEGRADED_LOG" 2>&1
DEGRADED_EXIT=$?
set -e
cat "$DEGRADED_LOG"

# No crash: a missing native must degrade, not abort. Anything above 128 on
# POSIX is a signal (SIGSEGV/SIGABRT); an unhandled managed exception prints a
# stack trace.
if [ "$DEGRADED_EXIT" -gt 128 ]; then
  echo "ERROR: 'calor verify' died with exit $DEGRADED_EXIT (signal) when Z3 was absent — must degrade, not crash" >&2
  exit 1
fi
if grep -q "Unhandled exception" "$DEGRADED_LOG"; then
  echo "ERROR: unhandled exception when Z3 was absent — must degrade, not crash" >&2
  exit 1
fi

# Loud: the consumer is told verification did not happen.
if ! grep -q "Calor0710" "$DEGRADED_LOG"; then
  echo "ERROR: no Calor0710 when Z3 was absent — this is the silent-verification-loss state" >&2
  exit 1
fi

# No silent pass: nothing may be reported Proven without a solver.
DEGRADED_PROVEN="$(sed -n 's/^[[:space:]]*Proven:[[:space:]]*\([0-9][0-9]*\).*/\1/p' "$DEGRADED_LOG" | head -1)"
if [ -n "$DEGRADED_PROVEN" ] && [ "$DEGRADED_PROVEN" -ne 0 ]; then
  echo "ERROR: $DEGRADED_PROVEN contracts reported Proven with no solver present" >&2
  exit 1
fi
echo "OK: documented degradation — Calor0710 raised, nothing Proven, exit $DEGRADED_EXIT, no crash"

echo "== gate 7 CLI leg PASS: clean install of calor $VERSION on $RID returns a solver verdict, and degrades loudly without its native =="
