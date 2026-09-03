#!/usr/bin/env bash
# Streams runner resource readings into the CALLING STEP'S LOG, one compact line
# per sample, until killed.
#
# WHY THIS EXISTS, AND WHY IT IS NOT AN `if: always()` STEP
# --------------------------------------------------------
# Issue #1150: the `tests (compiler)` shard is SIGTERM'd mid-run ("The runner has
# received a shutdown signal", exit 143) with ZERO test failures. #1153 added a
# post-hoc probe carrying `if: always()` and the comment "a reading taken at the
# moment of death. `if: always()` for exactly that."
#
# That does not work, and it has been checked rather than assumed. On run
# 33687411104 — a kill that happened AFTER that probe landed — the probe produced
# no output at all: not the step header, not `--- memory ---`, nothing. When the
# runner service is stopping, it does not go on to run more steps, so `always()`
# buys nothing. The job log ends
#
#     22:01:19  Passed …PpE1RouteD_FiresWithoutCitation_IsRejected [1 ms]
#     22:01:48  ##[error]The runner has received a shutdown signal…
#     22:01:48  ##[error]Process completed with exit code 143.
#
# An instrument that cannot fire on the event it was built for measures nothing.
#
# What DOES survive is the step's own streamed stdout: the log is captured live,
# so every line printed before the kill is in it. Hence a sampler running in the
# background OF THE TEST STEP ITSELF, interleaving readings with test output.
#
# The gap in that log is also why the interval is short. Across the three kills
# whose logs were retrieved, the last line of output precedes the kill by 29.1 s,
# 97.1 s and 0.0 s — the job goes SILENT before it dies. A 10 s sampler puts at
# least one reading inside a 29 s window of silence; a 60 s one would usually
# miss it, and the silence is the interesting part.
#
# Usage:  bash scripts/runner-resource-sampler.sh [interval-seconds]
# Emits:  [#1150 probe HH:MM:SS] memAvail=…M memUsed=…M swap=…/…M load=… diskFree=…M top=[…]

set -uo pipefail

interval="${1:-10}"

# --self-test validates the Linux parsing against canned `free -m` output. The
# sampler runs on Linux in CI but is usually edited on macOS, where the Linux
# branch is never exercised — so a typo in the awk would ship as a probe that
# runs, prints "memAvail=M", and records nothing. That silent-instrument failure
# is the one this whole issue is about; it does not get to happen twice.
if [ "${1:-}" = "--self-test" ]; then
    fixture=$(printf '%s\n' \
        "               total        used        free      shared  buff/cache   available" \
        "Mem:           15990        1234        9876          12        4880       14000" \
        "Swap:           4095          17        4078")
    got_mem=$(printf '%s\n' "$fixture" | awk '/^Mem:/ {print $2, $3, $7}')
    got_swap=$(printf '%s\n' "$fixture" | awk '/^Swap:/ {print $3, $2}')
    fail=0
    [ "$got_mem" = "15990 1234 14000" ] || { echo "FAIL Mem: parsed '$got_mem'"; fail=1; }
    [ "$got_swap" = "17 4095" ]         || { echo "FAIL Swap: parsed '$got_swap'"; fail=1; }
    if [ "$fail" = "0" ]; then
        echo "runner-resource-sampler self-test passed (Mem and Swap columns parse)."
    fi
    exit "$fail"
fi

sample_linux() {
    # free -m columns: total used free shared buff/cache available
    local total used avail swap_used swap_total
    read -r total used avail <<<"$(free -m | awk '/^Mem:/ {print $2, $3, $7}')"
    read -r swap_used swap_total <<<"$(free -m | awk '/^Swap:/ {print $3, $2}')"

    local load disk top biggest
    load="$(cut -d' ' -f1-3 /proc/loadavg 2>/dev/null || echo '?')"
    disk="$(df -Pm / 2>/dev/null | awk 'NR==2 {print $4}')"
    # Top three resident processes, name:MB — names the memory holder if there is one.
    top="$(ps -o rss=,comm= -A 2>/dev/null \
           | sort -rn \
           | head -3 \
           | awk '{printf "%s:%dM ", $2, $1/1024}')"
    # `comm` is "dotnet" for the test host, every MSBuild node and the CLI driver
    # alike, so it cannot say WHICH of them is holding the memory. The argv tail
    # can: testhost.dll, MSBuild.dll and the `dotnet test` driver are distinct
    # there. Truncated hard because argv on these processes runs to kilobytes.
    biggest="$(ps -o rss=,args= -A 2>/dev/null \
               | sort -rn \
               | head -1 \
               | cut -c1-160 \
               | sed 's/  */ /g')"

    printf '[#1150 probe %s] memAvail=%sM memUsed=%sM swap=%s/%sM load=%s diskFree=%sM top=[%s]\n' \
        "$(date -u +%H:%M:%S)" "$avail" "$used" "$swap_used" "$swap_total" "$load" "$disk" "${top% }"
    printf '[#1150 argv  %s] %s\n' "$(date -u +%H:%M:%S)" "$biggest"
}

sample_fallback() {
    # macOS / anything without procfs. Developers run this locally; CI is Linux.
    printf '[#1150 probe %s] (no procfs; %s)\n' \
        "$(date -u +%H:%M:%S)" \
        "$(uptime 2>/dev/null | sed 's/^ *//' || echo 'no uptime')"
}

if command -v free >/dev/null 2>&1 && [ -r /proc/loadavg ]; then
    sampler=sample_linux
else
    sampler=sample_fallback
fi

# One reading immediately, so a job that dies early still leaves a baseline.
"$sampler"

while true; do
    sleep "$interval"
    "$sampler"
done
