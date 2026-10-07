# D1 #1427 — Codex adversarial review, round 2

- **Reviewer:** OpenAI Codex CLI, same command and reduced-independence terms as round 1.
- **Input:** `git diff origin/main...HEAD` at commit `3a69dbcc` (round-1 fixes).
- **Prompt:** the round-1 prompt plus: verify each round-1 disposition, then find new or remaining problems.
- **Verdict:** REQUEST CHANGES (1 BLOCKING, 5 MAJOR, 1 MINOR).

Round-1 verification by the reviewer: items 1, 2, 3, 6, 8 and 11 fixed; 4, 5, 7, 9 and 10 fixed or partially fixed, with residuals below.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Deleting `observed.proof` (and flipping status to `violates`) disabled the executable proof, Calor1124 and guard checks without touching the seal. | **Fixed.** D003 requires a recorded ObligationStatus whenever `expected.proof` exists; the case test is driven by `expected.proof` and fails if the observation is missing. Negative control "proof observation removed". |
| 2 | MAJOR | Allocating `InvalidByReferenceOperand` to an existing Calor02xx code (Calor0200, UndefinedReference) would turn an accidental refusal into a pass. | **Fixed.** D007 requires the allocated code to equal the compiler's `DiagnosticCode.InvalidByReferenceOperand` constant (read by reflection). Test `AllocationThatReusesAnExistingCodeFails`; the positive resolution test injects the constant. |
| 3 | MAJOR | RO-ANA-1's fresh-guard rule contradicted R-OBL's alias exception for by-reference parameters. | **Fixed.** RO-ANA-1 states the exception: with aliasable by-reference parameters, no fact about them is usable anywhere. New case D1-ANA-09 (`x` a `ref` parameter): Unsupported. |
| 4 | MAJOR | RO-NULL-2 missed a single by-reference parameter aliasing heap storage. | **Fixed.** RO-NULL-2 uses RO-ANA-1's alias condition (including one parameter plus any call or heap write). New case D1-NULL-10 (`Check(ref K.slot)` then a call that nulls `slot`): accepted today, `violates`. |
| 5 | MAJOR | RO-ORD-1 required a covariance check for `in` array elements, which C# does not perform. | **Fixed.** RO-ORD-1 limits the covariance check to `ref`/`out`; `in` must not be emitted as `ref`. New case D1-CS-27 (prints `n=x\|n`; a `ref` mutant throws ArrayTypeMismatchException). New mutant kind `modifier-change`. |
| 6 | MAJOR | Conditional ref operands were unclassified. | **Fixed.** RO-UNS-1 lists `ref (c ? ref a : ref b)` as preserved or refused; a value `§?` cannot stand for it. New case D1-CS-28 and required shape. |
| 7 | MINOR | D1-CS-16's note still said Oblivious. | **Fixed.** The note now defers the Calor state to D1-NULL-08. |

The reviewer verified the source tree, the 0.22 blob, all eight R0 fixture hashes, the seal reconstruction and 26 mutant occurrences; .NET execution was not possible in its read-only sandbox.
