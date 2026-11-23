#!/usr/bin/env bash
# Collects code coverage for the ratcheted test projects into artifacts/coverage/raw, for
# scripts/check_coverage.py. The coverage step of test.yml (quality-ratchets) and of
# publish-nuget.yml (release-quality) both call this, so the two cannot drift apart.
#
# #1749: the step ran Calor.Compiler.Tests' four parts one after another, about 10, 13,
# 14 and 13.5 min with coverage on (main run 45255366373). With the other four projects it took
# 50-59 min of quality-ratchets' 70-minute limit, and #1755's first run was cancelled at that
# limit. The parts now run as the compiler shards do (scripts/compiler-shard-filter.sh --pairs):
# the two parts of a pair at once, each in its own test host, the pairs back to back. Two hosts
# use two of the runner's four cores; the shards have run pairs this way since #1730.
#
# Safe for coverage: check_coverage.py rglobs every coverage.cobertura.xml under
# artifacts/coverage/raw and merges by taking max(hits) per (file, line), so partial reports
# for one project union correctly. Each `dotnet test` writes its report into its own
# GUID-named folder, so concurrent hosts never share a file.
#
# The historical replays are not part of any --part filter: a replay measures an archived tree,
# not this one (see compiler-shard-filter.sh).
#
# usage: bash scripts/collect-coverage.sh
set -euo pipefail
test "$#" -eq 0

raw=artifacts/coverage/raw
mkdir -p "$raw"

coverage() {
  # coverage <project> <trx name> [filter]
  local project="$1" trx="$2"
  local filter_args=()
  if [ "$#" -eq 3 ]; then
    filter_args=(--filter "$3")
  fi
  dotnet test "tests/$project/$project.csproj" \
    -c Release --no-build --no-restore \
    ${filter_args[@]+"${filter_args[@]}"} \
    --collect:"XPlat Code Coverage" \
    --logger "trx;LogFileName=$trx.trx" \
    --results-directory "$raw/$project" \
    --verbosity quiet
}

project=Calor.Compiler.Tests
failed=0
for pair in $(bash scripts/compiler-shard-filter.sh --pairs); do
  pids=()
  for part in ${pair//,/ }; do
    filter="$(bash scripts/compiler-shard-filter.sh --part "$part")"
    coverage "$project" "$project-$part" "$filter" > "coverage-part-$part.log" 2>&1 &
    pids+=("$part:$!")
  done
  for entry in "${pids[@]}"; do
    part="${entry%%:*}"
    status=0
    wait "${entry#*:}" || status=$?
    echo "--- $project part $part with coverage (exit $status) ---"
    cat "coverage-part-$part.log"
    [ "$status" = 0 ] || failed=1
  done
done
test "$failed" = 0

for project in \
  Calor.Conversion.Tests \
  Calor.Enforcement.Tests \
  Calor.Semantics.Tests \
  Calor.Verification.Tests
do
  coverage "$project" "$project"
done
