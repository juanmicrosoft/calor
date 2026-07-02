# N1-001 — String Utils (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Existing behavior (already implemented in the starting fixture)

`Slugify(s)` → string — lowercases `s` and replaces every space with a hyphen.

## Task: implement the missing operations

1. `Truncate(s, maxLen)` → string — shortens `s` to at most `maxLen`
   characters:
   - If `s` has `maxLen` or fewer characters, return `s` unchanged.
   - Otherwise, if `maxLen > 3`, return the first `maxLen - 3` characters of
     `s` followed by `"..."` (the result is exactly `maxLen` characters).
   - Otherwise (`maxLen <= 3`, ellipsis does not fit), return the first
     `maxLen` characters of `s` with no ellipsis. `maxLen` of `0` yields the
     empty string. `maxLen` is never negative.
2. `WordCount(s)` → integer — the number of words in `s`, where words are
   maximal runs of non-whitespace characters separated by whitespace (spaces,
   tabs, carriage returns, and/or newlines). The empty string and
   whitespace-only strings contain `0` words. Leading/trailing whitespace and
   repeated whitespace between words must not affect the count.

## Constraints

- Pure string computation only: no filesystem, network, console, or other
  side effects. Declare whatever your language requires to express that.
- Do not change `Slugify`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `Slugify(string) → string`, `Truncate(string, int) → string`,
`WordCount(string) → int`, reachable through the arm's `TestShim.cs`
(provided by the harness; not editable by the agent).
