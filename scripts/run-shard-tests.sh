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
# #1150: the compiler shard runs in FOUR invocations, so each `dotnet test` gets a FRESH test host
# and no single host accumulates the whole suite. This CAPS THE SYMPTOM; it does not fix the leak
# (see docs/plans/2026-09-03-issue-1150-kill-rate-measurement.md). The parts come from
# scripts/compiler-shard-filter.sh, whose final part complements the other three.
#
# 0.30 (#1730): the historical replays (Category=HistoricalReplay) are a fifth part. Each
# rebuilds an archived compiler and reruns one test with it, about 2-4 minutes, and they cost
# the serial parts about half an hour. Their host starts in the background after the build and
# runs beside the parts on cores the serial suite leaves idle; the step waits for it and fails
# when it fails. Its TRX is checked like every other part.
#
# 0.30 (#1730): the four parts run at the same time, each in its own fresh host with its own log
# and TRX (each host runs its tests serially, so the 4-vCPU runner has a core per part). The step
# waits for every part, prints each log, then fails if any part failed. Two pairs run back to back
# used 40.3 min on a slow runner (run 44793777859), past the job limit; four at once bound the
# step by the slowest part instead of two parts in sequence.
#
# 0.31 (#1749): five hosts on one 4-vCPU runner still took 32-33 min and hit the 40-minute cap on
# slow runners with every test passing. The job name now selects the hosts: test.yml and
# publish-nuget.yml run `compiler-1` (parts 1 and 4), `compiler-2` (parts 2 and 3) and
# `compiler-replay` (the replay host) on three runners. Each writes the same TRX names prefixed by
# its job name, and each workflow's `tests (compiler)` / `test (compiler)` job checks all of them
# together against eng/test-manifest.json. `compiler` still runs all five hosts on one machine,
# for a local run; no workflow uses it.
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
case "$name" in
  compiler|compiler-1|compiler-2|compiler-replay) compiler_job="$name" ;;
  *) compiler_job="" ;;
esac
if [ -n "$compiler_job" ]; then
  # 0.30 (#1730): five hosts (four parts and the replay host, which rebuilds archived
  # compilers) peak above the runner's 16 GB of RAM plus its 3 GB of swap: two runs on
  # 2026-11-20 filled the swap, and the second was killed (exit 143, memAvail 47M). On a
  # Linux runner with passwordless sudo, add an 8 GB swap file before the hosts start.
  # Elsewhere (no sudo, not Linux) this is skipped and the step runs as before.
  if [ "$(uname -s)" = "Linux" ] && sudo -n true 2>/dev/null && [ ! -e /calor-extra.swap ]; then
    if sudo fallocate -l 8G /calor-extra.swap && sudo chmod 600 /calor-extra.swap \
        && sudo mkswap /calor-extra.swap > /dev/null && sudo swapon /calor-extra.swap; then
      echo "Added 8 GB swap: $(swapon --show=NAME,SIZE --noheadings | tr '\n' ' ')"
    else
      echo "Could not add swap; continuing with the runner's own." >&2
    fi
  fi
  bash scripts/compiler-shard-filter.sh --self-test
  # The invocations pass --no-build, so build once up front.
  dotnet build "$project" -c Release
  # #1749: the job name selects the parts. Both workflows split them over compiler-1, compiler-2
  # and compiler-replay, and a gathering job checks the three jobs' reports against the manifest.
  # compiler (all four parts and the replays) is for a local run.
  parts="$(bash scripts/compiler-shard-filter.sh --job "$name")"
  replay_pid=""
  if [ "$name" = "compiler" ] || [ "$name" = "compiler-replay" ]; then
    replay_filter="$(bash scripts/compiler-shard-filter.sh --replay)"
    dotnet test "$project" -c Release --no-build --no-restore \
      --filter "$replay_filter" \
      --logger "trx;LogFileName=$name-replay.trx" \
      --results-directory artifacts/test \
      --verbosity normal > replay-output.log 2>&1 &
    replay_pid=$!
  fi
  # Every part but the last in the background; the last in the foreground, then wait for the rest.
  part_pids=()
  for part in $(printf '%s\n' "$parts" | sed '$d'); do
    filter="$(bash scripts/compiler-shard-filter.sh --part "$part")"
    dotnet test "$project" -c Release --no-build --no-restore \
      --filter "$filter" \
      --logger "trx;LogFileName=$name-$part.trx" \
      --results-directory artifacts/test \
      --verbosity normal > "part-$part.log" 2>&1 &
    part_pids+=("$part:$!")
  done
  part="$(printf '%s\n' "$parts" | tail -n 1)"
  if [ -n "$part" ]; then
    filter="$(bash scripts/compiler-shard-filter.sh --part "$part")"
    dotnet test "$project" -c Release --no-build --no-restore \
      --filter "$filter" \
      --logger "trx;LogFileName=$name-$part.trx" \
      --results-directory artifacts/test \
      --verbosity normal 2>&1 | tee -a test-output.log
  fi
  parts_failed=0
  for entry in ${part_pids[@]+"${part_pids[@]}"}; do
    part="${entry%%:*}"
    part_status=0
    wait "${entry#*:}" || part_status=$?
    echo "--- compiler part $part (exit $part_status) ---"
    tee -a test-output.log < "part-$part.log"
    [ "$part_status" = 0 ] || parts_failed=1
  done
  replay_status=0
  if [ -n "$replay_pid" ]; then
    wait "$replay_pid" || replay_status=$?
    echo "--- historical replay host (exit $replay_status) ---"
    tee -a test-output.log < replay-output.log
  fi
  test "$replay_status" = 0
  test "$parts_failed" = 0
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
