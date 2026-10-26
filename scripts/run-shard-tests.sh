#!/usr/bin/env bash
# Runs one test shard: the body of the "Run project tests" step in test.yml (remaining-tests) and
# publish-nuget.yml (test). Both steps are exactly
#   run: bash scripts/run-shard-tests.sh "${{ matrix.name }}" "${{ matrix.project }}"
# with no inline shell, and scripts/determinism_protocol.py (D014) checks that call, pins this
# file's SHA-256 (a change records the pin it replaces in repinnedFrom, D016), and checks its
# dotnet test commands and structure.
#
# #1666: a script that is changed and re-pinned could still skip or mask its tests (an
# `if (( 0 ))` wrapper, `trap 'exit 0' EXIT`). That no longer passes: the next step,
# "Enforce test inventory", runs scripts/check_trx.py on the TRX files the test host writes
# here. It checks each part on its own (tests executed, every executed test passed, results only
# from this project's assembly, no test in two parts) and the sum against eng/test-manifest.json.
# A skipped part leaves no TRX, and a masked failure is still a Failed result in it.
#
# #1150: the compiler shard runs in TWO invocations, so each `dotnet test` gets a FRESH test host
# and no single host accumulates the whole suite. This CAPS THE SYMPTOM; it does not fix the leak
# (see docs/plans/2026-09-03-issue-1150-kill-rate-measurement.md). The halves come from
# scripts/compiler-shard-filter.sh, whose part 2 is the exact negation of part 1.
set -euo pipefail
# usage: bash scripts/run-shard-tests.sh <matrix name> <test project>
test "$#" -eq 2
name="$1"
project="$2"
# A report left from anything earlier is never read as this run's.
rm -rf artifacts/test
mkdir -p artifacts/test
: > test-output.log
# #1150: stream resource readings into this step's log; a runner shutdown cannot be observed by a
# later step. Its own stdout, not through tee, so test-output.log stays clean for the Z3 grep.
bash scripts/runner-resource-sampler.sh 10 &
sampler_pid=$!
trap 'kill "$sampler_pid" 2>/dev/null || true' EXIT
if [ "$name" = "compiler" ]; then
  bash scripts/compiler-shard-filter.sh --self-test
  # The two invocations pass --no-build, so build once up front.
  dotnet build "$project" -c Release
  for part in 1 2; do
    filter="$(bash scripts/compiler-shard-filter.sh --part "$part")"
    dotnet test "$project" -c Release --no-build --no-restore \
      --filter "$filter" \
      --logger "trx;LogFileName=$name-$part.trx" \
      --results-directory artifacts/test \
      --verbosity normal 2>&1 | tee -a test-output.log
  done
else
  dotnet test "$project" -c Release \
    --logger "trx;LogFileName=$name.trx" \
    --results-directory artifacts/test \
    --verbosity normal 2>&1 | tee -a test-output.log
fi
if grep -q "Z3 not available" test-output.log; then
  echo "::error::Z3-gated tests skipped — natives did not reach this test host."
  grep -c "Z3 not available" test-output.log
  exit 1
fi
