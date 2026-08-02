#!/usr/bin/env bash
# M-A1 SDK-consumability check (wedge plan v0.11 D-W1.2, #787 / #790).
#
# Packs Calor.Sdk into a local folder feed, then proves that a fresh template
# consumer project (tests/SdkConsumer) restores, builds, and tests green
# against the PACKAGED SDK only — no source-tree ProjectReferences, no warm
# NuGet cache. Also asserts the packaged Z3 verify gate actually runs in the
# MSBuild task context (Calor0712 canary present, Calor0710 absent).
#
# Env:
#   CALOR_SDK_REQUIRE_ALL_RIDS=1  require Z3 natives for all supported RIDs in
#                                 the package inspection (CI, after the full
#                                 Z3 download; local dev machines carry fewer).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$REPO_ROOT/Directory.Build.props" | head -1)"
if [ -z "$VERSION" ]; then
  echo "ERROR: could not read <Version> from Directory.Build.props" >&2
  exit 1
fi
echo "== Calor.Sdk M-A1 consumer check (version $VERSION) =="

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
FEED="$WORK/feed"
CONSUMER="$WORK/consumer"
export NUGET_PACKAGES="$WORK/packages"   # fresh cache: restore must come from the feed
mkdir -p "$FEED"

echo "== 1. pack Calor.Sdk -> local feed =="
dotnet pack "$REPO_ROOT/src/Calor.Sdk/Calor.Sdk.csproj" -c Release -o "$FEED"

NUPKG="$FEED/Calor.Sdk.$VERSION.nupkg"
if [ ! -f "$NUPKG" ]; then
  echo "ERROR: expected package not produced: $NUPKG" >&2
  ls -la "$FEED" >&2
  exit 1
fi

echo "== 2. package-content inspection =="
if [ "${CALOR_SDK_REQUIRE_ALL_RIDS:-0}" = "1" ]; then
  "$SCRIPT_DIR/inspect-sdk-nupkg.sh" "$NUPKG" --all-rids
else
  "$SCRIPT_DIR/inspect-sdk-nupkg.sh" "$NUPKG"
fi

echo "== 3. materialize consumer from template =="
cp -R "$REPO_ROOT/tests/SdkConsumer" "$CONSUMER"
# Substitute placeholders (the template is deliberately unbuildable in place).
python3 - "$CONSUMER" "$VERSION" "$FEED" <<'EOF'
import pathlib, sys
consumer, version, feed = sys.argv[1:4]
for name in ("global.json", "nuget.config"):
    p = pathlib.Path(consumer) / name
    text = p.read_text()
    text = text.replace("CALOR_SDK_VERSION_PLACEHOLDER", version)
    text = text.replace("CALOR_LOCAL_FEED_PLACEHOLDER", feed)
    p.write_text(text)
EOF

echo "== 4. restore (packaged SDK from local feed only) =="
cd "$CONSUMER"
dotnet restore CalorLib.Tests/CalorLib.Tests.csproj

echo "== 5. build (CalorVerify=true; Z3 canary) =="
BUILD_LOG="$WORK/build.log"
if ! dotnet build CalorLib.Tests/CalorLib.Tests.csproj -c Release --no-restore >"$BUILD_LOG" 2>&1; then
  echo "ERROR: consumer build failed" >&2
  cat "$BUILD_LOG" >&2
  exit 1
fi
tail -n 20 "$BUILD_LOG"

if grep -q "Calor0710" "$BUILD_LOG"; then
  echo "ERROR: Calor0710 in consumer build — packaged Z3 is NOT loadable in the MSBuild task context" >&2
  grep "Calor0710" "$BUILD_LOG" >&2
  exit 1
fi
if ! grep -q "Calor0712" "$BUILD_LOG"; then
  echo "ERROR: expected Calor0712 refutation warning from the verify canary (quotes.calr f003) — Z3 verification did not run" >&2
  cat "$BUILD_LOG" >&2
  exit 1
fi
echo "OK: verify gate ran in the task context (Calor0712 canary present, no Calor0710)"

echo "== 6. test =="
dotnet test CalorLib.Tests/CalorLib.Tests.csproj -c Release --no-build

echo "== M-A1 PASS: consumer restored, built, and tested green against the packaged Calor.Sdk $VERSION =="
