# 0.25 F1 (#943) evidence: ref/out/in arguments

**Status: IN_PROGRESS** (branch `milestone-0.25/f1-943-refout`). This is implementation evidence,
not a READY record (see "What READY still needs"). Reduced independence: the reviews are OpenAI
Codex records only; nothing here was independently reviewed or verified.

Specification: the merged D1 contract, `docs/plans/v0.25-refout-contract.md` and
`docs/plans/evidence/v0.25-d1-1427/contract.json` (1.0.3; amendment 1.0.4 later corrected D1-LV-03's
source), with its §15 answers: parity with
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
| P2-OUTVAR-PLACEMENTS | native | native, matches | PR 2: assignment, declaration, return and `if` placements, no initializer |
| P2-OUTVAR-CALL-STATEMENT | native | native, matches | PR 2: call statements with out declarations and an `§ARR` declaration after a bare binding (W1 finding 1, Codex round 2) |
| P2-OUTVAR-CHAINED | preserved | preserved, matches | PR 2: a chained assignment with an out declaration (Codex round 2) |
| P2-REFRETURN-EXPRESSION | preserved | preserved, matches | PR 2: expression-bodied ref returns and a ref property, like a block-bodied ref return (W1 finding 2) |
| DOC-F1-01 | native | example compiles | `website/content/syntax-reference/calls.mdx` "By-reference arguments", plus a `Calor0212` negative example; `calor self-check docs` clean |

D1 registry (`contract.json`): 57 Calor cases; **57 hold**. This work changed 15 of them from
`violates`. The 16th, **D1-LV-03**, held only after D1 amendment 1.0.4 (2026-10-18): its sealed
source had declared `§PROP{p1:i32:N:pub}`, a property named `i32` of type `N`, so `N` was undefined
and only the C# compile failed. The amended source declares `§PROP{p1:N:i32:pub}` and reports
`Calor0212`, as `InvalidOperand_…("property")` in `RefOutImplementationTests` does. No compiler change
was needed. Of the two spec cases, D1-ANA-06 now has an executable witness
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
| `tests/Calor.Evaluation/Scorecard/baseline.json` | 086 partial, 4 round-trip errors | 086 partial, 1 error, 1 interop | `Swap<T>(ref T a, ref T b)` (method-generic `ref`, RO-UNS-1) is preserved; only entry 086 was updated, totals unchanged (95 fully converted, 98 round-trip) |

33 more members and types are preserved: 31 for an `out var` that cannot be lowered (type
unresolved in a one-file conversion, inside a lambda, or in a statement already partly C#; before,
most became an untyped `§B{~x}` that failed with `Calor0250`) and 2 for a method-generic `out T`
(FluentValidation `IValidationRule`, `RuleBase`).

## Maintainer decisions after merge (2026-10-18, @juanmicrosoft)

F1 merged as PR #1558 (`f83b4e593`). The maintainer then decided the two open questions below. They
were relayed through the agent session; no GitHub comment records them.

1. **Corpus opacity: accepted as F1's cost.** F1 adds 7 opaque boundaries (140 → 147) and 733
   opaque expressions (7,009 → 7,742) over the vendored corpora. They come from RO-DECL-1, which
   keeps a member as C# when its `out var` cannot be lowered (31 members), and from 2 method-generic
   `out T` parameters (RO-UNS-1). This is more than the 20-boundary / 180-expression budget, but that
   budget was written for #1191 and does not apply to F1.
2. **Scorecard entry 086: accepted.** F1 keeps `Swap<T>(ref T a, ref T b)` as C#, as D1 RO-UNS-1
   requires for a method-generic by-reference parameter. The PR's `conversion-scorecard` check failed
   only because it compares against the base branch's baseline, which did not have the new 086 entry
   yet. On main, `conversion-scorecard` passes for `f83b4e593` (Tests run 39137814012).

## PR 2: the W1 findings (maintainer decision 2026-10-18)

