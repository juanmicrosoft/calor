# 0.25 F1 (#943) evidence: ref/out/in arguments

**Status: IN_PROGRESS** (branch `milestone-0.25/f1-943-refout`). This is implementation evidence,
not a READY record (see "What READY still needs"). Reduced independence: the reviews are OpenAI
Codex records only; nothing here was independently reviewed or verified.

Specification: the merged D1 contract, `docs/plans/v0.25-refout-contract.md` and
`docs/plans/evidence/v0.25-d1-1427/contract.json` (1.0.3), with its §15 answers: parity with
`§ASSIGN` for immutable bindings and loop variables (#1550), RO-EFF-4 parity for bare-name fields
(#1551), the bounded `out var` lowering (RO-DECL-1), and `InvalidByReferenceOperand` allocated here
as **`Calor0212`** (`Calor0211` is #847's).

## What changed

| Surface | Change | Rules |
|---|---|---|
| Binder | `BindCallArguments` refuses, with `Calor0212`, every operand C# cannot pass by reference: values, call results, properties (in-module, metadata, scalar), indexer elements (`§IDX`/`§IDX2D` on a non-array), `this`, readonly fields outside their own instance's constructor, constants, `in` parameters passed as `ref`/`out`, and value-type paths whose root or an intermediate field is not writable storage (stopping at a reference-typed owner). `§A{out} _` is a discard unless a `_` is in scope; `§A{ref} _`/`§A{in} _` are refused. | RO-LV-1/2/3, RO-DECL-2 |
| Binder | Alias-aware narrowing: in a callable with by-reference parameters, anything that may change the heap (every bound kind except literals, built-in operators and string operations over built-in operands, lambdas and plain local reads), any statement that stores to a field, element or aliased parameter, and `§EACH`/`§USE` end the narrowings of all by-reference parameters; a loop's discovery pass applies the same to its back-edge, and also ends narrowings of names a loop body passes as `ref`/`out`. | RO-NULL-2, RO-ALIAS-1 |
| Binder | In-module struct fields resolve through element and dotted paths (`(§IDX{pts} 0).X`, `(§IDX{a} 0).X.Y`), so such operands are typed. | RO-COPY-1 |
| Type checker | An untyped by-reference operand (an unmodelled element or member) skips the exact-type rule; a discard takes the parameter's type. Typed operands keep RO-NULL-5 identity. | RO-NULL-5, RO-DECL-2 |
| Effects | A `ref`/`out` argument charges what `§ASSIGN` to it charges (`mut` for an element, receiver field or dotted path; nothing for a local or bare field). | RO-EFF-1/3/4 |
| C# emitter | Parameter attributes (`[NotNullWhen(true)]` on an `out` parameter) are formatted like other attributes; they had emitted the argument's type name. | RO-NULL-3 |
| Converter | RO-DECL-1 lowering (`LowerOutDeclarations`): `out var x`/`out T x` in an expression statement, local declaration, `return` or `if` condition directly in a block becomes `§B{~x:T}` before the statement when the type is known and the name collides with nothing; everything else (loop/using/lock headers, lambdas, queries, interpolation holes, expression bodies, statements partly kept as C#) preserves the member. | RO-DECL-1/2 |
| Converter | Preservation of RO-UNS-1 shapes and unsafe call shapes: ref locals/returns, conditional refs, `scoped`/`ref readonly`/`this ref`/`this in` parameters, by-reference delegates and lambdas, constructor and indexer arguments, omitted `in`, method-generic by-reference parameters, `_` bound to a declared symbol, a by-reference operand that runs code, a by-reference call whose arguments would be hoisted, and chain decomposition with a by-reference argument. A C# `ref` argument to an `in` parameter becomes `§A{in}`. The Calor emitter never hoists a field path rooted in an element. | RO-UNS-1, RO-COPY-1…4, RO-ORD-1, RO-CALL-1/3 |
| FeatureSupport | `out-var` Full → Partial; `ref-parameter`/`out-parameter`/`in-parameter` descriptions follow the contract (no tuple or `Result` advice). No level is promoted, so no D-S1.5 fixture is required. | RO-MIG-1 |

## Per-row results

Surfaces: `calor convert` default and `--passthrough`, `calor migrate`, MCP `calor_convert` default,
`passthroughOnError`, and `passthroughOnError` + `moduleName`. Each output is compiled with default
options (with `--permissive-effects` only when the default compile fails on effect diagnostics alone,
R0's rule) and its C# run against the original (R0 ProbeRunner). Data: `results.json`, `generated/`.

| Row | Expected | Result on all 6 surfaces | Notes |
|---|---|---|---|
| F1-REFOUT-01 | native | native, matches | control |
| F1-REFOUT-02 | native-or-rejected | native, matches | `out var n` in `?:` lowers to `§B{~n:i32}`; `out _` native. Was inline `§CS{}` |
| F1-REFOUT-03 | native | native, matches | control |
| F1-REFOUT-04 | native | native, matches | was preserved on main at R0 because of an unrelated empty-catch defect |
| F1-REFOUT-05 | native | native, matches | control |
| F1-REFOUT-06 | native | native, matches | control |
| F1-REFOUT-07 | preserved-or-rejected | preserved, matches | `TryCast<T>(…, out T)` and its call sites preserved; `[NotNullWhen]` carried inert |
| F1-REFOUT-08 / X-INT-01 | native | native, matches (`2`) | `Bump(ref pts[0].X)` passes the element field; default compile reports only `Calor0410` (struct members are unknown calls), so the run uses `--permissive-effects` as in R0 |
| F1-REFOUT-A1 | rejected (analysis) | covered by tests | D1-ANA-01…10 (contract cases) and `ByReferenceWrite_NeverDischargesAStaleFact`: no proof is Discharged through a `ref`/`out` write; `in` and by-value controls are |
| F1-REFOUT-A2 | rejected (effects) | covered by tests | D1-EFF-01/02 now `Calor0410`; `ReceiverFieldWrite_ChargesLikeAssign` |
| X-INT-05 | native-or-preserved | preserved, matches | F3's converter keeps local functions with parameter modifiers as C#; the language side (native local function with `ref`) is covered by `LocalFunction_ByReferenceArguments_FollowTheContract` (operand refusal, `mut` charge, Unsupported proof) |
| X-INT-08 | native-or-preserved | preserved, matches | an `out var` in an interpolation hole preserves the member; never inline `§CS{}` |
| DOC-F1-01 | native | example compiles | `website/content/syntax-reference/calls.mdx` "By-reference arguments", plus a `Calor0212` negative example; `calor self-check docs` clean |

D1 registry (`contract.json`): 57 Calor cases; **56 hold**, 15 of them changed from `violates` by this
work. **D1-LV-03 still violates**: its sealed source declares `§PROP{p1:i32:N:pub}`, a property named
`i32` of type `N`, so `N` is undefined and only the C# compile fails. A correctly declared property
reports `Calor0212` (`InvalidOperand_…("property")`). Fixing the registered source needs a D1
amendment and a new seal. Of the two spec cases, D1-ANA-06 now has an executable witness
(`DivisionByZeroChecker_DoesNotUseThePreCallValueOfARefOperand`, which discriminates in both
directions); D1-ANA-07 stays covered by R-OBL's opacity rule for preserved C# (no new witness).

D1 C# witnesses (28, run on all 6 surfaces): every row is within its allowed outcome except
**D1-CS-16** (`native`): its two `TryGetValue(…, out …)` calls convert natively, but its unrelated
`new Dictionary<int,string> { [1] = "one" }` index initializer is kept as inline `§CS{}` (pre-existing
`dictionary-initializer` loss), so the row counts as preserved. D1-CS-04 is now preserved (its
operand `ref Get().n` runs code). D1-CS-17 and D1-CS-19 are refused on `calor migrate` and MCP
default by `Calor0272`/`Calor0273` (nullability), preserved elsewhere.

## Tests

- `tests/Calor.Compiler.Tests/InteropScope/RefOutImplementationTests.cs` (296 cases): operand
  refusals each with an accepted control, effect charges, alias and loop back-edge narrowing with
  controls, overloaded operators and disposal, proof soundness, the D1-ANA-06 witness, local
  functions, out-var bounds, `ref`→`in`, and the convert/compile/run/compare oracle for every R0 F1
  row, both interaction fixtures, every D1 C# witness and the Codex conversion reproducers on 5
  surfaces.
- `RefOutContractTests`/`RefOutContractCaseTests`: the registry with updated observations; the
  hypothetical-allocation controls now use the real `Calor0212`.
- Totals (`dotnet test`, 2026-10-17): see the PR body.

## Effect on the corpora (regenerated with the documented switches)

| Ledger | main | F1 | Why |
|---|---|---|---|
| `binder-incomplete-baseline.json` native source expressions (Roslyn-selected) | 35,177 | 34,361 | members preserved as C#; opaque boundaries 140 → 147, opaque expressions 7,009 → 7,742 |
| `binder-source-carrier-evidence.json` | 72 guarded | 68 guarded + 4 `PreservedCases` | the carriers' members (`MessageTemplateTextFormatter.Format`, `MessageFormatter.BuildMessage`) are now preserved; 4 AbstractValidator lambda names shift by one |
| `calor0270-corpus-ledger.json` | 313 / 37 | 295 / 33 | serilog 34/11 → 16/7: unresolved receivers inside newly preserved members |
| `calor0425-corpus-ledger.json` enforced modules | 265 | 275 | modules that stopped on an untyped `out var` binding (`Calor0250`) now bind; 143 Calor0425 sites over 52 modules |
| `EffectInferrerCorpusDepthTests` cycle stops | 2 | 0 | both cyclic Serilog members are preserved |

33 more members and types are preserved: 31 for an `out var` that cannot be lowered (type
unresolved in a one-file conversion, inside a lambda, or in a statement already partly C#; before,
most became an untyped `§B{~x}` that failed with `Calor0250`) and 2 for a method-generic `out T`
(FluentValidation `IValidationRule`, `RuleBase`).

## What READY still needs (not claimed here)

READY (`docs/plans/v0.25-interop-scope-and-baseline.md` §10) needs: a #1423 candidate commit;
per-case evidence for every F1 case (F1-REFOUT-01…08, A1, A2, X-INT-01/05/08, DOC-F1-01) naming that
candidate and each case's current `requirementSha256`; the tests on the candidate; the website page;
the review records; the maintainer's approval; reduced independence; and `blockersResolved` for
#1427, #1423, and the two mapped 0.24 repairs:

- **R-OBL (#1496, merge `ba2e31cb`)**: a `witnessRun` on the candidate that lists and passes
  `tests/Calor.Compiler.Tests/S2ObligationStateTests.cs`, plus the F1 analysis witnesses
  (D1-ANA-01…10, `ByReferenceWrite_NeverDischargesAStaleFact`, `LocalFunction_…`).
- **R-OBL-RESIDUALS (#1503, merge `9f5dfd76`)**: a `witnessRun` on the candidate that lists and
  passes `tests/Calor.Compiler.Tests/S2ObligationResidualTests.cs` (out-before-throw: F1-REFOUT-04,
  D1-CS-05).

No obligation cache exists at this commit, so RO-ANA-4's same-body hit control is still not
applicable; D1-ANA-05 keeps its two-program witness.

## Residuals for the maintainer

- D1-LV-03's registered source is wrong (above); an amendment is needed for it to hold.
- The opacity added by RO-DECL-1 over the corpora (+7 boundaries, +733 expressions) exceeds the
  20-boundary / 180-expression budget accepted for #1191 if that budget applies here.
- Loop back-edges keep a narrowing across a plain `§ASSIGN x null` later in the body (no
  by-reference argument): a pre-existing 0.22 flow gap, not changed here.
- Struct members are still unknown calls for effects (`Calor0410` on F1-REFOUT-08's default
  compile), and `new T[n]` of a struct types its elements as `OBJECT`.
- `Analysis/MigrationAnalyzer.cs` still describes out/ref parameters as unsupported.

## Reproduce

From the repository root, after `src/Calor.Compiler/scripts/download-z3.sh` and Release builds of
`src/Calor.Compiler` and `src/Calor.Runtime`, with HOME and CALOR_HOME set to a scratch directory:

    python3 docs/plans/evidence/v0.25-f1-943/reproduce/reproduce.py

It reuses the R0 driver and ProbeRunner, rewrites `fixtures/d1/` from `contract.json`, and writes
`results.json` and `generated/`.
