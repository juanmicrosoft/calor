#!/usr/bin/env bash
# Emits the two complementary VSTest filters that split Calor.Compiler.Tests in half.
#
# WHY — issue #1150. The suite's test host accumulates anonymous memory that no GC
# setting can reach; it climbs past 9 GB on a 16 GB runner and the job is killed
# (exit 143) at a measured 14.7 % rate. Six candidate causes have been tested and
# refuted, so the allocator is still unattributed — see
# docs/plans/2026-09-03-issue-1150-kill-rate-measurement.md.
#
# Running the suite as TWO invocations gives each a fresh test host, so neither
# accumulates the whole suite. THIS CAPS THE SYMPTOM AND DOES NOT FIX THE LEAK: a
# single-host run on Linux still exhausts a 16 GB machine, and a green pipeline here
# must not be read as #1150 being solved.
#
# WHY A SCRIPT rather than the filters inlined at each call site: the suite runs in
# compiler test and coverage gates in test.yml and publish-nuget.yml, and all
# must split the same way. Inlined copies
# would drift, and a drifted partition fails silently: the halves stop being
# complementary, tests get skipped or double-run, and nothing says so. One source,
# shared callers.
#
# THE SPLIT is by test-class initial, balanced from real counts rather than guessed:
# these letters select 3,996 tests and their complement 3,982, of 7,978.
#
# Usage:
#   eval "$(bash scripts/compiler-shard-filter.sh)"   # sets SHARD1_FILTER, SHARD2_FILTER
# or:
#   filter="$(bash scripts/compiler-shard-filter.sh --part 1)"  # raw filter, no eval
# or:
#   bash scripts/compiler-shard-filter.sh --self-test

set -euo pipefail

SHARD1_LETTERS="B C G L O Q R S T U Y"

build_filters() {
    local include="" exclude="" letter
    for letter in $SHARD1_LETTERS; do
        include="${include:+$include|}FullyQualifiedName~Calor.Compiler.Tests.$letter"
        exclude="${exclude:+$exclude&}FullyQualifiedName!~Calor.Compiler.Tests.$letter"
    done
    SHARD1_FILTER="$include"
    SHARD2_FILTER="$exclude"
}

usage() {
    echo "Usage: $0 [--self-test | --part 1|2]" >&2
    exit 2
}

if [ "$#" -gt 0 ]; then
    case "$1" in
        --self-test) [ "$#" -eq 1 ] || usage ;;
        --part)
            [ "$#" -eq 2 ] || usage
            build_filters
            case "$2" in
                1) printf '%s\n' "$SHARD1_FILTER" ;;
                2) printf '%s\n' "$SHARD2_FILTER" ;;
                *) usage ;;
            esac
            exit 0
            ;;
        *) usage ;;
    esac
fi

if [ "${1:-}" = "--self-test" ]; then
    build_filters
    fail=0

    # Shard 2 must be the exact NEGATION of shard 1. If it is not, the halves stop
    # partitioning the suite and tests are silently skipped or run twice.
    expected_negation="$(printf '%s' "$SHARD1_FILTER" | sed 's/|/\&/g; s/FullyQualifiedName~/FullyQualifiedName!~/g')"
    [ "$SHARD2_FILTER" = "$expected_negation" ] \
        || { echo "FAIL: shard 2 is not the negation of shard 1"; fail=1; }

    # Word splitting must actually happen. The interactive shell used to develop this
    # is zsh, which does NOT split an unquoted variable, and the first version of this
    # loop silently produced ONE clause containing the whole letter list — a filter
    # that matched nothing while its negation matched everything.
    clauses="$(printf '%s' "$SHARD1_FILTER" | tr '|' '\n' | grep -c .)"
    letters="$(printf '%s' "$SHARD1_LETTERS" | wc -w | tr -d ' ')"
    [ "$clauses" = "$letters" ] \
        || { echo "FAIL: $clauses clauses for $letters letters — word splitting is broken"; fail=1; }

    [ "$(bash "$0" --part 1)" = "$SHARD1_FILTER" ] \
        && [ "$(bash "$0" --part 2)" = "$SHARD2_FILTER" ] \
        || { echo "FAIL: raw filter interface differs from the shared partition"; fail=1; }
    expected_assignments="$(printf 'SHARD1_FILTER=%q\nSHARD2_FILTER=%q\n' "$SHARD1_FILTER" "$SHARD2_FILTER")"
    [ "$(bash "$0")" = "$expected_assignments" ] \
        || { echo "FAIL: legacy assignment output changed"; fail=1; }
    for invalid in 0 3 invalid ""; do
        if bash "$0" --part "$invalid" >/dev/null 2>&1; then
            echo "FAIL: accepted invalid part '$invalid'"
            fail=1
        fi
    done
    if bash "$0" --part >/dev/null 2>&1 \
        || bash "$0" --part 1 extra >/dev/null 2>&1 \
        || bash "$0" --self-test extra >/dev/null 2>&1 \
        || bash "$0" --unknown >/dev/null 2>&1; then
        echo "FAIL: accepted invalid arguments"
        fail=1
    fi

    [ "$fail" = "0" ] && echo "compiler-shard-filter self-test passed ($clauses clauses, negation exact)."
    exit "$fail"
fi

build_filters
printf 'SHARD1_FILTER=%q\n' "$SHARD1_FILTER"
printf 'SHARD2_FILTER=%q\n' "$SHARD2_FILTER"
