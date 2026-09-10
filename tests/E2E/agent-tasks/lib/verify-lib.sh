#!/usr/bin/env bash
# Shared helpers for task verifiers.
#
# WHY THIS EXISTS. Verifier checks used to be file-global greps: "does §L{ appear
# anywhere in the file". That passes a wrong answer where the required construct sits in
# some unrelated function while the function the task asked for is an empty stub. It also
# passes when the name appears only in a comment. Both were demonstrated against every
# verifier that used the pattern.
#
# So: extract the named declaration's body first, and grep inside that.

# calor_body <file> <name>
#   Prints the body of the §F / §AF / §MT / §AMT declaration called <name>, stopping at
#   the next declaration at any level. Exits non-zero if no such declaration exists, so a
#   mention in a comment cannot satisfy a check.
#
#   The name is anchored between delimiters — `§F{f001:Sum:pub}` matches "Sum" but NOT
#   "SumMore", which an unanchored match would happily latch onto and then report on the
#   wrong function.
calor_body() {
    local file="$1" name="$2" out
    out=$(awk -v fn="$name" '
        BEGIN { pat = "§(F|AF|MT|AMT)\\{[^}]*:" fn "(:|\\})" }
        $0 ~ pat { found=1; inbody=1; print; next }
        inbody && /^[[:space:]]*§(F|AF|MT|AMT|CL|IFACE|EN|DEL)\{/ { inbody=0 }
        inbody { print }
        END { exit(found ? 0 : 1) }
    ' "$file") || return 1
    printf '%s\n' "$out"
}

# require_in_body <file> <name> <grep-args...> -- <message>
#   Fails with <message> unless the pattern matches inside <name>'s body.
require_in_body() {
    local file="$1" name="$2"; shift 2
    local -a pat=()
    while [[ $# -gt 0 && "$1" != "--" ]]; do pat+=("$1"); shift; done
    shift || true
    local msg="${1:-required construct not found in $name}"
    local body
    body=$(calor_body "$file" "$name") || {
        echo "$name is not declared (a mention in a comment does not count)"; exit 1; }
    grep -q "${pat[@]}" <<<"$body" || { echo "$msg"; exit 1; }
}

# calor_has_decl <file> <kind-regex> <name>
#   True when a declaration of that kind is actually declared with that exact name.
#   `§CL{cl1:Person:pub}` matches "Person" but not "PersonCount", and a bare mention in a
#   comment matches nothing.
calor_has_decl() {
    local file="$1" kind="$2" name="$3"
    grep -qE "§(${kind})\\{[^}]*[:{]${name}(:|\\})" "$file"
}
