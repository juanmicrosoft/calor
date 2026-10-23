# fix-1516 — Codex verification pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), a
verification-only prompt that lists each recorded finding and asks FIXED / NOT FIXED. The original
review context and the `origin/main...HEAD` diff (src and tests) were appended. Reviewed commit:
05124b455.

## Review output (verbatim)

- **Round 1 — FIXED:** `SharesEmittedName` uses the emitter’s sanitizer and rejects collisions for both locals and parameters read by their definitions.
- **Round 2 — FIXED:** `(inc #)` fails the plain-predicate check, so `_refinementChangesState` prevents all local resolution.
- **Round 3A — FIXED:** Every named refinement predicate and current callable’s inline parameter predicate is checked; dotted `Heap.Next` is rejected even inside an equality.
- **Round 3B — FIXED:** Any non-null argument modifier, including `in`, adds its root to `rebound`; both locals and definition-read parameters are checked against it.
- **Added — FIXED:** Module-wide operator/raw-member detection sets `OperatorsMayBeOverloaded`, which prevents every local from resolving.

No new blocking issue directly caused by these fixes. Static verification only; no builds or tests run.

**Final verdict: APPROVE.**

## Summary of the review

| Round | Verdict | Blocking findings | Status |
|-------|---------|-------------------|--------|
| 1 | REQUEST CHANGES | 1 (sanitized C# name collision) | fixed |
| 2 | REQUEST CHANGES | 1 (state-changing refinement guard) | fixed |
| 3 | REQUEST CHANGES | 2 (getter in a refinement guard; `in` argument) | fixed |
| Verification | APPROVE | 0 | — |

Independence: reduced (maintainer-directed agent plus Codex). Codex reviewed statically; it did not
build or run tests.

## Change after the verification pass (not re-reviewed)

The full `Calor.Compiler.Tests` run after commit 05124b455 failed one test,
`ArchitectureTests.CompilerComponents_MatchDeclaredDependencyContract`: calling
`CSharpEmitter.SanitizeIdentifier` from `Verification/` adds a Verification -> CodeGen dependency,
which `eng/compiler-components.json` does not allow. The round-1 fix now compares names by their
letters and digits only, and the emitter change is reverted. The emitter's sanitizer only drops
characters and prepends `_` or `@`, so two names that emit the same identifier always have the same
letters and digits. The new key therefore flags every collision the emitter can produce, plus some
that it cannot, which only refuses more locals. Local names that the emitter writes as keywords or
literals (`null`, `true`, `false`, `default`, `this`, `base`) are also refused. The review budget
(3 rounds + 1 verification pass) was spent, so this change was not re-reviewed by Codex. The
`LocalSharingItsCSharpName_IsRefused` rows still pass.
