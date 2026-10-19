# 0.25 pre-freeze PR (#1576): Codex round 3 (last adversarial round)

- **Invocation 3 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `a6832b738` (the round-2 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 2 MAJOR, 2 MINOR. No BLOCKING.

Codex confirmed: 14,587 = 14,493 + 46 + 4 + 44; all 65 requirement digests and the scope, D1 and T1
seals reproduce; earlier amendments and the 0.24 map entry are unchanged.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | The denominator was bypassable: emptying `scope.cases`, the T1 cases and the READY cases together passed. | Fixed: `denominator_problems` hashes `denominatorV1` with the evidence seal and requires the pinned `InteropScopeTests.FrozenSeal` (`65307b91…`, pinned in the gate), then replays every amendment's added, removed and changed cases from it; the current cases must equal the replay, with the same tracked fields and recomputed requirement digests (S004). Control: a case removed from both the scope and T1. |
| 2 | MAJOR | Anchor checking differed from `website/src/lib/headings.ts` (headings in fenced code, link destinations in slugs, no `-1` suffixes). | Fixed: `heading_ids` follows `headings.ts`: front matter and fenced code hold no headings, ATX and setext headings, links and images contribute their text, `section` for an empty slug, and `-1`, `-2` for repeats over every heading in document order. Checked against every W1 page anchor. Controls: an anchor to a heading inside fenced code fails; an anchor to a repeated heading's `-1` id passes. |
| 3 | MINOR | The duplicate-row control did not isolate the duplicate guard. | Fixed: a duplicated passing row must give only "listed more than once"; the failed-first-row case is a separate control. |
| 4 | MINOR | Only the date prefix of a timestamp was parsed. | Fixed: the whole value must parse as a date or a UTC timestamp. Control: `2026-10-19T99:99:99Z`. |

`ReleaseGateScopePacketTests`: 44 → 49 cases; test manifest 14,587 → 14,592. The producer-removal
control now trips two guards (the evidence seal and the pinned denominator seal); both messages name
`evidenceSeal`.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. MAJOR — scripts/verify_release_adjudication.py:659: The denominator remains bypassable. Read-only probes emptied scope.cases, every T1 family's cases, and READY cases while preserving denominatorV1, amendments and seals. Both packet and READY validation returned zero violations. Comparing T1 with unchecked scope does not enforce the registered denominator. Fix: replay and validate scope's case history against its sealed denominator before deriving READY requirements; add a control removing cases from both copies before freezing.

2. MAJOR — scripts/verify_release_adjudication.py:627: Anchor validation differs from website/src/lib/headings.ts:18. It accepts #phantom from ## Phantom inside fenced code and includes Markdown link destinations in slugs. It rejects valid duplicate-heading anchors such as #foo-1. Fix: derive anchors through the site's Markdown AST and heading-ID logic, with isolated controls for fences, inline markup and duplicate headings.

3. MINOR — tests/Calor.Compiler.Tests/ReleaseGate/ReleaseGateScopePacketTests.cs:136: The duplicate-row control does not isolate duplicate rejection. Its failed first row independently triggers case-result validation; removing the duplicate guard leaves the test passing because both messages contain F1-REFOUT-03. Fix: duplicate an otherwise passing row and require the "listed more than once" violation. Keep first-row behavior as a separate control.

4. MINOR — scripts/verify_release_adjudication.py:569: Timestamp validation parses only the date prefix. Setting the recorded decision date to 2026-10-19T99:99:99Z produces no precondition violations, contrary to C#'s DateTime.TryParse. Fix: parse the entire accepted timestamp and add an invalid-clock control.

Confirmed: 14,587 = 14,493 + 46 + 4 + 44. All 65 requirement digests and the scope/D1/T1 seals reproduce. Earlier amendments and the 0.24 map entry are unchanged. Seven scoped Python controls and the generator self-test passed; .NET tests were not run under the read-only sandbox.
```
