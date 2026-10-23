# D1 #1427 — Codex adversarial review, round 3

- **Reviewer:** OpenAI Codex CLI, same command and reduced-independence terms as rounds 1 and 2.
- **Input:** `git diff origin/main...HEAD` at commit `9ef6f234` (round-2 fixes plus the RO-CALL-3 CS9191 clause). A first attempt stopped at the Codex usage limit before a verdict; per the capacity rule the run waited for the reset and was repeated in full. Only the completed run is recorded.
- **Prompt:** the round-1 prompt plus: verify each round-2 disposition, then find new or remaining problems.
- **Verdict:** REQUEST CHANGES (1 BLOCKING, 2 MAJOR, 1 MINOR).

Round-2 verification by the reviewer: items 1, 2 (with the existing `AllDiagnosticCodeConstants_AreUnique` test), 5, 6 and 7 fixed; 3 and 4 partially fixed (finding 1 below).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | The alias condition said "any call or heap write", narrower than R-OBL's `MayChangeHeap`, which also counts member, element and property reads (a getter may run), `§NEW`, enumeration and collection mutation. R-OBL's own witness (`PropertyGetterRead_KillsAliasedRefParameterFact`) has no explicit call. | **Fixed.** RO-ANA-1 defines "may change the heap" as R-OBL's `MayChangeHeap` boundary; RO-NULL-2 uses exactly RO-ANA-1's condition. New cases D1-ANA-10 (R-OBL's getter witness; Unsupported, holds) and D1-NULL-11 (a getter nulls the aliased field after a narrowing; accepted today, `violates`). New required shape `getter-alias`. |
| 2 | MAJOR | RO-NULL-5 left native `in` annotation mismatches undecided. | **Fixed.** RO-NULL-5 covers `ref`, `out` and `in`. New cases D1-NULL-12 (`?str` to `str:in`) and D1-NULL-13 (`str` to `?str:in`): both Calor0208 today, `holds`. |
| 3 | MAJOR | The cache witness compiled the warm-up under a different file name, so a filename-keyed cache would miss and the witness would pass vacuously. | **Fixed.** Both compilations use the case's file name. RO-ANA-4 and the document require a same-body hit control once an obligation cache exists. |
| 4 | MINOR | `measuredAt` was neither validated nor sealed. | **Fixed.** D001 requires full non-zero SHAs; the seal now covers `measuredAt` and `consumes`. Negative control "measured identity zeroed". |

Reviewer checks: pinned seal, measured `src/` tree, 0.22 blob, all eight R0 fixture hashes and 27 mutant replacements verified by source inspection; .NET tests not executable in its read-only sandbox.
