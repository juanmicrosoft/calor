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
#   CALOR_SDK_EXPECTED_RID=<rid>   require the runner and packaged native asset
#                                 to match this RID.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

VERSION="${CALOR_SDK_VERSION:-$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$REPO_ROOT/Directory.Build.props" | head -1)}"
if [ -z "$VERSION" ]; then
  echo "ERROR: could not read <Version> from Directory.Build.props" >&2
  exit 1
fi
echo "== Calor.Sdk M-A1 consumer check (version $VERSION) =="

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
FEED="$WORK/feed"
CONSUMER="$WORK/consumer"
mkdir -p "$FEED"

echo "== 1. pack Calor.Sdk -> local feed =="
dotnet pack "$REPO_ROOT/src/Calor.Sdk/Calor.Sdk.csproj" -c Release -o "$FEED" -p:Version="$VERSION"

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

if [ -n "${CALOR_SDK_EXPECTED_RID:-}" ]; then
  ACTUAL_RID="$(dotnet --info | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -1 | tr -d '\r')"
  if [ "$ACTUAL_RID" != "$CALOR_SDK_EXPECTED_RID" ]; then
    echo "ERROR: expected runner RID $CALOR_SDK_EXPECTED_RID, actual RID $ACTUAL_RID" >&2
    exit 1
  fi

  case "$CALOR_SDK_EXPECTED_RID" in
    linux-x64|linux-arm64)
      NATIVE_ENTRY="tasks/net10.0/runtimes/$CALOR_SDK_EXPECTED_RID/native/libz3.so"
      ;;
    osx-arm64)
      NATIVE_ENTRY="tasks/net10.0/runtimes/osx-arm64/native/libz3.dylib"
      ;;
    win-x64|win-arm64)
      NATIVE_ENTRY="tasks/net10.0/runtimes/$CALOR_SDK_EXPECTED_RID/native/libz3.dll"
      ;;
    *)
      echo "ERROR: unsupported CALOR_SDK_EXPECTED_RID: $CALOR_SDK_EXPECTED_RID" >&2
      exit 1
      ;;
  esac

  if ! unzip -Z1 "$NUPKG" | grep -qxF "$NATIVE_ENTRY"; then
    echo "ERROR: package is missing current runner native: $NATIVE_ENTRY" >&2
    exit 1
  fi
  echo "OK: package contains and loads Z3 for runner RID $ACTUAL_RID"
fi

export NUGET_PACKAGES="$WORK/packages"   # fresh consumer cache: restore must come from the feed

echo "== 3. materialize consumer from template =="
cp -R "$REPO_ROOT/tests/SdkConsumer" "$CONSUMER"
# Substitute placeholders (the template is deliberately unbuildable in place).
python3 - "$CONSUMER" "$VERSION" "$FEED" <<'EOF'
import pathlib, sys
consumer, version, feed = sys.argv[1:4]
for name in ("CalorLib/CalorLib.csproj", "nuget.config"):
    p = pathlib.Path(consumer) / name
    text = p.read_text()
    text = text.replace("CALOR_SDK_VERSION_PLACEHOLDER", version)
    text = text.replace("CALOR_LOCAL_FEED_PLACEHOLDER", feed)
    p.write_text(text)
EOF

echo "== 4. locked restore (packaged SDK from local feed only) =="
cd "$CONSUMER"
dotnet restore CalorLib.Tests/CalorLib.Tests.csproj --use-lock-file

echo "== 5. offline locked restore (feed unavailable; fresh package cache retained) =="
mv "$FEED" "$FEED.offline"
dotnet restore CalorLib.Tests/CalorLib.Tests.csproj \
  --locked-mode --force-evaluate --ignore-failed-sources
mv "$FEED.offline" "$FEED"

echo "== 6. clean Release build (CalorVerify=true; Z3 canary) =="
dotnet clean CalorLib.Tests/CalorLib.Tests.csproj -c Release
BUILD_LOG="$WORK/build.log"
if ! dotnet build CalorLib.Tests/CalorLib.Tests.csproj -c Release --no-restore \
  -p:CalorVerbose=true >"$BUILD_LOG" 2>&1; then
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

echo "== 7. incremental Release rebuild =="
INCREMENTAL_LOG="$WORK/incremental.log"
dotnet build CalorLib.Tests/CalorLib.Tests.csproj -c Release --no-restore \
  -p:CalorVerbose=true --verbosity normal >"$INCREMENTAL_LOG" 2>&1
if ! grep -q "Calor: skipping (up-to-date):" "$INCREMENTAL_LOG"; then
  echo "ERROR: packaged SDK incremental rebuild did not report an up-to-date Calor input" >&2
  cat "$INCREMENTAL_LOG" >&2
  exit 1
fi

echo "== 8. design-time build =="
dotnet msbuild CalorLib/CalorLib.csproj \
  -t:Compile -p:Configuration=Release -p:DesignTimeBuild=true \
  -p:BuildingProject=false -p:ProvideCommandLineArgs=true -v:minimal

echo "== 9. clean Debug build =="
dotnet clean CalorLib.Tests/CalorLib.Tests.csproj -c Debug
dotnet build CalorLib.Tests/CalorLib.Tests.csproj -c Debug --no-restore

echo "== 10. execute consumer assertions =="
dotnet run --project CalorLib.Tests/CalorLib.Tests.csproj -c Release --no-build

echo "== M-A1 PASS: versioned consumer restored offline, built clean/incremental/design-time, and tested against Calor.Sdk $VERSION =="