W1 (#1429, PR #1566) found four defects while checking its pages against the converter
(`docs/plans/evidence/v0.25-w1-1429/README.md` on that branch, "Found while checking"). The
maintainer decided on 2026-10-18 that F1's second and last PR (#1571) fixes them; the decision was
relayed through the agent session, and no GitHub comment records it.

| # | Defect | Fix (D1 rule) | Witnesses |
|---|---|---|---|
| 1 | `int.TryParse(s, out var n);` lowered to a bare `§B{~n:i32}`, and the parser read the next statement's `§C` as the binding's initializer (CS0029 in the C# round trip, so the CLI rescued the type and MCP and migrate refused). Codex round 2 showed the same misread for a following `§ARR` declaration (`int[] a = new[] { Set(out var n) };` compiled to `int[] n = new int[] { Set(out n) };`, returning `1`, not `7`), and a chained assignment (`x = y = Set(out var n);`) ran the out call twice | **Parser** (`ParseBindStatement`): a binding's initializer must start on the binding's line; an expression start on a later line is a sibling statement, the rule `§C` inline arguments already follow (RFC v0.6 §3.2). The converter's RO-DECL-1 lowering is unchanged and now reads correctly in every placement it allows (expression statement, local declaration, `return`, `if` condition). **Converter**: a statement with an out declaration and a nested assignment (`x = y = …`, `int z = y = …`) preserves its member (`out-var`), because the converter evaluates a chained right side once per target (RO-ORD-1; the duplication itself is pre-existing, see residuals) | `P2-OUTVAR-CALL-STATEMENT` (two call statements, then an `§ARR` declaration after a bare binding): native, matches, all 6 surfaces; `P2-OUTVAR-PLACEMENTS` (assignment, declaration, return, condition, and a member named `@default` that must not be read): native, all 6; `P2-OUTVAR-CHAINED`: preserved, all 6; `P2-out-var-*` rows in `RefOutImplementationTests` (10 rows x 5 surfaces); `BareBinding_DoesNotTakeASiblingAsItsInitializer`; `OutDeclaration_IsLoweredWithoutAnInitializer` |
| 2 | `=> ref a[0]` had its `ref` stripped (`-> ref int` with a value body) and failed the round trip | RO-UNS-1: a `RefExpressionSyntax` reaching expression conversion now escalates, so the member is preserved like a block-bodied ref return (loss `method`/`property`, warning `ref-parameter`). Covers ref-returning expression-bodied methods and properties | `P2-REFRETURN-EXPRESSION`: preserved, all 6 surfaces; `ExpressionBodiedRefReturn_IsPreserved_NotStripped`; `P2-ref-return-*` rows |
| 3 | FeatureSupport text did not match behavior | Text only, no level changed (`ref-expression`, `multidim-array` and `in-parameter` stay Full; `out-var` and `out-parameter` stay Partial). Each now says what converts and what stays C#: `out-parameter` says `[NotNullWhen]` is copied but inert; `out-var` adds chained assignments to the preserved forms; `in-parameter` names the syntactic operand shapes that keep the statement as `§RAW` (a property-getter operand such as `a[I]` is not one of them) | `FeatureSupportText_MatchesTheConverter`; `scripts/check-d-s1.5-fixtures.sh origin/main` passes (no promotion) |
| 4 | `ref a[Next()]` (an operand that runs code) was reported under `in-parameter` | The statement-level loss names the parameter's own feature: `ref-parameter` (also for `ref readonly`), `out-parameter` or `in-parameter`; an omitted `in` stays `in-parameter`. A method-generic by-reference argument is reported under its keyword (`ref`/`in`/`out`) instead of always `out-parameter` | `ByReferenceStatementLoss_IsReportedUnderTheParametersFeature` (7 rows; the 2 `in-parameter` rows are controls); D1-CS-04 and D1-CS-19 now report `ref-parameter` |

Regression sensitivity (checked by reverting `src/Calor.Compiler/Migration` and `Parser.cs` to
`origin/main`: 41 failures in `RefOutImplementationTests`): the five call-statement, array-declaration
and chained-assignment rows fail on all 5 surfaces (on `main`, each either fails to compile or runs
differently from the C#), the two ref-return rows on 2 of 5 (the others already reached `preserved`
by rescue or passthrough), plus `BareBinding_DoesNotTakeASiblingAsItsInitializer` (the next-line
case), `ExpressionBodiedRefReturn_IsPreserved_NotStripped`, 5 of 7 attribution rows and all 5
FeatureSupport-text rows. The native placement rows and the two `in-parameter` attribution rows are
controls that pass on both.

The parser rule is a language change for hand-written Calor: a binding followed by an expression
on the next line at any indentation is now two statements. In the repository, the only committed
`.calr` that relied on the old reading was the #1104 cycle fixture
(`tests/Calor.Enforcement.Tests/Scenarios/Effects/Issue1104_BatchingSink_LoopAsync.calr`), which
reproduced exactly this converter output; it now writes its self-referencing call on the binding's
line, so its cycle test still exercises the cycle. `calor self-check docs` finds no drift. The
CHANGELOG states the rule.

Draft history (Codex, `reviews/pr2-round-*.md`): the first draft gave every lowered binding a
`default` initializer (round 1: it reads a member named `default`); the second preserved call
statements (round 2: an `§ARR` declaration was still misread, and chained assignments ran twice).

Re-run evidence (`reproduce.py`, Release, 2026-10-18): every existing row keeps its outcome and its
generated `.calr`; the only change is the attribution in item 4 (D1-CS-04, D1-CS-19: `in-parameter`
-> `ref-parameter` on all 6 surfaces), plus the four new rows. D1-CS-16 remains preserved for its
unrelated `dictionary-initializer`, as before.

Corpus ledgers (`CALOR_UPDATE_BINDER_BASELINE=1`, against `main`'s baseline):
`RoslynSelectedAttempted` 34,361 -> 34,358, opaque boundaries 147 -> 148, opaque expressions
7,742 -> 7,746. Per file (`binder-source-coverage.json`):

- FluentValidation `Validators/ChildValidatorAdaptor.cs` (binder attempts 101 -> 99, binding errors
  4 -> 2): its two `HandleCollectionIndex(context, out object originalIndex, out object
  currentIndex);` statements no longer become `currentIndex = HandleCollectionIndex(...)` bindings
  (the old misread, on the library surface that the ratchet measures).
- MediatR `Unit.cs` (+1 boundary, +4 opaque expressions, conversion success false -> true):
  `public static ref readonly Unit Value => ref _value;` is now preserved instead of losing its `ref`.

No other ledger changed (`calor0270`/`calor0425` ledgers, effect-resolver-key and higher-order-demand
ledgers over committed `.calr`, scorecard baseline, effect-inferrer depth). The T1 shared controls
(`SharedInteropControlsTests`, 19 controls x 7 surfaces) reproduce their committed results
unchanged; the T1 register records the new `results.json` hash as amendment 1.0.1.

Residuals found in PR 2 (pre-existing, not changed here):

- A chained assignment `x = y = G()` evaluates `G()` once per target in any converted C#, with or
  without `out` (`§ASSIGN y §C{G}` then `§B{i32:z} §C{G}`). PR 2 only keeps it away from out
  declarations.
- The converter writes `int x;` as `§B{~x:i32} \`default\``, and the C# emitter turns that
  `default` into `@default` when a member of that name exists (Codex round 1's reproducer, through a
  plain declaration).
- The C# emitter writes `= default` for any initializer-less mutable binding, so a lowered
  non-nullable `out string s` emits `string s = default;` (CS8600 warning, unchanged since PR 1).
- An `in`/`ref` operand that runs code only through a property getter (`a[I]`) converts natively;
  its evaluation order is unchanged because no argument is hoisted, but `in-parameter` does not
  call it "code that runs".

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

- D1-LV-03's registered source was wrong (above); resolved by D1 amendment 1.0.4.
- The opacity added by RO-DECL-1 over the corpora (+7 boundaries, +733 expressions) exceeds the
  20-boundary / 180-expression budget accepted for #1191. Resolved: the maintainer accepted it as
  F1's cost on 2026-10-18 (above).
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
