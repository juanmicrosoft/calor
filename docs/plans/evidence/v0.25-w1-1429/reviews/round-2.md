# W1 (#1429) Codex review, round 2 (invocation 2 of at most 4)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt: verify the
round-1 fixes, then adversarial review of `git diff origin/main...HEAD` at `c9c76ed64` (rebased onto
`0b9568b3f`) with the same claims-match-behavior focus. **Verdict:** REQUEST-CHANGES (1 MAJOR, 3 MINOR
new; 4 round-1 items NOT-VERIFIED). Reduced independence (R0 §9).

## Round-1 verification

| Item | Codex | Follow-up |
|---|---|---|
| R1-1 verbose stdout | VERIFIED | — |
| R1-2 MCP diagnostic locations | VERIFIED | — |
| R1-3 local-function slice | VERIFIED | — |
| R1-4 fragment and marker checks | NOT-VERIFIED: `Assert.Superset` argument order makes the required-marker check vacuous; the fragment check trims indentation and is unordered | Fixed: each required marker is asserted with `Assert.True(seen.Contains(...))`; fragment lines must occur in order, with indentation |
| R1-5 by-reference call sites | NOT-VERIFIED: "calls that omit `in`" remained in the member list; an expression-bodied member preserves the whole member | Fixed: removed from the member list; the call-site bullet and table row distinguish block bodies (`§RAW`, `in-parameter`) from expression-bodied members (`§CSHARP`, `method`). Verified with an MCP probe on the base |
| R1-6 hole subset | NOT-VERIFIED: verbatim strings and doubles such as `0.0000001` are preserved | Fixed: guide and feature-check row say regular strings and plain-digit fractional doubles |
| R1-7 rectangular-only multi-line elements | NOT-VERIFIED in the guide and adding-Calor tables | Fixed in both tables |
| R1-8 feature-check.md | VERIFIED | — |
| R1-9 wording scan | VERIFIED | — |

## New findings

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R2-1 | MAJOR | `[NotNullWhen]` on an `out` parameter does not force preservation; it converts natively with the attribute inert (also in the CHANGELOG #943 entry and the `out-parameter` registry text) | Fixed: guide (new "Flow attributes are kept but have no effect" note), adding-Calor table, CHANGELOG, both feature-check pages (fifth registry gap); pinned by new fixture `W1-F1-notnullwhen` (outcome `native` on CLI and MCP, `§I{?str:name:out}[@NotNullWhen(true)]`) |
| R2-2 | MINOR | `cli/self-check` and the guides index present 0.25 behavior without the unreleased qualifier | Fixed: both say "unreleased 0.25 release" |
| R2-3 | MINOR | Not every report feature name is a registry entry (`post-validation-fallback` returns `found: false`) | Fixed on `cli/feature-check` |
| R2-4 | MINOR | `Codes` de-duplicates, so a quoted warning count is not pinned | Fixed: codes are recorded with counts (`Calor0419 x1` for `W1-F5-arrays`) |
