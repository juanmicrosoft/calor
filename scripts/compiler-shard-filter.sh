#!/usr/bin/env bash
# Emits four disjoint VSTest filters covering Calor.Compiler.Tests.
#
# WHY — issue #1150. The suite's test host accumulates anonymous memory that no GC
# setting can reach; it climbs past 9 GB on a 16 GB runner and the job is killed
# (exit 143) at a measured 14.7 % rate. Six candidate causes have been tested and
# refuted, so the allocator is still unattributed — see
# docs/plans/2026-09-03-issue-1150-kill-rate-measurement.md.
#
# Running the suite as FOUR invocations gives each a fresh test host, so none
# accumulates the whole suite. THIS CAPS THE SYMPTOM AND DOES NOT FIX THE LEAK: a
# single-host run on Linux still exhausts a 16 GB machine, and a green pipeline here
# must not be read as #1150 being solved.
#
# WHY A SCRIPT rather than the filters inlined at each call site: the suite runs in
# compiler test and coverage gates in test.yml and publish-nuget.yml, and all
# must split the same way. Inlined copies
# would drift, and a drifted partition fails silently: tests get skipped or
# double-run, and nothing says so. One source,
# shared callers.
#
# THE SPLIT is by test-class initial, remeasured at the 16,068-case inventory:
# 4,056 / 3,937 / 4,041 / 4,034. Binding-routing CLI and W1 guide transcripts
# run once in Tasks.Tests instead (#1590); the class-level CLI loops dominated
# this shard's measured duration. The old two-host split dates from 7,978 cases;
# its hosts now run roughly a whole old suite and can exhaust the runner again.
#
# Usage:
#   bash scripts/compiler-shard-filter.sh --parts    # prints all part numbers
# or:
#   filter="$(bash scripts/compiler-shard-filter.sh --part 1)"  # raw filter, no eval
# or:
#   bash scripts/compiler-shard-filter.sh --self-test

set -euo pipefail

SHARD_LETTERS=("C D J U" "B F I Y" "A E S T V")
FILTERS=()

build_filters() {
    local include exclude="" letter group
    for group in "${SHARD_LETTERS[@]}"; do
        include=""
        for letter in $group; do
            include="${include:+$include|}FullyQualifiedName~Calor.Compiler.Tests.$letter"
            exclude="${exclude:+$exclude&}FullyQualifiedName!~Calor.Compiler.Tests.$letter"
        done
        FILTERS+=("$include")
    done
    # Include future class initials and shared tests in the final complement.
    FILTERS+=("$exclude")
}

usage() {
    echo "Usage: $0 [--self-test | --parts | --part 1|2|3|4]" >&2
    exit 2
}

if [ "$#" -gt 0 ]; then
    case "$1" in
        --self-test) [ "$#" -eq 1 ] || usage ;;
        --parts)
            [ "$#" -eq 1 ] || usage
            printf '%s\n' 1 2 3 4
            exit 0
            ;;
        --part)
            [ "$#" -eq 2 ] || usage
            build_filters
            case "$2" in
                1|2|3|4) printf '%s\n' "${FILTERS[$(($2 - 1))]}" ;;
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

    # The first three groups must be disjoint; the last is their exact complement.
    seen=""
    for group in "${SHARD_LETTERS[@]}"; do
        for letter in $group; do
            case " $seen " in
                *" $letter "*) echo "FAIL: overlapping initial $letter"; fail=1 ;;
            esac
            seen="$seen $letter"
        done
    done
    selected="$(printf '%s|' "${FILTERS[@]:0:3}")"
    expected_negation="$(printf '%s' "${selected%|}" | sed 's/|/\&/g; s/FullyQualifiedName~/FullyQualifiedName!~/g')"
    [ "${FILTERS[3]}" = "$expected_negation" ] \
        || { echo "FAIL: shard 4 is not the complement of shards 1-3"; fail=1; }

    # Word splitting must actually happen. The interactive shell used to develop this
    # is zsh, which does NOT split an unquoted variable, and the first version of this
    # loop silently produced ONE clause containing the whole letter list — a filter
    # that matched nothing while its negation matched everything.
    clauses="$(printf '%s' "$selected" | tr '|' '\n' | grep -c .)"
    letters="$(printf '%s' "$seen" | wc -w | tr -d ' ')"
    [ "$clauses" = "$letters" ] \
        || { echo "FAIL: $clauses clauses for $letters letters — word splitting is broken"; fail=1; }

    [ "$(bash "$0" --parts)" = "$(printf '%s\n' 1 2 3 4)" ] \
        || { echo "FAIL: part inventory differs from the shared partition"; fail=1; }
    for part in 1 2 3 4; do
        [ "$(bash "$0" --part "$part")" = "${FILTERS[$((part - 1))]}" ] \
            || { echo "FAIL: raw filter interface differs for part $part"; fail=1; }
    done
    expected_assignments="$(
        for part in 1 2 3 4; do
            printf 'SHARD%s_FILTER=%q\n' "$part" "${FILTERS[$((part - 1))]}"
        done
    )"
    [ "$(bash "$0")" = "$expected_assignments" ] \
        || { echo "FAIL: legacy assignment output changed"; fail=1; }
    for invalid in 0 5 invalid ""; do
        if bash "$0" --part "$invalid" >/dev/null 2>&1; then
            echo "FAIL: accepted invalid part '$invalid'"
            fail=1
        fi
    done
    if bash "$0" --part >/dev/null 2>&1 \
        || bash "$0" --part 1 extra >/dev/null 2>&1 \
        || bash "$0" --self-test extra >/dev/null 2>&1 \
        || bash "$0" --parts extra >/dev/null 2>&1 \
        || bash "$0" --unknown >/dev/null 2>&1; then
        echo "FAIL: accepted invalid arguments"
        fail=1
    fi

    [ "$fail" = "0" ] && echo "compiler-shard-filter self-test passed (four disjoint parts, complement exact)."
    exit "$fail"
fi

build_filters
for part in 1 2 3 4; do
    printf 'SHARD%s_FILTER=%q\n' "$part" "${FILTERS[$((part - 1))]}"
done
