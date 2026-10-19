# W1 (#1429) Codex review, round 3 (invocation 3 of at most 4; last adversarial round)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt: verify the
round-2 items, then adversarial review of `git diff origin/main...HEAD` at `c3e924d4d` (base `0b9568b3f`).
**Verdict:** REQUEST-CHANGES (1 MAJOR, 2 MINOR; every round-2 item VERIFIED). Reduced independence (R0 §9).

## Round-2 verification

R1-4, R1-5, R1-6, R1-7, R2-1, R2-2, R2-3, R2-4: all VERIFIED (ordered, indentation-preserving fragment
check and explicit required markers; block versus expression-bodied preservation; hole bounds;
rectangular-only multi-line elements; inert `[NotNullWhen]` pinned by `W1-F1-notnullwhen`; unreleased
qualifiers; `post-validation-fallback` labelled; codes pinned with counts). Wording scan: 0 findings.

## New findings

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R3-1 | MAJOR | Nothing binds the C# shown on the guide to the fixture that produced the quoted Calor: editing `x * 2` to `x * 3` on the page passes every check | Fixed: `{/* w1-source: ID */}` markers on the F1, F2, F3 and F5 C# blocks; `WebsiteSources_AreExcerptsOfTheFixtures` requires each to be an ordered excerpt of its fixture (leading whitespace and trailing `//` comments ignored), requires all four markers, and has the `x * 3` mutation as a negative control. The F1 `Bump` and F5 `Pick` blocks were rewritten to match the fixtures line for line |
| R3-2 | MINOR | Hole subset still too broad: call arguments exclude `null` and member access on a non-name receiver (`Pad(typeof(T).Name, 8)`); a verbatim/raw string whose format clause has a backslash keeps its statement as C# | Fixed in the guide and the feature-check row. Not pinned by a new W1 transcript: `InterpolationTargetTypingTests` already pins both shapes as preserved (F2 round-3 witnesses) |
| R3-3 | MINOR | README named `aea06ff8f` as the base after the rebase onto `0b9568b3f` | Fixed: base `0b9568b3f`, with the first-draft base and the note that the rebase changed no `src/`, `website/` or `CHANGELOG.md` file |
