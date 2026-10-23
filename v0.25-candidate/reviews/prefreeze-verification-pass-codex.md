# 0.25 pre-freeze PR (#1576): Codex verification pass

- **Invocation 4 of at most 4.** The review budget is spent.
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `140ecb952` (the round-3 fixes).
- **Verdict:** NOT-VERIFIED. Round-3 findings 3 and 4 VERIFIED; findings 1 and 2 NOT-VERIFIED.

## Open after review (not fixed: the budget is spent, so this PR stops here for a maintainer decision)

| # | Severity | Open finding | Note |
|---|---|---|---|
| 1 | MAJOR | `denominator_problems` replays the amendments more loosely than `InteropScopeValidator` S004: it ignores a change's `from` value, the allowed (tracked) fields, whether a case is active when added or removed, and a removal's `lastStatus`, and it applies removals before changes. A crafted amendment with a wrong `from`, or a removal without `lastStatus` (with matching scope and T1 deletions), passes the gate's packet check. | The pinned denominator and every case removal from the real packet are caught. The gap needs a crafted amendment inside the frozen packet; `InteropScopeValidator` (S004) rejects such an amendment in CI on the candidate, and any packet change after the freeze fails `G005`. A port of the full S004 replay is the fix. |
| 2 | MAJOR | `heading_ids` differs from `website/src/lib/headings.ts` for an image nested inside a link: `## [![Build](x.png)](https://example.com)` gives `build-https-example-com`; the site gives `build`. | All 23 W1 references (16 anchored) resolve identically under both implementations. A fix is to resolve nested images before links. |
| 3 | MINOR | The failed-first-row duplicate control still passes if first-wins becomes last-wins, because the duplicate warning also names `F1-REFOUT-03`. | Tighten the message to the failed-row text. |

Verified: the pinned seal equals `InteropScopeTests.FrozenSeal` and the real packet passes; fences, front
matter, simple links and images, empty slugs and repeated headings match the site; duplicate-row
isolation; full date parsing; the producer-drop control's two guards are acceptable; the manifest
(14,592 = 14,493 + 46 + 4 + 49).

## Codex output (final message, verbatim)

```text
Checked HEAD 140ecb952. No files modified.

1. NOT-VERIFIED — denominator/S004. The seal reproduces exactly and equals InteropScopeTests.FrozenSeal; the real committed packet passes. Removing one or all cases from scope and T1 fails. However, denominator_problems ignores S004's from, allowed-field, active-registration and lastStatus checks, and processes removals before changes. Probes with an incorrect from, or a removal lacking lastStatus with matching scope/T1 deletions, pass packet validation.

2. NOT-VERIFIED — heading IDs. Fences, front matter, simple links/images, empty slugs and repeated headings pass focused checks. All 23 W1 references, including 16 anchored references, resolve under both implementations. But ## [![Build](x.png)](https://example.com) produces build-https-example-com; the actual website parser and headings.ts produce build.

3. VERIFIED — duplicate-row isolation. Duplicating a passing row produces only "listed more than once." Removing that guard makes the rejection assertion fail.

4. VERIFIED — full date parse. Valid dates/UTC timestamps pass; invalid clocks, impossible dates and trailing garbage fail. Restoring prefix-only parsing defeats the new invalid-clock control.

Remaining issues and controls:

- MAJOR: The S004 replay and heading conformance gaps above remain unresolved.
- MINOR: The failed-first-row control still passes after replacing first-wins with last-wins: the surviving duplicate warning contains F1-REFOUT-03, satisfying its assertion.
- Other new denominator, timestamp, fenced-anchor and repeated-heading controls distinguish their removed guards. The producer-drop control's two guards are acceptable; both violations identify evidenceSeal.
- Manifest VERIFIED: 14,592 = 14,493 + 46 (#1575) + 4 + 49 (#1576).

Verification used read-only Python mutation probes and the actual website heading implementation. Native .NET fixtures were not run because they write files. No additional regressions observed.

VERDICT: NOT-VERIFIED
```
