#!/usr/bin/env bash
# Shell-side contract of token-usage.sh (#881, review of PR #1092 finding 2):
# the runners must never write a silent tokens.output = 0 on a valid run.
#   1. corrected figure + WARN on the 55x fixture
#   2. helper crash -> WARN, fallback to the naive figure, source=fallback-naive
#   3. truncated envelope -> helper says "missing", runner WARNs, zeros labelled
#   4. no envelope file -> source=missing, no WARN
#   5. normal run -> corrected == naive, no WARN
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BENCH="$(cd "$HERE/.." && pwd)"
FIX="$HERE/fixtures/token-usage"
# shellcheck source=../token-usage.sh
source "$BENCH/token-usage.sh"

fail() { echo "FAIL: $*" >&2; exit 1; }
pass=0
check() { [[ "$1" == "$2" ]] || fail "$3: expected [$2] got [$1]"; pass=$((pass+1)); }

run() { # run <envelope> -> sets OUT/IN/JSON/ERR
    local err_file; err_file="$(mktemp)"
    token_usage_collect "$1" "t" 2>"$err_file"
    ERR="$(cat "$err_file")"; rm -f "$err_file"
    OUT=$TOKENS_OUT; IN=$TOKENS_IN; JSON=$TOKEN_USAGE_JSON
    jq -e . <<<"$JSON" >/dev/null || fail "TOKEN_USAGE_JSON is not JSON: $JSON"
}

# 1. corrected + WARN
run "$FIX/subagent-run.agent.json"
check "$OUT" 30084 "55x corrected output"
check "$IN" 73 "55x corrected input"
check "$(jq -r .source <<<"$JSON")" modelUsage "55x source"
check "$(jq -r .output_tokens_naive <<<"$JSON")" 543 "55x naive retained"
[[ "$ERR" == *"WARN (#881)"*"under-counts"* ]] || fail "55x: no under-count WARN: $ERR"

# 2. helper crash -> fallback-naive, never 0
CRASH="$(mktemp -t crash-helper.XXXXXX)"; printf 'import sys\nsys.stderr.write("boom: synthetic helper crash\\n")\nsys.exit(3)\n' > "$CRASH"
TOKEN_USAGE_HELPER="$CRASH" run "$FIX/subagent-run.agent.json"
rm -f "$CRASH"
check "$OUT" 543 "crash: naive output kept (not 0)"
check "$IN" 2 "crash: naive input kept"
check "$(jq -r .source <<<"$JSON")" fallback-naive "crash: source"
check "$(jq -r .helper_exit_code <<<"$JSON")" 3 "crash: exit code recorded"
[[ "$(jq -r .helper_stderr <<<"$JSON")" == *"boom"* ]] || fail "crash: stderr not recorded"
[[ "$ERR" == *"WARN (#881)"*"token-usage.py failed"*"boom"* ]] || fail "crash: no WARN with stderr: $ERR"

# 2b. helper prints garbage without .source -> same fallback
GARBAGE="$(mktemp -t garbage-helper.XXXXXX)"; printf 'print("not json at all")\n' > "$GARBAGE"
TOKEN_USAGE_HELPER="$GARBAGE" run "$FIX/normal-run.agent.json"
rm -f "$GARBAGE"
check "$OUT" 8311 "garbage: naive output kept"
check "$(jq -r .source <<<"$JSON")" fallback-naive "garbage: source"

# 3. truncated envelope -> missing, WARN, zeros labelled
run "$FIX/truncated-run.agent.json"
check "$OUT" 0 "truncated: output"
check "$(jq -r .source <<<"$JSON")" missing "truncated: source"
[[ "$ERR" == *"WARN (#881)"*"no readable result envelope"* ]] || fail "truncated: no WARN: $ERR"

# 4. no file -> missing, silent
run "$FIX/does-not-exist.agent.json"
check "$OUT" 0 "absent: output"
check "$(jq -r .source <<<"$JSON")" missing "absent: source"
check "$ERR" "" "absent: no WARN"

# 5. normal -> corrected == naive, no WARN
run "$FIX/normal-run.agent.json"
check "$OUT" 8311 "normal: output"
check "$(jq -r .undercount_flagged <<<"$JSON")" false "normal: not flagged"
check "$ERR" "" "normal: no WARN"

echo "token-usage.sh fallback contract: $pass checks OK"
