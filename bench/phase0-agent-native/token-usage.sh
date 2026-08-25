#!/usr/bin/env bash
# Shared token-accounting step for run-pair.sh and run-bundle.sh (#881).
# Source this file, then call:
#
#   token_usage_collect <agent.json> <run-label>
#
# On return, three variables are set for the caller's result.json:
#   TOKENS_IN, TOKENS_OUT   integers — the cost-leg figures (corrected when
#                           token-usage.py succeeded)
#   TOKEN_USAGE_JSON        the audit block: token-usage.py's full output, or
#                           {"source":"fallback-naive",...} when the helper
#                           failed (its stderr is echoed as a WARN and kept in
#                           the block), or {"source":"missing"} when there is
#                           no envelope file at all.
#
# Contract (review of PR #1092, finding 2): a helper crash must NEVER become
# a silent tokens.output = 0 on a valid run — that would be worse than the
# original under-count. The fallback is the old `jq .usage.output_tokens`
# read, labelled as such. The helper path is overridable for tests via
# TOKEN_USAGE_HELPER.

token_usage_collect() {
    local envelope="$1" label="${2:-run}"
    local helper="${TOKEN_USAGE_HELPER:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/token-usage.py}"
    TOKENS_IN=0; TOKENS_OUT=0; TOKEN_USAGE_JSON='{"source":"missing"}'
    [[ -f "$envelope" ]] || return 0

    local out="" err="" src="" rc=0 errfile
    errfile="$(mktemp "${TMPDIR:-/tmp}/token-usage-err.XXXXXX")"
    out=$(python3 "$helper" "$envelope" 2>"$errfile") || rc=$?
    err=$(cat "$errfile" 2>/dev/null || true); rm -f "$errfile"
    if [[ $rc -eq 0 && -n "$out" ]]; then
        src=$(jq -r '.source // empty' <<<"$out" 2>/dev/null || true)
    fi

    if [[ -z "$src" ]]; then
        # Helper crashed, printed nothing, or printed something without .source.
        echo "WARN (#881) [$label]: token-usage.py failed (exit $rc): ${err:-<no stderr>}. Falling back to the naive usage.output_tokens read." >&2
        local naive_in naive_out
        naive_in=$(jq -r '.usage.input_tokens // 0' "$envelope" 2>/dev/null || echo 0)
        naive_out=$(jq -r '.usage.output_tokens // 0' "$envelope" 2>/dev/null || echo 0)
        [[ "$naive_in" =~ ^[0-9]+$ ]] || naive_in=0
        [[ "$naive_out" =~ ^[0-9]+$ ]] || naive_out=0
        TOKENS_IN=$naive_in; TOKENS_OUT=$naive_out
        TOKEN_USAGE_JSON=$(jq -n --argjson i "$naive_in" --argjson o "$naive_out" \
            --argjson rc "$rc" --arg err "$err" \
            '{source:"fallback-naive", input_tokens_naive:$i, output_tokens_naive:$o,
              helper_exit_code:$rc, helper_stderr:$err}')
        return 0
    fi

    TOKEN_USAGE_JSON="$out"
    TOKENS_IN=$(jq -r '.input_tokens_corrected // 0' <<<"$out")
    TOKENS_OUT=$(jq -r '.output_tokens_corrected // 0' <<<"$out")
    if [[ "$src" == "missing" ]]; then
        echo "WARN (#881) [$label]: $envelope exists but holds no readable result envelope (empty or truncated JSON); tokens recorded as 0 with tokenUsage.source=missing." >&2
    elif [[ "$(jq -r '.undercount_flagged // false' <<<"$out")" == "true" ]]; then
        echo "WARN (#881) [$label]: agent.json usage.output_tokens=$(jq -r '.output_tokens_naive' <<<"$out") under-counts; modelUsage total=$TOKENS_OUT (origin=$(jq -r '.origin_kind // "none"' <<<"$out")). Recording the corrected figure." >&2
    fi
    return 0
}
