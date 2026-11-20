#!/usr/bin/env bash
# Emits four disjoint VSTest filters covering Calor.Compiler.Tests.
#
# WHY — issue #1150 originally measured a 14.7 % host kill rate after anonymous
# memory climbed past 9 GB on a 16 GB runner. The original investigation is in
# docs/plans/2026-09-03-issue-1150-kill-rate-measurement.md.
#
# The 0.29 native traces (#1590) attributed repeated Roslyn PE metadata and an
# inheritance-checker Z3 context ownership leak. Bounded reference reuse and
# deterministic context disposal repair those identified sources. The separate
# unsplit Linux run at 19cf228f0 completed all 16,075 cases (three registered skips),
# with peak sampled RSS 3,183 MiB; see docs/plans/performance-gate-strategy.md and
# PR #1721 for artifacts and later-source verification.
#
# FOUR invocations retain process isolation as defense in depth, not a substitute
# for native allocator evidence. A green split alone never proves a leak fix.
# The actual latest-30-successful-main-run p90 remains a separate #1590 criterion.
#
# WHY A SCRIPT rather than the filters inlined at each call site: the suite runs in
# compiler test and coverage gates in test.yml and publish-nuget.yml, and all
# must split the same way. Inlined copies
# would drift, and a drifted partition fails silently: tests get skipped or
# double-run, and nothing says so. One source,
# shared callers.
#
# THE SPLIT is by test-class initial, remeasured at the 16,075-case inventory:
# 4,062 / 3,938 / 4,041 / 4,034. Binding-routing CLI and W1 guide transcripts
# run once in Tasks.Tests instead (#1590); the class-level CLI loops dominated
# this shard's measured duration. The old two-host split dates from 7,978 cases;
# each of its halves had grown to roughly a whole old suite before these repairs.
# 0.30 (#1730): P moved from the complement to part 1, which balances the paired run
# by measured time (local, paired: parts 239 / 295 / 279 / 507 s became about 337 /
# 295 / 279 / 409 s). Cases at the 22,562 inventory: 6,305 / 4,941 / 5,802 / 5,502,
# and 8 historical replays.
#
# HISTORICAL REPLAYS (0.30, #1730) are the fifth, separate part. Each test in
# Category=HistoricalReplay rebuilds an archived compiler at its registered producer
# commit and reruns one test with it (scripts/registered_ledger_historical_replay.py),
# about 2-4 minutes on a hosted runner. Nine of them in the serial parts cost the
# shard about half an hour, so the four parts exclude them and run-shard-tests.sh runs
# them in their own test host beside the parts, on cores the serial suite leaves idle.
# Coverage loops use --parts only: a replay measures an archived tree, not this one.
#
# Usage:
#   bash scripts/compiler-shard-filter.sh --parts    # prints all part numbers
# or:
#   filter="$(bash scripts/compiler-shard-filter.sh --part 1)"  # raw filter, no eval
# or:
#   filter="$(bash scripts/compiler-shard-filter.sh --replay)"  # the historical replays
# or:
#   bash scripts/compiler-shard-filter.sh --pairs    # "1,4" and "2,3": parts the shard runs together
#
# PAIRS (0.30, #1730): the four parts run two at a time, each in its own test host (separate
# environment, console and CALOR_HOME). Paired by measured duration so the pairs finish close
# together: parts 1 and 4, then 2 and 3. Coverage loops still run --parts one at a time.
# or:
#   bash scripts/compiler-shard-filter.sh --self-test

set -euo pipefail

SHARD_LETTERS=("C D J U P" "B F I Y" "A E S T V")
REPLAY_FILTER="Category=HistoricalReplay"
PAIRS=("1,4" "2,3")
NOT_REPLAY="Category!=HistoricalReplay"
FILTERS=()

build_filters() {
    local include exclude="" letter group
    for group in "${SHARD_LETTERS[@]}"; do
        include=""
        for letter in $group; do
            include="${include:+$include|}FullyQualifiedName~Calor.Compiler.Tests.$letter"
            exclude="${exclude:+$exclude&}FullyQualifiedName!~Calor.Compiler.Tests.$letter"
        done
        FILTERS+=("($include)&$NOT_REPLAY")
    done
    # Include future class initials and shared tests in the final complement.
    FILTERS+=("$exclude&$NOT_REPLAY")
}

usage() {
    echo "Usage: $0 [--self-test | --parts | --part 1|2|3|4 | --replay | --pairs]" >&2
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
        --replay)
            [ "$#" -eq 1 ] || usage
            printf '%s\n' "$REPLAY_FILTER"
            exit 0
            ;;
        --pairs)
            [ "$#" -eq 1 ] || usage
            printf '%s\n' "${PAIRS[@]}"
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
    # Every part excludes the replays, which form the separate fifth part.
    selected=""
    for filter in "${FILTERS[@]:0:3}"; do
        case "$filter" in
            "("*")&$NOT_REPLAY") ;;
            *) echo "FAIL: a letter part does not exclude the historical replays: $filter"; fail=1 ;;
        esac
        filter="${filter#(}"
        selected="$selected${filter%)&$NOT_REPLAY}|"
    done
    expected_negation="$(printf '%s' "${selected%|}" | sed 's/|/\&/g; s/FullyQualifiedName~/FullyQualifiedName!~/g')&$NOT_REPLAY"
    [ "${FILTERS[3]}" = "$expected_negation" ] \
        || { echo "FAIL: shard 4 is not the complement of shards 1-3"; fail=1; }
    [ "$(bash "$0" --replay)" = "$REPLAY_FILTER" ] && [ "$NOT_REPLAY" = "${REPLAY_FILTER/=/!=}" ] \
        || { echo "FAIL: the replay part is not the exact complement of the replay exclusion"; fail=1; }

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
    # The pairs name every part exactly once, two per pair.
    [ "$(bash "$0" --pairs | tr ',' '\n' | sort)" = "$(bash "$0" --parts)" ] \
        && [ "$(bash "$0" --pairs | grep -cx '[1-4],[1-4]')" = 2 ] \
        || { echo "FAIL: the pairs do not name each part exactly once"; fail=1; }
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
        || bash "$0" --replay extra >/dev/null 2>&1 \
        || bash "$0" --pairs extra >/dev/null 2>&1 \
        || bash "$0" --unknown >/dev/null 2>&1; then
        echo "FAIL: accepted invalid arguments"
        fail=1
    fi

    [ "$fail" = "0" ] && echo "compiler-shard-filter self-test passed (four disjoint parts and the replay part, complements exact)."
    exit "$fail"
fi

build_filters
for part in 1 2 3 4; do
    printf 'SHARD%s_FILTER=%q\n' "$part" "${FILTERS[$((part - 1))]}"
done
