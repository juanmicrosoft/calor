#!/usr/bin/env bash
# Build Calor's publication artifacts from one source tree, the way the release
# workflows do, so two trees can be compared byte for byte.
#
#   scripts/reproducible-build.sh <tree> <out> [packages|website|all]
#
# packages: the publish-nuget.yml `publish` job's commands (locked restore, Release
#           build, pack the CLI with --no-build, pack the SDK with every RID, the SDK
#           content inspection and the packaged-Z3 checks), then
#           scripts/generate-release-metadata.py with the tree's HEAD as the commit.
# website:  the nextjs-gh-pages.yml build (npm ci, npm run build) copied to <out>/website.
#
# Z3 must already be bootstrapped and verified in <tree> (download-z3.sh, the owned
# bootstrap action, or `reproducible_builds.py clone`). Nothing here downloads Z3.
# The hashing and comparison live in scripts/reproducible_builds.py.
set -euo pipefail

if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  echo "usage: $0 <tree> <out> [packages|website|all]" >&2
  exit 2
fi
tree="$(cd "$1" && pwd -P)"
mkdir -p "$2"
out="$(cd "$2" && pwd -P)"
what="${3:-all}"
case "$what" in packages|website|all) ;; *) echo "unknown target: $what" >&2; exit 2 ;; esac

cd "$tree"
commit="$(git rev-parse HEAD)"

if [ "$what" != website ]; then
  rm -rf "$out/nupkg" "$out/release-metadata"
  dotnet restore src/Calor.Compiler/Calor.Compiler.csproj --locked-mode
  dotnet build src/Calor.Compiler/Calor.Compiler.csproj -c Release --no-restore
  dotnet pack src/Calor.Compiler/Calor.Compiler.csproj -c Release --no-build -o "$out/nupkg"
  dotnet pack src/Calor.Sdk/Calor.Sdk.csproj -c Release -o "$out/nupkg" /p:CalorSdkRequireAllRids=true
  bash .github/scripts/inspect-sdk-nupkg.sh "$(ls "$out"/nupkg/Calor.Sdk.*.nupkg)" --all-rids
  python3 scripts/check-packaged-z3.py "$out"/nupkg/calor.*.nupkg --prefix tools/net10.0/any --all-rids
  python3 scripts/check-packaged-z3.py "$out"/nupkg/Calor.Sdk.*.nupkg --prefix tasks/net10.0 --all-rids
  # Same arguments as the publish job; --repository is fixed so a clone's
  # file:// origin URL does not leak into the provenance.
  python3 scripts/generate-release-metadata.py \
    --name "calor-nuget-$commit" \
    --artifacts "$out/nupkg" \
    --output "$out/release-metadata" \
    --repository "https://github.com/juanmicrosoft/calor" \
    --commit "$commit"
fi

if [ "$what" != packages ]; then
  rm -rf "$out/website"
  (cd website && npm ci --no-audit --no-fund && npm run build)
  cp -R website/out "$out/website"
fi
