# PR 2 closer-fix verification pass, Codex (#1132 F5, #1554)

- **Exception:** `EXC-F5-PR2-CLOSER-FIX` (R0 scope amendment 1.2.0), recorded in commit `5fac52fd7`
  before the fix and this pass. @juanmicrosoft, 2026-10-17: "One more fix + 1 pass"; any further problem
  drops F5 from 0.25. It allows exactly one fix and exactly one verification-only pass.
- **Reviewer:** OpenAI Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.
  This was the one allowed pass.
- **Scope:** commit `3dbd108ee` (sized expression-position `§ARR` puts the id first unless the parser
  reads the element type first), witness `SizedArrays_OfLowercaseMappedTypes_ParseWithTheirCloser`.
- **Verdict:** **REQUEST CHANGES**, with **no BLOCKING or MAJOR finding**.
  - The scoped defect (`new Guid[0]`) is fixed. The witness would fail on revert, and the recognized-type
    form (`§ARR{i32:arrI32003:n} §/ARR{arrI32003}`) is unchanged.
  - The verdict is NOT-VERIFIED only because the prompt asked for "every Boolean combination". One
    combination still swaps: an element type the parser does not read as a type, with an id it does read
    as a type (for example `guid` with an id `MyArray`). Codex states this "also existed before the fix;
    it is not a new regression". The migration path cannot produce it, because generated array ids start
    with `arr` and are never read as types (`RoslynSyntaxVisitor.cs:12750`).
  - Not changed: the exception allows no further fix. This is recorded as a residual for the maintainer.

## Review output (verbatim)

NOT-VERIFIED: the scoped defect is fixed, but requirement (1) does not hold for every Boolean combination.

Tracing [the emitter](src/Calor.Compiler/Migration/CalorEmitter.cs:3655) and [the parser](src/Calor.Compiler/Parsing/Parser.cs:7579), for ordinary, undotted attributes:

| Type recognized | Id recognized | Emitted order | Result |
|---|---|---|---|
| false | false | id:type | Correct |
| false | true | id:type | **Swapped** |
| true | false | type:id | Correct |
| true | true | id:type | Correct |

For example, `elementType=guid`, `id=MyArray` emits `§ARR{MyArray:guid:0} §/ARR{MyArray}`. The parser treats `MyArray` as the type and `guid` as the id, producing a closer mismatch. This behavior also existed before the fix; it is **not a new regression**.

The actual migration path avoids that case: [generated array ids](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:12750) start with `arr` and contain sanitized letters/digits, so `IsLikelyType(arrGuid152)` is false.

The witness would fail upon reverting: its Guid output assertion requires id-first ordering; the reverted output also makes the parser select `guid` as the id and reject the closer. The recognized-type snapshot `§ARR{i32:arrI32003:n} §/ARR{arrI32003}` remains unchanged.

New BLOCKING or MAJOR regressions found: **none**. The requested universal guarantee remains unverified because the false/true case demonstrably fails.

Read-only inspection only; no Calor or tests executed.

VERDICT: REQUEST CHANGES
