# Effect Rows in the Type System (TIER2D)

**Status:** Draft v4 + emitter-spike results (roadmap §4.1 term 1 executed — §12.4, §13.5, §15 round 4)
**Date:** 2026-08-25 (v1–v3 same day; review rounds 1, 2 and 3 applied — §15)
**Measured against:** `main` @ `82338e37` (v0.14.3 + PR #1089 E1 slice 1 + PR #1090)
**Governing inputs:** `docs/design/calor-direction.md` (`:23` TIER2D, `:33` generics deferral,
`:57` the three worked examples, `:90-120` the postscript); `docs/plans/roadmap-v0.13-v0.15.md`
§4.0–§4.5 (Draft v4); `docs/plans/v0.14-metadata-binding-scoping.md` §2 D2 (`:90-101`), D5
(`:166-171`), D6 (`:173-181`), §3 S6 (`:301-317`);
`bench/phase0-agent-native/higher-order-demand-ledger.json`;
`bench/phase0-agent-native/metadata-binding-corpus-ledger.json`.

**Evidence discipline (v2), pinned (v3).** Every claim about how the compiler behaves today is
backed by an **executed** experiment against a compiler built from this tree, not by reading
source. Cases are labelled `X*` / `Y*` / `Z*` and their **verbatim** output is quoted;
`docs/design/spikes/effect-rows/experiments/` holds the scripts, their canonical transcripts, and
`regenerate-transcripts.py`.

v2 left those outputs *reproducible but unobserved*, which is the failure mode this document
criticises Draft v1 for. v3 closes it:
`tests/Calor.Compiler.Tests/Effects/EffectRowExperimentHarnessTests.cs` re-runs all six scripts
and diffs against the committed transcripts (**P29**), and shape-checks the corpus baseline
(**P30**). It never skips — a missing compiler build is a hard failure, because a skipped
evidence pin is how Draft v1's fabricated quotations survived. If the compiler changes, the test
goes red naming the script, and the doc and the transcripts are updated in the same PR.

Structural claims (line numbers, file counts) carry `file:line` and the command that produced
them. Where a governing input disagrees with the source, the source wins and the disagreement is
recorded in §14.1.

---

## 0. Entry-gate checklist (roadmap §4.1)

| # | Term | Status |
|---|---|---|
| 1 | **Emitter spike producing actual compiler output** on named, frozen artifacts | **MET by the spike PR** (the follow-up §12 required). Artifacts, emitted C# and diagnostic lists are committed under `docs/design/spikes/effect-rows/{before,after}/`; the verdict is `spike-verdict.json`; P27/P28/P31 pin it. **G-CODEGEN PASS; ramp VALIDATED (R1 ∧ R2 ∧ R3); A3's middleware spelling decided MEMBER-LEVEL.** §12.4 carries the executed table and the three caveats. The prototype that produced the AFTER artifacts is **throwaway and unmerged** (branch `spike/effect-rows-emitter`); the spike PR carries no `src/` change. E2 still does not merge until this PR does. |
| 2 | **External critique cycle with a pass bar** | **Round 1 complete** — evidence 92%, consistency 88%, test-lens 88%, all NEEDS-FIXES. Every finding is dispositioned in §15. Round 2 pending; the bar is APPROVE from the evidence and consistency lenses. |
| 3 | **Priced blast radius in the doc** | **§9**, one table, each row with the command that produced its number. |
| — | **Demand denominator registered before the doc opens** | **DONE** (PR #1086). D-A **3**, D-B **3121**, total **3124**, floor **25**. §1. |
| — | **E1 permitted to start before this doc merges** | Slice 1 executed (PR #1089); slice 2 pending. §8.1 records both. |
| — | **Diagnostic allocation frozen at design-doc merge** | **Four** codes: **Calor0404 `EffectVariableScope`**, **Calor0405 `EffectRowMisplaced`**, **Calor0424 `EffectRowMismatch`**, **Calor0425 `EffectRowUnknown`** (§6.1). All four verified free: `grep -rn "Calor042[4-9]\|Calor040[4-9]" src/ tests/` → no hits; `Diagnostic.cs:378` is `Calor0403`, `:381` `Calor0410`, `:437` `Calor0423`, `:440` `Calor0500`. |

---

## 1. The demand denominator

The postscript reframed TIER2D as "architectural elegance … not a new user-facing capability"
(`calor-direction.md:112`). Testable only against a denominator a Calor-shaped corpus cannot
supply, because Calor0418 rejects the idiom — the circularity that killed TIER1A. Hence two
denominators, frozen before this doc opened.

**D-A — Calor-native: 3.** From the ledger's `dA`: `"calor0418": 1, "calor0419FunctionTyped": 2,
"total": 3` over `"fileCount": 886` (`"filesNotReachingEffectPass": 45`, listed by name). The
three sites are `bench/phase0-agent-native/fixtures/d-s1.5/conditional-declaration/expected.calr`
(0418×1) and `tests/Calor.Conversion.Tests/Snapshots/05-02.approved.calr` /
`05-03.approved.calr` (0419×1 each).

D-A is near zero because the language rejects the idiom. Corroboration, measured: the whole
886-file corpus holds **9 `§LAM` occurrences in 7 files, 3 `§DEL` in 2 files, and 5
`Func<`/`Action<`/`Predicate<` type positions in 5 files** — and all 5 of those files are C#→Calor
conversion snapshots (`07-01`, `07-02`, `07-03`, `07-04`, `13-03`). No hand-written Calor in this
repository contains a function-typed position. The ledger's own scope note records that the
Calor0419 count is a floor, not a ceiling (three reasons shown per diagnostic, remainder
truncated).

**D-B — C#-shaped backstop: 3121.** `dB.aggregate`: `"filesScanned": 364, "lambdas": 2676,
"anonymousMethods": 0, "delegateDeclarations": 2, "delegateTypedDeclarations": 311,
"delegateInvocations": 132, "total": 3121`. Per subject: MediatR 92 (36 files), Serilog 171 (112),
FluentValidation 2858 (216, of which 2524 lambdas).

**Floor and gate 2.** `demandTotal` 3124 vs `"floor": 25`, so gate 2 is adjudicable. At the
release commit the ledger is re-executed and the bar is Calor0418 firings on the registered
classes going to zero without `--permissive-effects` and without interop wrapping. §13.3 states
how the frozen ledger reconciles with a zero count.

**Honest reading.** D-B is the number that carries the argument and it counts *C# syntax in C#
files*. It shows the shape is pervasive in what Calor migrates from. It does not show that agents
writing Calor would reach for it; only D-A after the ceiling is removed can, which is why gate 2
re-executes the ledger rather than retiring it.

---

## 2. Today's effect system, precisely

All rows verified at `82338e37`. `EEP` = `src/Calor.Compiler/Effects/EffectEnforcementPass.cs`
(2993 lines).

| Fact | Where | Detail |
|---|---|---|
| Pass is an **AST** walk, SCC-based, interprocedural | `EEP:10-15`, `:88`, `:93-120` | not a bound-tree walk |
| Binder enters as a **side channel** | `Analysis/CallGraphAnalysis.cs:424-425`, `:485-586` | constructs a *second* `Binder` inside `try` (`:492-495`), scrapes `ResolvedSymbols`/`ReceiverSymbol` (`:521-549`), **discards its diagnostics** (`:494`), maps back to legacy ids by `DefinitionSpan` (`:588-600`); on throw returns `Complete=false` (`:578-583`) |
| Lookup key is a string tuple | `:436`, `:554` | `AstCallKey(callerId, targetString, spanStart, spanEnd)` — **the effect pass has no bound tree, only a table** |
| Resolver is string-keyed end to end | `Effects/EffectResolver.cs:48`, cache key `:59`, order `:5-14`, miss `:100` | `Resolve(string type, string method, params string[] parms)` |
| Resolution status is 3-valued, no `Assumed` | `EffectResolver.cs:596-612` (`Unknown` at `:611`) | `Resolved \| PureExplicit \| Unknown` |
| Receiver types recovered by **lexical AST search** | `EEP:1719-1744` (`ResolveLocalValueType`; field arm `:1738-1741`) | 11 call sites: `:1269, :1342, :1454, :1532, :1560, :2022, :2092, :2157, :2225, :2339, :2384` |
| Type-name expansion | `EEP:830` (`MapShortTypeNameToFullName`) | 13 call sites: `:820, :1996, :2025, :2065, :2069, :2118, :2189, :2291, :2325, :2372, :2406, :2829, :2899` |
| Function-typedness is a **string test** | `EEP:1939-1955` | `Action`/`Action<`/`Func<`/`Predicate<`/`Comparison<`/`Converter<`/`Delegate`/`MulticastDelegate`/`EventHandler`/`EventHandler<` + module `§DEL` names. A `Func` behind an alias, a type parameter, or a metadata return is invisible |
| `EffectSet` carrier | `Effects/EffectSet.cs:9` | `HashSet<(EffectKind, string)>`; `Empty:14`; `Union:83-91` (absorbing on Unknown `:86`) |
| `Unknown` is a **sentinel member** | `:20`, `:200-203`, detected `:73` | `(EffectKind.Unknown, "*")` — an element of the same type, not a separate node |
| The fit test and its two special rules | `:97-119` | `:100` "everything is a subset of unknown" → `true`; `:101` "Unknown is not a subset of anything else" → `false` |
| Fail-closed guarantee (PR #968) | `EEP:1434`, `:2928`, `:2939` | unresolved callee → `EffectSet.Unknown` → fits no declared set → Calor0410 |
| The waiver short-circuits earlier | `EEP:1427-1430`, `:2935-2936` | `Permissive` returns `EffectSet.Empty` before the Unknown is produced |
| Subtyping is **not** colon-derived | `Effects/EffectSubtyping.cs:14-43`, `:52-66` | a hand-written table of exactly four entries, all `*_readwrite ⊃ {*_read, *_write}` for `filesystem`/`network`/`database`/`environment` |
| …so a family code does **not** admit its narrow codes | `Effects/EffectTypes.cs:65-109` | `db` = `("io","database")`, `db:r` = `("io","database_read")`; `Subtypes` has no `database` entry. There is **no bare `fs` code** (`:71-73` has only `fs:w`/`fs:r`/`fs:rw`) |
| Registry size | `EffectTypes.cs:65-109` | 31 entries, 5 `Legacy` (`fw`/`fr`/`fd` `:75-77`, `dbr`/`dbw` `:90-91`), 26 documented (`:134-135`); `ColonPrefixes` `:142-148` |
| `Assumed` is a **side table**, not a state | `EEP:34` (`_assumedEffects`), surfaced `:448-463` | severity `:450-452`; three reasons then `"; and N more"` `:453-455`; second site `:602-611` |
| Missing `§E` = pure | `EEP:473-502` (doc `:475-478`) | `GetDeclaredEffects(null)` → `EffectSet.Empty` |
| `await` is transparent | `EEP:2538` | `AwaitExpressionNode a => InferFromExpression(a.Awaited)` |
| No async effect kind | `EffectTypes.cs:6-14` | `Unknown \| IO \| Mutation \| Memory \| Exception \| Nondeterminism`; no `async`/`task`/`await` in the registry |
| Lambda declaration ignored | `EEP:2942-2954` | `InferFromLambda` charges the body; `lambda.Effects` is never read anywhere under `Effects/` |
| **Calor0410** subset test / message / report | `EEP:377` / `:427` / `:433`, `:441` | see §2.1 — the message is **per effect**, and Draft v1 quoted it wrongly |
| **Calor0418** site A (parameter/`§B`/field) | `EEP:1452-1472`, code `:1465`, msg `:1466-1469`, returns `Empty` `:1471`, demote `:1457-1459` | doc comment `:1437-1450` states the escape hatch does not exist (`:1445-1446`) |
| **Calor0418** site B (returned delegate) | `EEP:2679-2697`, code `:2690`, msg `:2691-2694` | lambda-IIFE arm `:2674-2677` charges the body instead |
| Free name ≠ 0418 | `EEP:1483-1500` | routes to Calor0411 |
| **Calor0419** function-typed flavour | `EEP:1259-1288`, text `:1282-1284` | the string the ledger's `dA.calor0419FunctionTyped` matches; method-group arm `:1272-1278` |
| **Calor0420 / Calor0421** | `EEP:515-616`; tests `:533`/`:571`; reports `:537-545`/`:584-593`; spans `:538`/`:581-583` | already declaration-local typing rules; external base → Assumed (`:548-553`, `:596-611`) |
| …and both are **demoted by `--permissive-effects`** | `EEP:517-519` | executed: **Y8a** vs **Y8b** below |
| A **fourth** `IsSubsetOf` compatibility site | `Effects/CrossModuleEffectEnforcementPass.cs:162` | `resolution.DeclaredEffects.IsSubsetOf(declaredEffects)` — absent from Draft v1 |

### 2.1 The real Calor0410 message (executed)

Draft v1 quoted a message that does not exist. Case **X12b**
(`§F{f001:Log:pub} (str:m) -> void` / `§E{}` / `§P m` / a `File.WriteAllText` call):

```
X12b.calr(3,5): error Calor0410: Function 'Log' uses effect 'cw' but does not declare it
X12b.calr(3,5): error Calor0410: Function 'Log' uses effect 'fs:w' but does not declare it
  Call chain: Log → System.IO.File.WriteAllText
```

One diagnostic **per forbidden effect** (`foreach` at `EEP:421`, message at `:427`), optionally
followed by a call-chain line. §3.6, §5 and §10 quote this shape; where they show a *new*
sentence, §3.3 says so and §13.2 pins it.

### 2.2 What E1 slice 1 changed (PR #1089), and what slice 2 owes

Changed: `_variableTypeMap` is gone from `Effects/` (`grep -rn "_variableTypeMap" src/` → 8 hits,
all `Migration/RoslynSyntaxVisitor.cs`), with a grep pin at
`tests/Calor.Enforcement.Tests/EffectsSuggestTests.cs:148-157`. Receivers come from the bound tree
(`Effects/ExternalCallCollector.cs:287-328`, indexed at `:103`, consulted at `:254-283`), with
three honesty guards (`:62-67`) and a `ReceiverResolved` bit (`:34-38`, documented `:23-32`); a
binder throw makes every dotted receiver unresolved (`:81-82`, `:107`).

Owed by slice 2, each with the evidence it is unmet: a receiver `BoundExpression` on the call
nodes and binder-emitted `UnresolvedBoundType` (both named as slice 2 in the file's own header,
`ExternalCallCollector.cs:66-68`; the type exists at `Binding/BoundTypes/BoundType.cs:248-250`
but the binder does not emit it); the enforcement pass's string resolvers (`EEP:1719-1744`,
`:1939-1955` untouched); symbol-identity keying (`EffectResolver.cs:48` intact, so roadmap
§4.2's E1 exit pin (c) is unmet); the lambda `FunctionBoundType`
(`Binding/BoundNodes.cs:2178` is still `NominalBoundType("LAMBDA(…)")`).

> **Superseded, and left as written on purpose.** Every row above, and §2's table, is a
> snapshot at `82338e37` — the state E1 was scoped against, not a claim about the tree today.
> All four items are now executed: slice 2a (#1095), slice 2b (#1099), and slice 2c, which
> deleted `Resolve(string, string, params string[])` and every sibling string overload. §8.1
> carries the current state and the pins that hold it.

`FunctionBoundType` exists with the slot reserved:
`Binding/BoundTypes/BoundType.cs:209-211` — *"Kind 5: function type (for lambdas / delegates).
Effect rows attach here in 0.15 (§4.2)"* — carrying `ParameterTypes`/`ReturnType` (`:214-226`)
and a structural `Equals` (`:228-231`).

**Resolution ceiling.** `metadata-binding-corpus-ledger.json`: 817 of 1248 (65.46%); MediatR
129/226, Serilog 104/113, FluentValidation 584/909. **431 BCL call sites do not resolve**, and
each function-typed one becomes an Unknown row. §13.4 registers the ledger that measures the
consequence.

### 2.3 Persistence

`EffectSummary` is a JSON POCO (`Effects/EffectSummary.cs:14-16`) held in the incremental build
cache (`Incremental/BuildStateCache.cs:52`; format `"3.0"` `:121`, semantics stamp `:122`,
options stamp `:123`, global invalidation `:676-678`), keyed by **names**
(`EffectSummaryBuilder.cs:68` `function.Name`, `:75` `"{cls.Name}.{method.Name}"`;
`EffectSummary.cs:59-60` carries `Name` + `ClassName?`, never a `SymbolId`). The project index
(`Indexing/ProjectIndex.cs:145`, format `"3.0"`) holds **no** effect facts, and `calor query`
exposes six facets (`Commands/QueryCommand.cs:26-34`). `ProjectIndex` is referenced from exactly
three source files — `Commands/IndexCommand.cs`, `Commands/QueryCommand.cs`,
`Indexing/ProjectIndexBuilder.cs` — and **nothing under `Effects/` or `Incremental/`**, so
`calor build` has no index dependency today. §8.5 keeps it that way.

---

## 3. Decision 1 — Row syntax

> **Decision.** A row is written with the **existing `§E{…}` tag**, and **line adjacency** decides
> what it annotates:
>
> - a `§E{…}` whose first token sits on the **same source line** as the last token of the type
>   immediately preceding it is **that type's row**;
> - **otherwise the `§E` is not consumed at that position** and the enclosing production resumes
>   as it does today. In `§F` / `§MT` / `§DEL` section loops that means the token reaches their
>   existing `§E` arm and is the **declaration's own row**, unchanged. At the four positions with
>   no such arm — inline parameter, binding, field, and a wrapped inline signature — it is a parse
>   error today, so E2 replaces the cascade with a row-aware recovery diagnostic,
>   **Calor0405 `EffectRowMisplaced`**.
>
> **Eight positions** (§3.3). No new token, no new AST node type, no new `IAstVisitor` method.

> **STATUS — LANDED IN FULL, PR #1106, v0.15 E3 slice b.** Site 6 is implemented:
> at a call to a declaration carrying `eff` binders each variable is solved ONCE
> from the argument rows (§7.4's one-line solve, no fixpoint — **R3**), the
> instantiated own-row is charged to the CALLER — and, since review round 1
> finding 1, to the caller's own callers, to a fixpoint: the solve runs after the
> SCC pass, so without that propagation a three-level chain could launder an
> effect (under `--permissive-effects` the laundering program compiled clean).
> A variable the arguments cannot determine yields Calor0425 at the call site. Rows carry variable
> **ordinals** (`EffectsNode.EffectVariableOrdinals`), so an interface member's
> `eff e` and its implementation's `eff f` unify under the ORDINARY `fits`
> relation with no rank-1-specific branch — **R2**, and `A3-middleware-alpha`
> passes for that reason rather than, as in slice a, because the two were never
> compared. **R1** holds: all four combinators compile with zero Calor0424, zero
> Calor0425 and zero Calor0404, the only residue being the Calor0418 at each
> invocation of a row-less value, which is E4's. The §7.5 ramp does **not** fire.
>
> **One divergence from §6.2, reported not absorbed.** §6.2's row 6 writes
> Calor0424 for site 6's `DoesNotFit`. Under §7.4's own solve that cell is
> **unreachable**: the solution is defined as the join of the residuals
> `ρ(argⱼ) ⊖ ρ_declⱼ`, so the substituted parameter row contains every argument's
> row by construction and no argument at a variable-mentioning position can fail
> `fits`. What site 6 catches is the CALLER under-declaring the instantiated
> row — which is exactly what §10.3's own worked example spells, as **Calor0410
> plus the new provenance clause**. Gate 1's sixth class is closed; the code in
> that cell is 0410, not 0424. Adjudication of §6.2's table belongs to this
> document's owner.
>
> **STATUS — LANDED (syntax only), PR #1101, v0.15 E2 slice a.** All eight positions parse,
> `CalorEmitter` round-trips every one, and **Calor0405** replaces the cascade at the positions
> with no `§E` arm. What landed is the *writing* of a row; **nothing compares two rows yet**.
> Still pending, and owned by slice b / E3: §3.5's Calor0425-on-omitted-row and the
> **function-typedness check** (a row on an `i32` — pin **P6**), §3.4's Calor0410 provenance
> clause, and every §6 site.
>
> **One correction to this section, measured.** §3.3 and §9 say adding a `Row` child to the four
> node classes forces an `eng/ast-schema.json` edit, because of
> `ArchitectureTests.cs:158`. It does not. That test compares
> `AstSchemaMetadata.Nodes[].ChildProperties` against `RecursiveAstWalker.GetAllChildProperties`,
> and **both sides are computed by reflection** (`Ast/AstNode.cs:633`); the committed JSON carries
> only `{name, source}` per node and has no `childProperties` key at all. A schema edit is forced
> by a new node *type*, not a new child property. E2 slice a adds no node type and edited no
> schema. (`facts.py` already recorded the JSON's shape; nothing re-read it against the claim.)

### 3.1 The collision, executed

Draft v1 assumed a suffix `§E` could be told apart from a declaration `§E` positionally. It
cannot: `Parser.cs:1358-1403` is a **flat token loop** with independent arms for `§I`, `§O` and
`§E`, and no line or indent awareness. Executed proof that *every* `§E` in that loop is the
declaration's row today, and that the **last one wins**:

```
CASE Y1a  exit 1        CASE Y1b  exit 0        CASE Y1c  exit 0
  §F{f001:Log:pub}        §F{f001:Log:pub}        §F{f001:Log:pub}
    §I{str:m} §E{cw}        §I{str:m} §E{cw}        §I{str:m} §E{}
    §O{void}                §O{void}                §O{void}
    §E{}                    §P m                    §E{cw}
    §P m                                            §P m
→ Calor0410:            → Compilation           → Compilation
  Function 'Log' uses     successful.             successful.
  effect 'cw' but does
  not declare it
```

Y1a: the same-line `§E{cw}` is overwritten by the later `§E{}`, so the declaration is pure and
`§P m` is forbidden. Y1c: the same-line `§E{}` is overwritten by `§E{cw}`, so it compiles. The
same holds at `§O` (**X1b** two-line → Calor0410 at `(5,5)`; **X2b** same-line → Calor0410 at
`(4,14)`) and at the arrow (**Y5a**: `§F{f001:Log:pub} (str:m) -> void §E{cw}` compiles today,
with `cw` as the declaration's row).

So the ambiguity is real at three positions, not one, and the docs make the two-line form
canonical (`docs/syntax-reference/effects.md:44-51` — *"Place the effect declaration after the
output type"*; repeated `structure-tags.md:170-176`).

**And the "later line ⇒ declaration row" reading only holds where a `§E` arm exists.** Draft v2
stated it unconditionally; executed, it is false at four positions. `§FLD` sits in the
class-member loop, `§B` in the statement loop, and a wrapped inline signature inside `( )` — and
none of the three has a `§E` arm, so today a non-adjacent `§E` there is a four-diagnostic cascade
that never mentions effects:

```
CASE Z1  §CL{…} / §FLD{i32:x:pri} ⏎ §E{cw}
  (4,5)  Calor0100: Expected TP, WHERE, EXT, IMPL, FLD, PROP, IXER, CTOR, OP, METHOD,
                    AMT, EVT, CSHARP, PP, CLASS, IFACE, EN, DEL, or END_CLASS but found Effects
  (4,7)  … but found OpenBrace      (4,8) … but found Identifier      (4,10) … but found CloseBrace

CASE Z2  §B{y:i32} INT:1 ⏎ §E{cw}
  (5,5)  Calor0100: Expected statement but found Effects
  (5,7)  … OpenBrace      (5,8) … Identifier      (5,10) … CloseBrace

CASE Z3  §F{…} ( ⏎ Func<i32,i32>:transform ⏎ §E{cw} ⏎ ) -> i32
  (4,7)  Calor0100: Expected CloseParen but found Effects
  … then 7 more cascade lines through the rest of the signature and the body
```

Z3 is not hypothetical: **Z5** confirms inline parameter lists really do wrap across lines and
compile, so an author writing a wrapped signature is one newline away from the cascade. E2 must
therefore emit a **row-aware recovery** at these four positions rather than a token cascade:

```
Calor0405: A §E{…} effect row must be on the same line as the type it annotates.
Move it onto the end of the 'transform' parameter line, or — if this is meant to be
the function's own effect declaration — onto its own line in the §F body.
```

Recovery consumes the `§E{…}` group so the rest of the declaration still parses, turning four (or
eleven) diagnostics into one. §13.2 P2 pins all four positions against their Z-case baselines.

### 3.2 Why line adjacency, and what it costs

`TextSpan` exposes `Line` (`Parsing/TextSpan.cs:12`), so the rule is one comparison —
`Current.Span.Line == Peek(-1).Span.Line` — evaluated **immediately after the type is consumed**,
where `Peek(-1)` is by construction the type's last token.

Measured corpus impact of the rule (`git ls-files '*.calr'`, regex over all 886 files):

| Form | Occurrences | Files | Under the rule |
|---|---|---|---|
| `§O{…}` ⏎ `§E{…}` (two-line, canonical) | **54** | **23** | unchanged — declaration row |
| `) -> T` ⏎ `§E{…}` (two-line, compact) | **2948** | **471** | unchanged — declaration row |
| `§O{…} §E{…}` same line | **0** | 0 | *would* become the return type's row |
| `§I{…} §E{…}` same line | **0** | 0 | *would* become the parameter's row |
| `) -> T §E{…}` same line | **0** | 0 | *would* become the return type's row |
| `§FLD{…} §E{…}` same line | **0** | 0 | new syntax — does not parse today (**X9b**) |

**Zero corpus occurrences of any form whose meaning changes.** This is the "zero regressions"
claim, executed rather than asserted. *(E5, 2026-08-27: one committed file now writes a same-line
parameter row — `tests/TestData/QueryCorpus/project/app.calr`, AUTHORED under Decision 1 for gate
7's polymorphic golden. It is not a regression of the claim, which is about files written before
the rule; `EffectRowCorpusShapeTests` carries it on an explicit, reasoned allowlist rather than
widening the sweep.)* It is still a **breaking change to a form that parses
today** (Y1b, X2b, Y5a all compile now and would mean something else), and the release notes must
say so; it is not a change to any form anybody has written.

The 23 files were each compiled as the baseline the E2 PR re-runs (`experiments/compile53.py`,
results in `o53/baseline.json`, shape-pinned by **P30**). Today **22 of the 23 are already
compile-red for reasons unrelated to effects** — **18** `bench/mcp/tasks/*` on Calor0830 legacy
closers, **3** `benchmarks/security/*` on Calor0006/0100/0102 (the #901 stale subjects), **1**
lint error fixture on Calor0002 — which is the same set the demand ledger lists in
`notReachingEffectPass`. (v2 wrote "15 + 3 + 1 = 22"; the ledger says 18. P30 asserts the
breakdown so the arithmetic cannot drift again.) **Exactly one is green**:
`tests/E2E/agent-tasks/fixtures/collections-project/Collections.calr`. So the live risk surface
for the two-line `§O`/`§E` form is one file and one occurrence, and the dominant real corpus is
the 2948/471 arrow form, which the rule provably cannot reach.

### 3.3 The eight positions and the six insertion points

v2 said "seven positions" over a table with eight labelled rows (…6, 6b, 7). There are **eight**;
position 6 simply has two spellings. Renumbered here and used consistently throughout.

| # | Position | Spelling | Parses today? |
|---|---|---|---|
| 1 | Function / method declaration (its own row) | `§E{…}` on its own line | **yes** — `Parser.cs:1377-1380` (`§F`), `:8836-8840` (`§MT`), and the async variants (**Y6a** executed on `§MT`) |
| 2 | Lambda literal | `§LAM{id:x:i32} §E{…}` | **yes** — `:11392-11397`; **X7** compiles today |
| 3 | Delegate declaration | `§DEL{id:Name}` … `§E{…}` | **yes** — `:11574-11577`; **X8** compiles today |
| 4 | Parameter, tag form | `§I{Func<i32,i32>:f} §E{…}` | parses, **wrong meaning** — Y1a/Y1b |
| 5 | Parameter, inline form | `(Func<i32,i32>:f §E{…}, i32:v)` | **no** — **X9c**: `Calor0100: Expected CloseParen but found Effects` |
| 6 | Return (two spellings) | `§O{Func<i32>} §E{…}` and `-> Func<i32> §E{…}` | parses, **wrong meaning** — X2b, Y5a |
| 7 | Binding | `§B{f:Func<i32,i32>} §E{…} <init>` | **no** — **Y3a**: `Calor0100: Expected statement but found Effects` |
| 8 | Field | `§FLD{Action<i32>:onChange:pri} §E{…}` | **no** — **X9b**: `Calor0100: Expected TP, WHERE, EXT, IMPL, FLD, … but found Effects` |

(Draft v1's §14 Q4 closed position 8 by *reading* `ParseClassField`'s default-value guard and
concluded it already parsed. **X9b disproves that**: the class-member loop rejects `Effects`
before the guard is relevant. Corrected here; the correction is why v2 and v3 execute everything.)

**Insertion points: six, not twenty.** The evidence lens counted 9 `§I`→`ParseParameter` dispatch
arms (`Parser.cs:1369, :1565, :8271, :8826, :8971, :10137, :10300, :10390, :11566`) and 7
`§O`→`ParseOutput` arms (`:1375, :1571, :8278, :8833, :8978, :10397, :11572`) — correct, and
fatal to Draft v1's "one check per arm". **The check moves inside the shared productions
instead**, so each is written once:

| Insertion point | Covers position(s) |
|---|---|
| end of `ParseParameter()` (`Parser.cs:1740-1752`) | **4** — all **9** `§I` arms |
| end of `ParseOutput()` (`:1754-1768`) | **6** (`§O` spelling) — all **7** `§O` arms |
| `TryParseInlineSignature`, after the modifier slot (`:13567`) | **5** |
| `TryParseInlineSignature`, after the arrow's return type (`:13620`) | **6** (`->` spelling) |
| `ParseClassField()`, before the default-value branch (`:8709`) | **8** |
| the `§B` production, after its attribute group | **7** |

Positions 1–3 need **no** parser change at all. `TokenKind.Effects` is absent from
`ExpressionParsers` (`Parser.cs:15-65`, exactly 47 entries), so `IsExpressionStart()`
(`:2466-2469`) is false for it and no initializer or default-value parse can swallow a row —
which is why insertion points at positions 5, 7 and 8 are safe. That property is pinned (P5).

Each of the six also carries the **Calor0405 recovery** of §3.1 on its non-adjacent branch, at
the four positions (5, 6-wrapped, 7, 8) that have no `§E` arm to fall through to.

For completeness, one more place a `§E` can be written and is rejected today, so that E2 does not
accidentally start accepting it: inside a call's argument list. **Z10** —
`§R §C{Helper} §A INT:1 §E{cw} §/C` — gives `Calor0100: Expected EndCall but found Effects` at
(7,28) plus five cascade lines. Arguments are values, not declarations; they have no row, and
Calor0405 is *not* extended there. P3 pins the rejection.

**Row storage.** `EffectsNode? Row` on four **existing** classes:
`ParameterNode` (`Ast/FunctionNode.cs:252`), `OutputNode` (`Ast/FunctionNode.cs:21`),
**`BindStatementNode`** (`Ast/ControlFlowNodes.cs:161`), `ClassFieldNode`
(`Ast/ClassNodes.cs:554`). Draft v1 called the third `BindingNode`; `grep -rn "\bBindingNode\b"
src/` → **0 hits**. Adding a child property to those four requires an
`eng/ast-schema.json` edit, forced by
`tests/Calor.Compiler.Tests/ArchitectureTests.cs:158`
(`AstSchema_CoversEveryNodeDispatchAndChildRelation`) — §9.

### 3.4 Composition with declaration-level `§E{…}`

`§E` on a declaration **is** the row of that declaration's own type. One tag, one meaning: *the
effects this callable may perform*. Consequences:

- The `FunctionSymbol` for `§F{f001:Log:pub} (str:m) -> void` + `§E{cw}` gets
  `FunctionBoundType(…, Row: Concrete({cw}))` (§8.2).
- The method-group special case at `EEP:1272-1278` is subsumed: the argument's *type* carries the
  row and §6 site 2 checks it. Its existing pin
  (`StrictnessBatchTests.cs:612` `C4_MethodGroupArgument_ChargesCalleeDeclaredEffects`) keeps
  passing because for a monomorphic destination "the row fits" reduces to today's charge.
- The body-vs-declaration check stays **Calor0410** (`EEP:377`, `:427`). Rows do not replace it.
  Calor0424/0425 are about two rows meeting at a binding site; Calor0410 is about a body
  exceeding its own declaration. **E2 does append a clause to the Calor0410 message when the
  charge comes from a row** (§10.1); that is a message change, and §13.2 pins the new text
  rather than claiming byte-stability. The *code* and the per-effect cardinality are unchanged.

### 3.5 What an omitted row means

| Site | Omitted row | Why |
|---|---|---|
| Declaration (`§F`/`§MT`/`§DEL`) | **pure** | unchanged (`EEP:473-478`); 390 of 886 committed files have no `§E` at all |
| Lambda literal | **inferred from the body** | the body is present and already walked (`EEP:2942-2954`) |
| **Binding with an initializer** | **inferred from the initializer** | *changed from Draft v1.* `§B{f} §LAM …` must not produce Calor0425, or §5's inferred-lambda rule is dead on arrival (`Y9a` is that exact shape). The initializer's row is known, so use it |
| Binding with **no** initializer, parameter, return, field | **Unknown** | nothing to infer from and nothing declared; defaulting to pure re-opens the hole PR #968 closed. New syntactic positions: 0 corpus occurrences (§3.2) |

The asymmetry is three different epistemic situations — *a promise*, *a computable fact*, *no
information*. Collapsing them would have to pick pure (unsound) or Unknown (breaks 390 files and
every `§B{f} §LAM`).

**And a row on a position that is not function-typed is Calor0405.** v2 defined what an *omitted*
row means but never what a *present* row means on an `i32`. Executed, all four such forms compile
today with the `§E` read as the declaration's row:

```
CASE Z9   §F{f001:Log:pub} (i32:x) -> void §E{cw}      → Compilation successful
CASE Z9b  §I{i32:x} §E{cw}                             → Compilation successful
CASE Z9c  §O{i32} §E{cw}                               → Compilation successful
          (§FLD{i32:x} §E{cw} does not parse at all — X9b)
```

Under the line rule each becomes a suffix row on `void` / `i32`, which has no meaning: a
non-function type performs no effects. **Decision: E2 reports Calor0405 `EffectRowMisplaced`**,
the same code as §3.1's recovery, with a second message:

```
Calor0405: 'x' has type 'i32', which is not a function type, so it cannot carry an
effect row. Remove the §E{cw}, or — if this is the function's own effect declaration
— move it onto its own line.
```

One code, two situations, both "a row where a row cannot go". Note this makes **Z9 a second
breaking change on a form that compiles today**, alongside §3.2's three — and like those, its
corpus count is **0**: the same-line sweep that found 0 function-typed same-line rows found 0
same-line rows of any kind. P6 pins one case per position.

### 3.6 Six worked examples with executed BEFORE diagnostics

Future syntax is in `text` fences, which `calor self-check docs` never scans
(`docs/cli/self-check.md:80-86`); and `docs/design/` is outside its covered set anyway.

**E-1 — function-typed parameter, invoked.** BEFORE (**X10**, verbatim):

```
X10.calr(4,8): error Calor0418: Invocation of function-typed value 'transform' (type
'Func<i32,i32>') is an error under effect enforcement: function-typed values carry no
effect contract, so the call cannot be charged. Wrap the call in §CSHARP interop
(surfaced as an assumption via Calor0419) or compile with --permissive-effects (an
explicit waiver).
```

AFTER: `§F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32` with `§E{cw}` —
no diagnostic; the invocation charges `{cw}` and the declaration allows it.

**E-2 — the charge exceeds the declaration.** Same source with `§E{}` on `Apply`. AFTER:
`Calor0410: Function 'Apply' uses effect 'cw' but does not declare it` — today's code, today's
per-effect shape, plus §10.1's new provenance clause.

**E-3 — argument row wider than the parameter's.** BEFORE (**X11**, verbatim): a pure `Apply`
taking a row-less `Func`, called with a `§E{cw}` method group, **compiles silently** —
`Compilation successful`. That is the laundering. AFTER: `Calor0424` at the argument span
(message in §6.4).

**E-4 — argument row unknown.** `§F{f001:Run:pub} (Func<i32>:make) -> i32` with `§E{}`. AFTER:
`Calor0425` at the `make` parameter span, then `Calor0410` because Unknown fits no declared set.
Under `--permissive-effects` the 0425 is suppressed and the charge short-circuits to `Empty`,
mirroring `EEP:1427-1430`.

**E-5 — lambda row narrower than its body.** BEFORE: silence — **X7** shows
`§B{f} §LAM{lam1:x:i32} §E{} (+ x 1) §/LAM{lam1}` compiles today with the `§E` parsed and
discarded. AFTER: `Calor0410` at the `§LAM`'s `§E` span when the body exceeds it (§5).

**E-6 — override broadening its base.** BEFORE (**Y8b**, verbatim):

```
Y8b.calr(10,7): error Calor0420: Override 'Derived.Render' declares effect(s) [cw] not
declared by base method 'Base.Render' (base declares: [pure]). An override may not
broaden its base method's effect set — broader effects would launder through dynamic
dispatch.
```

AFTER: identical code and message, computed by the shared `fits` relation (§6.3).

---

## 4. Decision 2 — The row lattice

> **LANDED — E2 slice b, PR #1102.** `EffectRow` implements this section in
> `src/Calor.Compiler/Binding/BoundTypes/BoundType.cs`: `Concrete`/`Assumed`/`Unknown`, `Join`
> (§4.2), the three-valued `Fits` (§4.3, all nine cells), `AtDestination` (§4.4),
> `AtDeclarationBoundary` (§5) and `FamilySubtypes`/`Encompasses` (§4.1). Pins **P6–P10** are
> green; the corpus delta over all 886 committed `.calr` is **zero files**.
>
> **One deviation, forced by the architecture pin.** §8.3 says the row's display string extends
> `EffectSet.ToDisplayString()`'s compact surface codes. `EffectRow` lives in `Binding/`, which
> `ArchitectureTests.BindingLayer_HasNoReferenceToEffectsNamespace` forbids from naming the
> `Effects` namespace at all — and the compact spelling is a projection through
> `EffectCodes.Registry`, which is an `Effects` table. So `S` is carried in the INTERNAL
> `category:value` vocabulary `BoundFunction.DeclaredEffects` already uses, and the compact
> rendering is `Effects.EffectRowDisplay.ToCompactDisplayString` — an extension method on the row,
> living on the side of the layering that owns the registry. Same for the `EffectSet` ↔
> `EffectRow` bridge. §8.3's user-facing spellings (`[unknown]`, `[pure]`, `cw, fs:w`,
> `[assumed: cw]`) are unchanged and pinned.

> **Decision.** `Row ::= Concrete(S) | Assumed(S, R) | Unknown`, with `S` a registry-closed effect
> set and `R` a **canonically ordered set** of reasons. Inference uses a join `⊔` (⊤ = Unknown).
> Checking uses a **separate three-valued relation** `fits` — `Fits | DoesNotFit | CannotTell` —
> **totally defined over all nine source×destination cells**. `EffectSet.IsSubsetOf` is not
> reused as `fits`.

### 4.1 The carrier, and the family/narrow gap

`S` ranges over `EffectCodes.Registry` (`EffectTypes.cs:65-109`), ordered by
`EffectSubtyping.Encompasses` (`EffectSubtyping.cs:52-66`).

**Sub-decision — EXECUTED, E2 slice b (PR #1102).** `database`, `network` and `environment` now
encompass their narrow siblings. The table itself moved: `EffectRow.FamilySubtypes` (in
`Binding/BoundTypes/`, over the internal `category:value` codes) is the single source of truth,
and `EffectSubtyping.Subtypes` is DERIVED from it by splitting on the first colon — because
`Binding/` may not reference `Effects/` and two hand-written tables would be two things to keep
in step. The `:rw` rows are listed first so `GetBroadestEncompassing` keeps 0.14's answer for a
NARROW code (`db:r` still resolves to `db:rw`, not to `db`). **Its answer for a `_readwrite` code
does change and the ordering cannot prevent it:** on 0.14 nothing covered `db:rw`, so it returned
`db:rw`; here `db` covers it and is returned. Suppressing that would mean dropping `db:rw` from
`db`'s subtype list, which would make `§E{db}` stop admitting `db:rw` — the opposite of the
widening. `GetBroadestEncompassing` has **no production caller**, so the change is test-visible
only, and `EffectSubtypingTests` pins the new answer rather than leaving it to be found. An
earlier revision of this paragraph and of `EffectRow.FamilySubtypes`'s doc comment claimed the
method was byte-identical; that was **false for the three `_readwrite` codes** and is corrected
here (review round 1, MAJOR 1). **Calor0410s that DISAPPEARED from the corpus: none** — the 886-file differential
against `4766c8fc` shows zero files with a changed exit code or diagnostic-code set, which is
gate 5's "listed by name" leg discharged with an empty list. Pin **P7**
(`EffectSubtypingTests.cs`) covers all nine family/narrow pairs, the one-way direction, the
`fs:rw` regression half, and the reach into `EffectSet.IsSubsetOf`. The original wording follows.

**Sub-decision, executed in E2's PR:** add `database`, `network` and `environment` to
`EffectSubtyping.Subtypes` (`:14-43`) so a bare family code encompasses its narrow siblings.
Today it does not (§2), which under rows becomes visible at every binding site instead of only at
a declaration. `filesystem` has no bare code so `fs:rw` stays the filesystem top; `proc` and
`http` have no narrow siblings. This is a **widening** — nothing that compiled stops compiling —
and any Calor0410 that *disappears* is listed by name in the E2 PR body and counted by gate 5.
`Calor0401 UnusedEffectDeclaration` is declared and never reported (`Diagnostic.cs:376`), so the
widening has no 0401 blast radius.

**The widening does NOT reach the registry's LEGACY internal values, and that gap is E3's**
(E2 slice b, review round 1 MINOR 8). `EffectCodes.Registry` carries legacy entries whose compact
code duplicates a modern one — `("io","dbr")` and `("io","dbw")` both spell `db:r`/`db:w`, and
`("io","file_write"/"file_read"/"file_delete")` spell `fw`/`fr`/`fd` — and
`EffectRow.FamilySubtypes` lists none of them, so `Encompasses(("io","database"), ("io","dbr"))`
is **False**. It is **not reachable by parsing**: `EffectCodes` groups by compact code and prefers
the non-legacy entry, so `§E{db:r}` and `EffectSet.From("db:r")` both yield
`("io","database_read")`, which the table does cover. It is reachable only by a caller that
constructs the legacy internal value directly — `EffectSet.FromInternal`, or a manifest written
against the legacy spelling — and no production caller does so today. Slice b deliberately leaves
it: adding the aliases is a widening **beyond** this section's nine pairs, and a Calor0410 could
disappear from the corpus on the back of it, which would break the reviewed property that
`EffectSubtyping` is main plus exactly those nine.
`EffectSubtypingTests.LegacyInternalValues_AreOutsideTheFamilyTable_AndThatIsE3s` pins the gap so
it is observed rather than latent.

### 4.2 The join `⊔` (inference)

```
Concrete(A)     ⊔ Concrete(B)     = Concrete(A ∪ B)
Concrete(A)     ⊔ Assumed(B, R)   = Assumed(A ∪ B, R)
Assumed(A, R₁)  ⊔ Assumed(B, R₂)  = Assumed(A ∪ B, R₁ ∪ R₂)
X               ⊔ Unknown         = Unknown                  (absorbing, either side)
```

`∪` on effects is `EffectSet.Union` (`EffectSet.cs:83-91`, already absorbing on Unknown `:86`).
**`R` is a set ordered canonically** (ordinal sort on the reason string), not a list — Draft v1
used `++` concatenation, which made `⊔` non-commutative and the semilattice claim false, and
would have made a diagnostic's reason order depend on traversal order. With a canonically ordered
set, `⊔` is associative, commutative and idempotent, with identity `Concrete(∅)` and top
`Unknown`. The presentation rule (first three, then `"; and N more"`, `EEP:453-455`) is unchanged
and now deterministic. §13.2 pins the laws.

### 4.3 `fits` — all nine cells

`⊆ₑ` is `EffectSet.IsSubsetOf`'s loop body (`EffectSet.cs:103-118`) **without** its two Unknown
special cases (`:100-101`).

| src ↓ / dst → | `Concrete(B)` | `Assumed(B, R_d)` | `Unknown` |
|---|---|---|---|
| **`Concrete(A)`** | `Fits` if `A ⊆ₑ B`, else `DoesNotFit` | `Fits` if `A ⊆ₑ B` **+ 0425 carrying `R_d`**, else `DoesNotFit` | `CannotTell` (0425 at the dst) |
| **`Assumed(A, R_s)`** | `Fits` if `A ⊆ₑ B` **+ 0425 carrying `R_s`**, else `DoesNotFit` | `Fits` if `A ⊆ₑ B` **+ 0425 carrying `R_s ∪ R_d`**, else `DoesNotFit` | `CannotTell` (0425 carrying `R_s`) |
| **`Unknown`** | `CannotTell` | `CannotTell` (0425 carrying `R_d`) | `CannotTell` |

Draft v1 left the `Assumed`-destination column undefined; it is reachable (a manifest resolution
becomes a row, §8.4, and site 5's destination is an external interface member's row). Reading the
table as three sentences:

- **A concrete row never fits into a narrower one** — `Concrete({cw})` into `Concrete(∅)` is
  `DoesNotFit`. That is E-3.
- **Unknown never yields `Fits`, on either side.** In particular `EffectSet.cs:100`'s "everything
  is a subset of unknown" is **not** carried into `fits`: it is sound for its own caller (a
  *computed* set against a *declared* set, which can never be Unknown because `§E{unknown}` is
  unwritable — **X5b**-style, `unknown` is not in the registry) and unsound for rows, where a
  destination can be Unknown by omission.
- **Assumed fits like its underlying set and always propagates 0425**, from whichever side the
  assumption came. `DoesNotFit` on an `Assumed` row is a hard 0424: if the assumed set already
  exceeds the destination, no further assumption rescues it.

`CannotTell` propagates through conjunctions; `DoesNotFit` dominates `CannotTell`.

### 4.4 The destination row at each site, so `Assumed` survives

Draft v1 defined `fits` but never said what the destination row *is* per site, so an `Assumed`
source could reach a `Concrete` destination and the assumption would vanish at the next hop —
the two-hop laundering the design claims to close. The rule, stated once:

> **When `fits(src, dst)` returns `Fits` while carrying reasons `R`, the value's row at the
> destination is `Assumed(S_dst, R)`, not `Concrete(S_dst)`** — where `S_dst` is the
> destination's declared set. The reasons ride along; Calor0425 is emitted once per hop.

Concretely, per site (§6.2's numbering): 1 the binding's row; 2 the parameter's row; 3 the
declaration's return row; 4 the base method's row; 5 the interface member's row; 6 the
instantiated bound. In every case an `Assumed` source produces an `Assumed` destination.

**One edge, stated so E3 does not trip on it** (E2 slice b). When the DESTINATION is `Unknown`,
`EffectRow.AtDestination` returns `Unknown` and **discards the source's reasons**. That is sound
only because such a hop is `CannotTell`, never `Fits` — E3 reports Calor0425 there from the
verdict itself, so no reason is lost. **E3 must not call `AtDestination` on a `CannotTell` hop
expecting reasons to survive it**; `EffectRow.CarriedReasons` is the total function that keeps
them, on every cell.

### 4.5 `--permissive-effects`

> **Decision.** A `DoesNotFit` verdict is **never waived, at any of the six sites, by any flag**.
> `--permissive-effects` waives only `CannotTell` (Calor0425). Consequently
> **Calor0420/0421 lose their `--permissive-effects` demotion** (`EEP:517-519`).

Draft v1 said "0424 is never waived" while leaving `EEP:517-519` alone, which made the sentence
false for two of the six sites. Executed proof of today's behaviour — **Y8a**, the E-6 source
with `--permissive-effects`:

```
Compilation successful: Y8a.g.cs
Y8a.calr(10,7): warning Calor0420: Override 'Derived.Render' declares effect(s) [cw] …
```

versus **Y8b** (no flag): the same text as an `error`. Under the decision, Y8a becomes an error
too. Priced: **no existing test asserts the demotion** (`grep -n "Permissive"
tests/Calor.Enforcement.Tests/StrictnessBatchTests.cs` → only `:64`/`:75`, the 0418 waiver), and
gate 5's corpus legs confirm no committed `.calr` depends on it. §13.2 pins both polarities.

`--permissive-effects` therefore becomes **strictly less powerful** in 0.15 than in 0.14: it
stops hiding Calor0418 (which no longer exists), stops demoting variance errors, and keeps
exactly one job — waiving "we cannot tell". A waiver for *we do not know* is honest; a waiver for
*we know it is wrong* is not. Release notes carry it; §13.4 makes it a checked artifact rather
than a promise.

> **A residual §4.5 leaves open, recorded by E3a review round 1 (F15).** An **Unknown** source
> entering a **declared** row is admitted on the author's say-so after exactly one Calor0425, and
> the declaration then governs every later hop. Executed, at the E3a branch head:
>
> ```
> §F{f002:Main:pub} (Func<i32,i32>:opaque) -> i32     ← no row: Unknown
>   §E{cw}
>   §B{g:Func<i32,i32>} §E{cw} opaque                  ← hop 1: CannotTell → ONE Calor0425
>   §R §C{Apply} §A g §A INT:1 §/C                     ← hop 2: Concrete(cw) vs Concrete(cw) → SILENT
> ```
>
> ```
> f15.calr(7,25): warning Calor0425: Initializer of binding 'g' has effect row [unknown] and
> binding 'g' declares row cw, so it cannot be decided whether the row fits. …
> Compilation successful
> ```
>
> That is §4.4 working as specified — `AtDestination` gives the binding its DECLARED row, and
> §4.4's note says the Calor0425 at the `CannotTell` hop is where the provenance is surfaced, so
> nothing is lost. But two consequences follow and neither was stated:
>
> 1. **The declaration launders an Unknown into a Concrete after one warning.** It is not the
>    two-hop laundering M5 closes — that one was silent — but it is a one-hop *annotation*
>    laundering, and its only guard is a warning.
> 2. **`--permissive-effects` silences the whole chain**, because it silences the single
>    Calor0425 that guards it. §4.5's claim that the waiver is "honest" rests on the assumption
>    being visible somewhere; here, under the flag, it is visible nowhere.
>
> This is **not** a defect in slice a — it is the specified behaviour of §4.4 plus §4.5 — and it
> is left open rather than fixed, because closing it means deciding whether an author may assert a
> row over an Unknown at all, which is a design question and not an implementation one. E4 owns
> the answer, since E4 is where an Unknown row starts costing something at every invocation.
>
> **ANSWERED — v0.15 E4. YES: an author may assert a row over an Unknown, and the assertion
> costs exactly one Calor0425, at the hop.** Rationale, in three parts. (1) A declared row on a
> binding is the author's contract for that name, with the same standing as a `§F`'s own `§E{…}`
> — and §5 already lets a declaration convert `Assumed` to `Concrete` at its boundary, reporting
> the provenance there rather than carrying it onward. Refusing the assertion would make a
> row-less BCL-returned delegate un-annotatable in Calor: the only remaining moves would be
> `§CSHARP` interop or the flag, which is the ergonomics §13.4 exists to avoid. (2) The one
> Calor0425 at the hop is the honest place: it names the source as `[unknown]` and the
> declaration as the row assumed, and §4.4's rule then gives the value its DECLARED row, which
> is what every later hop and the invocation read. The invocation therefore charges the declared
> row (`Effect row: charged by invoking 'g' (row: cw)`) and reports nothing further — one
> report per hop, P10(b), and the invocation is not a hop. (3) Consequence 2 is accepted with
> the answer, and narrowed: under `--permissive-effects` the single Calor0425 is silenced, which
> is the flag's specified meaning (waive *we cannot tell*), but the **charge is not a "cannot
> tell" and is not waived** — the declared `cw` still reaches Calor0410. So the flag hides the
> assumption, never the effect asserted. Pinned, both policies, by
> `StrictnessBatchTests.AuthorMayAssertARowOverAnUnknown_OneCalor0425AtTheHop_TheDeclaredRowIsCharged`.

**Is Calor0424 defeatable by deleting the source's `§E`?** No, and the reason is the composition.
Deleting `§E{cw}` from an in-module source makes its declared row pure, so it would fit — but its
*body* still prints, so Calor0410 rejects the source itself (`EEP:377`). Soundness is
`body ⊑ declaration` (0410) **∧** `declaration ⊑ destination` (0424). For a source whose body
Calor cannot see, the row is `Unknown` or `Assumed`, so the verdict is `CannotTell` → 0425, never
a silent pass. §13.2 pins the composition as a two-diagnostic fixture.

### 4.6 Subtyping for function types

> **Decision.** A function type's **own row is covariant**; its **parameters' rows are
> contravariant**; parameter and return **types stay invariant** in 0.15.

For `Fᵢ = (Pᵢⱼ !ρᵢⱼ …) -> Tᵢ !ρᵢ`:

```
fits(F₁, F₂) = Fits  iff  arity equal
                     and  P₁ⱼ ≡ P₂ⱼ ∀j    and  T₁ ≡ T₂        (types invariant)
                     and  fits(ρ₁,  ρ₂ ) = Fits                (own row COvariant)
                     and  fits(ρ₂ⱼ, ρ₁ⱼ) = Fits ∀j             (param rows CONTRAvariant)
```

Types stay invariant because `FunctionBoundType.Equals` is structural equality over parameters
and return (`Binding/BoundTypes/BoundType.cs:228-231`), and making them variant is a *generics*
change that `calor-direction.md:33` defers ("Generics with constraints, variance, higher-rank …
Deferred because the type-system work for effect rows is more foundational"). 0.15 changes what
rows do, not what types do.

Contravariance, read aloud. A destination
`§F{f001:RunTwice:pub} (Func<i32,i32>:g §E{cw}) -> void` promises its caller *"the `g` I hand you
may print"*. A source that accepts only a pure `g` (`§E{}`) **does not fit** — it accepts fewer
functions than the destination promises to supply. A source accepting `§E{cw,fs:w}` **does** fit.
Formally the *destination's* parameter row must fit into the *source's*.

---

## 5. Decision 3 — The fate of `§LAM`'s `§E`

> **Decision.** It **becomes the lambda's declared row**, checked against the body exactly as a
> function's `§E` is (Calor0410). Omitted → inferred from the body, no diagnostic. Not removed.

> **STATUS — LANDED, PR #1106, v0.15 E3 slice b (P14).** The effect pass records ρ_body for
> every `§LAM` it walks (`InferenceContext.RecordLambdaBody`, written from `InferFromLambda`,
> last-write-wins so an SCC's fixpoint leaves the CONVERGED row) and checks it against ρ_decl in
> a new phase 3e: `DoesNotFit` → Calor0410 at the `§E` span, per effect, in today's shape;
> `CannotTell` → Calor0425. An un-annotated lambda's TYPE row is now ρ_body rather than E2b/E3a's
> `Unknown` placeholder, and an annotated one's is `Concrete(declared)` — the declaration
> boundary of the paragraph below, observed by `LambdaTypeCarriesDeclaredNotInferred`.
>
> **Measured effect of replacing the Unknown placeholder: two harness sites, zero corpus sites.**
> `run2.py`'s **Y3a-B-with-sameline-E** (site 1) and **Y4a-O-sameline-E-decl-later** (site 3) each
> lose the Calor0425 slice a gave them, because a decided `Fits` replaces `CannotTell`. The
> committed corpus does not move at all, and its one Calor0425
> (`13-03.approved.calr`) correctly SURVIVES: that site is a row-less `§LAM` returned into a
> **row-less return**, so ρ_body fixes the source while the destination stays Unknown — and
> `fits(anything, Unknown)` is `CannotTell` by §4.3. The expectation that ρ_body would clear it
> was wrong about which side was unknown.

Today it is parsed (`Parser.cs:11392-11397`), carried into the bound tree
(`Binding/BoundNodes.cs:2128-2129`, AST field `Ast/LambdaNodes.cs:41`) and **discarded** —
`InferFromLambda` (`EEP:2942-2954`) charges the body and never reads `lambda.Effects`. Executed:
**X7** (`§LAM{lam1:x:i32} §E{}` around an impure-free body) compiles today; nothing observes the
annotation. Roadmap §4.1 forbids leaving it parsed-and-ignored.

**Why it becomes the row.** (1) It is the only place an author can state intent about a function
*value*; inference cannot express intent, so a lambda meant to stay pure would silently widen when
someone adds a `§P` and the failure would surface at a distant call site. (2) It costs nothing —
parse, AST field and bound field all exist. (3) Removal is free today and expensive later:
**0 of the 9 `§LAM` occurrences in the committed corpus carry a `§E`** (7 files:
`d-s1.5/conditional-declaration/expected.calr` ×1 and Conversion snapshots `05-02` ×1, `05-03`
×3, `07-02`/`07-03`/`07-04`/`13-03` ×1 each).

**The rule.** Let `ρ_body` be the body's inferred row, lifted into the lattice (`Assumed`/`Unknown`
arrive through §4.2's join automatically). If `§E` is present with `ρ_decl = Concrete(declared)`:
`fits(ρ_body, ρ_decl) = DoesNotFit` → **Calor0410** at the `§E` span, per-effect, in today's
shape; `CannotTell` → **Calor0425**. The lambda's *type* then carries `ρ_decl` — the declaration
is the contract, as for a function. If `§E` is absent the type carries `ρ_body` and nothing is
reported.

**The declaration boundary converts `Assumed` to `Concrete`, deliberately.** If `ρ_body` is
`Assumed(S, R)` and the author writes `§E{…}`, the type that leaves the declaration is
`Concrete(declared)` — the reasons do not ride onward, unlike at the six binding sites of §4.4.
That is not the two-hop laundering M5 closes: a declaration is exactly where Calor0419 already
reports the assumption today (`EEP:448-463`, per function, with its reason list), so the
provenance is surfaced at the boundary rather than carried past it. The alternative — an
`Assumed` row escaping every annotated function — would make Calor0425 fire at every downstream
call site of anything that touches interop, which is the noise §13.4's ledger exists to avoid.
The boundary is a **seventh** place a row changes form and is deliberately **not** a §6 site;
P10 covers it as a third case so the conversion is observed rather than assumed.

**Interaction with §3.5.** A `§B{f} §LAM …` with no binding row takes the *initializer's* row,
which is the lambda's — so the common shape stays silent. Executed baseline **Y9a**: today that
shape yields `Calor0418` at the invocation (demoted to a warning under `--permissive-effects`);
after E4 it compiles, with `{}` charged. That is §13.1's rewrite of
`StrictnessBatchTests.cs:47`.

---

## 6. Decision 4 — Compatibility checking sites (E3)

> **LANDED (five of six) — E3 slice a, PR #1103.** Calor0424 and Calor0425 are allocated
> (`Diagnostics/Diagnostic.cs`), sites **1, 2, 3** are adjudicated by
> `EffectEnforcementPass.CheckRowCompatibility` / `RowSiteChecker`, sites **4 and 5** keep
> Calor0420/0421 and are re-implemented on `EffectRow.Fits` (§6.3), the
> `--permissive-effects` demotion at `EEP:517-519` is **deleted** (§4.5), and
> `CrossModuleEffectEnforcementPass.cs:162` routes through the same relation. `EffectRow.FitsFunction`
> lands §4.6's whole-function variance rule with no production caller yet, stated as such.
> Site **6** (rank-1 instantiation) is **slice b's**, and
> `StrictnessBatchTests.RowMismatch_AtGenericInstantiation_IsSliceBs_AndTheGapIsObserved` observes
> the gap rather than leaving it absent from gate 1's denominator.
>
> **Three scope decisions the slice took, recorded because none is obvious from this section.**
>
> 1. **A site exists only where a DESTINATION DECLARATION is visible.** An external callee has no
>    destination row at all — §8.4 freezes the manifest schema without a row field in 0.15 — so a
>    BCL parameter is *not a site*, rather than a site with an Unknown destination. The alternative
>    puts a Calor0425 on every BCL argument in every converted file, which is the noise §13.4's
>    ledger exists to bound.
> 2. **An effect-POLYMORPHIC position is skipped.** A row mentioning an `eff` variable is site 6.
>    Treating it as Unknown here would put a Calor0425 on every call in the four A3 combinator
>    fixtures, whose PP-E1 leg-A negative-control baseline is zero effect-family diagnostics. Sites
>    4/5 keep computing from `EffectSet` (where an `eff` name contributes nothing), which is
>    byte-identical to today's answer — so `A3-middleware-alpha` passes **without** alpha-equivalence
>    being implemented. Slice a does not unify `e` with `f`; it never compares them.
> 3. **Neither external-base Calor0419 is retired**, against this section's own text, and they must
>    move together. The interface arm is a direct report and is trivially convertible; the override
>    arm is an `AddAssumption` whose propagation feeds every caller's *computed* effect set, so
>    converting it deletes that propagation and moves Calor0410 across the corpus. Converting only
>    one would make sites 4 and 5 disagree about what an unresolvable base means. Owed by the slice
>    that redesigns the assumption channel; §13.1's `:260` and `:607` rewrites are **not** discharged,
>    and §6.4's **third** message sample ships with them.
>
> **Corpus differential: ONE file of 886 moves**, gaining one Calor0425 at a real site 3
> (`tests/Calor.Conversion.Tests/Snapshots/13-03.approved.calr`, `GetComparer` returning a row-less
> `§LAM` into a row-less `Func<…>` return). Calor0418 is unchanged at 619 and Calor0419 at 4, which
> is §8.2's widening measured rather than feared. This section's earlier expectation of **zero** was
> one file too strong: a site can be `CannotTell` because *neither* side carries a row, which is
> §6.4's second message sample and PP-E1's `L7` class.

> **Decision.** **Six** sites, one shared relation, and **Calor0420/0421 stay as distinct codes**
> re-implemented on top of it. The gate-1 denominator frozen by this document is **six classes,
> dropping to five if the §7.5 ramp fires**.

### 6.1 Diagnostic allocation (frozen at this doc's merge)

```
Calor0424  EffectRowMismatch    Error.   fits(...) = DoesNotFit. Never waived, any flag, any site.
Calor0425  EffectRowUnknown     Warning; Error under --strict-effects.
                                fits(...) = CannotTell. Waived by --permissive-effects.
Calor0404  EffectVariableScope  Error.   An effect variable declared or used where §7.3 forbids
                                it, out of scope, or named after a live effect code (§7.2).
                                A declaration-shape violation, never a binding-site verdict.
Calor0405  EffectRowMisplaced   Error.   A §E{…} row written where no row can attach: not on the
                                same line as its type (§3.1 recovery), or on a position whose
                                type is not a function type (§3.5). Replaces a 4–11 diagnostic
                                Calor0100 cascade with one actionable message.
```

**Calor0405 fires at two different stages, and only one of them recovers.** Worth stating,
because "one code, two situations" (§3.5) could otherwise read as one implementation:

| Stage | Trigger | Recovery |
|---|---|---|
| **Parser** (§3.1) | a `§E{…}` token at a position that accepts no row and has no `§E` arm to fall through to — Z1/Z2/Z3/X9c | **yes.** The `§E{…}` group is consumed so the rest of the declaration still parses; that is the whole point, since today's alternative is a 4–11 diagnostic cascade. Parsing continues with a complete AST |
| **Binder** (§3.5) | a syntactically well-placed, same-line row whose annotated type turns out **not to be a function type** — Z9/Z9b/Z9c | **no, and none is needed.** The row parsed cleanly and is attached to a `ParameterNode`/`OutputNode`/`BindStatementNode`/`ClassFieldNode`; the binder simply reports and drops it. There is nothing to resynchronise |

The stages cannot be merged: the parser does not know whether `Func<i32,i32>` is a function type
(types are strings until binding, §3.1), and the binder never sees a token the parser could not
place. Same code because the author's fix is the same in both — *move or delete the row* — and
the two messages of §3.1 and §3.5 say which. P2 pins the parser stage against its Z-baselines;
P6 pins the binder stage against Z9/Z9b/Z9c.

All four free: `grep -rn "Calor042[4-9]\|Calor040[4-9]" src/ tests/` → no hits;
`Diagnostic.cs:378` `Calor0403`, `:381` `Calor0410`, `:437` `Calor0423`, `:440` `Calor0500`.
Draft v1 used Calor0424 for rank-1 scope violations, conflating a *declaration* defect with a
*binding-site verdict*; Calor0404 separates them.

### 6.2 The six sites

| # | Site | `ρ_src` | `ρ_dst` | Span | `DoesNotFit` |
|---|---|---|---|---|---|
| 1 | **Assignment** — `§B` init, and re-assignment to a function-typed mutable | initializer's row | binding's declared row (§3.5) | the initializer | Calor0424 |
| 2 | **Argument** | argument's row | parameter's declared row | the argument | Calor0424 |
| 3 | **Return** — `§R <function value>` under a function-typed return | returned value's row | the `§O`/`->` row | the `§R` expression | Calor0424 |
| 4 | **Override** | override's declared row | base method's declared row | override's `§E` (`EEP:538`) | **Calor0420** |
| 5 | **Interface implementation** | implementation's row | interface member's row | impl's `§E`, or the class span when inherited (`EEP:581-583`) | **Calor0421** |
| 6 | **Rank-1 generic instantiation** | argument's row | the instantiated bound (§7.2) | the call site | Calor0424 |

`CannotTell` at **any** site reports **Calor0425** at the same span, whatever that site's
`DoesNotFit` code is — including sites 4 and 5, which today emit Calor0419 for external bases
(`EEP:548-553`, `:596-611`). Those two Calor0419 emissions are retired in favour of Calor0425;
§13.1 disposes of the three existing pins that observe them.

> **STATUS — BOTH ARMS RETIRED, PR #1106, v0.15 E3 slice b.** Slice a took neither, on the
> ground that they must move together (§6.2's third scope decision). Both move here. The override
> arm's `AddAssumption` is gone: measured rather than feared, the assumption channel carries
> **reasons, not effects** (`AddAssumption` appends to `_assumedEffects`, which drives Calor0419
> and nothing else), so retiring it cannot move a computed effect set or a Calor0410 — and the
> committed corpus confirms it, with Calor0410 unchanged at 464 and the three surviving Calor0419s
> all the `§CS`-interop kind. §6.4's **third** message sample ships with them, pinned by full
> equality. `StrictnessBatchTests.cs:260` and `:607` are re-pinned in place, preserving the file's
> line numbering so `facts.py`'s probes keep their meaning.
>
> **P32 moves 0 → 4 and this is the whole cause** — 1 in serilog, 3 in FluentValidation, attributed
> by disabling each candidate change in turn rather than by inference. The ledger's cause taxonomy
> has no bucket for it, so all four land in its `UnknownSource` ELSE arm; §13.4's taxonomy is E4's
> to revisit.

Per §4.4, a `Fits`-carrying-reasons verdict makes the destination row `Assumed`, so the assumption
survives the hop.

> **STATUS — v0.15 E4: Calor0418 replaced by fits-at-invocation, and INVOCATION IS NOT A
> SEVENTH SITE.** This table has no invocation row, and E4 confirms that is right rather than
> an omission: at `§C{f}` there is no destination row for `f`'s row to fit into — the invoked
> value's row IS what the caller performs. So the three verdicts of §4.3 collapse to what a
> row contributes when charged: `Concrete(S)` charges `S` (silently; §10.1's provenance clause
> rides into Calor0410 if the caller under-declares); `Assumed(S, R)` charges `S` and reports
> one Calor0425 carrying `R`; `Unknown` reports Calor0425 at the invocation and charges the
> fail-closed `EffectSet.Unknown` (nothing under `--permissive-effects`). **Calor0424 is never
> reported at an invocation** — there is no `DoesNotFit` cell to report it from — and
> `StrictnessBatchTests.MessageTexts_Calor0410_InvocationProvenance_IsTheDesignDocSample` pins
> that the under-declared caller draws Calor0410, not 0424. The charge lives INSIDE inference
> (`EffectInferrer.ChargeInvokedRow`), not in `RowSiteChecker`, for one reason: a `§LAM` that
> invokes a captured function value must carry that row in its ρ_body, and ρ_body is computed
> by inference; a phase-3d charge would have arrived after the lambda had already been
> adjudicated as a source. The row is read from the declaration's own `EffectsNode` — the node
> `FunctionBoundType.Row` was built from — because the bound projection collapses a
> variable-mentioning row to Unknown (`Binder.BindRow`) and `f §E{e}` invoked inside `Map` must
> charge `e`, not Unknown. **Calor0418 is retained for exactly one residual**: invoking a value
> whose type is PROVABLY not a function type (`TypeIdentity.IsProvablyNonFunctionType` —
> `i32`, `str`, an array), where there is no row to read; measured, the binder does not reject
> `§C{x}` on an `i32` parameter, so the code is the only guard. It is an error under every
> policy. A value whose type is not provable either way (an external nominal the binder does
> not know) is Unknown, not the residual. Pinned: `DiagnosticCodeTests.Calor0418_IsRetainedForTheProvablyNonFunctionResidual`
> reads the catalogue; `StrictnessBatchTests.Invocation_ProvablyNonFunctionValue_*` and
> `Invocation_NotProvablyFunctionTyped_IsUnknown_NotTheResidual` pin both sides of the line.
>
> **Corpus differential: ONE file of 886 moves** — `bench/phase0-agent-native/fixtures/d-s1.5/conditional-declaration/expected.calr`
> loses its single Calor0418 (a typed `§B` bound to a pure `§LAM`, then invoked: the inferred
> row is `{}`, charged silently) and gains nothing. Every other per-code total is identical
> (0410 464, 0411 633, 0419 3, 0425 1, green 653). Calor0418 over the CLI harness was already
> **1**, agreeing with E3b's harness and not with E3a's 619; the 619 remains the open number
> E3b's notes named. The two instruments differ in exactly one respect — the CLI stops at
> binder errors, an in-process count does not — which is a plausible cause and is stated as
> that, not as a measurement.

**Cross-module is the Calor0410 rule, not a seventh site.**
`Effects/CrossModuleEffectEnforcementPass.cs:162`
(`resolution.DeclaredEffects.IsSubsetOf(declaredEffects)`) is a fourth `IsSubsetOf` compatibility
site in the tree, absent from Draft v1. It compares a cross-module *callee's declared effects*
against the *caller's declaration* — the cross-module leg of the body-vs-declaration rule, so it
keeps Calor0410 semantics. It is nonetheless re-implemented on `fits` so an `Assumed` or `Unknown`
row from another module propagates instead of collapsing (§10.2), and both cross-module files are
in §9's blast radius.

### 6.3 Why Calor0420/0421 stay separate — and the consequence

1. **The four existing assertions do not change.** The four positive assertions on the two codes
   are `StrictnessBatchTests.cs:152` (`OverrideWithBroaderEffects_IsError`, method `:133`),
   `:172` (`GenericOverrideWithAlphaEquivalentTypeParameters_IsMatchedForVariance`, `:156`),
   `:219` (`InterfaceImplementationWithBroaderEffects_IsError`, `:200`) and **`:582`**
   (`C3_InheritedImplementation_BroaderEffects_IsError`, `:555`) — the fourth, which Draft v1
   omitted. Their `_Compiles` counterparts are `:177` and `:223`. Keeping the codes keeps all six
   verbatim. (Roadmap §4.4 gate 1 cites `:132/176` and `:198/221`; those are `[Fact]` and blank
   lines. This document cites **assertion lines** throughout.)
2. **Gate 1 needs each class independently observable** in the diagnostic stream; a folded
   Calor0424 would make its rows indistinguishable.
3. **The messages carry class-specific advice** (`EEP:543-544` "launder through dynamic
   dispatch"; `:591-592` "interface dispatch launders effects identically"). A merged code would
   need a sub-kind, which is a diagnostic code in disguise.

**The consequence:** three codes express one relation. `CheckEffectVariance` (`EEP:515-616`) keeps
its two report sites (`:537`, `:584`) but its two `IsSubsetOf` calls (`:533`, `:571`) become calls
to the shared `EffectRow.Fits`, as does `CrossModuleEffectEnforcementPass.cs:162`. §13.2 pins that
a change to `Fits` moves all of them together.

### 6.4 Message samples (new text; §13.2 pins it)

```
Calor0424: Argument 'Shout' has effect row cw, which does not fit parameter
'transform' of 'Apply' (declared row: [pure]). Extra effect(s): cw. Widen
'transform' to §E{cw}, or pass a function whose row fits. An effect row that does
not fit is never waived.

Calor0425: Parameter 'make' of 'Run' is function-typed with no effect row, so its
effects are Unknown. Add §E{…} on the same line as the type to state what callers
may pass, or compile with --permissive-effects. Invoking a value whose row is
Unknown charges Unknown to 'Run'.

Calor0425: Class 'ConsoleRenderer' implements 'IRenderer.Render' through a member
not visible in this module (inherited from external base 'RendererBase'), so its
effect row is Unknown. The interface's declared row [cw] is assumed here, not
verified.
```

> **Corrected, E3a review round 1 (F6).** These samples first wrote the source row **bracketed**
> — `has effect row [cw]` — which is not what `EffectRowDisplay.ToCompactDisplayString` emits.
> §8.3 froze that spelling as `EffectSet.ToDisplayString()`'s: brackets mark the three SHAPES
> (`[unknown]`, `[pure]`, `[assumed: cw]`) and a concrete non-empty row is bare (`cw`, `cw, fs:w`).
> Bracketing every row would have made the shape marker meaningless and moved `EffectRowDisplay`
> and its pins, so the DOC was corrected to the emitter and not the reverse. P22 asserts these
> strings by full equality, so the two can no longer drift.

The third **re-words** today's Calor0419 text (`EEP:605-611`) rather than merely re-coding it —
Draft v1 claimed otherwise. The re-wording is deliberate (it names the row) and pinned.

---

## 7. Decision 5 — Rank-1 effect polymorphism

> **Decision.** Effect variables are declared with an **`eff` modifier in the existing
> type-parameter list** (`§F{f001:Map:pub}<T, U, eff e>`) and used as a **bare identifier**
> inside `§E{…}` (`§E{e}`). Not `!e`. They may appear only in a declaration's own row and its
> parameters' rows; anything else is **Calor0404**. Ships iff the §7.5 ramp does not fire.

> **STATUS — LANDED (syntax only), PR #1101, v0.15 E2 slice a.** The `eff` modifier parses in the
> `§F`/`§AF`/`§MT`/`§AMT` type-parameter lists (including an interface member's, which is the
> spelling the spike chose), the one-token lookahead keeps **Z4**'s `<eff>` compiling, §7.2(c)'s
> name-collision ban is **Calor0404**, and `CalorEmitter` round-trips the binder. Binders are
> stored as `EffectParameters` on the declaring node and are deliberately **not**
> `TypeParameterNode`s, so codegen erases them by construction (G-CODEGEN, §12.2).
>
> **Three of §7.3's seven rejection sites are Calor0404 today** — return, binding and lambda —
> plus the class/interface-level binder. The other three are refused, but not by that code, and
> the honest reading is recorded rather than papered over:
>
> | §7.3 site | Slice a | Why |
> |---|---|---|
> | return · binding · lambda row | **Calor0404** | the binder is in scope and the position forbids the mention |
> | class/interface-level `eff` | **Calor0404** | rejected at the binder, message points at the member-level spelling |
> | field row · delegate row | Calor0403 | **unreachable with a bound variable.** A `§FLD` is not inside the member that could bind one, and a `§DEL` has no type-parameter list at all (**Z8**) and is a sibling of every declaration that has one. Refused by the taxonomy instead |
> | inside a generic argument | no row produced | inline types are read as strings (`ReadInlineTypeToken`), so a `§E` there never reaches a row parse. Pinned as "no row", not as a Calor0404 the compiler cannot reach |
>
> **Out-of-scope use stays Calor0403.** §7.2(b)'s second half — routing an *unbound* `§E{e}` to
> Calor0404 — needs the binder and is **E3's**. Pinned as a boundary
> (`EffectVariableTests.OutOfScope_KeepsTodaysCalor0403_AndIsE3sToMove`) so it is observed rather
> than forgotten. Instantiation (§7.4) and the join are also E3's; slice a only records.

### 7.1 Why not `!e` — executed

Draft v1 chose `!e` by inspecting `ParseValue`. Three executed results kill it:

```
CASE X3   §E{!e}          → Calor0403: Unknown effect code '!e'.
CASE X3b  §E{alloc, !e}   → Calor0403: Unknown effect code '! e'.      ← lossy

CASE Z11  §O{str!str}     → parses through the parser and binder; the only diagnostic is
  Calor1002: Generated C# failed compilation (CS0029): Cannot implicitly convert type
  'Calor.Runtime.Result<object, string>' to 'Calor.Runtime.Result<string, string>'
  ← `str!str` really is read as Result<str,str>: `!` is the live fallible-type suffix

CASE Y2b  §B{r:i32!str}   → Compilation successful
  + warning Calor0200: Type 'i32!str' is not known to the Calor type checker.
```

(v2 paraphrased the Y2a line as "→ parses". Z11 is the same case with its verbatim output; the
Calor1002 makes the point *more* strongly, since only a type actually read as `Result<_,_>`
reaches C# codegen and fails there.)

`!` is already owned by the type grammar (`T!E`, documented at
`docs/syntax-reference/effects.md:140`), and X3b shows the attribute round-trip inserts a space,
so the sigil is not even reconstructed losslessly. Rejected on both grounds. (The consistency
lens predicted `§E{!e, alloc}` would die at `Expect(CloseBrace)`; it does not — it reaches
Calor0403. The conclusion stands, the mechanism differs; recorded in §15.)

### 7.2 Why `eff` + bare identifier — executed

```
CASE X5b  §E{e}                        → Calor0403: Unknown effect code 'e'.   ← exact, lossless
CASE X6a  §F{…}<T, U, eff e> (…)       → Calor0100: Expected Greater but found Identifier
CASE X6b  §F{…}<T, out U> (…)          → Calor0119: Type parameter variance is only legal on
                                          interfaces and delegates, not this declaration.
```

X5b: a bare identifier survives `ParsePositionalAttributes` → `ParseValue` → 
`InterpretEffectsAttributes` **byte-exact**, and lands on the one code (`Calor0403`) whose lookup
E2 extends. X6a: `eff e` is new syntax today.

X6b establishes a **weaker** precedent than v2 claimed, and the claim is softened accordingly:
`in`/`out` show that an identifier-shaped modifier can precede a type-parameter name in this
production (`Parser.cs:7612-7621`), but they are matched with **no lookahead** and, on a `§F`,
they are **rejected** (`Calor0119`, because `allowVariance: false`). So `eff` reuses the *shape*,
not a working code path: it needs its own branch, its own lookahead, and its own enablement
per declaration form. That is a real cost and §9 carries it.

**Exact changes.** (a) `ParseOptionalTypeParameterList` (`Parser.cs:7596-7639`) gains a branch
beside the variance branch: `Check(TokenKind.Identifier) && Current.Text == "eff" &&
Peek(1).Kind == TokenKind.Identifier` marks the next identifier an effect variable. The one-token
lookahead keeps a type parameter literally named `eff` working — **Z4** confirms `<eff>` compiles
today (`§F{f001:M:pub}<eff> (eff:x) -> void` → `Compilation successful`), so that is a real
compatibility obligation, not a hypothetical. (b)
`AttributeHelper.InterpretEffectsAttributes` (`:329`) resolves each code against the enclosing
declaration's in-scope effect-variable set **before** `EffectCodes.TryParseCompact`, so `§E{e}`
binds and an out-of-scope `§E{e}` raises **Calor0404**, not Calor0403. No lexer change, no new
token kind, no `IsKeyword` change.

**(c) An `eff` name may not collide with the effect taxonomy.** Resolving variables before codes
(step b) means a variable named after a live code would make that code unwritable inside the
declaration. Executed, the collision is reachable today:

```
CASE Z6   §F{f001:M:pub}<T, cw> (T:a, cw:b) -> void   → Compilation successful
CASE Z6b  §F{f001:M:pub}<T, fs> (T:a, fs:b) -> void   → Compilation successful
```

`<T, cw>` compiles as an ordinary type parameter today, so `<T, eff cw>` would silently shadow
console-write. **Decision: an `eff` name that appears in `EffectCodes.Registry` (any of the 31
entries, legacy included) or in `EffectCodes.ColonPrefixes` (`EffectTypes.cs:142-148`) is
**Calor0404** at the declaration.** Ordinary *type* parameters named `cw` keep working — the ban
is on `eff` names only, so Z6/Z6b stay green. P18 pins both polarities.

### 7.3 Scope (Calor0404) — a partition of the eight positions

v2's permitted/forbidden lists left positions 2 and 3 unmentioned. The complete partition:

| Position | Effect variable? | Why |
|---|---|---|
| **1** declaration's own row (`§F`/`§MT`) | **permitted** | this is where it is bound |
| **2** lambda literal (`§LAM`) | **forbidden** | a lambda has no type-parameter list to bind one — **Z8b**: `§LAM{lam1}<T>` → `Calor0100: Expected statement but found Less`. Its row is inferred or concrete |
| **3** delegate declaration (`§DEL`) | **forbidden** | `§DEL` has no type-parameter list at all — **Z8**: `§DEL{d001:Handler}<T>` → `Calor0100: Expected I, O, E, or END_DEL but found Less`. Giving delegates one is a generics change, deferred (`calor-direction.md:33`) |
| **4** parameter, tag form · **5** parameter, inline form | **permitted** | the binding site the solve reads |
| **6** return row | **forbidden** | a returned function mentioning the caller's variable is rank-2 |
| **7** binding · **8** field | **forbidden** | nothing binds a variable there |
| — inside a generic argument (`List<Func<i32,i32> §E{e}>`) | **forbidden** | types are strings in the parser (§3.1) |
| — **class / interface-level** (`§CL{…}<T, eff e>`, `§IFACE{…}<T, eff e>`) | **forbidden in E2 — CONFIRMED** | the cell v2 left blank while §7.4's middleware form used it — see below. **The spike chose member-level (§12.4), so this row does NOT flip and §9's seventh insertion point stays conditional at zero cost** |

**Class/interface-level `eff` is forbidden in E2, and the spike decided that it holds.** The
sequencing below ran as written: member-level was tried first, it expressed R2, and
class-level therefore does not ship. `after/A3-middleware-alpha.calr` is the executed proof
that `fits` identifies the interface's variable with the implementation's even when they are
spelled differently — the half **W1c did not settle**. Kept in the past tense below because it
is the record of what the spike was told to do.
The partition above must be total, because **R1 is recomputed by P27 and requires all four A3
fixtures to compile with zero Calor0404** — so a middleware fixture that binds `eff e` at
`§IFACE<…, eff e>` would fail R1 *by this document's own rule*, and the ramp would fire for a
reason that is an artefact of the doc rather than a fact about rank-1. Resolved by deciding the
cell and sequencing the spike:

1. **E2 forbids it.** A declaration-level binder is a generics change
   (`calor-direction.md:33`), and §9's seventh parser insertion point stays **conditional**.
2. **The spike tries the member-level spelling first** — `§MT{mt001:Handle:pub}<eff e> (…)`,
   which is **position 1 and already permitted**. **W1a** shows a member of an interface can
   carry its own type-parameter list today (`§MT{mt001:Handle}<T> (T:r) -> T` compiles), and
   **W1b** shows the implementing class member can too.
3. **Class-level becomes permitted only if the spike PR demonstrates member-level cannot express
   R2**, and at that point §9's seventh insertion point becomes **unconditional** and this table
   row flips — in the spike PR, with the evidence attached.

What W1 does *not* settle is whether `fits` would identify the interface's `e` with the
implementation's `e`. It is encouraging: **W1c** renames the member type parameter (`<T>` on the
interface, `<U>` on the implementation) and Calor0421 **still fires**, which means
`CheckEffectVariance` matched the two members alpha-equivalently — the same property
`StrictnessBatchTests.cs:172` pins for overrides. Encouraging is not proven, and the proof is
the spike's.

Seven rejection sites, each its own Calor0404 message; P18 covers all seven. A row may mix a variable
and concrete codes: `§E{cw, e}` denotes `Concrete({cw}) ⊔ e`.

### 7.4 Instantiation, and the combinator set

At a call site, `e := ⊔ { ρ(argⱼ) ⊖ ρ_declⱼ : parameter j's row mentions e }`, where `⊖` is
difference over the concrete part. Any `Unknown` contributor makes `e := Unknown` and the site
reports Calor0425. One variable, one solution, at the call site — that is what rank-1 means here,
and it is why no constraint solver is needed.

**`Map`** — BEFORE, executed shape **X10**: `Calor0418` at `§C{f}`; the module does not compile,
so there is no BEFORE for its callers. AFTER:

```text
§M{m001:After}
  §F{f001:Map:pub}<T, U, eff e> ([T]:xs, Func<T,U>:f §E{e}) -> [U]
    §E{e, alloc}
    §B{~out:[U]} §ARR{U} (len xs)
    §L{l1:i:0:(len xs):1}
      §IX{out i} = §C{f} §A §IX{xs i} §/C
    §R out
```

Called with a pure `Double`, `e := Concrete(∅)` and `Map`'s row is `{alloc}`. Called with an
`§E{cw}` `Announce`, `e := Concrete({cw})`, `Map`'s row is `{cw, alloc}`, and a caller declaring
only `§E{alloc}` gets `Calor0410: Function 'UseImpure' uses effect 'cw' but does not declare it`
plus the new provenance clause (§10.3).

**`Match`** — one variable across both arms, so `e` is their join: a pure `onNone` and a printing
`onSome` give `{cw}`.

```text
§M{m001:After}
  §F{f001:MatchOption:pub}<T, U, eff e> (?T:opt, Func<T,U>:onSome §E{e}, Func<U>:onNone §E{e}) -> U
    §E{e}
    §IF{if1} (is_some opt)
      §R §C{onSome} §A (unwrap opt) §/C
    §R §C{onNone} §/C
```

**Middleware / `next`** — R2's decisive case, restored here because v2's compression deleted it
and left §12's A3 pointing at a spelling the document no longer contained:

```text
§M{m001:After}
  §IFACE{i001:IPipelineBehavior}<TReq, TRes, eff e>
    §MT{mt001:Handle} (TReq:request, Func<TRes>:next §E{e}) -> TRes
      §E{e}

  §CL{c001:LoggingBehavior:IPipelineBehavior:pub}<TReq, TRes, eff e>
    §MT{mt001:Handle:pub} (TReq:request, Func<TRes>:next §E{e}) -> TRes
      §E{e, cw}
      §P "before"
      §R §C{next} §/C
```

**The spelling above binds `eff e` at the class/interface level, which §7.3 forbids in E2** — so
as written this fixture would fail R1 on Calor0404. That is a live open question, not an
oversight, and §7.3 sequences it: **the spike PR must decide A3's middleware spelling before
freezing A3**, trying the **member-level** form first —

```text
§IFACE{i001:IPipelineBehavior}<TReq, TRes>
  §MT{mt001:Handle}<eff e> (TReq:request, Func<TRes>:next §E{e}) -> TRes
    §E{e}
```

— which is **position 1 and already permitted**. **W1a**/**W1b** show a member of an interface
*and* of the implementing class can carry its own type-parameter list today; **W1c** shows
Calor0421 still fires when those parameters are renamed, so the interface↔implementation match is
already alpha-equivalent. Only if member-level provably cannot express R2 does the class-level
binder ship, and only then does **§9's seventh insertion point become unconditional**.

Either way R2 was, when this section was written, this document's most-likely ramp trigger (§14 Q1 — the spike has since answered it: the ramp did not fire): the implementation
declares `{e, cw}` against an interface row of `{e}`, which the ordinary `fits` relation must
reject as Calor0421, and the *corrected* program requires widening `IPipelineBehavior` — an
interface Calor does not own.

**Callbacks** need **no** effect variable at all: a `§FLD{Action<i32>:onChange} §E{cw}` (position
8) carries a concrete row.

```text
§M{m001:After}
  §CL{c001:Counter:pub}
    §FLD{Action<i32>:onChange:pri} §E{cw}
    §MT{mt001:Bump:pub} (i32:n) -> void
      §E{cw}
      §C{onChange} §A n §/C
```

So three of the four combinators need rank-1 and the fourth does not — if the ramp fires,
callbacks still work.

### 7.5 The exit ramp

Rank-1 is **validated** iff all three hold on the frozen artifacts of §12:

- **R1 — the four combinators type-check.** Each of `Map`, `Match`, middleware/`next` and
  callbacks, in its AFTER form, compiles with **zero Calor0424, zero Calor0425 and zero
  Calor0404**, without `--permissive-effects` and without any `§CSHARP` block, when every
  participating row is concrete and every callee resolves.
- **R2 — the interface/implementation interaction resolves with no carve-out.** In the MediatR
  shape, an implementation row `{e, cw}` against an interface row `{e}` must be rejected by the
  *ordinary* `fits` relation as Calor0421, and the corrected program accepted by it, with no
  rank-1-specific branch in `CheckEffectVariance`.
- **R3 — instantiation is decidable by §7.4's one-line solve** on all four, with no case needing a
  second variable, a constraint set, or a fixpoint.

Draft v1's criterion 2 (byte-identical emitted C#) is **removed from the ramp and promoted to a
feature-wide blocking gate, G-CODEGEN** (§12.2): a codegen diff is not a rank-1 question, and if
rows move codegen then E2 does not ship at all, monomorphic or not.

**If the ramp fires:** 0.15 ships monomorphic rows only; `Map`/`Match`/middleware still *compile*,
with `Unknown` callback rows producing Calor0425 and an Unknown charge — worse ergonomics than
today's Calor0418, better expressiveness, and the imprecision is named instead of the program
being rejected. **E3 loses site 6**, **gate 1's denominator drops from six classes to five**,
Calor0404 is not allocated, and the `eff` branch does not ship. The release notes state all four.

**R2 is the most likely trigger.** MediatR's `IPipelineBehavior` is *someone else's* interface: a
Calor implementation cannot widen it, so if the only spelling that type-checks requires editing
the interface, rank-1 rows do not compose with external interfaces. §14 Q1.

---

## 8. Decision 6 — Binder and representation

### 8.1 Recorded, not decided (E1)

Roadmap §4.1: *"`UnresolvedBoundType` → `Unknown` row, `FunctionBoundType`'s effect slot, and
symbol-identity keying are E1 decisions, made in §4.2, not design-doc decisions."* Status:
receiver-from-`BoundExpression.Type` and `_variableTypeMap` deletion **executed** (#1089);
receiver `BoundExpression` on the call nodes and binder-emitted `UnresolvedBoundType`
**executed** (#1095 — E1 slice 2a); the enforcement pass's string resolvers reading receivers
from the bound tree and `BoundLambdaExpression`'s `FunctionBoundType` **executed**
(PR #1099 — E1 slice 2b, below); symbol-identity keying in `EffectResolver`/manifests/IL
summaries **executed** (E1 slice 2c, below). Still **pending**: the `Unknown`-row contribution
(E2 — there is no `EffectRow` type yet, so an unresolved receiver contributes
`EffectSet.Unknown`, not an `Unknown` *row*).
**E2 consumes all six.** Separately, and not one of the six: the `Binder` → `Effects` layering
hole is closed by PR #1099 (§4.2 E1's `Binding/**` cleanliness pin, below).

**E1 exit pins (roadmap §4.2). All three are now MET, so E1 is complete.**
(a) the `_variableTypeMap` grep pin — **MET**,
`Calor.Enforcement.Tests/EffectsSuggestTests.cs:159`. (b) the original S6 behavioural criterion,
"a receiver whose type is available **only** through metadata (no AST type string anywhere in the
module) resolves its effects" — **MET by PR #1099**,
`EffectEnforcementTests.E1Slice2b_InferredLocalReceiverTypedOnlyByTheBinder_ChargesTheRealCallee`,
which fails on a clean `main` worktree and passes here. (c) no
`EffectResolver.Resolve(string, string, …)` overload remains — **MET by slice 2c**, pinned by
`ArchitectureTests.EffectResolver_ExposesNoStringTypeNameResolveOverload`
(`tests/Calor.Compiler.Tests/ArchitectureTests.cs`), a reflection pin rather than a grep: it
fails if ANY public `Resolve*` member on `EffectResolver` takes a `string`, wherever and however
that overload is re-added, and its positive half asserts the keyed
`Resolve(EffectResolverKey)` still exists so the pin cannot be satisfied by deleting resolution.

**What slice 2c did.** `EffectResolver` has ONE resolution entry point,
`Resolve(EffectResolverKey)`, and the key is the member's identity: declaring type (generic
DEFINITION plus arity for a `GenericInstantiationBoundType` — the spelling every committed
manifest uses; the instantiated `ILogger<Foo>` form matches no entry at all), member name,
parameter types, `EffectMemberKind`, plus provenance the lookup never reads (`IsStatic`,
`ReceiverInterfaces`, `FromStringFallback`). `ResolveExtension`, `ResolveGetter`, `ResolveSetter`
and `ResolveConstructor` are gone with it; `Kind` is the structural replacement for the
`"m:"`/`"g:"`/`"s:"`/`"c:"` cache-key prefixes, so the setter-vs-`set_X` collision those prefixes
worked around is now unrepresentable. Manifests are parsed into keys ONCE at load, and the four
per-type dictionaries collapse into one. `ILEffectAnalyzer.TryResolve` takes the key.
The six-step order and `"*"` wildcard semantics are unchanged, statement for statement.

- **The hardcoded Linq receiver list is now the documented FALLBACK, not the rule.**
  `IsCompatibleExtensionReceiver` asks `EffectResolverKey.ReceiverInterfaces` first — "does the
  receiver implement `IEnumerable`?", which is the real predicate the name-shape list was a
  proxy for. The list survives because bound types carry no interface set today (`TypeSymbol`
  has none; that is E2 work), so the binder has nothing to say at most call sites, and deleting
  the list would delete resolution — the mistake slice 2b measured and reverted. What the bound
  path answers structurally is the set the LANGUAGE guarantees: arrays, and the generic
  collection definitions the manifests already name.
- **Nothing moved, and that is measured, not asserted.** Gate 6's ledger (817/1248, aggregate and
  per subject) is byte-identical; the D-A demand ledger is unmoved at 3; the Calor0270 volume
  ledger is unmoved; `LosslessFormattingTests` is green; the P29 transcripts
  (`ExperimentTranscripts_MatchARerun`) are untouched — including `facts.py`'s
  `Effects/*.cs` file-count row, which is why `EffectResolverKey` lives inside
  `EffectResolver.cs` rather than in a file of its own.
- **Two residuals, named rather than absorbed into "complete"** (review round 1). First,
  `ILEffectAnalyzer.TryResolve` takes a key, but every key on the IL path is built by
  `FromStrings` from metadata TEXT (a `MethodKey`'s type name, member name, parameter
  signature) — so "IL summaries key on symbol identity" is not literally true, and the ledger
  counts those keys as string fallbacks, which is the honest answer. Second, the key's
  parameter component is inferred ARGUMENT types (§8.4), not the callee's resolved signature.
  The declaring type is symbol-derived; the parameter list is not. Both are E2's.
- **A new ledger, because the structural pin alone can be satisfied cosmetically.** An API can
  be keyed on symbol identity while every caller still funnels text through one factory. So
  `bench/phase0-agent-native/effect-resolver-key-ledger.json` records, per subject, how many
  keys the compiler builds from a bound receiver versus from
  `EffectResolverKey.FromStrings` — the single string-fallback factory, which stamps
  `FromStringFallback` on everything it produces. **At registration: 202 bound / 751 string**
  over 844 measured committed `.calr` (42 not measured and counted as such, never dropped);
  `bench` 176/551, `tests` 22/135, `samples` 1/36, `src` 3/26, `tools` 0/3, `benchmarks` 0/0,
  `scripts` 0/0. Exact per-subject equality, `[SkippableFact]`, compiler shard
  (`tests/Calor.Compiler.Tests/Effects/EffectResolverKeyLedgerTests.cs`). The numbers are a
  BASELINE, not a target: slice 2c re-keys the resolver, it does not widen what the binder can
  type, and §2.2's resolution ceiling is untouched. E2 is what moves them.

**What slice 2b did.** Unlike 2a, it **does** resolve receivers that did not resolve before —
measured against a clean `main` worktree (f7cd1c46), not asserted. Two of the four behavioural
pins added in `EffectEnforcementTests.cs` FAIL there and pass here:

- `E1Slice2b_InferredLocalReceiverTypedOnlyByTheBinder_ChargesTheRealCallee` — an inferred `§B`
  whose initializer is a BCL call (`§B{g} §C{System.Guid.NewGuid}`) has no type string anywhere
  in the AST. On `main` the following `g.ToString` is an unknown call and fail-closed produces
  `Calor0410: Function 'Go' uses effect 'unknown'`. Here the bound receiver is `System.Guid` and
  the manifest resolves it.
- `E1Slice2b_LocalShadowsFieldAndTheStringPathIsWrong_TheBoundTypeWins` — a local shadowing a
  field of a different type. The AST search misses the local and falls through to the FIELD, so
  the string path answers with the wrong scope's type; `main` emits `Calor0411` on `x.ToString`.
  The bound receiver is the local's real type.

The remaining two are equivalence pins and say so. **Ceiling unchanged:** gate 6's ledger
(817/1248, aggregate and per subject) is byte-identical, the D-A demand ledger is unmoved at 3,
the Calor0270 volume ledger is unmoved, and the P29 transcripts are untouched — the newly
resolved receivers are Calor-side, not new `MetadataBinder` resolutions.

- `CallGraphAnalysis.ResolveBoundCallSites` returns, per legacy caller id, the bound
  `BoundType` of the **receiver** of every call site, keyed by the receiver path as the target
  spells it. `ResolveLocalValueType` / `ResolveVariableType` / `ResolveReceiverChain`
  (`EEP:1719-1744` and its callers, pre-slice numbering) consult it first; the AST string
  searches survive as fallbacks, each with a comment naming the shape it covers.
- Fail-closed, scoped to **reported** unresolvedness. `UnresolvedBoundType` gains a `Reported`
  bit carrying #1095's existing marking-vs-reporting split (`ShouldReportUnresolvedReceiver`).
  Where the binder told the author it could not name the type (Calor0270), the pass ends the
  lookup with null and the call reaches `ReportUnknownCall` / `EffectSet.Unknown` rather than a
  guessed nominal type. Where it marked silently — member chains, converter-synthesized
  `_chainNNN` temporaries — the AST fallback still decides.

  **This scoping is measured, not stylistic.** An unconditional veto was implemented first and
  deleted resolution the fallback still performs: `tests/Calor.Conversion.Tests/Snapshots/
  05-02.approved.calr` and `05-03` went from clean to `Calor0411` + `Calor0410` on
  `_chainWhere005.ToList`, failing `LosslessFormattingTests` (which passes on `main`). The
  `Reported` bit is the discriminator #1095 already computes, so no new judgement was invented.

  **The veto branch is CORPUS-UNREACHABLE BUT OBSERVABLE** — correcting round 1, which called it
  unreachable and claimed deleting it changed no diagnostic. That was false. It is pinned by
  `EffectEnforcementTests.E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel`, which fails
  when the branch is deleted, with an explicit control
  (`_SameBindingWithoutAReceiverUse_StillTakesTheAstSentinel`).

  The reachability path is the **name-keyed side channel** (§8.1 below, and
  `CallGraphAnalysis.BoundValueTypes`): a name used as a receiver anywhere in a function answers
  from the channel at *every* occurrence, including positions the channel never collects. A name
  that is both a receiver and a bare call target therefore carries its `Reported`
  `UnresolvedBoundType` into `InferFromBareNameTarget`:

  ```
  §B{u} §C{Mystery.Make} §/C
  §C{u.Run} §/C     ← receiver use: records u's Reported UnresolvedBoundType
  §C{u} §/C         ← bare target: reads it back
  ```

  Without the veto the bare target falls through to the AST search, which returns the **sentinel**
  `"?"` for a `§B` it cannot type. `InferFromBareNameTarget` tests `!= null`, not the sentinel, so
  `"?"` is treated as a type and the call takes the delegate-invocation arm — `Calor0418
  "declared type '?'"`, charging `EffectSet.Empty`. Measured: `0411, 0411, 0418, 0410` without the
  branch versus `0411, 0411, 0411, 0410` with it. Guessing there launders effects, so the veto is
  load-bearing.

  **Corpus claim, and only that:** over all 301 committed `.calr` files every unresolved receiver
  arriving at the resolver is `Reported=false` — 32 sites, all `_chainNNN` or member chains, zero
  `Reported=true`. That is why the ledgers and transcripts are unmoved. It is **not** evidence
  that the branch is unobservable, which is the inference round 1 got wrong.

  **Slice-2c debt — RESOLVED, and here is what it cost.** The debt was: `ResolveVariableType`
  guards `declared == "?"`, the other `ResolveLocalValueType` call sites do not. Slice 2c
  adds the guard at the ONE site where the sentinel was a decision rather than a formatting
  detail — `InferFromBareNameTarget`, named `UnknownLocalTypeSentinel` — so a bare `§C{u}` on a
  `§B{u}` the pass cannot type is **Calor0411 whether or not `u` was also used as a receiver**.
  It used to be Calor0418 `"declared type '?'"` in the no-receiver case, charging
  `EffectSet.Empty` on a value with no type at all, which is laundering.

  **The corpus delta is ZERO, measured.** The D-A demand ledger is an exact-equality pin over
  all 886 committed `.calr` and counts Calor0418 firings; it is unmoved at 3 (0418 = 1,
  0419-function-typed = 2). A single 0418→0411 conversion anywhere in the corpus would have
  decremented it. So the shape does not occur in committed Calor, and the change is observable
  only through the fixtures that pin it.

  **A blanket removal was rejected on a code-path argument, not a preference.** Making
  `FindLocalDeclarationType` return `null` instead of the sentinel changes what
  `ResolveLocalValueType` hands to `InferFromReference` and `InferSetterEffects`, both of which
  branch `receiverType == null → EffectSet.Empty`. An untyped receiver's property read would go
  from a REPORTED unknown operation to silence — a fail-OPEN change, the exact opposite of what
  this slice is for. The sentinel therefore survives on the paths where it still carries
  information ("there is a value here, of unknown type"), and is named rather than left as a
  bare `"?"`.

  **What it cost: `_VetoesTheAstSentinel` is no longer a discriminating pin.** The guard
  subsumes the veto for that fixture, so the test now passes even with the veto branch deleted.
  The veto is retained anyway — it states the fail-closed rule at the layer that owns it
  (`AskBoundTree`), and E2 needs it there the moment chains carry types — but it is a
  behavioural pin, not a discriminating one, and this document says so rather than leaving the
  reader to assume otherwise. Slice 2b's control test is re-specified accordingly as
  `E1Slice2c_BareCallOnUnknownTypedBinding_IsCalor0411WithOrWithoutAReceiverUse`, which runs
  both fixtures and asserts they agree.

  **`ChainWalkCouldChargeEffects` is untested, and its `FIXME(E2)` now says so.** Its only
  caller is `ResolveReceiverChain`'s bound-type shortcut, which is unreachable because slice 2a
  types every member chain `UnresolvedBoundType`. Deleting its body would fail no test. E2 must
  land a pin — a chain the binder types, an effectful getter partway along it, the effect
  asserted as charged — **before** chain typing merges, because the day the shortcut goes live a
  wrong answer there silently under-charges.
- **Receivers only.** An earlier revision recorded every bound name; that made the side channel
  answer in non-receiver positions and regressed the method-group-argument charging arm
  (`StrictnessBatchTests` C2/C4). Names outside receiver positions keep resolving through the
  AST, because the string the pass gets back is quoted verbatim in Calor0418's message.
- `BoundLambdaExpression.Type` is a `FunctionBoundType` carrying real parameter and return
  `BoundType`s. Its `DisplayString` is deliberately **unchanged** (`LAMBDA(i32)->INT`):
  `Binder.cs:1320` infers an untyped `§B`'s `TypeName` from the initializer's `DisplayString`, so
  the lambda's string escapes into other expressions' types, the verifier cache and the LSP
  call-graph key. `FunctionBoundType` gains an optional `displayOverride` for exactly that;
  §8.3's canonical `(p1, p2) -> ret` stays the default for every other construction, and
  `BoundTypeTests.cs:139`/`:150` are untouched.
- Function-typedness is asked of the bound type first (`EffectEnforcementPass.IsFunctionBoundType`
  — a `FunctionBoundType`, or a `NominalBoundType` whose declaration is a `§DEL`, marked by the
  new `TypeSymbol.IsDelegate`). The prefix-string test survives as the fallback for types that
  reach a consumer only as text, which is what keeps Calor0418 byte-stable.
- `MapShortTypeNameToFullName` and `IsTypeQualifiedReference` moved to `Binding/TypeIdentity`
  (`Binding/Scope.cs`), with forwarders left in `Effects/`. `Binding/` no longer references
  `Effects/`, pinned by `ArchitectureTests.BindingLayer_HasNoReferenceToEffectsNamespace` — the
  existing `compiler-components.json` contract matched only the fully-qualified spelling and was
  blind to the namespace-relative `Effects.EffectEnforcementPass` reference in `Binder.cs`.

**What slice 2a did and did not do — stated so E2 does not over-read it.** The slice moved a
decision and added structure. It resolved **no** receiver that did not resolve before.

- `BoundCallStatement.Receiver` / `BoundCallExpression.Receiver` carry the receiver as a real
  `BoundExpression` in four shapes (bound variable, member-access chain, `BoundTypeReferenceExpression`,
  or null), and `ExternalCallCollector` reads `Receiver.Type` instead of reconstructing one.
- The binder emits `UnresolvedBoundType(reason)` — the first production emission of that type —
  where it previously handed back `NominalBoundType("OBJECT")`, so "unresolved" and "genuinely
  `object`" stop being the same value.
- `BuildCallReceiver` consumes **exactly the three inputs slice 1 consumed** (`ReceiverSymbol`,
  `ReceiverTypeSymbol`, `ResolvedTypeName`). It consults no `MetadataBinder`, opens no new
  resolution path, and reaches no type source slice 1 could not reach.
- Evidence: `calor effects suggest --json` over a fixture exercising all seven receiver shapes is
  **byte-identical to `main`** apart from the `generatedAt` timestamp, stderr included; gate 6's
  ledger (817/1248) is unmoved; `EffectsSuggestTests.cs`'s metadata-only pin
  (`g.ToString → System.Guid`) passed on `main` before this slice and still passes.

So slice 1's "step 1 provably reduces to `ReceiverSymbol.TypeName`" is **narrowed, not falsified**:
the reduction still holds for the bound-variable shape today, but it is no longer a property of the
*collector* — the binder owns the decision, and the collector can no longer reconstruct a different
answer. That is what E2 gets to build on: a receiver whose unresolvedness is a typed fact
(`UnresolvedBoundType`) rather than a string sentinel the consumer had to re-derive, which is what
**P17** (§13) needs in order to be writable at all. What E2 does **not** get is any additional
resolved receiver — the resolution ceiling in §2.2 (431 unresolved BCL call sites) is untouched.

**The spike did not discharge any of the six, and says so.** Its prototype reads rows off the
**AST**, because the effect pass is an AST walk (§2) and the bound tree is not on that path. So
§8.2's `FunctionBoundType.Row` / `ParameterRows` is **still owed by E2** — the spike is evidence
that rank-1 rows *type-check* and *erase at codegen*, not that the representation work is done.

### 8.2 `FunctionBoundType`

> **THE BOUND TYPE HAS A PRODUCTION READER — E3 slice b, PR #1106.**
> `RowSiteChecker.IsFunctionTyped` now asks the BOUND answer before the string test:
> `CallGraphAnalysis` exposes `DeclaredFunctionTypes` / `DeclaredReturnFunctionType` /
> `DeclaredFieldFunctionType`, collected from the same `Bind()` call that already resolves the
> call sites, and `VariableSymbol.FunctionType` and `FunctionSymbol.ReturnFunctionType` gain
> their first readers in `src/`. It is load-bearing rather than decorative: a `§CSHARP`-declared
> delegate parameter — **the A2 shape** — is not recognised by
> `TypeIdentity.IsFunctionTypeName`, so slice a MISSED that site entirely. Measured, not claimed:
> the same source draws zero Calor0424 at `9119397e` and one here. The string test remains as the
> documented fallback for positions the binder has no symbol for.
>
> **LANDED — E2 slice b, PR #1102.** `Row` and `ParameterRows` exist, default to
> `EffectRow.Unknown`, are part of `Equals`/`GetHashCode`, and are absent from `DisplayString`.
> `ParameterRows` is length-aligned to `ParameterTypes` by construction. Producers today:
> `BoundLambdaExpression` (the `§LAM`'s declared row, §5), `VariableSymbol.FunctionType` (a rowed
> parameter, field or `§B`, plus §3.5's inference from a function-typed initializer) and
> `FunctionSymbol.ReturnFunctionType` (position 6). `displayOverride` is retained and extended:
> a rowed declared position keeps **the type name the binder already holds for it** rather than
> §8.3's canonical `(p1, p2) -> ret`, for the same byte-identity reason lambdas keep
> `LAMBDA(i32)->INT`.
>
> **That name is the parser-EXPANDED spelling for the BLOCK forms and the raw source text for the INLINE forms.**
> `ExpandType` rewrites `Func<i32,i32>` to `Func<INT, INT>` before it reaches `ParameterNode.TypeName` /
> `OutputNode.TypeName` in the block forms (`§I{Func<i32,i32>:cb} §E{cw}`, `§O{Func<i32,i32>} §E{fs:w}`
> both display `Func<INT, INT>`), but the inline signature `(Func<i32,i32>:g §E{cw})` and the arrow form
> `-> Func<i32,i32> §E{fs:w}` keep the raw `Func<i32,i32>` (measured in review round 2 of PR #1102, R2-B).
> The split is block-vs-inline, not parameter-vs-return. Two earlier revisions of this note were wrong
> in different ways ("keeps its SURFACE spelling"; then "both positions expand"). The expanded name is the right
> string to use: it is exactly what `VariableSymbol.TypeName` and every existing consumer of these
> positions already carry, so nothing moves. Reaching for the raw source text would be the change
> with a blast radius.
>
> **Carry-over 1 (Equals vs DisplayString) is decided: display does NOT participate in equality.**
> `Equals` is shape + rows; `DisplayString` stays a diagnostic artifact. Two structurally
> identical function types that print differently are still equal, and a cache keyed on
> `DisplayString` is therefore coarser than the type, not finer — which is the safe direction.
> Unifying the spelling would move `BoundTypeTests.cs:139`/`:150`, the corpus golden and the LSP
> call-graph key for zero behavioural gain.
>
> **Carry-over 2 (raw lambda parameter spellings) is NOT taken in slice b, and the reason is
> measured, not stylistic.** Canonicalising them changes `FunctionBoundType.Equals` for lambdas
> without changing anything that reads the result, so it is a change with a blast radius and no
> observer. Slice b leaves the spellings raw and hands the normalisation to E3, which is the
> slice that first compares two function types for assignability and therefore the first slice
> that can pin the difference.
>
> **What slice b deliberately does NOT do:** it does not give every function-typed position a
> `FunctionBoundType`. Only rowed positions (and a `§B` inferring from one) get one. Doing it
> unconditionally would make `EffectEnforcementPass.IsFunctionBoundType` answer true where it
> answers false today, moving Calor0418's behaviour on programs that contain no rows at all —
> the opposite of a slice whose corpus delta is zero. E3 widens it when it has a checking site
> to serve.

```csharp
// extends Binding/BoundTypes/BoundType.cs:212-241
public ImmutableArray<BoundType> ParameterTypes { get; }
public ImmutableArray<EffectRow>  ParameterRows  { get; }   // NEW — required by §4.6
public BoundType ReturnType { get; }
public EffectRow  Row        { get; }                        // NEW — the callee's own row
```

Both default to `EffectRow.Unknown` when the source omits them, **never** to pure. `Equals` and
`GetHashCode` (`:228-231`, `:233-241`) include the rows: two function types differing only in row
are different types, which is the claim of TIER2D. Note `Equals` is *equality* and `fits` is
*assignability* — deliberately different relations.

**Two E1-slice-2b carry-overs E2 must decide (review round 1, findings 7 and 9).**

1. **`Equals` is shape-only while `DisplayString` can differ.** Slice 2b gave
   `FunctionBoundType` an optional `displayOverride` (§8.3) so lambdas keep their
   `LAMBDA(i32)->INT` spelling. `Equals` compares `ParameterTypes` and `ReturnType` only, so a
   lambda's type and a structurally identical `(i32) -> INT` are **equal but print differently**.
   That is deliberate today — printing is a diagnostic decision, not type identity — but once
   rows join `Equals`, E2 should decide explicitly whether display participates too, because a
   cache keyed on `DisplayString` and a set keyed on `Equals` would then disagree about the same
   two types.
2. **A lambda's `ParameterTypes` are surface spellings, its `ReturnType` is bound.** The
   parameters are `NominalBoundType(parameter.TypeName)` — the source's own `i32`, `str` — while
   an expression lambda's return is the body's real bound type (`INT`). So one
   `FunctionBoundType` can mix vocabularies. Slice 2b did not normalise, because the parameter
   spellings feed the `DisplayString` that must stay byte-identical. **Pending for E2:**
   normalise parameters through the binder (not by canonicalising the string), keeping the
   display string as a separate, frozen artifact.

Both are recorded rather than fixed because fixing either moves `DisplayString`, which §8.3 and
the corpus golden pin.

### 8.3 `DisplayString` — rows do not appear

> **Decision.** `FunctionBoundType.DisplayString` stays `"(p1, p2) -> ret"`
> (`BoundType.cs:224-225`). A separate `RowDisplayString` carries the row for diagnostics and
> hover.
>
> **Exception, added by E1 slice 2b (PR #1099).** The constructor takes an optional
> `displayOverride`, used by exactly one caller: `BoundLambdaExpression`, which passes the
> pre-slice `LAMBDA(i32)->INT` / `ASYNC_LAMBDA(…)->…` spelling. Every other construction still
> gets `"(p1, p2) -> ret"`, so `BoundTypeTests.cs:139`/`:150` are untouched. The exception exists
> because a lambda's display string is **not private to lambdas**: `Binder.cs:1320` infers an
> untyped `§B`'s `TypeName` from the initializer's `DisplayString`, so changing it would move the
> display string of `BoundVariableExpression`s — the byte-identity this section exists to
> protect. E2 decides whether to unify the spelling when rows land (§8.2, carry-over 1).

This is already enforced by **existing exact-equality pins** — 
`tests/Calor.Compiler.Tests/Binding/BoundTypes/BoundTypeTests.cs:139`
(`Assert.Equal("() -> VOID", t.DisplayString)`) and `:150`
(`Assert.Equal("(INT, STRING) -> BOOL", t.DisplayString)`) — which Draft v1 claimed did not
exist. Appending a row breaks both without any new test.

The byte-identity discipline matters because consumers compare `DisplayString` to strings:
`src/Calor.LanguageServer/Utilities/SymbolFinder.cs:173`, `:246`;
`State/WorkspaceState.cs:1137`, `:1377` (which builds a call-graph key), `:2542`. Beyond the LSP
it is read 34× in `Analysis/BugPatterns/TypedBugPatternAnalysis.cs`, 27× in `Binding/Binder.cs`
(incl. `:210`), 9× in `Binding/Metadata/MetadataBinder.cs`, and in **24 test files**. Putting rows
in `DisplayString` would touch all of them and change `WorkspaceState.cs:1377`'s key format — an
index-visible change gate 3 would surface on every edit script. Declined.
`EffectRow.ToDisplayString()` extends `EffectSet.ToDisplayString()` (`EffectSet.cs:172-183`,
already `[unknown]`/`[pure]`/`"cw, fs:w"`) with `[assumed: cw]`.

### 8.4 Manifests and the resolution ceiling

A manifest resolution yields an `EffectRow`: `EffectResolutionStatus.Resolved` →
`Concrete(S)`, `PureExplicit` → `Concrete(∅)`, `Unknown` → `EffectRow.Unknown`
(the three-valued `EffectResolutionStatus`, unchanged by slice 2c). A BCL method that
**returns** a delegate yields `EffectRow.Unknown` on that return and is a frozen gate-1
residual (roadmap §4.2 DEFERRED); the manifest schema gains no row-on-return field in 0.15.
With 431 of 1248 BCL call sites unresolved, roughly a third will produce Unknown rows —
§13.4 registers the ledger that counts it.

**What a manifest resolution is keyed on, since E1 slice 2c.** The subject of a lookup is an
`EffectResolverKey`, not a `(type, method, parameters)` string triple: declaring type in the
manifest's own spelling (generic definition plus arity), member name, parameter types, and an
`EffectMemberKind` that separates methods, extension methods, getters, setters and
constructors. Manifest entries are parsed into keys once at load, so a lookup is a dictionary
hit on an identity rather than a signature string rebuilt per call site. Two consequences for
E2. First, `EffectRow` attaches to the key's answer, and the key is stable across the AST
spellings of one call — which is what lets a row be cached and compared rather than
re-derived. Second, the manifest schema is unchanged: keys are a lookup-side refactor, and no
committed manifest was edited to land them.

> **The key's parameter component is weaker than the rest of it, and E2 should not assume
> otherwise** (review round 1, MAJOR 3). On a manifest entry the parameter list is the DECLARED
> signature. On a **call-site** key it is the inferred types of the **arguments**
> (`EffectEnforcementPass.InferExpressionType` — bound where the receiver side channel typed the
> name, AST-derived otherwise), not the callee's resolved parameter types. The binder's
> `BclCallResolution` is private to `Binder` and unreferenced under `Effects/`, and the effect
> pass is an AST walk that never holds a `BoundCallExpression`, so the callee's real signature is
> not on this path at all. Overload discrimination is therefore exactly as good as it was before
> slice 2c — re-keying preserved it rather than improving it. Making the declaring type
> symbol-derived while the parameter list stays inference-derived is a deliberate asymmetry, and
> it is the second of E1's two named residuals (roadmap §4.2).

**The key also records what the manifest cannot.** `IsStatic`, `ReceiverInterfaces` and
`FromStringFallback` sit outside key equality on purpose — no manifest entry names them, so
letting them split the cache would let a bound-receiver key and a string-fallback key for one
member disagree. They are provenance: `ReceiverInterfaces` is what
`IsCompatibleExtensionReceiver` consults before falling back to its name-shape list, and
`FromStringFallback` is what the key ledger (§8.1) counts. E2 widens the first — bound types
carrying interface sets — and should move the second.

### 8.5 `EffectSummary` — a projection, index-independent

> **Decision.** Neither "derived from the index" (Draft v1) nor "migrated into it". `EffectSummary`
> becomes an **in-process projection of the compilation's own symbol-keyed effect facts, written
> into the build cache**, and `ProjectIndex`'s effects facet is a **second consumer of the same
> projection**. One producer, two consumers, no dependency either way.

Draft v1's reasoning refuted its own conclusion. Measured: `ProjectIndex` is referenced from
exactly `Commands/IndexCommand.cs`, `Commands/QueryCommand.cs` and
`Indexing/ProjectIndexBuilder.cs` — **nothing under `Effects/` or `Incremental/`**. Deriving the
summary from the index would make `calor build` depend on `calor index build`, which no fresh
clone runs. §13.2 pins that with a fresh-clone `calor build` (no `obj/calor` present).

The E5 structural pin still lands: `EffectSummaryBuilder`'s name keys (`:68`, `:75`) are deleted
because the projection is symbol-keyed. `BuildStateCache.CurrentFormatVersion` moves `"3.0"` →
`"4.0"` (`:121`) since `BuildFileEntry.EffectSummary`'s shape changes — one cold rebuild on first
0.15 build, which is the mechanism's design (`:676-678`). `CurrentCompilerSemanticsVersion`
(`:122`) does **not** change (G-CODEGEN, §12.2). **`ProjectIndex.CurrentFormatVersion` also moves
`"3.0"` → `"4.0"`** (`Indexing/ProjectIndex.cs:145`): E5 adds a facet, gate 3's instrument
compares serialized index bytes, and an unversioned facet addition changes those bytes silently.
Draft v1 missed this.

### 8.6 The effects facet (E5)

`calor query effects <symbol>` joins the six existing facets
(`Commands/QueryCommand.cs:26-34`), answering with the declared row, the inferred row, the verdict
between them, and the assumption reasons when the row is `Assumed`. `QueryGoldenTests.cs:134`
throws on an unknown facet, so the E5 PR must add the arm or its golden cannot land.
Blast radius reuses `impact`'s transitive-caller closure (`ProjectIndex.cs:372-408`) unchanged.

> **EXECUTED — v0.15 E5** (`docs/plans/2026-08-27-v0.15-e5-notes.md`). The facet is
> `ProjectIndex.EffectRows` — one `IndexedEffectRow` per declaration (function, method,
> constructor, accessor) and per rowed parameter/return position, keyed by symbol id. The
> declaration-level fact is a projection of the enforcement pass's own result: a new phase 5
> (`EffectEnforcementPass.ProjectDeclarationFacts` → `DeclarationFacts`, keyed by the pass's
> structural function id) records the declared row, the inferred row (Concrete / Assumed with
> the D-W2.3 reasons / Unknown), the verdict phase 4 reached, the code it reports, and the
> undeclared codes. Cross-module callees are folded in through the cross-module pass's own
> resolution (`CrossModuleEffectEnforcementPass.ResolveCrossModuleEffects`, a projection of
> `CheckCaller`), on the same `EffectRow.Fits` — so `calor query effects Run` says what
> `calor build` says. Nothing re-infers. `calor query effects <name>` prints declared /
> inferred / verdict (with the code and the undeclared codes) and the assumption reasons
> when Assumed, then the position rows the declaration owns; `--json` wraps the same rows in
> the v1.1 envelope. `calor query impact <name> --effects [--row cw,fs:w]` joins the
> unchanged closure with the verdict of fitting the hypothetical row into each affected
> caller's DECLARED row. Two things this section did not foresee: the index recorded call
> edges for call EXPRESSIONS only, so `callers`/`impact` were blind to `§C{Log} §A x §/C` on
> its own line — fixed (statement calls are edges now; occurrences untouched); and the
> position rows give `FunctionBoundType.Row` its first production reader (`BoundRow`),
> pinned to agree with the `§E` node wherever the row mentions no `eff` variable.

---

## 9. Priced blast radius

Every row measured at `82338e37`; the command is named where it is not a plain `grep -c`.

| Bucket | Files | Evidence / note |
|---|---|---|
| `IAstVisitor` interfaces + 5 implementers | **0 forced** | 184 methods each (`grep -c "^    void Visit"` / `"^    T Visit"` on `Ast/AstNode.cs`; interfaces `:59`, `:247`); implementers `Ids/IdScanner.cs:9`, `CodeGen/CSharpEmitter.cs:88`, `Migration/CalorEmitter.cs:12`, `Verification/ExpressionSimplifier.cs:13`, `LanguageServer/Utilities/AstPositionVisitor.cs:10`. Rows add **no node type**. Counterfactual for a new node kind: both interfaces (one file, `Ast/AstNode.cs`) + 5 implementers = **6 files**, ×2 methods, plus CLAUDE.md's seven-step checklist |
| …but `CalorEmitter.cs` **does** change | **1** | round-trip fidelity: `calor fmt` and the harness must re-emit the row (§13.2 pins parse→emit→parse per position) |
| AST node classes | **4** | `ParameterNode` (`Ast/FunctionNode.cs:252`), `OutputNode` (`Ast/FunctionNode.cs:21`), `BindStatementNode` (`Ast/ControlFlowNodes.cs:161`), `ClassFieldNode` (`Ast/ClassNodes.cs:554`) |
| **`eng/ast-schema.json`** | **1** | forced by `tests/Calor.Compiler.Tests/ArchitectureTests.cs:158` `AstSchema_CoversEveryNodeDispatchAndChildRelation`; **this is also the existing "zero visitor churn" pin** — Draft v1 counted 0 here |
| Parser | **1** | `Parsing/Parser.cs`: **6** row insertion points covering all eight positions (§3.3), each also carrying the Calor0405 recovery; **1** `eff` branch in `ParseOptionalTypeParameterList` (`:7596-7639`) with its own lookahead and per-declaration-form enablement (§7.2 — `in`/`out` are *not* a working precedent, only a shape one); **+0 — RESOLVED BY THE SPIKE.** The seventh insertion point (a binder in the `§CL`/`§IFACE` type-parameter lists, plus scope threading into member row resolution) was contingent on the spike proving that member-level `§MT{…}<eff e>` **cannot** express R2. It can (§12.4), so this line is **zero**: position 1 was already permitted and needed no new insertion point. `§9`'s parser cost is therefore **6 insertion points + 1 `eff` branch**, full stop |
| Lexer / `Token.cs` | **0** | no new token kind, no `IsKeyword` change (§7.2) |
| Effects subsystem | **10 existing + 1 new** | `ls src/Calor.Compiler/Effects/*.cs` → 10, all touched (incl. both `CrossModuleEffect*.cs`, §6.2), plus new `EffectRow.cs` |
| Binder | **2** | `Binding/BoundTypes/BoundType.cs`, `Binding/BoundNodes.cs` |
| Build cache | **1** | `Incremental/BuildStateCache.cs:121` `"3.0"`→`"4.0"` |
| Index / query | **2** | `Indexing/ProjectIndex.cs:145` `"3.0"`→`"4.0"`, `Commands/QueryCommand.cs` |
| Diagnostics | **1** | `Diagnostics/Diagnostic.cs`: Calor0404, **0405**, 0424, 0425 |
| Harness + its pin | **2 + 1** | `docs/design/spikes/effect-rows/experiments/` (6 scripts + 6 transcripts + `regenerate-transcripts.py` + `o53/baseline.json`) and `tests/Calor.Compiler.Tests/Effects/EffectRowExperimentHarnessTests.cs`. **Already landed in this PR** — the evidence base is observed, not merely reproducible |
| `.calr` goldens under `tests/TestData` | **0** | `grep -rlE 'Func<\|Action<\|Action[}:]\|Predicate<\|§DEL\|§LAM' tests/TestData --include='*.calr'` → **0 of 359**. Draft v1 said "≤8" from a whole-corpus grep. *E5: 1 of 359 — `QueryCorpus/project/app.calr`, authored with a row on purpose (gate 7's polymorphic golden); `facts.txt` records the count as 1* |
| `.cs` goldens under `tests/TestData` | **0** | 391 exist; rows are erased at codegen — **G-CODEGEN** (§12.2) makes that blocking, not assumed |
| Conversion snapshots | **0 texts, 0 assertions** | 57 `.calr`; **`Calor.Conversion.Tests` never runs the effect pass** — `TestHelpers.cs:40-70` is Lexer→Parser→`CSharpEmitter`, no binder, no effect pass — so no 0419/0425 assertion exists to move. Draft v1 claimed 2. The **7** snapshots holding function-typed shapes (`05-02`, `05-03`, `07-01`…`07-04`, `13-03`) change diagnostics only where the **demand ledger** compiles them (§13.3) |
| Committed `.calr` whose meaning changes | **0** | §3.2's table, executed |
| `DisplayString` consumers | **0** | decision §8.3; the two existing exact-equality pins stay green |
| Round-trip harness | **0 expectations, 1 unmeasured risk** | `tools/Calor.RoundTrip.Harness/ProjectConfigs.cs:37` (5 subjects); converter emits no rows (§9 note below), so converted Calor is byte-stable. Calor0425 volume on converted code is §13.4's ledger |
| LSP | **1 (SHOULD)** | `Handlers/HoverHandler.cs` showing `RowDisplayString` |
| Docs | **2 MUST**, ~4 SHOULD of 31 | `docs/syntax-reference/effects.md` (`:36-51` the `§E` section, `:44-51` the two-line canonical layout that §3 now makes normative, the effect-code table for §4.1) and `structure-tags.md` (`:170-176`). `calor self-check docs` mechanically enforces the registry leg (`EffectTypes.cs:134-135`) but **not** §4.1's `Subtypes` change, which has no doc-drift check — §14 Q3 |
| Website | ~2 of 41 | separate PR |
| Tests referencing `DelegateInvocation` | **3 files** | `StrictnessBatchTests.cs`, `EffectEnforcementTests.cs`, `Effects/HigherOrderDemandLedgerTests.cs` |
| Benchmarks corpus | **226** `.calr` (17 with `§E{`) | pin `BulkBenchmarkCompilationTests.cs:171` `entries.Count >= 200`. Not 217 (§14.1) |

**Converter posture (sub-decision).** The C#→Calor converter emits **no** rows in 0.15: a
delegate-typed C# parameter converts with no `§E`, i.e. an Unknown row. Emitting rows would need
Roslyn-side effect inference over the whole 3121-site D-B surface, a campaign roadmap §3.4
declines. This is what keeps all 57 snapshot texts byte-stable.

---

## 10. Worked examples

`calor-direction.md:57` asks for intra-module, cross-module, and generic effect polymorphism.

### 10.1 Intra-module — a callback field

BEFORE, executed (**X13**, verbatim):

```
X13.calr(6,7): error Calor0418: Invocation of function-typed value 'onChange' (type
'Action<i32>') is an error under effect enforcement: …
```

Binder symbols BEFORE: `onChange` is a `VariableSymbol` whose `TypeName` is the string
`"Action<i32>"`; its `BoundType` is `NominalBoundType("Action<i32>")` — nothing builds a
`FunctionBoundType` for a field. The effect pass never sees a bound symbol at all: it finds the
field by **name** on the owner class (`EEP:1738-1741`) and string-matches `Action<`
(`:1946`).

AFTER — `§FLD{Action<i32>:onChange:pri} §E{cw}` (position 8; **X9b** shows this does not parse
today) and `§E{cw}` on `Bump`. `onChange`'s `BoundType` becomes
`FunctionBoundType([i32], void, ParameterRows: [Unknown], Row: Concrete({cw}))`;
`DisplayString` is `"(i32) -> void"` — unchanged per §8.3 — and `RowDisplayString` is `"cw"`. No
diagnostic.

With `§E{}` on `Bump`, the error the author wants:

```
Calor0410: Method 'Counter.Bump' uses effect 'cw' but does not declare it
  Effect row: charged by invoking 'onChange' (row: [cw])
```

The second line is **new** (today's Calor0410 carries only an optional `Call chain:` line,
**X12b**) and is the payload of the design: today the compiler can say *you invoked something*;
after rows it says *you invoked something that prints*. §13.2 pins that text.

> **LANDED — v0.15 E4.** The shipped string, pinned by FULL equality (P22,
> `StrictnessBatchTests.MessageTexts_Calor0410_InvocationProvenance_IsTheDesignDocSample`):
>
> ```
> Function 'Bump' uses effect 'cw' but does not declare it
>   Effect row: charged by invoking 'onChange' (row: cw)
> ```
>
> Two corrections to the sample above, both to the emitter and not the reverse. (1) `(row: cw)`,
> not `(row: [cw])` — §8.3 froze the compact spelling as `EffectSet.ToDisplayString()`'s, where a
> concrete non-empty row is bare and brackets mark the three shapes; §6.4's F6 correction already
> moved the 0424/0425 samples to it. (2) `Function 'Bump'`, not `Method 'Counter.Bump'` — the
> Calor0410 head is the X12b text this section itself calls the real one, and it names the
> callable by its own name. X9b's `.calr` now compiles to `exit 0` (obligation #2, §13.5).
>
> **The string E4 adds that this section did not spell — Calor0425 at an invocation whose row
> is Unknown**, pinned by full equality (`MessageTexts_Calor0425_AtInvocation_NamesTheValueTheCauseAndTheWaiver`):
>
> ```
> Calor0425: Invocation of 'transform' in 'Apply' cannot be charged: its effect row is
> Unknown (parameter 'transform' of 'Apply' (type 'Func<i32,i32>') carries no effect
> row). Add §E{…} on the same line as the type to state what 'transform' may do, or
> compile with --permissive-effects. 'Apply' is charged Unknown.
> ```
>
> The parenthetical names the DECLARATION whose row is missing (parameter / field / binding, or
> "returns a function type with no effect row" / "is not visible to the effect pass" for a
> value produced by a call), which is what §13.4's schema-2 ledger buckets on. The last
> sentence is literal: under the default policy the Unknown row is **charged** — the same
> fail-closed `EffectSet.Unknown` an unknown external call contributes, so the declaration draws
> Calor0410 `'unknown'` exactly as §13.1's first row and §6.4's second sample say. It is what
> keeps PR #968's hole closed at the one place a row-less value finally costs something
> (§3.5: "defaulting to pure re-opens the hole"). Under `--permissive-effects` the Calor0425 is
> suppressed and nothing is charged — the flag waiving *we cannot tell*, §4.5. An `Assumed` row is
> charged and reported once: `Invocation of 'h' in 'Go' is charged [assumed: pure] under an
> assumption: contains a raw C# interop expression (§CS). The row is charged as an assumption,
> not a proof.` — also pinned by full equality.

### 10.2 Cross-module

`§M{m001:Registry}` exposes `§F{f001:Register:pub} (str:name, Func<str,bool>:handler §E{fs:r})`;
`§M{m002:Client}` defines `ReadsAndLogs` with `§E{fs:r, cw}` and passes it.

BEFORE: at `§C{Handlers.Add}` the pass records the Calor0419 assumption *"passes function-typed
value 'handler' to 'Handlers.Add', which may invoke it with unverifiable effects"*
(`EEP:1282-1284`) — the exact string the demand ledger's `dA.calor0419FunctionTyped` class
matches. At the `§C{Register}` site, `ReadsAndLogs` is a **method group**, so `EEP:1272-1278`
charges its declared `{fs:r, cw}` to `Setup`, which declares `{mut}` → Calor0410 on `Setup`.
**Nothing checks `ReadsAndLogs` against `handler`'s intent**: `Register` says "I take a filesystem
reader", `ReadsAndLogs` also prints, and the compiler only notices the total.

AFTER: `Register`'s `FunctionSymbol` carries
`FunctionBoundType([STRING, FunctionBoundType([STRING], BOOL, Row: Concrete({fs:r}))], VOID,
Row: Concrete({mut}))`. Site 2 fires:

```
Calor0424: Argument 'ReadsAndLogs' has effect row cw, fs:r, which does not fit
parameter 'handler' of 'Register' (declared row: [fs:r]). Extra effect(s): cw.
```

The `Handlers.Add` site's Calor0419 disappears when `Handlers.Add` resolves (the assumption it
justified no longer exists) and becomes Calor0425 when it does not.
`CrossModuleEffectEnforcementPass.cs:162` carries the row across the module boundary so an
`Assumed` callee row stays `Assumed` in the caller (§4.4); `CrossModuleEffectRegistry.cs` stores
it. §13.1 disposes of the existing pin that observes the old behaviour
(`StrictnessBatchTests.cs:640`).

### 10.3 Generic effect polymorphism

The `Map` of §7.4, with `Double` (`§E{}`) and `Announce` (`§E{cw}`) and callers `UsePure`
(`§E{alloc}`) and `UseImpure` (`§E{alloc}`).

BEFORE: the module does not compile at all — `Calor0418` at `§C{f}` (**X10**'s shape), so there is
no BEFORE for the callers.

AFTER: `UsePure` is clean (`e := Concrete(∅)`). `UseImpure`:

```
Calor0410: Function 'UseImpure' uses effect 'cw' but does not declare it
  Effect row: effect variable 'e' of 'Map' instantiated to [cw] at this call site,
  from argument 'Announce' (row: [cw])
```

With an unresolved argument instead (one of the 431 unresolved BCL sites), `e := Unknown`:

```
Calor0425: Effect variable 'e' of 'Map' instantiates to Unknown at this call site:
the row of argument 'selector' could not be determined (receiver
'System.Collections.Generic.IList<T>' has no member named 'Select'). 'UseImpure' is
charged Unknown effects. Add a .calor-effects.json manifest entry, or compile with
--permissive-effects.
```

The parenthetical reuses the resolution ledger's own `unresolvedByClass` strings
(`metadata-binding-corpus-ledger.json`) so the diagnostic and the ledger name the same failure the
same way. §13.2 pins both message texts.

---

## 11. Async

> **Decision (made here, not handed to the spike).** Async/`Task`-shaped effects are **deferred to
> 0.16**, unchanged from roadmap §5.1. The three conditions below are the **0.16 re-entry test**,
> re-adjudicated at the 0.15.0 retro — they are not spike criteria and the spike does not evaluate
> them.

Deferral is coherent because async is not modelled today: `await` is transparent
(`EEP:2538` — `AwaitExpressionNode a => InferFromExpression(a.Awaited)`), `EffectKind` has no
async member (`EffectTypes.cs:6-14`), the registry has no `async`/`task`/`await` code
(`:65-109`), and `BoundLambdaExpression.IsAsync` affects only a display string
(`Binding/BoundNodes.cs:2178`). An async function's row is its body's row, exactly as its effect
set is today, and rows can ship without touching any of it.

**The 0.16 re-entry test.** Async rows are taken up only if all three hold: **(a)** asynchrony is
expressible as a *row property* (a flag on `EffectRow`) rather than an effect code, so no
`EffectKind` member and no registry entry are needed and `EffectEntry{Kind,Value}` serialization
(`EffectSummary.cs:107-111`) is untouched; **(b)** `fits` needs no async-specific case — an async
row fits a sync destination exactly when their effect sets do, with asynchrony carried by the
`Task<T>` return type the binder already has; **(c)** the change is additive, so a 0.15 program
compiles unchanged under 0.16. Until then, `RequestPreProcessorBehavior`'s `async Task<TResponse>`
is spiked as a **sync-shaped row over a `Task`-returning function**, which is what §12 assumes.

---

## 12. The emitter spike

Roadmap §4.1 term 1. Draft v1 froze two modules and claimed the MediatR one exercised "four of
the six §6 sites". It exercises **one** — interface implementation. A delegate-typed *parameter*,
an *invocation* and a `foreach` are not §6 sites. So criterion R1 was not readable from either
module. Corrected by splitting the artifacts by what each can adjudicate.

### 12.1 The frozen artifacts

| Artifact | What it is | Adjudicates | Verified |
|---|---|---|---|
| **A1** `tools/calor-allowlist-audit/allowlist-audit.calr` | the dogfood utility: 127 lines, 7 `§E{`, sibling `CalorAllowlistAudit.csproj`, built in CI at `.github/workflows/test.yml:174` and run at `:176`, **no higher-order code** | **G-CODEGEN** only — the regression module | exists at `82338e37` |
| **A2** `bench/corpus/MediatR/src/MediatR/Pipeline/RequestPreProcessorBehavior.cs` | **29** lines at the pinned MediatR SHA `fb309026775ef953a64fb5339d074426c1ad2c37`: interface implementation (`:12`), delegate-typed parameter `RequestHandlerDelegate<TResponse> next` (`:20`), invocation `await next()` (`:27`). Delegate declared at `IPipelineBehavior.cs:12`, contract at `:29` | **R2**, and G-CODEGEN | 29 by `awk 'END{print NR}'` / `grep -c ''`; `wc -l` reports 28 because the last line is unterminated. Draft v1 said 29 and was right; v2 "corrected" it to 28 from a lens finding **without executing**, and enshrined the error in §14.1. Restored, and the measuring command named |
| **A3** `docs/design/spikes/effect-rows/combinators/{map,match,middleware,callback}.calr` | the four AFTER forms of §7.4, **all four of which now exist in this document** (v2's compression had deleted three, leaving A3 pointing at spellings the doc did not contain) | **R1** and **R3** | to be authored by the spike PR, transcribed from §7.4 — **except the middleware fixture, whose spelling the spike must decide first (below)** |

> **Open Major, carried openly (consistency lens, round 3).** §7.4's middleware form binds
> `eff e` at `§IFACE<…, eff e>` / `§CL<…, eff e>` — the one spelling **§7.3 forbids in E2** — so
> R1, which **P27 recomputes** as "all four A3 fixtures compile with zero Calor0404", would fail
> on it *by this document's own rule*. That would fire the ramp for an artefact of the doc rather
> than a fact about rank-1, so it must not be left implicit.
>
> **Sequencing decision: the spike PR decides A3's middleware spelling BEFORE freezing A3.**
> Member-level first — `§MT{mt001:Handle}<eff e> (…)`, position 1, already permitted — on the
> strength of **W1a**/**W1b** (interface and implementing-class members each carry their own
> type-parameter list today) and **W1c** (Calor0421 fires across *renamed* member type
> parameters, so the interface↔implementation match is already alpha-equivalent, the property
> `StrictnessBatchTests.cs:172` pins for overrides). Class/interface-level ships **only** if
> member-level provably cannot express R2, and only then does §9's seventh parser insertion point
> become unconditional and §7.3's last row flip.
>
> What is **not** settled: whether `fits` identifies the interface's `e` with the
> implementation's `e`. W1c is evidence about *member matching*, not about *row unification*.
> That is the spike's to prove, and it is why this is recorded as open rather than closed.

A2 is chosen over `Wrappers/RequestHandlerWrapper.cs` (72 lines, method-group→delegate cast
`:43`, `Aggregate` fold whose lambda closes over `next` and is immediately invoked `:44`) because
that file drags in `async` (§11, deferred), generic *type* variance
(`calor-direction.md:33`, deferred) and a fold whose accumulator is itself a function — rank-2,
not rank-1. It is named as a **non-gating stretch subject**: re-attempted after A2 passes, with
its outcome published either way.

### 12.2 G-CODEGEN — a feature-wide blocking gate

> Rows are a *checking* feature. For **A1 and A2**, the emitted C# must be byte-identical between
> BEFORE and AFTER, modulo whitespace. If it is not, **E2 does not merge** — monomorphic or not.

Promoted out of the ramp (Draft v1's criterion 2) because a codegen diff is not a rank-1
question. This is also what makes §9's "0 `.cs` goldens" and "`CurrentCompilerSemanticsVersion`
unchanged" claims blocking rather than assumed.

### 12.3 What is committed

Under `docs/design/spikes/effect-rows/`:

1. `before/` and `after/` — for A1, A2 and each A3 fixture: the `.calr` source, the emitted
   `.g.cs`, and the compiler's full diagnostic list, one file per artifact per side.
2. `experiments/` — `run.py`, `run2.py`, `run3.py`, `facts.py`, `facts2.py`, `compile53.py`,
   their canonical `transcripts/`, `regenerate-transcripts.py`, and `o53/baseline.json`. Already
   committed and **pinned by P29/P30**; the spike PR only adds to it.
3. **`spike-verdict.json`** — the machine-readable verdict, replacing Draft v1's prose `README`.
   Schema: `{schemaVersion, measuredCommit, artifacts:{A1,A2,A3…}, gCodegen:{artifact→PASS|FAIL,
   diffBytes}, ramp:{R1,R2,R3→PASS|FAIL, evidence}}`, with `ramp.verdict` = `VALIDATED` iff
   R1 ∧ R2 ∧ R3.

**What P27 recomputes** (v2 left "MatchesRecomputation" undefined, which the test lens correctly
called out as unfalsifiable):

- **the `gCodegen` block**, by re-running the emitter over each artifact's `before/` and `after/`
  `.calr` and comparing the two `.g.cs` byte-for-byte modulo trailing whitespace — this *is*
  **P28**, so G-CODEGEN's claim is recomputed, not recorded;
- **the `ramp` block's R1 leg**, by compiling each A3 fixture and asserting zero Calor0404, zero
  Calor0424 and zero Calor0425 without `--permissive-effects`;
- **shape only** for `schemaVersion` (must be 1) and `measuredCommit` (40 hex, not compared to
  HEAD — the two existing ledgers' convention, `HigherOrderDemandLedgerTests.cs:480-498`);
- **R2 and R3 are recorded, not recomputed.** Both are judgements about whether a *carve-out* was
  needed and whether the solve stayed one-line; a test cannot re-derive them. P27 asserts they
  are present and well-formed, and the spike PR's diff is where they are reviewed. Saying so is
  better than implying a machine adjudicates them.

**P31 — artifact manifest.** v2 declined a spike-directory presence test on the grounds that P27
subsumed it. That was unsound: P27 reads one JSON file and would pass with every `.calr`/`.g.cs`
missing. P31 asserts the manifest instead — for each artifact named in `spike-verdict.json`, the
four files of item 1 exist, are non-empty, and the diagnostic list parses.

**Submodules.** A2 lives under `bench/corpus/MediatR/`, which `git clone` does not init
(CLAUDE.md). P27/P28/P31's A2 legs therefore `Skip.IfNot(Directory.Exists(...))` — the
`BinderIncompleteRatchetTests` pattern — while the **A1 and A3 legs never skip**, since both live
in-repo. The skip is registered in `eng/test-manifest.json`'s `expectedSkipped` so a *silent*
skip trips the count. Home: `tests/Calor.Compiler.Tests/Effects/SpikeVerdictTests.cs`; the
`compiler` shard already opts into submodules.

### 12.4 Pass/fail, per criterion per artifact — **EXECUTED**

The spike ran. `docs/design/spikes/effect-rows/spike-verdict.json` is the machine-readable
result; this table is its summary, and where the two disagree the JSON wins.

| | A1 | A2 | A3 |
|---|---|---|---|
| **G-CODEGEN** (blocking, feature-wide) | **PASS** — byte-identical, `diffBytes: 0` | **PASS modulo `#line`** — every differing byte is inside a `#line` directive and the files are the same length; **0** emitted C# lines differ otherwise | **PASS** — all four fixtures byte-identical |
| **R1** four combinators clean | n/a | n/a | **PASS** — each AFTER form compiles `exit: 0`, `diagnostics: 0`, no flag, no `§CSHARP` |
| **R2** interface/impl, no carve-out | n/a | **PASS** — recorded | n/a |
| **R3** one-line instantiation solve | n/a | n/a | **PASS** — recorded |
| artifacts present and well-formed | P31 | P31 | P31 |

**Ramp verdict: `VALIDATED`.** R1 ∧ R2 ∧ R3, so §7.5's ramp does **not** fire: site 6 stays,
gate 1's denominator stays at **six** classes, Calor0404 stays allocated, and the `eff` branch
ships. **G-CODEGEN does not block.**

**A3's middleware spelling is decided: MEMBER-LEVEL** (`§MT{mt001:Handle}<eff e> (…)`), the
open Major §12.1 carried. Consequently **§7.3's last table row stands** — class/interface-level
`eff` remains forbidden in E2 — and **§9's seventh parser insertion point stays conditional at
zero cost**, because position 1 needed no new insertion point. The proof §12.1 said W1c did not
supply is executed: `after/A3-middleware-alpha.calr` binds `eff e` on the interface and `eff f`
on the implementation and **compiles**, because a row carries binder **indices**, not names.
Its residual is recorded rather than closed: the interface must itself be Calor for a row to
exist on it (§14 Q1).

**Six caveats the verdict states in full and this table must not soften.**

1. The prototype is **additive by construction** — every new code path is gated on a row being
   *present* — so §3.5's "row-less function-typed position ⇒ Calor0425" is **not implemented**;
   such a position still reaches today's Calor0418. R1's bar is the *absence* of three codes, so
   it is weaker evidence than its wording suggests.
2. The prototype does **not** put rows in the bound tree. §8.2's `FunctionBoundType.Row` /
   `ParameterRows` is still owed by E2; the spike reads rows off the AST, which is where the
   effect pass already walks (§2).
3. The prototype is **throwaway** and is **not** merged. It lives on branch
   `spike/effect-rows-emitter`; `spike-verdict.json` records its commit. This PR carries the
   artifacts and no `src/` change, so **P29 stays green here** — with the prototype in the build
   it is red on **seven** cases, listed as E2 obligations in §13.5.
4. **A2's AFTER form still exits 1, and rows are not what is left.** The `Calor0418` on
   `§C{next}` is **gone** — that is the row doing its job — but `Calor0410: Function 'Handle'
   uses effect 'unknown' but does not declare it` remains, because `processor.Process` and
   `Task.ConfigureAwait` do not resolve and the unknown-call channel then dominates the total.
   That is §2.2's resolution ceiling (431 of 1248 BCL sites), which rows do not address and
   §13.4's ledger exists to measure. G-CODEGEN is unaffected: the artifact still emits under the
   waiver, and the emitted C# is what §12.4's table compares.
   **A consequence that later cost a proof point, recorded here rather than left implicit:**
   `after/A2.diagnostics.txt` was produced under that waiver — its header reads
   `# emit args: --permissive-effects` — so its diagnostic multiset is **not** the output of the
   flagless invocation, and it was never reproducible on any compiler. Annex entry A-1.11 froze
   PP-E1 leg A's A2 negative control from that file while the same row forbids the flag; annex
   sub-entry **A-1.11.1** (2026-08-26) re-freezes it under the pinned invocation. The four A3
   `.diagnostics.txt` headers read `# emit args: (none)`, so the flag defect is A2's alone — but
   all five lists come from the **throwaway prototype** (caveat 3), and the shipping compiler
   reproduces none of them until E4 replaces Calor0418 (caveat 1). **Read this section's
   diagnostic lists as prototype output under a stated flag, never as a frozen baseline.**
5. **Calor0404 is allocated but under-reached.** §7.3 forbids seven positions; the prototype
   wires Calor0404 to exactly two of them (a class/interface-level `eff` binder, and an `eff`
   name colliding with the taxonomy). The rest — including a `§CL{…}<eff e>` written where the
   scope is never established, and an out-of-scope `§E{e}` — come out as
   `Calor0403: Unknown effect code 'e'`. They are still *forbidden*, so the prototype is not
   unsound; it forbids them **incidentally**, by never binding the name, and the author is told
   about the effect *taxonomy* for what is really a *scope* error. **E2 obligation:** make all
   seven rejection sites report Calor0404 with the scope message, which is what P18's
   `Rejected_*` cases assert.
6. **Calor0421's message renders a polymorphic interface row as `[pure]`.** On
   `after/A3-middleware-broadening.calr` the interface declares `§E{e}` and the diagnostic reads
   *"(interface declares: `[pure]`)"*, because the message is built from
   `EffectSet.ToDisplayString()`, which knows only the concrete part. The **verdict** is right —
   the implementation's extra `[cw]` is correctly rejected — but the text tells the author the
   interface promised purity when it promised *"whatever the caller's function does"*.
   **E2 obligation:** render the row, not the set, at both variance sites. §8.3 already reserves
   `EffectRow.ToDisplayString()` for exactly this, and **P22** is where the text is pinned.

The verdict is **read off `spike-verdict.json`**, not argued from prose. The spike PR must merge
before E2; if it does not, E2 does not merge (§0 term 1).

### 12.5 What P27/P28/P31 verify on main, and what waits for E2

The AFTER forms carry rows, which main's compiler does not parse. The pins split accordingly,
and the split is asserted rather than assumed:

| Leg | On main today | After E2 |
|---|---|---|
| **P28** BEFORE side | **recomputed** — `before/A1.calr` and `before/A2.calr` are re-emitted with the current compiler and diffed against the committed `before/*.g.cs` | unchanged, and it becomes A1's real question: *did the row feature move codegen for a row-less program?* |
| **P28** before/after pair | **compared** — the committed `.g.cs` pairs are diffed under the `#line`-normalisation rule. Needs no compiler, so it is a real assertion now | unchanged |
| **P27** shape | **asserted** — `schemaVersion`, 40-hex `measuredCommit` **and** `prototype.commit`, `prototype.throwaway`, and that every `before/…`/`after/…` path cited as evidence exists | unchanged |
| **P27** R1 | **recorded** — the four diagnostic lists are read and asserted to contain none of Calor0404/0424/0425. `ramp.R1.recomputedBy` is `"P27 once E2 lands; recorded until then"`, and the test asserts that **exact string** (an earlier version asserted only that it contained "E2", which a joke string satisfied), so it cannot be left recorded silently | **recomputed** by compiling each A3 fixture |
| **P27** R2, R3 | **recorded** — judgements a test cannot re-derive (§12.3) | unchanged |
| **P31** manifest | **asserted** — every artifact's three files exist, are non-empty, and the diagnostic list's header count matches its body | unchanged |

No leg is skipped and no leg fakes a pass. The A2 **corpus-subject** leg is the only skip, and
it skips only without submodules — registered in `eng/test-manifest.json`.

---

## 13. Test plan (E2–E4)

Every pin below states its **home file** and its **freeze point** — Draft v1 gave neither, which
was the test lens's cross-cutting defect. "Design-doc merge" means this document merging;
"before E2" means the pin lands in a PR that merges before the first effect-row implementation PR.

### 13.1 Existing pins: what changes

| Pin (assertion line) | Today | Disposition |
|---|---|---|
| `StrictnessBatchTests.cs:43` (`:29` `DelegateInvocation_FunctionTypedParameter_IsError`) | Calor0418 | **rewrite** → `..._WithoutRow_IsUnknown`: Calor0425 at the parameter + Calor0410 at `§E{}` |
| `:60` (`:47` `_LambdaBoundLocal_IsError`) | Calor0418 | **rewrite** → `_ChargesInferredRow`: compiles; Calor0410 when `§E` is narrowed. Baseline **Y9a** |
| `:77-80` (`:64` `_UnderPermissiveEffects_IsWaivedToWarning`) | 0418 demoted | **rewrite** → Calor0425 suppressed under the flag **+ a new sibling** asserting Calor0424 is not |
| `:745` (`:728` `M1_ExpressionCallSpelling_DelegateValue_IsError`) | Calor0418 | **rewrite** → row of the invoked value is charged; 0425 when Unknown. *Orphaned in Draft v1* |
| `:768` (`:749` `M1_ReturnedDelegateInvocation_IsError`) | Calor0418 | **rewrite** → returned value's row charged; 0425 when the `§O` carries no row |
| `:502` (`:472` `C2_DecoyNamedDelegateParameter_ShadowsFunction_IsError`) | Calor0418 | **rewrite** → the decoy parameter's row governs, not the shadowed function's. *Orphaned in Draft v1* |
| `:260` (`:245` `OverrideOfExternalBase_RoutesToAssumedChannel`) | Calor0419 | **rewrite** → Calor0425 (§6.2 retires the 0419). *Orphaned in Draft v1* |
| `:607` (`:587` `C3_ExternalInheritedImplementation_RoutesToAssumed`) | Calor0419 | **rewrite** → Calor0425. *Orphaned in Draft v1* |
| `:657` (`:640` `C4_DelegateValueArgument_ToKnownHigherOrderName_SurfacesAssumption`) | Calor0419 warning | **rewrite** → Calor0424/0425 at the argument per §10.2. *Orphaned in Draft v1* |
| `:152`, `:172`, `:219`, `:582` (0420/0421 `_IsError`) | as today | **unchanged** (§6.3) |
| `:177`, `:223` (0420/0421 `_Compiles`) | as today | **unchanged** |
| `:612` `C4_MethodGroupArgument_ChargesCalleeDeclaredEffects` | charges callee | **unchanged** (§3.4) |
| `:106` `FreeBareName_FailsClosed_NotSilentlyPure` | Calor0411 | **unchanged** — free-name path (`EEP:1483-1500`) untouched |
| `EffectEnforcementTests.cs:374` (`:354`), `:398` (`:378`) | **Calor0411**, despite `DelegateInvocation_` names | **unchanged** — rewriting them would silently loosen the free-name rule |
| `EffectsSuggestTests.cs:148-157` (`_variableTypeMap` grep) | E1 slice-1 pin | **unchanged** |
| `MetadataBinderCorpusMeasurementTests.cs:37-118` (65.46% floor) | exact-equality, two-sided | **unchanged**; a move needs regeneration in the same PR (gate 6) |
| `BoundTypeTests.cs:139`, `:150` (`DisplayString` exact-equality) | `"() -> VOID"`, `"(INT, STRING) -> BOOL"` | **unchanged** — and they are the pre-existing enforcement of §8.3 |
| `ArchitectureTests.cs:158` (`eng/ast-schema.json`) | child-property sets | **edited** — four classes gain a `Row` child (§9) |
| `BulkBenchmarkCompilationTests.cs:171` (`>= 200`) | as today | **unchanged** |
| `EditScriptIdentityTests.cs:217-231` (`RegisteredScriptIdsAreStable`) | 7 ids, ES-01…07 | **+ ES-08**, the effect-row script. The F-3 supersession that had to precede it **already merged** — `b5d61e18` (PR #1085) |
| `QueryGoldenTests.cs:134` (unknown facet throws) + `:152-172` (`EveryGoldenStatesWhyItExists`) | as today | **unchanged**; E5 adds the `effects` arm and its golden |

> **EXECUTED — v0.15 E4.** Every Calor0418 row above is discharged, in place and at the same
> line numbers so `facts.py`'s probes keep their meaning (the file's line count is unchanged;
> exactly the four probe lines the table names — `:472`, `:502`, `:728`, `:745` — move):
>
> | Pin | Shipped as | Note |
> |---|---|---|
> | `:29` → `DelegateInvocation_FunctionTypedParameter_WithoutRow_IsUnknown` | Calor0425 at the invocation + Calor0410 `'unknown'` | span is the **invocation**, not the parameter (A-1.11's L7 cells say "at `§C{f}`"); the 0410 is the fail-closed charge the row promised |
> | `:47` → `_LambdaBoundLocal_ChargesInferredRow` | compiles; `{}` charged | baseline Y9a. The "Calor0410 when `§E` is narrowed" half is `Invocation_LambdaBoundLocal_NarrowedDeclaration_IsCalor0410`, appended |
> | `:64` → `_UnderPermissiveEffects_Calor0425IsSuppressed` | no 0425, nothing charged, compiles | the "0424 is not" sibling is P11's `NeverWaived_DoesNotFit_AtEveryMonomorphicSite`, already landed |
> | `:728` → `M1_ExpressionCallSpelling_DelegateValue_ChargesTheRow` | Calor0425 (row-less) | same program |
> | `:749` → `M1_ReturnedDelegateInvocation_ChargesTheReturnRow` | Calor0425 naming `returned by 'GetF'` | the `§O` carries no row; a rowed `§O{Func<i32>} §E{cw}` charges `cw` (measured, not pinned — the enclosing function's own Calor0410 on the lambda body pre-empts a clean positive control there) |
> | `:472` → `C2_DecoyNamedDelegateParameter_ShadowsFunction_RowGoverns` | Calor0425 on `'Helper'` in `'Go'` + 0410 `'unknown'` on `Go` | the decoy's (absent) row governs; the shadowed pure function is never charged |
> | `EffectEnforcementTests.cs:354`, `:378` | Calor0411 | **unchanged**, as the table said |
> | `EffectEnforcementTests.cs:1175` (`E1Slice2b_FunctionTypedParameterAsBareTarget_UsesTheAstParameterType`) | Calor0425 quoting `parameter 'make' of 'Go'` and `Func<` | not in the table; the AST type string still fills the message, under the new code |
> | `GenericSyntaxTests.cs:279`, `CliMultiFileTests.cs:479` | comments only | both compile under `--permissive-effects` as before; the comments now say why (0425 suppressed, nothing charged) |
>
> Positive controls added (the rewrite's other half): `Invocation_RowedValue_FitsAndChargesTheRow_PositiveControl`
> (A3-callback's shape: rowed field invoked by a method declaring the row — zero effect-family
> diagnostics), `Invocation_PolymorphicRow_ChargesTheVariable_WhichTheDeclarationBinds`
> (A3-middleware's `RunTwice`).

### 13.2 New pins — home and freeze

> **P1 and P6 contradict each other, and slice b re-specifies P1.** Recorded in review round 1 of
> PR #1101 rather than discovered when slice b turns a green pin red.
>
> P1's own case is **Y1b** — `§I{str:m} §E{cw}` over a `str` parameter — and P1 says it must be
> **Calor0410** after E2. But `str` is not a function type, so under §3.5 that row is a row on a
> position that cannot carry one, and **P6 says it must be Calor0405**. The two pins name
> different answers for the same source.
>
> Five more cases are the same shape and the same collision: **Y5a** and **X2a**/**X2b**
> (`-> void §E{cw}` and `§O{void} §E{cw}`) and **Z9**/**Z9b**. §13.5(a) lists all six as having
> moved to Calor0410 in slice a; **all six move again, to Calor0405, when P6 lands in slice b.**
>
> This is not a defect in either pin — it is the seam between a slice that consumes rows and a
> slice that checks what they are attached to. But it means **P1 as written is a state slice b
> must break**, so slice b re-specifies P1 onto a function-typed subject
> (`§I{Func<i32,i32>:f} §E{cw}` against a pure declaration) and hands the non-function-typed
> cases to P6. A slice-b PR that merely regenerates these six transcripts without saying that has
> silently changed what P1 asserts.
>
> **EXECUTED — E2 slice b, PR #1102.** P1 is re-specified exactly as above, in
> `EffectRowSyntaxTests.RowSuffix_SameLineOnI_IsParameterRow_NotDeclarationRow`, whose doc
> comment records the collision and the move. What P1 still owns is the LINE RULE — which type
> the row attached to — now asserted on a `Func<i32,i32>` parameter against a separate later-line
> `§E{}`, plus a control that no Calor0405 fires on a function-typed subject, plus the bound
> `FunctionBoundType.Row`. Its discriminating revert is unchanged in kind: drop the `Span.Line`
> comparison and the row becomes the declaration's again.
>
> **The count above is SIX and the executed number is NINE.** Beyond Y1b, Y5a, X2a, X2b, Z9 and
> Z9b, three more cases are the same shape and move for the same reason: **Y1a** and **Y1c**
> (`§I{str:m} §E{…}` with a separate declaration-level `§E`, which this blockquote's enumeration
> missed — they differ from Y1b only in also having a declaration row) and **Z9c**
> (`§O{i32} §E{cw}`), which is not in this list because it compiles CLEAN today rather than
> landing on Calor0410, but which **P6's own row names explicitly**. Measured from a full diff of
> all six transcripts, not from a summary — the same discipline §13.5(a)'s closing note demands.

| # | Pin | Home | Freeze | Discriminating revert |
|---|---|---|---|---|
| P1 | `RowSuffix_SameLineOnI_IsParameterRow_NotDeclarationRow` — **RE-SPECIFIED by E2 slice b** (see the blockquote above). Subject is now **function-typed**: `§I{Func<i32,i32>:f} §E{cw}` against a declaration whose own later-line `§E{}` stays pure. Asserts the row attached to the PARAMETER, that no Calor0405 fires on a function-typed subject, and that the bound `FunctionBoundType.Row` is `Concrete({cw})`. Its old subject **Y1b** moved to P6, which answers Calor0405 for it. *(Slice a's wording — "case Y1b: compiles today, must be Calor0410 after" — was a state slice b had to break, and is superseded rather than deleted so the history is legible.)* | `Calor.Enforcement.Tests/EffectRowSyntaxTests.cs:51` | with E2 | drop the `Span.Line` comparison → the row becomes the declaration's again |
| P2 | `RowSuffix_NonAdjacent_*` — **two halves.** (a) *falls through to a `§E` arm*: X1b/Y6a/Y5a unchanged, the 2948/471 arrow corpus safe. (b) *no `§E` arm to fall through to*: **Calor0405** replaces the cascade at all four positions, one case each against its executed baseline — **Z1** (`§FLD` ⏎ `§E`, 4× Calor0100 today), **Z2** (`§B` ⏎ `§E`, 4×), **Z3** (wrapped inline signature, 8×), and the inline-parameter form (**X9c**) | same | with E2 | invert the line test → the whole corpus moves; drop the recovery → the cascade counts come back |
| P3 | `RowParses_AtEveryEightPositions` — one case per §3.3 row, incl. the three that already parse (X7, X8, Y6a), which **no test covers today** (`grep '§LAM.*§E' tests/` → 0); plus a negative: `§E` inside a `§C…§/C` argument list stays rejected (**Z10**) | same | with E2 | remove any insertion point → that row fails |
| P4 | `RowRoundTrips_ParseEmitParse` — parse → `CalorEmitter` → parse, byte-identical, **one case per position** | `Calor.Compiler.Tests/NewFeatureRoundTripTests.cs` | with E2 | drop the `CalorEmitter` row emission → 7 cases fail |
| P5 | `EffectsTokenIsNotAnExpressionStart` — `TokenKind.Effects ∉ Parser.RegisteredExpressionStartTokens` (`Parser.cs:67-68`) | `Calor.Compiler.Tests/ExpressionRegistrationTests.cs` | **before E2** | add `Effects` to `ExpressionParsers` → initializers swallow rows |
| P6 | `OmittedRow_PerSite` — declaration=pure, lambda=inferred, **binding-with-initializer=inferred**, bare binding/param/return/field=Unknown (four omitted sites, not one). **Plus `RowOnNonFunctionTypedPosition_IsCalor0405`**, one case per position against its executed baseline: **Z9** (`-> void §E{cw}`), **Z9b** (`§I{i32:x} §E{cw}`), **Z9c** (`§O{i32} §E{cw}`) — all three compile today (§3.5) | `Calor.Enforcement.Tests/EffectRowLatticeTests.cs` | with E2 | make the parameter default `Concrete(∅)` → E-3's laundering re-opens; drop the function-typedness check → Z9/Z9b/Z9c go green again |
| P7 | `FamilyCodeEncompassesNarrowCode` — `§E{db}` admits `db:r`; `fs:rw ⊇ fs:w` regression | **`tests/Calor.Enforcement.Tests/EffectSubtypingTests.cs`** (exists; `:20` is the `fs:rw ⊇ fs:w` case, `:29`/`:38` are the network pair — all `[Fact]` lines, the one place this doc cites a `[Fact]` rather than an assertion, because these tests assert inside a single-expression body) | with E2 | remove the `database` entry → first half fails |
| P8 | `FitsIsTotalOverNineCells` — table-driven over §4.3, **including the three `Assumed`-destination cells** | `EffectRowLatticeTests.cs` | **design-doc merge** (the table is normative) | re-introduce `EffectSet.cs:100`'s `if (other.IsUnknown) return true` → `fits(Concrete, Unknown)` returns `Fits` |
| P9 | `EffectRowJoin_IsASemilattice` — associative, commutative, idempotent, identity `Concrete(∅)`, top `Unknown`, **reason sets canonically ordered** | `EffectRowLatticeTests.cs` | with E2 | make `R` a concatenated list → commutativity fails |
| P10 | `AssumedSurvivesTheDestination` — **three cases.** (a) two-hop: `Assumed` source → `Fits` → destination row is `Assumed`; (b) **cardinality**: exactly one 0425 per hop, not two (the claim §4.4 makes and v2 never asserted); (c) **the declaration boundary converts** `Assumed`→`Concrete` and Calor0419 reports it there (§5) — asserted as intended behaviour so the conversion is observed, not assumed | `EffectRowLatticeTests.cs` | with E2 | make the destination `Concrete` → (a) goes silent; emit 0425 twice → (b) fails; carry reasons past the declaration → (c) fails |
| P11 | `NeverWaived_DoesNotFit_AtAllSixSites` + `PermissiveWaivesUnknown` — incl. **Y8a's flip**: 0420/0421 stop demoting | `Calor.Enforcement.Tests/StrictnessBatchTests.cs` | with E3 | route 0424 through the policy check, or restore `EEP:517-519` |
| P12 | `Calor0424_NotDefeatableByDeletingSourceE` — deleting the source `§E` yields Calor0410 on the source (§4.5) | `EffectRowLatticeTests.cs` | with E3 | skip the body check on rowed sources → one diagnostic disappears |
| P13 | `ParameterRowsAreContravariant` **and** `FunctionTypesAreInvariantInTypes` | `EffectRowLatticeTests.cs` | with E3 | flip the argument order in the parameter conjunct |
| P14 | `LambdaDeclaredRow_NarrowerThanBody_IsError`; `_CannotTell_IsCalor0425`; `_OmittedRow_IsInferred`; `_TypeCarriesDeclaredNotInferred` | `Calor.Enforcement.Tests/EffectRowLambdaTests.cs` | with E2 | restore `InferFromLambda` to ignore `lambda.Effects` |
| P15 | Six `_IsError`/`_Compiles` pairs **plus a `_CannotTell` arm each**: `RowMismatch_At{Assignment,Argument,Return,Override,InterfaceImpl,GenericInstantiation}` | `StrictnessBatchTests.cs` | **design-doc merge** — this is gate 1's frozen denominator | delete E3's rule for one site → that `_IsError` fails |
| P16 | `AllMismatchCodesShareOneRelation` — Calor0424, 0420, 0421 **and** `CrossModuleEffectEnforcementPass.cs:162` move together | `Calor.Enforcement.Tests/CrossModuleEffectTests.cs` | with E3 | give `CheckEffectVariance` its own subset test back |
| P17 | **`UnresolvedReceiver_YieldsCalor0425_NeverConcrete`** — an `UnresolvedBoundType`/unresolved receiver must produce `EffectRow.Unknown`, never a `Concrete` row. **The pin the whole design rests on**; absent from Draft v1 | `Calor.Enforcement.Tests/EffectRowLatticeTests.cs` | ~~before E2~~ → **with E3** (see the note below) | make the unresolved branch return `Concrete(∅)` → the fixture goes silent |
| P18 | `EffectVariable_*`: `Declares_EffModifier` (X6a's shape now parses); **`TypeParamNamedEff_StillWorks`** (the lookahead guard — **Z4** compiles today and must keep compiling); **`EffectVariableNamedLikeACode_IsCalor0404`** (`<T, eff cw>` rejected, while the ordinary type parameter `<T, cw>` of **Z6**/**Z6b** stays green); `InScope_DoesNotRaise0403`; `OutOfScope_Raises0404`; `Rejected_In{Return,GenericArg,Binding,Field,Lambda,Delegate,ClassOrInterfaceLevel}` (**seven** rejection sites — §7.3's total partition, with the `§LAM`/`§DEL` halves anchored on **Z8b**/**Z8** and the class/interface half flipping to `_Permitted` only if the spike PR proves member-level cannot express R2); **`MemberLevelEffOnInterfaceMember_Parses`** (position 1, anchored on **W1a**/**W1b**); `MixedRow_IsJoin`; `InstantiatesFromArgumentRow`; `UnknownContributor_YieldsUnknown` | `Calor.Enforcement.Tests/EffectVariableTests.cs` | with E2 | **all of P18 is deleted if the ramp fires**, together with P15's site-6 pair |
| P19 | `FunctionTypesDifferingOnlyInRow_AreNotEqual` + the `GetHashCode` half + `RowsDefaultToUnknownNotPure` | **`Calor.Enforcement.Tests/EffectRowLatticeTests.cs:462`, `:479`** — *moved by E2 slice b from `BoundTypeTests.cs`*; see the note below the table | with E2 | drop `Row` from `Equals` |
| P20 | `DisplayStringIsRowFree` — belt to `BoundTypeTests.cs:139`/`:150`'s braces | **`Calor.Enforcement.Tests/EffectRowLatticeTests.cs:492`** — *moved, same reason* | with E2 | append the row → three tests fail |
| P21 | `ManifestResolutionMapsToRow` — `Resolved`/`PureExplicit`/`Unknown` → `Concrete(S)`/`Concrete(∅)`/`Unknown` | `Calor.Enforcement.Tests/EffectResolverTests.cs` | with E2 | map `Unknown` to `Concrete(∅)` → P17's sibling fails |
| P22 | `MessageTexts` — the four new strings: §10.1's `Effect row: charged by invoking …`, §10.3's two, §6.4's 0424 text. Existing pins assert `Message.Contains`; these assert the **full** new clause | `StrictnessBatchTests.cs` | with E3 | reword any clause |
| P23 | `BuildStateCacheConstants` — `"4.0"`, `CurrentCompilerSemanticsVersion` **unchanged**, `CurrentOptionsSerializerVersion` unchanged (`BuildStateCache.cs:121-123`) | `Calor.Compiler.Tests/Incremental/` | with E5 | bump the semantics stamp → fails, and G-CODEGEN is contradicted |
| P24 | `ProjectIndexFormatBumped` — `"4.0"` (`ProjectIndex.cs:145`) when the effects facet lands | same | with E5 | add the facet without the bump → gate 3's index bytes move silently |
| P25 | `EffectSummaryIsIndexIndependent` — a **fresh-clone `calor build`** with no `obj/calor` present produces a complete summary; plus a structural pin that no `Effects/` or `Incremental/` file references `ProjectIndex` | `Calor.Compiler.Tests/Incremental/` | ~~before E5~~ **with E5**. *Disclosed as a late landing by the bound party: P25 was scheduled before E5 and E5 landed it; nothing between E4 and E5 depended on it, and the E5 PR is the first change that could have violated it* | derive the summary from the index → the fresh-clone build fails |
| P26 | `NoNameKeyedEffectStoreRemains` — grep pin, `EffectSummaryBuilder.cs:68,:75` keys gone | same | with E5 | re-introduce one name key |

> **EXECUTED — v0.15 E5.** Status of every pin this slice owed (all in
> `tests/Calor.Compiler.Tests/`; there is no `Incremental/` subdirectory — P23's home,
> `IncrementalCliBuildTests.cs`, is where the four live):
>
> | Pin | Status | Test |
> |---|---|---|
> | **P23** | **LANDED** — `"4.0"` (the summary's caller entries are keyed by structural id, P26, so `BuildFileEntry.EffectSummary`'s shape changed); semantics stamp `calor-compile-semantics-v1` and options stamp `compile-inputs-v3` frozen | `BuildStateCacheConstants_FormatBumpedByE5_SemanticsAndOptionsFrozen` (renamed from `_AreUnchangedByEffectRows`) |
> | **P24** | **LANDED** — `"4.0"`, and the facet is asserted to be IN the serialized bytes (`"EffectRows"`, `"Verdict"`, `"EffectRowsUnavailable"`) | `ProjectIndexFormatBumped` |
> | **P25** | **LANDED**, both legs: a CLI `--cache` build of three files (one cross-module call) in a directory with no `obj/` and no `.calor-index.json` anywhere writes a complete, symbol-keyed summary for every file and creates no index; and every `.cs` under `Effects/` and `Incremental/` is scanned for `\bProjectIndex\w*\b` on any line, comments included | `EffectSummaryIsIndexIndependent`, `EffectsAndIncrementalLayers_DoNotReferenceProjectIndex` |
> | **P26** | **LANDED**, three legs: reflection (`EffectCallerSummary` has `CallerId` + `DisplayName`, no `CallerName`; `RawCall` has `CallerId`), a source regex over `EffectSummaryBuilder.cs` (groups by `call.CallerId`, never passes `.Name` as `callerId:`), and the behavioural discriminator — two overloads `Box.Run(i32)` / `Box.Run(str)` are TWO caller entries (`Box.m001`, `Box.m002`) with one display name, where the name key made them one | `NoNameKeyedEffectStoreRemains` |
> | gate 7 | **LANDED** — ten `effects` / `impact-effects` goldens authored from the fixture (`tests/TestData/QueryCorpus/project/app.calr` and `contracts.calr`, extended), every verdict and a firing code exercised (`TheEffectsGoldensExerciseEveryVerdict`, anti-vacuity). Review round 1 added the two that discriminate what the first eight could not: **`Whisper`** (under-declaring caller of an EFFECTFUL cross-file callee — deleting the cross-module fold turns it green-to-red; `Run`'s callee is pure, so folding pure changed nothing) and **`Map<eff e>`** (the inferred row keeps its VARIABLE part; dropping it, in the builder or in the pass's bookkeeping, reads `[pure]`) | `QueryGoldenTests` |
> | E4's obligation | **half discharged** — `FunctionBoundType.Row` has a production reader (`IndexedEffectRow.BoundRow`), pinned equal to the `§E` node's row wherever no `eff` variable is mentioned and `[unknown]` where one is; the invocation-row span-matching stays registered (roadmap §4.2 E5) | `ProjectIndexTests.BoundPositionRow_AgreesWithTheDeclaredRow_WhereTheBinderDoesNotCollapse` |
>
> **No ledger moved.** The effects ground truth was appended to the existing
> `QueryCorpus/project/app.calr` and `contracts.calr` rather than committed as a new file, so
> the 886-file corpus §3.2/§9 quote — and `higher-order-demand-ledger.json`,
> `binder-incomplete-baseline.json`, `effect-resolver-key-ledger.json`,
> `formatter-corpus-baseline.json` — are byte-identical to main. (A first cut added an 887th
> file; every one of those instruments went red on the count alone, which is what they are
> for.) `transcripts/facts.txt`: the `IsSubsetOf` site moved `:401` → `:600` (phase 5's record,
> fields and variable-charge bookkeeping sit above `CheckEffects`); its count stays two — phase 5
> uses `Except()`, which is empty exactly when that subset test passes, so P16's structural pin
> still reads 2. `o53/baseline.json` re-stamped by the regeneration script, as every prior slice
> did. **The §9 "0 of 359 function-typed `.calr` goldens" moves to 1** (`facts.txt` `count: 0` →
> `1`; whole-corpus function-typed positions 5 → 7): review round 1 required a rank-1 function IN the golden corpus (`Map<eff e>`, then `Twice<eff e>` in round 2), because
> the in-process pin had hidden that the inferred row lost its variable part. §3.2's same-line
> sweep (`EffectRowCorpusShapeTests`) carries `app.calr` on a reasoned allowlist — authored
> under Decision 1, not a regression of it — and §9's row says so.

| P27 | `SpikeVerdictMatchesRecomputation` — recomputes `gCodegen` (via P28) and the R1 leg; shape-checks `schemaVersion`/`measuredCommit`; asserts R2/R3 are present and well-formed but **does not re-derive them** (§12.3) | `Calor.Compiler.Tests/Effects/SpikeVerdictTests.cs` | **spike PR** | edit a verdict field, or flip an A3 fixture to need `--permissive-effects` |
| P28 | **`GCodegen_BeforeAfterEmittedCSharpIsByteIdentical`** — the pin G-CODEGEN never had. Re-emits A1's and A2's `before/`/`after/` `.calr` and diffs the `.g.cs` byte-for-byte modulo trailing whitespace. §9's "0 `.cs` goldens" and §8.5's "semantics stamp unchanged" both rest on it | `SpikeVerdictTests.cs` | **spike PR** (blocking, feature-wide) | make a row change codegen → red, and E2 does not ship |
| P29 | **`ExperimentTranscripts_MatchARerun`** — re-runs all six harness scripts and diffs against the committed transcripts. Closes the round-2 finding that ~40 quoted outputs were reproducible but unobserved. **Never skips**: a missing compiler build is a hard failure | `tests/Calor.Compiler.Tests/Effects/EffectRowExperimentHarnessTests.cs` — **landed in this PR** | **before E2** (already frozen) | reword any diagnostic the doc quotes → red, naming the script and the first differing line |
| P30 | **`O53Baseline_HasLedgerShape_AndTheCountsTheDocQuotes`** — `o53/baseline.json` gains `schemaVersion` + `measuredCommit` (40-hex, shape-checked not compared to HEAD, per `HigherOrderDemandLedgerTests.cs:480-498`) and the test asserts 23 files / 54 occurrences / 1 green / 22 red **and the 18+3+1 breakdown** §3.2 quotes | same — **landed in this PR** | **before E2** (already frozen) | change any count → red |
| P31 | **`SpikeArtifactManifestIsComplete`** — for every artifact in `spike-verdict.json`, the `before/`/`after/` `.calr`, `.g.cs` and diagnostic list exist, are non-empty, and the diagnostic list parses. Replaces v2's unsound decline of the presence check (P27 would pass with every artifact missing) | `SpikeVerdictTests.cs` | **spike PR** | delete one `.g.cs` → red |
| P32 | **`Calor0425CorpusLedgerMatchesRecomputation`** — §13.4's ledger, exact-equality per subject and per cause, `measuredCommit` shape-checked. The only instrument v2 left without a P-number | `tests/Calor.Compiler.Tests/Effects/Calor0425CorpusLedgerTests.cs` (`compiler` shard, `Skip.IfNot` on submodules, registered in `eng/test-manifest.json`) | ~~before E2~~ → **with E3** (see the note below) | change one per-subject count → red |

> **EXECUTED — E3 slice b, PR #1106.** Status of every pin this slice owed:
>
> | Pin | Status | Home |
> |---|---|---|
> | **P14** | **LANDED.** Four cases plus two the pin did not name: an omitted row is not merely inferred but CHECKED at the binding site, and an annotated lambda's TYPE carries ρ_decl not ρ_body | `Calor.Enforcement.Tests/EffectRowLambdaTests.cs` (new) |
> | **P15** | **LANDED for all six.** The site-6 gap pin is flipped from `..._IsSliceBs_AndTheGapIsObserved` to `RowMismatch_AtGenericInstantiation_IsError`, with `_Compiles` and `_CannotTell` halves. **Its code is Calor0410, not Calor0424** — see §7's status block; the class is closed, the cell's code moves | `StrictnessBatchTests.cs` |
> | **P18** | **LANDED**, ordinal cases: `EffVariableOrdinal_AlphaEquivalent`, `..._UnifiesAcrossInterfaceAndImpl`, and a discriminator proving the ordinal is relative to the `eff` list and NOT to `EffectParameterInfo.Ordinal`'s combined position | `EffectVariableTests.cs` |
> | **P22** | **LANDED** for §10.3's two strings and §6.4's third sample, all by FULL equality. §10.3's second string ships with a **different tail** from the doc's sample: the doc says the caller "is charged Unknown effects", and charging `unknown` would raise a Calor0410 no author can declare away, so the shipped text says what actually happens — nothing is charged. §10.1's string remains E4's | `StrictnessBatchTests.cs` |
> | **P23** | **LANDED**, with `CurrentCompilerSemanticsVersion` frozen explicitly against G-CODEGEN | `Calor.Compiler.Tests/IncrementalCliBuildTests.cs` |
> | **P32** | **REGENERATED**, 0 → 4 (serilog 1, FluentValidation 3), cause bisected to the external-base Calor0419 retirement and to nothing else | `Calor0425CorpusLedgerTests.cs` |
>
> **Gate 1's denominator is now six closed classes**, with the sixth spelled Calor0410 + §10.3's
> provenance clause rather than Calor0424. The §7.5 ramp did not fire, so P18 and P15's site-6 pair
> both ship.
>
> A seventh pin lands that §13.2 did not name, because §8.2's bound reader had no pin at all:
> `CSharpDeclaredDelegateParameter_IsASite_ThroughTheBoundFunctionType`, both polarities.

> **EXECUTED — v0.15 E4.** Status of every pin this slice owed or touched:
>
> | Pin | Status | Home |
> |---|---|---|
> | **P22** | **LANDED for §10.1's string**, by FULL equality, with two corrections to the doc's sample recorded in §10.1 (`(row: cw)` bare; `Function 'Bump'`). Plus the invocation-Unknown and invocation-Assumed strings E4 adds, both by full equality | `StrictnessBatchTests.cs` (appended past `:1691`) |
> | **P27** | unchanged; `transcriptDivergences.e2Obligation` still holds seven rows | `SpikeVerdictTests.cs` |
> | **P29** | **REGENERATED** — obligations **#2 (X9b)** and **#3 (X9c)** discharged, and every other moved line accounted for in §13.5's E4 block | `EffectRowExperimentHarnessTests.cs` |
> | **P32** | **REGENERATED, 4 → 8**, schema 1 → 2 with §13.4's widened taxonomy; every moved module named in §13.4's E4 block and three spot-checked by hand | `Calor0425CorpusLedgerTests.cs` |
> | gate 2's ledger | **REGENERATED** — `dA.calor0418` 1 → 0, `dA.total` 3 → 2, `demandTotal` 3124 → 3123 (floor 25 untouched); the one file is the corpus differential's one file | `HigherOrderDemandLedgerTests.cs` |
> | **M1 / A-1.11.1** | **PP-E1's negative control RESTORED**: `PpE1NegativeControls_MatchA1111Baselines_PreE4` flipped to `_PostE4` on the registered post-E4 multisets; `A3Fixtures_AreExactlyCalor0418PerInvocation` → `A3Fixtures_AreExactlyZeroCalor0418_PostE4`; **new** `PpE1_L7RowErasureMutants_DrawCalor0425AtTheRegisteredInvocation_PostE4` applies each of A-1.11's five L7 diffs textually and asserts the Calor0425 at the registered invocation rises above the unmutated fixture's zero | `SpikeVerdictTests.cs` |
> | new, unnumbered | the residual Calor0418 read off the catalogue; §4.5's design question, both policies | `DiagnosticCodeTests.cs`, `StrictnessBatchTests.cs` |
>
> Not a pin, recorded: `Calor0425CorpusLedgerTests` now prints every site it counts
> (`Calor0425-corpus site <subject>/<file>(line,col): <message>` under the detailed logger), so
> the next regeneration can be spot-checked the way this one was.

> **P17 and P32's freeze column was impossible, and is corrected to "with E3"** (E2 slice b,
> review round 1 MINOR 12). Both were frozen "before E2", and **neither can exist until E3**:
> P17's name asserts *Calor0425*, and P32 is the *Calor0425* corpus ledger. Calor0425 is allocated
> by §6.1 but **no code path emits it** — slice b builds the relation, E3 emits the diagnostic. A
> pin cannot be frozen before the diagnostic it asserts exists, so the column said something that
> could not be done rather than something that was skipped.
>
> Slice b lands the **substitutable half of P17** — the part that does not need a diagnostic:
> `EffectRowLatticeTests.ManifestResolutionStatusMapsToRow` asserts that
> `EffectSet.Unknown.ToRow()` is `EffectRow.Unknown` and never `Concrete(∅)`, which is the mapping
> P17 exists to protect, and `UnknownRow_FitsNothing_AndIsFittedByNothing` (P8) asserts that such
> a row can never yield `Fits`. What is **still owed** is P17's own sentence — that an unresolved
> RECEIVER, reaching the effect pass, produces that row and surfaces as Calor0425 at a site — and
> P32 entirely. Naming this rather than treating the substitutes as P17 is the point: the two
> pins are E3's, and E3's PR body must say so.

> **EXECUTED — E3 slice a, PR #1103.** Status of every pin this slice owed:
>
> | Pin | Status | Home |
> |---|---|---|
> | **P11** | **LANDED.** `NeverWaived_DoesNotFit_AtEveryMonomorphicSite` (all three DoesNotFit codes stay ERRORS under the flag, including **Y8a's flip**), `PermissiveWaivesUnknown_BothPolarities`, `StrictEffectsRaisesCalor0425ToAnError` | `StrictnessBatchTests.cs` |
> | **P12** | **LANDED.** Two fixtures: the mismatch, and the Calor0410 that catches the "fix" of deleting the source's `§E` | `EffectRowLatticeTests.cs` |
> | **P13** | **LANDED**, on `EffectRow.FitsFunction` — contravariance, covariance, type invariance, and Unknown's absorption, both polarities each | `EffectRowLatticeTests.cs` |
> | **P14** | **NOT LANDED — slice b's.** ρ_body needs the effect pass to compute a lambda's inferred row; in slice a an un-annotated lambda's row is Unknown | — |
> | **P15** | **LANDED for five of six.** Site 6 gets `RowMismatch_AtGenericInstantiation_IsSliceBs_AndTheGapIsObserved`, which asserts the class is **not** closed | `StrictnessBatchTests.cs` |
> | **P16** | **LANDED.** The structural half counts the surviving `IsSubsetOf` occurrences under `Effects/` — **two**, neither a compatibility site — plus two behavioural halves on the cross-module site | `CrossModuleEffectTests.cs` |
> | **P17** | **LANDED**, with a rowed control so an unconditional emitter cannot pass it | `EffectRowLatticeTests.cs` |
> | **P21** | **LANDED**, plus a literal pin on §6.1's code allocation | `EffectResolverTests.cs` |
> | **P22** | **LANDED** for §6.4's first and second samples, as FULL message equality. §10.1's and §10.3's strings are E4's and slice b's | `StrictnessBatchTests.cs` |
> | **P32** | **LANDED, and it reads ZERO** — 0 Calor0425 across MediatR (26 modules), Serilog (47) and FluentValidation (26). §13.4's worry is answered NO at slice a; if the hundreds come they come with E4 | `Calor0425CorpusLedgerTests.cs` |
>
> **P10 gains its diagnostic half** — `AssumedSource_ReportsExactlyOneCalor0425_AtTheHop` and
> `EveryUndecidableHopReportsExactlyOnce` — but §4.4's **two-hop** shape has no source-level witness
> in slice a and the file says so rather than implying otherwise: the only producer of an `Assumed`
> row is a method group, and the binder rejects a bare method group as a `§B` initializer
> (Calor0200). A lambda whose ρ_body is `Assumed` is the spelling that reaches it, and that is
> slice b's.
>
> **Everything appended to `StrictnessBatchTests.cs` sits past line 745** on purpose:
> `facts.py` probes that file by line number and §13.5(a) permits E3 exactly one regeneration.
> Verified — `facts.py`'s output is byte-identical to its committed transcript apart from the
> `IsSubsetOf` sweep.

> **Why P19/P20 do not live in `BoundTypeTests.cs`** (E2 slice b). `facts.py` probes
> `grep -n 'DisplayString' tests/Calor.Compiler.Tests/Binding/BoundTypes/BoundTypeTests.cs` and pins
> the result as a transcript line. A pin named `DisplayStringIsRowFree` in that file moves that
> probe, and §13.5(a) permits exactly the transcript changes it names. The assertions are
> unchanged — only the file differs — and `BoundTypeTests.cs:139`/`:150` remain the belt this is a
> brace for. Re-homing is free the next time that transcript legitimately moves, and is E3's to
> take if it moves the sweep.

### 13.3 Gate rows

| Gate | Instrument | Freeze | Discriminating pin |
|---|---|---|---|
| **1** laundering, closed classes | P15's **six** `_IsError`/`_Compiles` pairs (five if the ramp fires) — the denominator is exactly those pairs, identified by **code and polarity**. **Diagnostic *span* is explicitly outside the frozen denominator** (see §14 Q4): gate 1 observes *which classes are closed*, and a class is closed or not regardless of where the message points. Span is pinned by P22, which freezes with **E3**, so §14 Q4 can still be settled by §13.4's measurement without reopening a design-doc-merge freeze | **design-doc merge** | delete E3's rule for one class |
| **2** higher-order expressiveness | `HigherOrderDemandLedgerTests.cs` re-executed at the release commit. It asserts **exact equality** on `Calor0418`, `Calor0419FunctionTyped`, `Total`, `PerFile` and `NotReachingEffectPass` (`:192-199`), so driving 0418 to zero turns it **red** — it is *not* "extended, not rewritten" as Draft v1 said. **The ledger is regenerated in the E4 PR with the delta and its cause disclosed**, which is what the test's own failure message already instructs: *"The ledger is the frozen denominator — regenerate in this PR and name the cause (§4.4 gate 2 discriminating pin)."* | ledger registration PR (#1086) | re-introduce the 0418 rejection for one class |
| **3** surface agreement | `EditScriptIdentityTests` + **ES-08**; plus three legs Draft v1 omitted: a **CLI-process** leg and a **`Calor.Sdk`** leg over the same scripts, and a test enumerating the **four entry points' default `UnknownCallPolicy`**. The F-3 supersession that had to precede ES-08 **already merged** (`b5d61e18`, PR #1085) | each leg registered **before E2** | drop ES-08 → `RegisteredScriptIdsAreStable` fails; flip one surface's default → the equivalence test fails |
| **4** the probe (PP-E1, annex A-1.11 / A-1.11.1) | `bench/phase0-agent-native/effect-rows-probe-ledger.json` read by exact equality in `EffectRowsProbeLedgerTests.PpE1LedgerMatchesRecomputation` (`compiler` shard): leg A **recomputed** (each of the ten frozen diffs re-applied to its frozen fixture, compiled with the pinned flag-free invocation via the shared `PpE1Probe`, codes and declarations compared; negative control against A-1.11.1's post-E4 multisets; routes (a)/(b) recomputed), leg B **recorded** (only its arithmetic recomputed by `bench/phase0-agent-native/ppe1-analyze.py` from the archived `result.json` files into `epochs/e1-rows-parity-001/ppe1-analysis.json`; epoch runner `run-ppe1-epoch.sh`, refuses without `--confirm-paid-epoch`), verdict **derived** from the frozen map in its precedence. **Status, 2026-08-27: instrument BUILT (PR #1109); leg A recomputed 10/10 at `758d86843cdf413bace73ed33005ffc2873f036f`, clean control, no drift; leg B not run; verdict NOT-ADJUDICATED pending the release-commit epoch** (`docs/plans/2026-08-27-v0.15-ppe1-instrument-notes.md`) | A-1.11, before E2 merged | dropping any registered mutation from the ledger, or hand-writing a verdict leg B's recorded state does not imply, fails the exact-equality test |
| **5** compatibility over the corpus | leg (a) what CI compiles today — `tests/TestData/Benchmarks` (226, pinned `>= 200`), `samples/` (11), every `.calr` a test project compiles; leg (b) the remainder of the 886 via a **`compile-all-committed-calr` job registered before E2**. **E1-attributable firings separated**: E1 resolves callees string-guessing missed and fires new, correct Calor0410/0419, which are fixed in-corpus and counted. **0.15-specific additions**: the Calor0410s that *disappear* from §4.1's `Subtypes` widening are listed by name; the §3.2 line-adjacency baseline (`o53/baseline.json`, 23 files, 1 green today) is re-run; §4.5's 0420/0421 de-demotion is confirmed to break no committed file | branch cut | revert one in-corpus fix → leg (a) red |
| **M1** (roadmap §4.2) — *not a gate, a merge precondition* | Roadmap §4.2: *"No effect-row implementation (E2) merges before M1 is done."* v2 omitted it from the before-E2 chain entirely. Status: **COMPLETE.** PR #944 dispositioned and #881 addressed (PRs #1090/#1091/#1092); the annex append-only **guard** half landed as **A-1.10**; the **0.15 PP row registered as `PP-E1` at A-1.11 (2026-08-25)** — the freeze event, verified against an empty `grep -rn "Calor0424" src/` at `f7cd1c46`. Its denominator is the five §12.1 spike fixtures with ten injectable mutations (`L5` interface implementation, `L6` rank-1 instantiation, `L7` row erasure), bar 10/10 with a clean negative control, plus a cost leg whose margin re-derives on `w5-parity-002` to point 1.35 ∧ bootstrap lower bound > 1.0. **Correction, 2026-08-26 — annex sub-entry `A-1.11.1`:** leg A's negative control is re-frozen under the pinned invocation. A-1.11's A2 baseline was transcribed from `after/A2.diagnostics.txt`, recorded with `--permissive-effects` (§12.4 caveat 4) — the flag that same row forbids — so it was never reproducible. A2's baseline is now the measured no-flag multiset at `9119397e` (1× Calor0410 (23,9) + 2× Calor0411 + 1× Calor0418 (27,27)), with the Calor0418 registered as **E4's**: post-E4 the expected multiset drops it, and that post-E4 multiset is the binding one. The four A3 fixtures' "exit 0, zero diagnostics" (A-1.11's words) stands as the post-E4 expectation, their pre-E4 Calor0418 counts recorded. **Until E4 merges, leg A is a MISS under A-1.11's own-goal clause.** Pin: `PpE1NegativeControls_MatchA1111Baselines_PreE4` | **before E2 merges** | E2's PR body must cite **A-1.11**; without it, M1 is unmet and E2 does not merge. **E4's PR must flip the A-1.11.1 pre-E4 pin to its registered post-E4 multisets** |
| **6** resolution floor | `MetadataBinderCorpusMeasurementTests.cs:37-118`, two-sided exact equality | v0.14.3 values | the test as it stands |
| **7** index/query effects leg | `QueryGoldenTests` + the `effects` golden authored (not recorded) per `EveryGoldenStatesWhyItExists` (`:152-172`). **LANDED (E5):** ten goldens over `QueryCorpus/project/app.calr` (+ `contracts.calr`) — `effects` (Leaky does-not-fit/Calor0410/cw with `§E{}` written; Log fits; Quiet omitted-row-is-pure, `written=false`; AsksMissing cannot-tell yet Calor0410, partial; Run cross-file pure callee; **Whisper** — the cross-module FOLD, an under-declaring caller of an effectful callee in another file, red when the fold is deleted; **Map<eff e>** — the inferred row keeps its variable part, red when it is dropped) and `impact-effects` (row `fs:w` → all three transitive callers stop fitting, incl. Fan through Relay; row `cw` → only Leaky; row pure → none) | E5 PR | alter one expected effects answer |

### 13.4 The Calor0425 corpus ledger — a decision, not an open question

> **Decision.** `bench/phase0-agent-native/calor0425-corpus-ledger.json` is registered **before E2
> merges**, in the shape of the two existing ledgers: per subject (MediatR, Serilog,
> FluentValidation), split **by cause** — unresolved receiver / row-less function-typed
> declaration / BCL-returned delegate — with `measuredCommit`, and re-executed by **P32
> `Calor0425CorpusLedgerMatchesRecomputation`**, home
> `tests/Calor.Compiler.Tests/Effects/Calor0425CorpusLedgerTests.cs`, on the `compiler` shard
> (which already opts into submodules), `Skip.IfNot` on `bench/corpus/` with the skip registered
> in `eng/test-manifest.json`'s `expectedSkipped` so a silent skip trips the count.
>
> **A fourth split — "declared but never invoked" vs "invoked"** — ships with the ledger, because
> that is exactly the number §14 Q4 needs.

> **EXECUTED — E3 slice a, PR #1103. The number is ZERO, and the denominator is 27% of the
> corpus.** Both halves matter and the second was added by review round 1 (F7), because a zero
> quoted without its denominator is the failure mode this ledger exists to prevent.
>
> | Subject | Calor0425 | enforced | excluded | excluded: convert / parse / **bind** | Calor0418 witness |
> |---|---|---|---|---|---|
> | MediatR | **0** | 26 | 10 | 0 / 3 / **7** | 2 |
> | serilog | **0** | 47 | 65 | 0 / 8 / **57** | 1 |
> | FluentValidation | **0** | 26 | 190 | 0 / 48 / **142** | 1 |
> | **aggregate** | **0** | **99** | **265** | 0 / 59 / **206** | 4 |
>
> Per-cause split: `RowlessDestination` 0, `UnknownSource` 0, `Assumed` 0. Fourth split
> (invoked / never invoked): 0 / 0.
>
> **265 of 364 modules — 73% — never reach the effect pass**, 206 of them because the Lossy
> conversion does not BIND. Modules that fail binding are excluded on purpose: `Program.Compile`
> returns as soon as binding has errors, so their Calor0425s could never be emitted and counting
> them would inflate the ledger with diagnostics no user can see. But the consequence is that
> **the zero is a zero over the 27% that binds**, not over the corpus, and FluentValidation
> contributes 26 enforced modules against 190 excluded. The exclusion count and its
> reason histogram are now ledger fields, pinned by exact equality, so the rate cannot drift
> unobserved.
>
> **The anti-vacuity witness is weak, and is written down as weak.** Four Calor0418 across all
> three subjects (2/1/1) establishes that the pass reached higher-order code *at all*. It does
> **not** establish that the measured subset is representative. An earlier revision of the test's
> own comment claimed "Calor0418 fires in the hundreds", which was false by two orders of
> magnitude; corrected.
>
> **What the zero does and does not answer.** It answers §13.4's question for *slice a*:
> the five binding sites cost converted code nothing, because converted code hands function values
> to BCL callees — which have no destination row (§8.4) — rather than to in-module rowed
> positions. It does **not** forecast E4, whose Calor0418 replacement puts a row check at every
> invocation, and whose PR must both widen this ledger to §13.4's three-way cause split and
> re-measure against a denominator this small.

> **EXECUTED — v0.15 E4. The number is EIGHT, over the same 27%, and the taxonomy is widened
> (schema 2).** Re-measured with the same denominator (99 enforced of 364; exclusion histogram
> unchanged at 0 / 59 / 206):
>
> | Subject | Calor0425 | rowless dst | unknown src | assumed | **external base** | **invocation: rowless / undetermined / assumed** | invocation witness |
> |---|---|---|---|---|---|---|---|
> | MediatR | **2** | 0 | 0 | 0 | 0 | **2** / 0 / 0 | 2 |
> | serilog | **2** | 0 | 0 | 0 | 1 | **1** / 0 / 0 | 1 |
> | FluentValidation | **4** | 0 | 0 | 0 | 3 | **1** / 0 / 0 | 1 |
> | **aggregate** | **8** | 0 | 0 | 0 | **4** | **4** / 0 / 0 | 4 |
>
> **The taxonomy revisit E3b asked for, done.** Two new buckets and one re-labelling. (a)
> `ExternalBase` — E3b's four sites, which the ELSE arm had filed as `UnknownSource` although
> they are not a source row at a binding site at all (§6.4's third sample; the override arm
> reads "overrides a member of external base class …, which is not visible in this module").
> `UnknownSource` now reads **0**, honestly. (b) `InvocationRowless` / `InvocationUndetermined`
> / `InvocationAssumed` — the three verdicts an invocation can draw, bucketed on the clause the
> message quotes. This IS §13.4's "row-less function-typed declaration" and "BCL-returned
> delegate", seen from the invoking side where E4 makes them cost something. §13.4's third
> cause, "unresolved receiver", is deliberately **not** a bucket: an invoked value whose type
> came from an unresolved receiver never reaches Calor0425 — the bare-target guard routes it to
> Calor0411 (E1 slice 2c), the older fail-closed path, and this ledger counts Calor0425 only.
>
> **The four invocation sites are exactly the four pre-E4 Calor0418 witnesses (2/1/1)**, under
> their new code, and three were checked by hand against the C# source: MediatR
> `Pipeline/RequestPostProcessorBehavior.cs:22` `await next()` on a row-less
> `RequestHandlerDelegate<TResponse> next`; serilog `Core/Filters/DelegateFilter.cs:29`
> `_isEnabled(logEvent)` on a row-less `Func<LogEvent,bool>` field; FluentValidation
> `InlineValidator.cs:47` `ruleCreator(this)` on a row-less `Func<…>` parameter. In each, nothing
> in the module states what the value does, so *cannot tell* is the honest code. The witness is
> renamed `InvocationWitness` (= the sum of the three invocation buckets) and asserted `> 0`, and
> is exactly as weak as `Calor0418Witness` was — it says the pass reached higher-order code, not
> that 27% is representative.
>
> **What the eight say about §13.4's worry.** Still not "hundreds": converted code invokes
> function values rarely in the modules that bind, and each such invocation is one warning plus
> one fail-closed Calor0410 under the default policy — the same cost as one unknown BCL call.
> §14 Q4's fourth split is unchanged at 0 / 0 because no `RowlessDestination` exists in the
> measured set.

Draft v1 left this as "open question 1" with the instrument being a promise that the E2 PR would
publish a count. That is the failure mode §13.3 gate 2 exists to prevent. The number matters
because 431 of 1248 BCL call sites do not resolve (§2.2): if the 0425 count per subject is in the
hundreds, rows are ergonomically *worse* than Calor0418 for converted code and
`--permissive-effects` becomes mandatory rather than exceptional — which would make §4.5's
"strictly less powerful waiver" decision land badly. Registering the ledger makes that visible
before E2 rather than after.

### 13.5 E2 obligations the spike created

The spike PR carries **no `src/` change**, so nothing below is red today. Each item is a
commitment E2 inherits, stated here so it cannot be discovered late.

**(a) Exactly SEVEN transcript regenerations, and no others.** With the prototype in the build
`ExperimentTranscripts_MatchARerun` (**P29**) is red on seven cases across three scripts;
`run2.py`, `facts2.py` and `compile53.py` are clean. They were **recorded, not regenerated** —
the transcripts are E2's frozen evidence base and a throwaway prototype does not rewrite them.

| # | Script / case | Committed | With rows | Why |
|---|---|---|---|---|
| 1 | `run.py` **X6a** (`§F{…}<T, U, eff e>`) | exit 1; a **14-line** `Calor0100`/`Calor0114` cascade from `Expected Greater but found Identifier` | the type-parameter list parses; the case's own `§E{}` then under-declares its body → one `Calor0410 … uses effect 'alloc'` | **Intended.** X6a exists to show `eff e` is new syntax *today* (§7.2). E2 must **re-author** it as a positive case, not merely regenerate it |
| 2 | `run.py` **X9b** (`§FLD{Action<i32>:onChange:pri} §E{cw}`) | exit 1; **4×** `Calor0100 … but found Effects` | **exit 0**, `Compilation successful` | **Intended** — **position 8** doing what §3.3 promises. X9b is the case §14.1 cites to disprove Draft v1's claim that `§FLD` already parsed a row; once position 8 lands it *must* compile |
| 3 | `run.py` **X9c** (`(Func<i32,i32>:transform §E{cw}, i32:value)`) | exit 1; **12×** `Calor0100` from `Expected CloseParen but found Effects` | **exit 0**, `Compilation successful` | **Intended** — **position 5**, §3.3. Same disposition as X9b |
| 4 | `run3.py` **Z1** (`§FLD{i32:x:pri}` ⏎ `§E{cw}`) | **4×** `Calor0100` cascade | one `Calor0405` naming the `x` field line | **Intended** — §3.1's row-aware recovery. The 4→1 collapse is exactly the claim **P2(b)** makes |
| 5 | `run3.py` **Z3** (wrapped inline signature) | **8×** `Calor0100` through the rest of the signature and body | one `Calor0405` naming the `transform` parameter line | **Intended**, and the strongest Calor0405 case: §3.1 calls Z3 *not hypothetical* because **Z5** shows wrapped parameter lists really are written. 8→1 |
| 6 | `facts.py` line 6 | `Ast/FunctionNode.cs:252` | `Ast/FunctionNode.cs:283` | **Unavoidable.** `facts.py` pins `file:line` structural facts; adding `ParameterNode.Row` moves the line, and any E2 implementation moves it too |
| 7 | `facts.py` — the `IsSubsetOf` compatibility-site sweep | `EffectEnforcementPass.cs:533` and `:571` are listed | both **vanish**; five `EffectSet.cs` lines appear instead | **Intended, and it is R2's own evidence moving a pinned fact.** §6.3 says those two calls "become calls to the shared `EffectRow.Fits`"; this sweep is the instrument that observes it, so when the claim comes true the transcript *must* change |

**If E2's regeneration moves any other line, that is a behaviour change the spike did not make,
and it needs its own justification in the E2 PR body.**

> **EXECUTED, E2 slice a, PR #1101 — FIFTEEN moved items, not seven, and every extra is
> accounted for.** Counted from a full diff of all six transcripts, not from this table:
> `run.py` **5** (X2a, X2b, X6a, X9b, X9c), `run2.py` **3** (Y1b, Y3a, Y5a), `run3.py` **5**
> (Z1, Z2, Z3, Z9, Z9b), `facts.py` **2** file:line probes — **13 named cases plus 2 probes**.
> The `IsSubsetOf` row below is the sixteenth line of the table and did **not** move; it is
> listed because the spike obliged E2 to say what happened to it, not because anything changed.
> (Review round 1 caught this table being read as if every row were a divergence — the same
> summary-instead-of-measurement error §13.5(a)'s own closing note warns about. Re-derived from
> the diff.) The seven above were produced by a prototype that implemented **only**
> positions 5 and 8 (`spike-verdict.json.prototype.notImplemented`). Slice a implements **all
> eight**, so every case that exercises a position the prototype skipped moves too. The full
> accounting, by script:
>
> | Case | Obligation | Result |
> |---|---|---|
> | `run.py` **X6a** | #1 | **as predicted** — one `Calor0410 … uses effect 'alloc'`, verbatim |
> | `run3.py` **Z1** | #4 | **as predicted** — 4 → 1 Calor0405 naming the `x` field line |
> | `run3.py` **Z3** | #5 | **as predicted** — 8 → 1 Calor0405 naming the `transform` parameter line |
> | `facts.py` `ParameterNode` probe | #6 | **as predicted** — `FunctionNode.cs:252` → `:283`, the same line the prototype produced |
> | `run.py` **X9b** | #2 | **partially** — the 4× `Calor0100` cascade is gone (position 8 lands), but the case does **not** reach `exit 0`: it reaches today's `Calor0418`, because accepting the invocation needs row **checking**. The syntax half is discharged; the acceptance half is E3's |
> | `run.py` **X9c** | #3 | **partially** — same, for position 5: 12 → 1, `Calor0418` not `exit 0` |
> | `facts.py` `IsSubsetOf` sweep | #7 | **not moved** — correctly. It observes `CheckEffectVariance` routing through `EffectRow.Fits`, which is E3's change |
> | `facts.py` `ClassFieldNode` probe | — | `ClassNodes.cs:554` → `:564`. Same unavoidable mechanism as #6; the spike's table listed only the `ParameterNode` line because its prototype touched `ClassFieldNode` differently |
> | `run2.py` **Y1b** | — | `Compilation successful` → `Calor0410`. **This is pin P1's own case**, named in §13.2. Position 4 |
> | `run2.py` **Y5a** | — | same flip, arrow spelling. §3.2 names Y5a as one of the three forms this decision knowingly breaks |
> | `run.py` **X2a**, **X2b** | — | same flip / same code with a moved span, `§O` spelling. §3.2 names X2b |
> | `run3.py` **Z9**, **Z9b** | — | `Compilation successful` → `Calor0410`. These are §3.5's non-function-typed cases, whose **final** answer is Calor0405. Slice a consumes the row but does not yet check function-typedness (pin **P6**), so they land on 0410 in between. Slice b moves them to their final state |
> | `run2.py` **Y3a** | — | 16 × `Calor0100` → compiles. **Position 7**, the `§B` row, which §3.3 lists and the prototype did not implement |
> | `run3.py` **Z2** | — | 4 × `Calor0100` → 1 Calor0405. Position 7's recovery — listed explicitly by **P2(b)**, absent from the spike's table only because the prototype had no position 7 |
>
> `run2.py`'s remaining cases, `facts2.py` and **`compile53.py` are CLEAN** — so
> `o53/baseline.json`'s 23 files / 54 occurrences / 1 green / 22 red are **unchanged**, which is
> gate 5's line-adjacency leg discharged.
>
> **Two defects were found this way and fixed; both were the same mistake.** A first draft put
> the Calor0405 recovery in the statement and class-member **loops** rather than in the
> productions that own a row. That made it fire on any stray `§E`, and two cases proved it wrong:
> **Z10** (`§R §C{Helper} §A INT:1 §E{cw} §/C`) gained a Calor0405 inside an argument list, which
> §3.3 forbids outright — *"Arguments are values, not declarations; they have no row, and
> Calor0405 is not extended there"* — and **X4** had the function's own perfectly correct `§E{}`
> line reported as misplaced, because a broken `§O{str!str}` parse had left it in statement
> position. A "the row must start its line" guard fixed Z10's single-line spelling and hid the
> rest: review round 1 found the **multi-line** call (`§C{Helper}` ⏎ `§A INT:1` ⏎ `§E{cw}`) still
> reporting Calor0405, since that `§E` does start its line. Anchoring the recovery to the `§B` /
> `§FLD` production that owns the row closes all of it by construction, and **X4's and Z10's
> transcripts are byte-identical to the committed ones**.
>
> **A third came out of the same review.** A type carries at most one row, but nothing said so:
> `§I{str:m} §E{cw} §E{net}` was silently reading the first as the parameter's row and the second
> as the *declaration's*, via the `§F` loop's `§E` arm. Now one Calor0405, naming the repair
> (`§E{cw, net}`).

> **EXECUTED, E2 slice b, PR #1102 — NINE moved cases, all of them P6, and no other line in any
> transcript moved.** Counted from a full diff of all six transcripts. `facts.py`, `facts2.py`
> and `compile53.py` are **CLEAN** — in particular the `IsSubsetOf` compatibility-site sweep did
> **not** move, because slice b does not touch `IsSubsetOf` (E3 owns it, obligation #7), and the
> `Effects/*.cs` file-count row is unmoved because slice b adds **no** file under `Effects/`.
> `o53/baseline.json`'s counts (23 files / 54 occurrences / 1 green / 22 red) are unchanged; only
> its `measuredCommit` is re-stamped, which is gate 5's line-adjacency leg re-run.
>
> | Case | Committed (after slice a) | After slice b | Why |
> |---|---|---|---|
> | `run.py` **X2a** | `Calor0410` at (2,3) | `Calor0405` at (4,14), *"The return type 'VOID' is not a function type…"* | §3.5 / P6 — `§O{void} §E{cw}` |
> | `run.py` **X2b** | `Calor0410` at (2,3) | `Calor0405` at (4,14) | same, `§E{}` spelling |
> | `run2.py` **Y1a** | `Calor0410` at (5,5) | `Calor0405` at (3,15), *"'m' has type 'STRING'…"* | §3.5 / P6 — a row on a `str` parameter. **Not in §13.2's list of six**; same shape as Y1b |
> | `run2.py` **Y1b** | `Calor0410` at (2,3) | `Calor0405` at (3,15) | §3.5 / P6. **P1's old case** |
> | `run2.py` **Y1c** | `exit 0` | `Calor0405` at (3,15) | same shape, pure row. **Not in §13.2's list of six** |
> | `run2.py` **Y5a** | `Calor0410` at (2,3) | `Calor0405` at (2,36), *"The return type 'VOID'…"* | §3.5 / P6 — `-> void §E{cw}` |
> | `run3.py` **Z9** | `Calor0410` at (2,3) | `Calor0405` at (2,36), *"The return type 'VOID'…"* | §3.5 / P6, named by P6 |
> | `run3.py` **Z9b** | `Calor0410` at (2,3) | `Calor0405` at (3,15), *"'x' has type 'INT'…"* | §3.5 / P6, named by P6 |
> | `run3.py` **Z9c** | `exit 0` | `Calor0405` at (3,13) | §3.5 / P6, **named by P6's own row** in §13.2 and absent from the "six" only because its baseline is clean, not Calor0410 |
>
> **Each moved case is now ONE diagnostic, not two.** Calor0405 is reported by the binder, and
> `Program.Compile` returns as soon as binding has errors, so the consequential Calor0410 —
> which existed only because the row had been taken away from the declaration — does not fire.
> That is the same 4→1 / 8→1 collapse §3.1's recovery makes, arrived at by the pipeline's own
> ordering rather than by a suppression rule.
>
> **Two message details worth recording, because they are what a reader will notice first.**
> (1) The diagnostic quotes the BINDER's type vocabulary — `'STRING'`, `'INT'`, `'VOID'` — not the
> surface spelling `str`/`i32`/`void`. §3.5's illustrative message writes `i32`; the implementation
> writes what it actually knows. **The two return spellings do not arrive in that vocabulary
> equal**: `ExpandType` rewrites `§O{void}` to `VOID`, but the arrow form `-> void §E{cw}` reaches
> `OutputNode.TypeName` as the raw `void`, so the first revision of this slice reported the same
> mistake as `'VOID'` in one spelling and `'void'` in the other (review round 1, MINOR 5). The
> binder now runs `TypeIdentity.Canonicalize` **in the message only**, which gives one vocabulary
> across all nine cases. Expanding the arrow form in the parser would move
> `FunctionNode.Output.TypeName` for every arrow-form function in the corpus — a blast radius with
> nothing to do with rows. Y5a and Z9 were regenerated for this and nothing else.
> (2) It says *"Remove the `§E{…}`"* rather than quoting the author's codes. Quoting them needs
> the compact projection, which is an `Effects` table `Binding/` may not reach (§4's deviation
> note). Naming the row's position is enough to find it.

> **EXECUTED, E3 slice a, PR #1103 — EIGHT moved items across FOUR scripts.** Counted from a full
> diff of all six, not from P29's first-difference message. `facts2.py` and `compile53.py` are
> **CLEAN**, so `o53/baseline.json`'s 23 files / 54 occurrences / 1 green / 22 red are unchanged and
> only its `measuredCommit` is re-stamped — gate 5's line-adjacency leg re-run.
>
> | Case | Obligation | Result |
> |---|---|---|
> | `facts.py` `IsSubsetOf` sweep | **#7** | **as predicted, and it is the slice's headline claim moving a pinned fact.** `EffectEnforcementPass.cs:533` and `:571` and `CrossModuleEffectEnforcementPass.cs:162` all **vanish** into `EffectRow.Fits`; `:377` → `:384`. One deviation from the spike's forecast: it expected "five `EffectSet.cs` lines appear instead" — **none** does. `EffectSet.cs:97` is in both transcripts as `IsSubsetOf`'s own DEFINITION, which was baseline context and not a new caller (review round 1, F12). The prototype re-expressed `fits` over `EffectSet`; the shipping relation calls its own element test (`EffectRow.Encompasses`), so `EffectSet.IsSubsetOf` gains no caller |
> | `run.py` **X11-E3** | — | `Compilation successful` gains **one Calor0425** at the argument. **This is E-3, the silent-laundering case, becoming audible.** The transcript is where the design's central claim is now observable |
> | `run2.py` **Y3a-B** | — | site 1, new Calor0425 |
> | `run2.py` **Y4a-O** | — | site 3, new Calor0425 |
> | `run2.py` **Y7a-IFACE-E** | — | Calor0421 message gains the row clause |
> | `run2.py` **Y8a** | — | **§4.5's own executed proof coming true.** `exit 0` + `warning Calor0420` → `exit 1` + `error Calor0420`: `--permissive-effects` stops demoting |
> | `run2.py` **Y8b** | — | Calor0420 message gains the row clause |
> | `run3.py` **W1c** | — | Calor0421 message gains the row clause |
>
> **Obligations #2 (X9b) and #3 (X9c) are NOT discharged.** Slice a made both reach one diagnostic
> instead of a cascade, but neither reaches `exit 0`: both end at **Calor0418**, because what they
> need is acceptance of an *invocation*, and that is E4's — not site 2's. The design's own note on
> them ("the acceptance half is E3's") was one slice too optimistic and is corrected here.

> **EXECUTED, E3 slice b, PR #1106 — SIX moved lines across TWO scripts.** Counted from a full
> diff of all six, not from P29's first-difference message. `run.py`, `run3.py`, `facts2.py` and
> `compile53.py` are **CLEAN**, so `o53/baseline.json`'s 23 files / 54 occurrences / 1 green /
> 22 red are unchanged and only its `measuredCommit` is re-stamped — gate 5's line-adjacency leg
> re-run.
>
> | Case | Obligation | Result |
> |---|---|---|
> | `facts.py` `ParameterNode` probe | — | `Ast/FunctionNode.cs:283` → `:315`. **Unavoidable**, and the same mechanism as obligation **#6**: `EffectsNode` gains `EffectVariableOrdinals` (§7.4's binder positions) and is declared above `ParameterNode` in that file |
> | `facts.py` `IsSubsetOf` sweep | **#7** | `EffectEnforcementPass.cs:384` → `:398` — a LINE SHIFT only. The sweep still lists exactly **two** occurrences, neither a compatibility site, so obligation #7 stays discharged exactly as slice a left it. (`PolyRow.Fits` deliberately spells its ordinal containment as an explicit loop rather than calling the library helper, whose NAME this sweep counts.) |
> | `facts.py` `StrictnessBatchTests.cs:260` | — | `AssumedEffects` → `EffectRowUnknown`. **§13.1's `:260` rewrite, discharged** |
> | `facts.py` `StrictnessBatchTests.cs:607` | — | `AssumedEffects` → `EffectRowUnknown`. **§13.1's `:607` rewrite, discharged** |
> | `run2.py` **Y3a-B-with-sameline-E** | — | loses one Calor0425. **ρ_body (§5).** A row-less `§LAM` into a `§E{cw}` binding was `CannotTell` while the lambda's row was slice a's Unknown placeholder; ρ_body makes it a decided `Fits` |
> | `run2.py` **Y4a-O-sameline-E-decl-later** | — | loses one Calor0425, same cause, at site 3 |
>
> The two `run2.py` cases are the answer to "how many Calor0425 does ρ_body resolve?": **two
> transcript sites, ZERO corpus sites**. The corpus's single Calor0425
> (`13-03.approved.calr`) correctly SURVIVES — it is a row-less `§LAM` returned into a
> **row-less return**, so ρ_body fixes the source while the destination stays Unknown.
>
> **Every other line in all six transcripts is byte-identical**, including the
> `StrictnessBatchTests.cs` probes at `:472/:502/:555/:582/:587/:612/:640/:656/:728/:745`: the two
> in-place re-pins were written to preserve the file's line COUNT, and everything new is appended
> past line 745 — slice a's discipline kept. A first draft broke it and moved ten probe lines onto
> unrelated source text; caught by diffing every script in full, and fixed rather than regenerated
> over. Obligations **#2 (X9b)** and **#3 (X9c)** remain undischarged: both still end at Calor0418,
> which is E4's.

> **EXECUTED, v0.15 E4 — TWELVE moved items across THREE scripts; obligations #2 and #3
> DISCHARGED.** Counted from a full diff of all six transcripts (`git diff -U0`), not from P29's
> first-difference message. `run3.py`, `facts2.py` and `compile53.py` are **CLEAN**, so
> `o53/baseline.json`'s 23 files / 54 occurrences / 1 green / 22 red are unchanged and only its
> `measuredCommit` is re-stamped by the regeneration script, as every slice before this one
> did — gate 5's line-adjacency leg re-run. (A first draft of this block said "not even
> re-stamped"; the numstat said otherwise — review round 1, F4.)
>
> | Case | Obligation | Result |
> |---|---|---|
> | `run.py` **X9b** | **#2** | **DISCHARGED, exactly as the spike predicted**: `exit 1` + Calor0418 → `exit 0`, `Compilation successful`. Position 8's row is read at the invocation of `onChange` and charged to `Bump`, which declares it |
> | `run.py` **X9c** | **#3** | **DISCHARGED, as predicted**: `exit 1` + Calor0418 → `exit 0`. Position 5, same mechanism |
> | `run.py` **X9a** | — | same flip, the `§I` spelling (position 4) |
> | `run.py` **X14** | — | `exit 1` + Calor0418 on `'f' (type 'Func<>')` → `exit 0`. A lambda-bound local's ρ_body is pure and is charged; **§13.1's Y9a rewrite, in the strict transcript** |
> | `run2.py` **Y9a** | — | the same program under `--permissive-effects` **loses its demoted Calor0418 warning**; nothing replaces it. The only `run2.py` line that moves |
> | `run.py` **X10** | — | Calor0418 (error) → **Calor0425 at the invocation + Calor0410 `'unknown'`** on `Apply`. This is the BEFORE case of §10's worked example becoming §10.1's own after-state for a row-less parameter: fail-closed under a new code, `exit 1` both before and after |
> | `run.py` **X13** | — | same, on a row-less FIELD (`Counter.onChange`): Calor0425 naming `field 'onChange' of 'Counter'` + Calor0410 `'unknown'` on `Bump` |
> | `facts.py` `IsSubsetOf` sweep | #7 | `EffectEnforcementPass.cs:398` → `:401`, a LINE SHIFT only (the pass gains a field initialiser above it); still exactly two occurrences, neither a compatibility site |
> | `facts.py` `StrictnessBatchTests.cs:472`, `:502` | — | §13.1's `C2` rewrite: `_IsError` → `_RowGoverns`, the assertion now `EffectRowUnknown … 'Helper' … 'Go'` |
> | `facts.py` `StrictnessBatchTests.cs:728`, `:745` | — | §13.1's `M1` rewrite: `_IsError` → `_ChargesTheRow`, the assertion now `EffectRowUnknown … 'f'` |
>
> **Every other probe line is byte-identical** — `:152/:172/:219/:245/:260/:555/:582/:587/:607/:612/:640/:656/:106/:133`
> and the `HigherOrderDemandLedgerTests.cs:186-200` block — because the six rewritten tests
> were rewritten in place at their original line counts and everything new was appended past
> `:1691`, E3a's discipline kept. The seven Calor0418 transcript lines that existed before E4
> (X9a, X9b, X9c, X10, X13, X14, Y9a) are the seven that moved; no line moved for any other
> reason.
>
> **Review round 1 of the E4 PR (#1107) moved three things, recorded here.** (F1) An UNTYPED
> mutable `§B{~f} §LAM …` re-bound to an impure value and invoked under `§E{}` was silently
> accepted: site 1 never put it in scope (`DeclaredFunctionTypes` carried parameters only —
> §8.2's "parameters and locals" was one word too generous), so the re-binding was never
> checked and the invocation charged the initializer's row. Site 1 now treats a lambda
> initializer as function-valued by construction and reads the binder's local
> `FunctionBoundType` (`CallGraphAnalysis.DeclaredLocalFunctionType`); the re-binding is
> Calor0424, typed and untyped alike. (F2) The invocation resolved a name to the FIRST `§B` of
> that name in lexical order, so two sibling branches each binding `f` charged the wrong row in
> one direction and nothing in the other. It now resolves to the BOUND declaration
> (`CallGraphAnalysis.BoundValueDeclaration`: the reference span → the resolved
> `VariableSymbol.DeclarationSpan`, from `BoundCallStatement.ReceiverSymbol` /
> `BoundVariableExpression.Variable`), and where binding threw it uses the name only when
> exactly one candidate is visible — two same-named `§B`s, or a `§B` beside a same-named
> parameter, fail CLOSED as Unknown. Shadowing a parameter in a nested scope is rejected by the
> binder (Calor0255) and never reaches the pass; pinned as such. (F6, E3b's, made visible by
> the L7-MID mutant) `PolyRow.Fits` ran its ordinal-containment test before the lattice's
> Unknown check, so a variable-mentioning source into a row-less destination was `DoesNotFit`
> (Calor0424 "declared row: [unknown]", leaking "(binder #0)") where §4.3 says `CannotTell`.
> Fixed: an Unknown on either side defers to `EffectRow.Fits` first; the mutant now draws only
> Calor0425s (and the fail-closed Calor0410s).
>
> **`FunctionBoundType.Row` is WRITE-ONLY in 0.15, stated plainly.** E4's charge reads the
> declaration's `§E` node, not the bound row, because `Binder.BindRow` (`Binder.cs:5453-5477`)
> collapses a variable-mentioning row to Unknown, and the A3 fixtures need `e`. So §8.2's
> "first production reader" is a reader of `FunctionBoundType` (function-typedness), never of
> its `Row`. **Obligation, 0.15.x (registered on the roadmap's E5 row): key the invocation row
> on the bound symbol end-to-end — `FunctionBoundType.Row` carrying the variable part (or the
> binder recording the `EffectsNode` on the symbol) so it gains a production reader and the
> AST span-matching in `ResolveInvokedValueRow` can go.** Pinned by the F2 tests, which are
> what that refactor must keep green.

`spike-verdict.json`'s `transcriptDivergences.e2Obligation` carries the same sentence in
machine-readable form, and P27 asserts that the case list holds exactly seven rows.

> **How the count was wrong the first time, and why that matters.** The spike PR first recorded
> **three**. They had been read off P29's failure message, which prints the **first** difference
> per script and hides the rest — so three scripts drifting looked like three cases. Review
> round 1 caught it by diffing every script in full. The correction is recorded rather than
> quietly applied because it is the same failure mode §1's evidence discipline exists for: a
> number taken from a summary instead of from the measurement. Both `facts.py` rows in
> particular were invisible that way, and row 7 is **the spike's own headline claim** moving a
> fact the harness pins.

**(b) Spike artifacts are not corpus.** Committing `.calr` fixtures under
`docs/design/spikes/` collided with **three** frozen instruments that enumerate `.calr`
repo-wide — `HigherOrderDemandLedgerTests` (filesystem walk), `LosslessFormattingTests`
(`git ls-files`), and the harness's own `facts.py` (`git ls-files`). All three now exclude
`docs/design/spikes/` by path, and **none of their counts changed**, because those files were
never part of the 886. Round 3 solved the same problem for *scratch* files by moving them
outside the repository; committed artifacts cannot move, so they are excluded instead. Any
future spike that commits `.calr` under `docs/` inherits this.

**(c) One message text was protected, not changed.** An early prototype draft trimmed the
unknown-effect-code string and moved **X5a** from `' ^ e'` to `'^ e'`. That is a gratuitous
change on a path rows do not own; it was fixed rather than recorded. E2 should keep the
untrimmed code in `Calor0403`.

---

> **EXECUTED, v0.15 E5, PR #1108 — THREE moved items across ONE script, none of them a
> compiler-output change.** Counted from `git diff -U0` of all six transcripts after
> `regenerate-transcripts.py`: `run.py`, `run2.py`, `run3.py`, `facts2.py` and `compile53.py`
> are **CLEAN**; `o53/baseline.json`'s 23 files / 54 occurrences / 1 green / 22 red are
> unchanged and only its `measuredCommit` is re-stamped, as every slice before this one did.
>
> | Case | Result |
> |---|---|
> | `facts.py` `IsSubsetOf` sweep | `EffectEnforcementPass.cs:401` → `:600`, a LINE SHIFT only: phase 5's `DeclarationEffectFact` record, `DeclarationFacts`, and the `_chargedVariables` bookkeeping sit above `CheckEffects`. Still exactly two occurrences (`EffectSet.cs:97` + this one); phase 5 computes its forbidden set with `Except()`, so P16's structural count stays 2 |
> | `facts.py` "tests/TestData function-typed .calr" + "whole-corpus function-typed positions" | `count: 0` → `1`, and the whole-corpus position count 5 → 7 with one file added to its list — `tests/TestData/QueryCorpus/project/app.calr`, gate 7's fixture, now carries `Map<eff e>` / `Twice<eff e>`, two same-line parameter rows (review round 1, #2 asked for the rank-1 golden IN the corpus). §9's row and §3.2's sweep note say so; `EffectRowCorpusShapeTests` carries the file on a reasoned, anti-staleness-checked allowlist |
> | `o53/baseline.json` `measuredCommit` | re-stamped `dd4d8f27…` → `d2f7e4bb…` by the regeneration script; counts unchanged |
>
> **Every other probe line is byte-identical.** Nothing under `bench/phase0-agent-native/`
> moved: the ground truth was appended to two EXISTING fixture files rather than committed as
> an 887th `.calr`, after a first cut with a new file turned four count-pinned instruments red
> on the count alone (§13.2's E5 block).

## 14. Open questions

1. **Does §7.5's R2 have a usable spelling for a foreign interface?** — **ANSWERED IN PART BY
   THE SPIKE. The ramp did not fire.** This was named *"most likely ramp trigger"*; A2's spike
   output is now in, and the honest reading is narrower than either a pass or a fail.

   **What is settled.** When the interface **is** Calor, the member-level spelling works and
   needs no carve-out: `after/A3-middleware.calr` and `after/A2.calr` are accepted,
   `after/A3-middleware-broadening.calr` and `after/A2-broadening.calr` are rejected as
   Calor0421 by the ordinary `fits` relation, and `after/A3-middleware-alpha.calr` shows the
   interface's `eff e` and the implementation's `eff f` are identified — rows carry binder
   **indices**, not names. Crucially, a Calor implementation **does not have to widen** the
   interface: it declares the *same* variable, so `IPipelineBehavior` is left alone. That is the
   part of the question that read "requires editing the interface", and the answer is **no**.

   **What is not settled, and is now the sharper question.** A2's `IPipelineBehavior` is
   *converted* Calor, not the C# assembly. For an interface that stays **C#-declared** there is
   no row on the interface member at all, so there is nothing for the implementation's row to be
   checked *against*: §6.2 site 5 routes to the assumed/unknown channel, exactly as it does for
   an external base today (`EEP:596-611`). Rank-1 rows compose with a *Calor* interface; whether
   they compose with a *metadata* one is a **manifest** question — can an effect manifest carry a
   row on an interface member? — and §8.4 explicitly gives the 0.15 manifest schema **no**
   row-on-return field. *Evidence needed:* the §13.4 Calor0425 corpus ledger, which counts how
   often this actually bites on converted code. **No longer a ramp trigger; it is a scope
   boundary, and it is E2's to state in the release notes.**
2. **Is `eff` safe against a type parameter literally named `eff`?** Half executed: **Z4** shows
   `§F{f001:M:pub}<eff> (eff:x) -> void` compiles **today**, so the compatibility obligation is
   real and measured. What cannot be executed is the *other* half — that §7.2's one-token
   lookahead preserves it — because the branch does not exist yet. This is the one v3 decision
   still resting on reasoning rather than execution, and it is bounded to a single `Peek(1)`.
   *Evidence needed:* P18's `TypeParamNamedEff_StillWorks`, written **before** the branch is
   considered done.
3. **How does §4.1's `Subtypes` widening get a doc-drift guard?** `calor self-check docs` checks
   the effect-code *registry* against `effects.md`'s table (`EffectTypes.cs:134-135`,
   `Calor1323`/`Calor1324`) but has **no check for the subtyping relation**, so a `Subtypes` entry
   can drift from its documentation silently. *Evidence needed:* a decision on whether to extend
   the drift checker with a `Calor13xx` code for the relation, taken in the E2 PR. (Note the
   asymmetry this document now lives with: the *harness* is pinned by P29, but the *subtyping
   table's documentation* is not pinned by anything.)
4. **Where should Calor0425 be reported for a row-less parameter that is never invoked?** §6.2
   puts it at the parameter span, so a converted file with FluentValidation's 236 delegate-typed
   declarations would emit 236 warnings even where none is invoked. Reporting at the *invocation*
   is quieter but loses the declare-your-intent pressure.

   **This is no longer a freeze conflict.** v2 left it open while P6/P15 froze at design-doc
   merge, so a design in flux sat under a frozen pin. Resolved two ways at once: (a) **span is
   explicitly outside gate 1's frozen denominator** (§13.3 gate 1 — the gate observes which
   classes are closed, by code and polarity, and a class is closed wherever the message points);
   (b) span is pinned by **P22**, which freezes with **E3**, not at design-doc merge. So the
   measurement can still move it. *Evidence needed:* §13.4's ledger, whose fourth split
   ("declared but never invoked" vs "invoked") ships for exactly this purpose. **This draft
   chooses the parameter span** and will keep it unless the ledger shows the never-invoked
   fraction dominating.

### 14.1 Corrections to governing inputs

| Claim | Where | Source says |
|---|---|---|
| "`IAstVisitor` … ~236 methods each" | `CLAUDE.md:160`, roadmap §4.1 term 3 | **184** each (`Ast/AstNode.cs:59`, `:247`) |
| Family codes encompass their narrow siblings (`db ⊃ db:r`), and a bare `fs` code exists | working assumption in the roadmap's shorthand — **not a quotation**: `grep -rn "fs:w\|colon-hierarchy" docs/plans/roadmap-v0.13-v0.15.md docs/design/calor-direction.md` → **0 hits**. Draft v1 attributed a phrase no governing input contains; the attribution is withdrawn, the substance stands | `EffectSubtyping.cs:14-43` has only four `*_readwrite` entries; `EffectTypes.cs:71-73` has `fs:w`/`fs:r`/`fs:rw` and no bare `fs`. §4.1 closes the gap deliberately |
| "the 217-program benchmark corpus" | roadmap `:276`, `:848` | **226** `.calr` under `tests/TestData/Benchmarks`; the pin asserts `>= 200` (`BulkBenchmarkCompilationTests.cs:171`) |
| Gate 1's denominator is "five classes … four classes if the ramp fires" | roadmap §4.4 gate 1 | The same sentence **enumerates six**: virtual override, interface implementation, assignment, argument, return, rank-1 generic instantiation. This document freezes the denominator at **six, dropping to five** (§6, P15) |
| Gate-1 pin citations `StrictnessBatchTests.cs:132/176` and `:198/221` | roadmap §4.4 gate 1 | `:132`, `:198` are `[Fact]` lines and `:221` is blank. Assertion lines are `:152`, `:172`, `:219`, `:582`; `_Compiles` methods are `:177`, `:223`. This doc cites assertion lines |
| "`EffectSet.Unknown` sentinel `:101,:200`" | attributed to roadmap §4.0 in Draft v1 | The roadmap (`:353`) cites only `EffectSet.cs:101`. Draft v1 corrected a citation pair that does not exist; withdrawn |
| Generics deferral at `calor-direction.md:57-60` | Draft v1 §4.6, §12.2 | It is at **`:33`**; `:57` is the TIER2D-design-doc bullet |
| "architectural elegance …" at `calor-direction.md:114` | Draft v1 §1 | **`:112`** |
| `ParseLambdaExpression` at `Parser.cs:11299` | working note | **`:11330`**; `:11299` is inside `ParseYieldReturnStatement`'s doc comment |
| `EffectResolutionStatus` at `EffectResolver.cs:596-608` | Draft v1 §2.2, §4.2, §8.4 | **`:596-612`**; the `Unknown` member is at `:611` |
| Calor0410 path at `EEP:410-443` | Draft v1 | the subset test is `:377`, demotion `:381-383`, message `:427`, reports `:433`/`:441` |
| `BindingNode` | Draft v1 §3.2, §9 | no such class (`grep -rn "\bBindingNode\b" src/` → 0). It is **`BindStatementNode`** (`Ast/ControlFlowNodes.cs:161`) |
| The Calor0410 message shape quoted four times | Draft v1 §3.5, §5.3, §10.1, §10.3 | Fabricated. Real text: `Function '{name}' uses effect '{code}' but does not declare it`, once per effect (`EEP:427`, loop `:421`) — executed as **X12b** |
| `§FLD{…} §E{…}` already parses (Draft v1 §14 Q4) | Draft v1's own reasoning from `ParseClassField:8709-8719` | **X9b**: `Calor0100: Expected TP, WHERE, EXT, IMPL, FLD, … but found Effects`. It does not parse; position 8 is new syntax |
| `§E{!e, alloc}` fails at `Expect(CloseBrace)` | consistency lens C2 | **X3b**: it reaches `Calor0403: Unknown effect code '! e'`. The lens's conclusion (reject `!`) is right; the mechanism differs |
| MediatR `RequestPreProcessorBehavior.cs` is **28** lines | **Draft v2 §12.1 / §14.1** — v2's own regression | **29**. `awk 'END{print NR}'`, `grep -c ''` and `cat -n` all say 29; `wc -l` says 28 because the last line is unterminated (`tail -c1` is `}`). **Draft v1 was right**; v2 accepted a lens finding without executing it and wrote the error into its corrections table. Restored in §12.1 with the measuring command named. Recorded here as the clearest instance of the failure mode this document's evidence discipline exists to prevent — including when the unexecuted claim comes from a reviewer |
| Roadmap §4.5's `--permissive-effects` inventory: *"Waives Calor0410/0411/0418 today"* | roadmap §4.5, the waiver row | Incomplete: it **also** demotes **Calor0420/0421** (`EEP:517-519`), executed as **Y8a** vs **Y8b**. The omission matters because that row is what §4.5 executes, and its "a row that does not fit is never waived" clause is unsatisfiable for two of the six sites unless the demotion is removed — which §4.5 now does |
| `eff` reuses the `in`/`out` variance shape, so it is "the same shape, not a new one" | **Draft v2 §7.2** — v2's own overstatement | Weaker than claimed. `in`/`out` are matched with **no lookahead** (`Parser.cs:7612-7621`) and are **rejected on `§F`** (`allowVariance: false` → `Calor0119`, executed as **X6b**). `eff` reuses the surface *shape* but needs its own branch, its own lookahead and its own per-declaration-form enablement. §7.2 softened; §9 prices it |

---

## 15. Review record

Exit criterion (roadmap §4.1 term 2): evidence **and** consistency return APPROVE on a revision,
or every declined finding is recorded here with its rationale.

| Round | Doc | evidence | consistency | test-lens |
|---|---|---|---|---|
| 1 | Draft v1 | NEEDS-FIXES 92% | NEEDS-FIXES 88% | NEEDS-FIXES 88% |
| 2 | Draft v2 | **APPROVE 94%** | NEEDS-FIXES 85% | NEEDS-FIXES 91% |
| 3 | Draft v3 | **APPROVE 95%** | **APPROVE 88%** (one open Major, carried — blocks the spike PR, not this merge) | NEEDS-FIXES 93% |
| 4 | Draft v4 (this revision) | pending | pending | pending |

**Round 1 (on Draft v1). 87 disposition rows** — 22 evidence + 22 consistency + 43 test-lens —
**= 81 applied + 4 declined + 1 partly declined (M13) + 1 superseded (test-lens 8.9)**. One of the
four declines (test-lens 12.3) was **reversed in v3** after round 2 judged it unsound; it is
counted here as declined because that is what round 1 decided, and the reversal is recorded in
round 2's N6 row.

(Arithmetic history, since this line has now been wrong twice: v2 said "62 applied, 4 declined";
v3's first pass said "80 dispositions, 74 applied". Both were hand-totalled. The figures above are
counted from the tables below — 87 rows, and 81 + 4 + 1 + 1 = 87.)

### Evidence lens, round 1 (22 findings; ids run 1–23 with 4 and 22 merged)

| # | Finding | Disposition |
|---|---|---|
| 1 | Calor0410 message quoted four times does not exist | **applied** — real text executed as **X12b**; §2.1, §3.6, §10; §14.1 |
| 2 | `EffectResolutionStatus` is `:596-612`, not `:596-608` | **applied** — §2 table, §14.1 |
| 3 | Calor0410 path is `:377`/`:427`/`:433`/`:441`, not `:410-443` | **applied** — §2 table, §14.1 |
| 4, 22 | "~24 `MapShortTypeNameToFullName` sites" is the *pair* count | **applied** — §2 table splits 11 + 13 |
| 5 | §6.4's site-5 sample is re-worded, not merely re-coded | **applied** — §6.4 says so explicitly |
| 6 | `BindingNode` does not exist | **applied** — `BindStatementNode` (`Ast/ControlFlowNodes.cs:161`); §3.3, §9, §14.1 |
| 7 | 9 `§I` arms + 7 `§O` arms, not 5 | **applied** — §3.3 moves the check **inside** `ParseParameter`/`ParseOutput`, so **six** insertion points cover all 16 arms |
| 8, m1 | "six positions" vs seven rows vs "position 7" | **applied in v2, completed in v3** — v2 said "seven" over eight rows; v3 renumbers to **eight** (round-2 residual) |
| 9 | `tests/TestData` golden bucket is 0, not ≤8 | **applied** — §9 (`0 of 359`, measured) |
| 10 | Conversion tests never run the effect pass; four more snapshots gain diagnostics | **applied** — §9: **0 texts, 0 assertions**, `TestHelpers.cs:40-70` cited; the 7 function-typed snapshots enumerated |
| 11 | MediatR module exercises **one** §6 site, not four | **applied** — §12.1; artifact **A3** added to carry R1/R3 |
| 12 | The `fs:w ⊂ fs` quotation is unsourced | **applied** — attribution withdrawn, substance kept; §14.1 |
| 13 | The `:101,:200` citation pair being corrected does not exist | **applied** — §14.1 |
| 14 | Generics deferral is `calor-direction.md:33`, not `:57-60` | **applied** — §4.6, §12.1, §14.1 |
| 15 | "architectural elegance" is `:112`, not `:114` | **applied** — §1, §14.1 |
| 16 | "four pins", three lines, `:218` off by one | **applied** — §6.3 names four assertion lines `:152`, `:172`, `:219`, `:582` |
| 17 | Pin anchors mix method, `[Fact]` and blank lines | **applied** — assertion lines throughout, stated in §6.3 |
| 18 | Both interfaces live in one file, so "7 files" is wrong | **applied** — §9: **6 files** counterfactual |
| 19 | "Effects subsystem 6" names 7 via a glob | **applied** — measured **10** existing + 1 new |
| 20 | The 4-occurrence grep undercounts what `IsFunctionTypeName` accepts | **applied** — §1: **5 shapes in 5 files**, all conversion snapshots, plus 9 `§LAM` / 3 `§DEL` counted separately |
| 21 | §5.4's `§LAM` site arithmetic | **applied** — §5: 9 occurrences in 7 files, enumerated |
| 23 | `§MT` has its own `§E` arm at `:8836-8840` | **applied** — §3.3 position 1 |

### Internal-consistency lens, round 1 (3 critical, 13 major, 7 minor)

| # | Finding | Disposition |
|---|---|---|
| C1 | `§O`/`§E` token-stream collision, 53 canonical sites | **applied** — §3: **line-adjacency rule**, with the collision executed (Y1a/Y1b/Y1c, X1b/X2b, Y5a) and the corpus measured (54/23 two-line, 2948/471 arrow, **0** same-line of any form). 22 of the 23 files are already compile-red for unrelated reasons; 1 is green |
| C2 | `!e` collides with the `T!E` fallible suffix | **applied** — §7.1/§7.2: **`eff` modifier + bare identifier**, chosen on executed evidence (X3, X3b, X5b, X6a, X6b, Y2a, Y2b). The lens's *mechanism* (`Expect(CloseBrace)`) is corrected in §14.1: it actually reaches Calor0403 |
| C3 | Ramp criterion 1 not adjudicable from the two modules | **applied** — §12: three artifacts, a per-criterion/per-artifact table, criterion 2 promoted to **G-CODEGEN**, verdict as `spike-verdict.json` + P27 |
| M1 | Derive-from-index is self-refuting; `calor build` gains an index dependency | **applied** — §8.5: an **in-process projection**, index-independent, one producer and two consumers; P25 pins a fresh-clone `calor build` |
| M2 | Waiver policy drifts across sites | **applied** — §4.5 unifies: `DoesNotFit` never waived anywhere, so **0420/0421 lose their `--permissive-effects` demotion** (`EEP:517-519`), executed as Y8a/Y8b; P11 |
| M3 | 0424 defeatable by deleting the source `§E` | **applied** — §4.5 closes it via the 0410 ∧ 0424 composition; P12 |
| M4 | 2 of 9 `fits` cells undefined | **applied** — §4.3's nine-cell table; P8 |
| M5 | `Assumed` does not survive the destination (two-hop laundering) | **applied** — §4.4's destination rule, per site; P10 |
| M6 | `⊔` not commutative; message text nondeterministic | **applied** — §4.2 makes `R` a **canonically ordered set**; P9 |
| M7 | Omitted binding row = Unknown contradicts §5 | **applied** — §3.5: a binding **with an initializer** infers from it |
| M8 | Gate-1 class arithmetic inconsistent | **applied** — **six, dropping to five**, in §6, §7.5, §12, §13.3, and the roadmap's own six-enumerated/five-named error recorded in §14.1 |
| M9 | Cross-module pass missing from sites and blast radius | **applied** — §6.2 (`CrossModuleEffectEnforcementPass.cs:162`, kept as the Calor0410 leg, re-implemented on `fits`), §9, P16 |
| M10 | 0424 misused for a rank-1 scoping violation | **applied** — **Calor0404 `EffectVariableScope`** allocated (§6.1, §7.3) |
| M11 | Async punted to the spike | **applied** — §11 decides deferral to 0.16; (a)/(b)/(c) become the **0.16 re-entry test**, not spike criteria |
| M12 | Entry-gate term 1 unmet at merge | **applied** — §0 states it plainly; the spike PR **must merge before E2**, and this doc's merge still freezes gate 1/2 as roadmap §4.4 specifies |
| M13 | Ramp criteria 2 and 4 are not combinator questions | **applied for 2** (→ G-CODEGEN). **Declined for 4**: decidability by the one-line solve *is* a property of the combinator set — it is exactly what distinguishes rank-1 from rank-2 — so it stays as **R3** |
| m2 | §14.1 omitted two corrections it had evidence for | **applied** — §14.1 now carries 14 rows |
| m3 | `self-check docs` does not cover §4.1's `Subtypes` change | **applied** — §9's docs row states the gap; §14 Q3 asks for the decision in the E2 PR |
| m4 | `RowDisplayString` unlocated; LSP only SHOULD | **applied** — §8.3 locates it on `EffectRow`/`BoundType`; §9 keeps the hover a SHOULD, since `DisplayString` is untouched so nothing breaks without it |
| m5 | Argument-position rows undefined | **applied** — §4.4 states the destination row at all six sites |
| m6 | `§E` swallowed as a type name when written *before* its type (`Token.cs:384` `IsKeyword` includes `Effects`; `ReadInlineTypeToken` accepts `IsKeyword`) | **declined, with rationale** — a `§E` written where a type is expected is malformed source that already produces a type diagnostic, the shape has **0 corpus occurrences**, and a special case costs a branch in the shared type reader for no observed benefit. The *correct* orders are pinned positively by P3 and P5 |
| m7 | ~600–750 duplicated lines | **applied** — v2 is **≈1300 lines**: §9 collapsed to one table, §2 to a cited table, one `Map` example kept, `Match`/callbacks reduced to a sentence each |

### Test lens, round 1 (8 hard defects, 3 cross-cutting, ~25 claim-table gaps)

| # | Finding | Disposition |
|---|---|---|
| F-1 | `BindingNode` — a pin cannot be written against it | **applied** (see evidence #6) |
| F-2 | The four 0420/0421 pins mis-cited; `:582` omitted | **applied** — §6.3, §13.1 |
| F-3 | Five orphaned 0418/0419 pins undispositioned (`:472`, `:728`, `:245`, `:587`, `:640`) | **applied** — all five in §13.1, each marked *orphaned in Draft v1* |
| F-4 | Gate-1 denominator self-contradictory | **applied** (M8) |
| F-5 | `fits` specified for 6 of 9 cells | **applied** (M4) |
| F-6 | `ProjectIndex` format version not bumped for E5 | **applied** — §8.5 `"3.0"`→`"4.0"`; P24 |
| F-7 | `eng/ast-schema.json` / `ArchitectureTests.cs:158` omitted from §9 | **applied** — §9 counts it, and cites it as the **existing** "zero visitor churn" pin; §13.1 |
| F-8 | Gate 2's ledger cannot be "extended, not rewritten" | **applied** — §13.3 gate 2 quotes the test's own failure message (`HigherOrderDemandLedgerTests.cs:192-199`), which already instructs regeneration with the cause named |
| X-1 | No freeze point for any pin | **applied** — §13.2's **Freeze** column, every row |
| X-2 | No home file for any pin | **applied** — §13.2's **Home** column, every row |
| X-3 | Message text never pinned | **applied** — **P22** pins the four new clauses in full |
| 3.3 | No test parses `§LAM … §E` or `§DEL … §E` (0 hits in `tests/`) | **applied** — **P3** covers all **eight** positions, including the three that already parse |
| 3.13 | No pin on "no lexer change" | **applied** — §9 states 0 lexer files; P5 and P18 observe the token surface |
| 4.4 | "widening, never narrowing" is prose | **applied** — §13.3 gate 5 counts the disappearing Calor0410s |
| 4.5 | No structural pin that `EffectResolutionStatus` gains no member | **declined, with rationale** — the enum is explicitly **not** changing (§4.2); a pin asserting the absence of a change nobody is making is maintenance with no discriminating revert. P21 pins the *mapping* from its three members to rows, which is the behaviour that matters |
| 4.6, Q-C | No lattice property test | **applied** — **P9** |
| 4.9 | Assumed-source cases absent from the 9-cell pin | **applied** — P8 is table-driven over all nine, incl. both Assumed axes |
| 4.11 | "permissive is less powerful; release notes must carry it" has no instrument | **declined, with rationale** — the repo has no CHANGELOG-checking instrument, and inventing one for a single sentence is out of proportion. It stays a **release-notes commitment**, the same class as roadmap §4.5's "TIER1A: not run" row, which is also an honest negative with no instrument. The *behaviour* is pinned by P11 |
| 4.13 | `CannotTell` propagation / `DoesNotFit` dominance unobserved | **applied** — folded into P8's table |
| 5.1 | Lambda `CannotTell` arm and "type carries ρ_decl" unpinned | **applied** — P14's four cases |
| 6.3 | No `CannotTell` arm per site | **applied** — P15 adds a `_CannotTell` arm to each of the six pairs |
| 6.4 | Sites 4/5 retire Calor0419 — three pins go red | **applied** (F-3) |
| 6.7 | `CheckEffectVariance` structural claim unpinned | **applied** — P16 |
| 7.1–7.8 | Rank-1 parse/scope/mixed-row/Unknown-contributor arms unpinned; only 1 of 5 rejection sites covered | **applied** — **P18** covers all of them, and is deleted wholesale if the ramp fires |
| 7.9, 12.4 | Verdict lives in a prose README | **applied** — `spike-verdict.json` + **P27**, following the two existing ledgers |
| 8.1 | "never pure" default unpinned | **applied** — P19's `RowsDefaultToUnknownNotPure` |
| 8.2, 8.3 | `BoundTypeTests` cited wrongly; existing exact-equality pins already enforce §8.3 | **applied** — §8.3 cites `:139`/`:150` (the *assertions*; the lens's `:134`/`:143` are the `[Fact]`/method lines) and concedes Draft v1's claim was wrong; P19, P20 |
| 8.4 | `EffectRow.ToDisplayString` unpinned | **applied** — P20's scope |
| 8.5 | Manifest → row mapping unpinned | **applied** — P21 |
| 8.6 | BCL-returned delegates not explicitly in the frozen residual | **applied** — §8.4 names it as a frozen gate-1 residual |
| **8.7, Q-E** | **"No silent Unknown" — the pin the design rests on — absent** | **applied** — **P17**, frozen **before E2** |
| 8.8 | The 65.46% interaction has no instrument | **applied** — **§13.4** registers `calor0425-corpus-ledger.json`, per subject, split by cause, exact-equality, **before E2**. It was Draft v1's "open question 1"; it is now a decision |
| 8.9 | The grep pin observes key deletion, not derivation | **superseded** — §8.5 no longer derives from the index; P25 pins index-independence directly, which is the stronger property |
| 8.10 | The three `BuildStateCache` constants unpinned | **applied** — P23 |
| 8.11 | `ProjectIndex` facet version | **applied** (F-6) |
| 8.12 | The effects golden and gate 7's revert unnamed | **applied** — §13.3 gate 7, citing `EveryGoldenStatesWhyItExists` (`:152-172`) |
| 10.1–10.3 | Binder shape and new message sentences unpinned | **applied** — P22, and §10 marks each new clause as new |
| 12.1 | A1's byte-identity unpinned | **applied** — **G-CODEGEN** (§12.2), blocking |
| 12.2 | 28 lines, not 29 | **applied** — §12.1, §14.1 |
| 12.3 | No presence/schema test for the spike directory | **declined in v2 — REVERSED in v3.** The v2 rationale ("P27 subsumes it") was wrong: P27 reads one JSON file and would pass with every artifact missing. Round 2's N6 caught it; **P31** now asserts the manifest |
| Q-D | No parse → emit → parse round-trip per position | **applied** — **P4**, seven cases |
| G-3 | CLI leg, SDK leg, default-`UnknownCallPolicy` equivalence, F-3 supersession | **applied** — §13.3 gate 3; the supersession **already merged** as `b5d61e18` (PR #1085) |
| G-5 | No gate-5 row at all | **applied** — §13.3 gate 5, with legs (a)/(b), the E1-attributable separation, and three 0.15-specific additions |

### Round 2 (on Draft v2)

**Evidence: APPROVE 94%** — the lens re-executed all 36 committed cases and reported *"No
committed experiment output diverged from my re-run"*, plus independent re-measurement of the
corpus counts, `o53/baseline.json`, and every structural citation in §2–§14. Six Minors, all
applied below. **Consistency: NEEDS-FIXES 85%** — all three round-1 CRITICALs closed; five new
Majors, all on decisions v2 introduced. **Test-lens: NEEDS-FIXES 91%** — 12 of 13 round-1
load-bearing gaps closed; four blockers, all applied.

**22 disposition rows** — 10 consistency + 5 test-lens + 7 evidence — **= 22 applied, 0
declined.** (v3's first pass said "24 dispositions"; counted from the tables it is 22.) Every
round-2 finding was a sentence-plus-pin fix; none re-opened a decision.

### Consistency lens, round 2 (5 majors, 4 minors)

| # | Finding | Disposition |
|---|---|---|
| N1 | The line rule's second branch ("later line ⇒ declaration row") is **false at four positions** — `§FLD`, `§B`, a wrapped inline signature and the inline parameter form have no `§E` arm to fall through to, and cascade 4–11× Calor0100 with no mention of effects (**Z1**, **Z2**, **Z3**; **Z5** shows wrapped parameter lists are a real authoring shape) | **applied** — Decision 1 restated as *same line ⇒ that type's row; otherwise the token is not consumed at that position*, with **Calor0405 `EffectRowMisplaced`** as a row-aware recovery replacing the cascade (§3.1). **P2** extended to all four positions against their executed baselines |
| N2 | An `eff` name can shadow a live effect code: `<T, cw>` compiles today (**Z6**) and §7.2 resolves variables before codes, making the real `cw` unwritable | **applied** — §7.2(c): an `eff` name in `EffectCodes.Registry` or `ColonPrefixes` is **Calor0404**. Ordinary type parameters named `cw`/`fs` keep working (Z6/Z6b stay green). **P18** gains `EffectVariableNamedLikeACode_IsCalor0404` |
| N3 | §7.3's scope lists don't partition the positions — `§LAM` and `§DEL` are in neither, and `§DEL` has no type-parameter list at all (**Z8**) | **applied** — §7.3 is now a full partition table over all eight positions; `§LAM` and `§DEL` are **forbidden**, anchored on **Z8b** and **Z8**. Six rejection sites in **P18**, not five (round 3's T-N3 found the partition still had a blank cell and made it **seven**) |
| N4 | R2's middleware AFTER spelling was deleted in the v1→v2 cut, yet A3 is "the four §7.4 AFTER forms" and only `Map`'s existed | **applied** — all three restored to §7.4 (`Match`, middleware/`next`, callback field). The middleware spelling needs an `eff` variable on a **class/interface** read by a member row — a scope rule §7.3 does not grant; **Z7**/**Z7b** show class and interface type parameters do reach members, so it is bounded, and §9 prices it as a conditional seventh insertion point scheduled only if R2 needs it. Named as this document's own most-likely ramp trigger |
| N5 | A row on a non-function-typed position has no stated meaning — `-> void §E{cw}` compiles today (**Z9**) and becomes `void`'s row under the new rule | **applied** — §3.5: **Calor0405**, the same code as N1's recovery (one code, two situations, both "a row where a row cannot go"). **P6** gains one case per position against Z9/Z9b/Z9c |
| M5-res | The declaration boundary still converts `Assumed`→`Concrete` and is not one of the six sites; P10's fixture passes with that hop open | **applied** — §5 states the conversion is deliberate because Calor0419 already reports the assumption at that boundary, names it a seventh place a row changes form, and explains why the alternative (an `Assumed` row escaping every annotated function) is the noise §13.4 exists to avoid. **P10** gains it as case (c) |
| N6 | The round-1 decline of test-lens 12.3 was unsound; P27's "recomputation" undefined | **applied** — decline **reversed**. §12.3 defines exactly what P27 recomputes (`gCodegen` via P28, the R1 leg) versus what it only records (R2, R3 — judgements a test cannot re-derive, said plainly). **P31** adds the artifact manifest; submodule skip behaviour stated |
| N7 | The `eff` / `in`-`out` shape-parity claim is overstated | **applied** — §7.2 softened: `in`/`out` have **no** lookahead and are **rejected** on `§F` (`Calor0119`, **X6b**), so `eff` reuses the shape, not a working path. §9 prices its own branch, lookahead and per-form enablement; §14.1 records the overstatement |
| N8 | Roadmap §4.2's **M1** is absent from the doc's before-E2 chain | **applied** — §13.3 gains an M1 row: a merge precondition, not a gate. Status recorded — PRs #1090/#1091/#1092 merged, A-1.10 is the annex guard half, the 0.15 PP row registers at A-1.11+; E2's PR body must cite it |
| N9 | §14.1 still omits the roadmap §4.5 permissive-inventory correction | **applied** — §14.1 row added: §4.5's inventory says "0410/0411/0418" and omits the **0420/0421** demotion, which is exactly what makes its own "never waived" clause unsatisfiable until §4.5 removes it |

### Test lens, round 2 (4 blockers)

| # | Finding | Disposition |
|---|---|---|
| T1 | **The harness is not a test.** ~26 executed cases are the evidentiary base of nine sections and are observed by nothing; the generated outputs are gitignored, so there is nothing to diff even by hand. *"This is the exact failure mode v2 criticises v1 for."* | **applied, in this PR** — canonical `transcripts/` committed; `regenerate-transcripts.py` added; every script made deterministic and Debug/Release-agnostic; **P29** (`EffectRowExperimentHarnessTests.ExperimentTranscripts_MatchARerun`) re-runs all six and diffs, naming the script and first differing line on drift. **Never skips** — a missing compiler build is a hard failure, because a skipped evidence pin is how v1's fabricated quotations survived. Frozen now, ahead of E2 |
| T2 | `o53/baseline.json` is gate 5's named instrument but meets none of the three bars §12.3 sets for `spike-verdict.json` | **applied, in this PR** — the ledger gains `schemaVersion`, `measuredCommit` and `scope`, and **P30** asserts 23 files / 54 occurrences / 1 green / 22 red **and the 18+3+1 breakdown**. `measuredCommit` is shape-checked, not compared to HEAD, per `HigherOrderDemandLedgerTests.cs:480-498` |
| T3 | **G-CODEGEN has no pin**, though §12.2 makes it feature-wide blocking and §9/§8.5 lean on it | **applied** — **P28** `GCodegen_BeforeAfterEmittedCSharpIsByteIdentical`, re-emitting A1/A2 and diffing the `.g.cs`. P27's recomputation defined so it is falsifiable |
| T4 | §14 Q4 is in flux while P6/P15 freeze at design-doc merge | **applied** — resolved twice over: **span is explicitly outside gate 1's frozen denominator** (the gate observes which classes are closed, by code and polarity), and span is pinned by **P22**, which freezes with **E3**. §13.4's ledger gains a fourth split — "declared but never invoked" vs "invoked" — for exactly this question |
| T5 | §13.4's ledger test is unnamed and homeless — the only instrument without a P-number | **applied** — **P32** `Calor0425CorpusLedgerMatchesRecomputation`, home `tests/Calor.Compiler.Tests/Effects/Calor0425CorpusLedgerTests.cs`, `compiler` shard, `Skip.IfNot` on submodules registered in `eng/test-manifest.json` |

### Evidence lens, round 2 (6 minors + 1 round-1 residual)

| # | Finding | Disposition |
|---|---|---|
| N1 | The "22 of 23 red" breakdown is 15+3+1=19, but the ledger says **18** bench/mcp | **applied** — 18+3+1 in §3.2 and in `experiments/README.md`, and **P30 asserts the breakdown** so the arithmetic cannot drift again |
| N2 | `RequestPreProcessorBehavior.cs` is **29** lines; v2 "corrected" v1's 29 to 28 from a lens finding without executing, and enshrined it in §14.1 | **applied** — 29 restored in §12.1 with the measuring command named (`wc -l` undercounts an unterminated last line). §14.1 records it as the clearest instance of the failure mode this doc's discipline exists to prevent — *including when the unexecuted claim comes from a reviewer* |
| N3 | A1 is built at `test.yml:174` and run at `:176`, not `:181` | **applied** — §12.1 |
| N4 | P7's home path does not exist; `:29`/`:38` are network, not `fs`; and they are `[Fact]` lines | **applied** — home corrected to `tests/Calor.Enforcement.Tests/EffectSubtypingTests.cs`, `:20` identified as the `fs` case, and the `[Fact]`-line exception called out explicitly against the doc's own assertion-line convention |
| N5 | §15's arithmetic reconciles with nothing | **applied** — round-1 recounted from its own tables: **80 dispositions, 74 applied, 4 declined, 1 partly declined (M13), 1 superseded (8.9)**; the evidence header now says 22 findings, not 23 |
| N6 | §7.1 mixes verbatim output with paraphrase | **applied** — the Y2a paraphrase replaced by **Z11**'s verbatim `Calor1002 (CS0029)`, which supports the claim more strongly than the paraphrase did |
| r1-res | "seven positions" over eight labelled rows; `:767`/`:656` point at `Assert.Contains(` rather than the predicate | **applied** — renumbered to **eight positions** throughout (6 simply has two spellings); the two citations moved to `:768`/`:657` |

### Round 3 (on Draft v3 — produced this revision, v4)

**Evidence: APPROVE 95%.** **Consistency: APPROVE 88%**, with one open Major recorded rather than
closed — it blocks the **spike PR**, not this merge, and is carried in §12.1 and §7.3 so it is
visible. **Test-lens: NEEDS-FIXES 93%**, four mechanical items. **CI also failed** the harness
test v3 had just introduced, which is the round's most useful result: the pin worked.

**10 disposition rows: 10 applied, 0 declined.** (Nine from the three lenses, plus one
self-reported: the corpus-pollution defect below, found while verifying T-N2.)

| # | Finding | Disposition |
|---|---|---|
| CI | `tests (compiler)` run 32868158970 — **`ExperimentTranscripts_MatchARerun` red**. (a) `facts.py` line 4: committed `ClassNodes.cs:554` vs re-run `FunctionNode.cs:21` — recursive grep and glob results come back in **filesystem order**, which differs between APFS and ext4. (b) `facts2.py` line 94: committed F-3 subject line vs `<end of output>` — the transcript carried a **`git log`-derived** fact a shallow CI checkout does not have | **applied** — every multi-file probe now sorts by path then **numeric** line; `LC_ALL=C`/`LANG=C` pinned in both scripts; the `BoundTypeTests` probe's recursive fallback replaced by the real path; the F-3 probe reads the **pinned object's existence** (`git cat-file -e`) and prints a fixed marker in **both** branches, since the doc cites the SHA and not the subject. Full audit of all six scripts; ties in `facts2.py`'s listing made total rather than insertion-ordered. **Portability then proven three ways**: `LC_ALL=C` and `en_US.UTF-8` byte-identical; a second checkout at a different absolute path byte-identical; and no absolute path or timestamp appears in any transcript |
| **The pin caught its own author.** | — | Worth stating plainly, because it is the whole argument for P29: the transcripts were authored on macOS and were wrong on Linux, and **nothing in v1 or v2 would have noticed**. The first thing the new test did was fail on the doc it was written to protect |
| T-N1 | `compile53.py` rewrote `o53/baseline.json` — including a fresh `measuredCommit` — **unconditionally**, and P29 runs it every test. So every test run dirtied a committed gate-5 instrument, and **P30's "measuredCommit is 40-hex" leg was self-fulfilling**: its sibling had just written one | **applied** — the write is behind `CALOR_WRITE_O53_BASELINE=1` (set by `regenerate-transcripts.py`), the `CALOR_REGENERATE_S5_LEDGER` pattern. The default path **verifies** the recomputed counts against the committed ledger and reports a verdict. Both modes print identical lines, so the transcript is mode-independent. Confirmed: `git status` is clean after `dotnet test --filter EffectRowExperimentHarnessTests` |
| self | **The harness polluted the demand ledger's corpus.** Found by running the full `Calor.Compiler.Tests` suite while verifying T-N2's count: `HigherOrderDemandLedgerTests.DA_CalorNative_MatchesLedgerExactly` red with *"D-A corpus size moved: **941** `.calr` files vs ledger **886**"*. The runners wrote their scratch `.calr` next to the scripts, and the demand ledger enumerates every `.calr` under the repo root by **walking the filesystem**, not `git ls-files` — so gitignoring them was not enough; they were counted as corpus | **applied** — all four runners now write scratch to a temp `WORK` directory outside the repository (`CALOR_EXPERIMENT_WORKDIR` overrides), and elide it from the transcript so output stays path-free. `compile53.py`'s emitted `.g.cs` moved too; only the committed `baseline.json` stays in-tree. Verified: a full harness run leaves **zero** `.calr`/`.g.cs` under `docs/`, and `HigherOrderDemandLedgerTests` is green alongside P29/P30. **This is the second time in one round that the new pin caught a defect in its own scaffolding** — the first was CI's filesystem-order failure |
| T-N2 | `eng/test-manifest.json` not bumped; `scripts/check_trx.py:73` is **exact equality** | **applied** — `Calor.Compiler.Tests` `expectedTotal` **7670 → 7672** (main's current value + the two new `[Fact]`s), `expectedSkipped` unchanged at 3 since neither skips. `python3 scripts/check_test_quality.py` passes |
| T-N3 / C-Major | **§7.3's partition neither permitted nor forbade class/interface-level `eff`**, yet §7.4's middleware form used it and **R1 — which P27 recomputes — requires all four A3 fixtures to compile with zero Calor0404**. So the middleware fixture would have failed R1 *by this document's own rule*, firing the ramp for an artefact of the doc rather than a fact about rank-1 | **applied** — §7.3 gains the eighth row: **forbidden in E2**, with the sequencing spelled out. **The spike PR must decide A3's middleware spelling before freezing A3**: member-level `§MT{…}<eff e>` first (position 1, already permitted), class-level only if member-level provably cannot express R2 — at which point §9's seventh insertion point becomes **unconditional** and the table row flips. §9's "+1 conditional" is now contingent on that named outcome, and **zero** if member-level works. §12.1 carries the open Major in full. **P18** gains the seventh rejection case plus `MemberLevelEffOnInterfaceMember_Parses`. New evidence added to the harness: **W1a**/**W1b** (interface *and* implementing-class members each carry their own type-parameter list today) and **W1c** (Calor0421 fires across *renamed* member type parameters, so the interface↔implementation match is already alpha-equivalent — the property `StrictnessBatchTests.cs:172` pins for overrides). What W1 does **not** settle, and the doc now says so: whether `fits` identifies the interface's `e` with the implementation's `e` |
| E-V1 | §0's diagnostic row listed three codes; §6.1 allocates four | **applied** — §0 now says **four** and names Calor0405 |
| E-N5 | §15's counts do not self-sum against its own tables | **applied** — recounted **from the tables**: round 1 is **87 rows = 81 applied + 4 declined + 1 partial + 1 superseded**; round 2 is **22 rows, all applied** (v3's first pass said 24). The arithmetic history is recorded in-line, since this line had by then been wrong twice |
| — | The Calor0405 two-stage distinction was implicit | **applied** — §6.1 gains a table: the **parser** stage (§3.1) **recovers** by consuming the `§E{…}` group so the rest of the declaration still parses; the **binder** stage (§3.5) does not and needs no recovery, because the row parsed cleanly and is simply reported and dropped. Same code because the author's fix is the same — move or delete the row |
| E-r2 | The test-lens reviewer's own correction | **recorded** — v2's "28 lines" for `RequestPreProcessorBehavior.cs` came from **the reviewer's `wc -l`**, which undercounts an unterminated final line; **v3's 29 is right**, and v1 had been right all along. §14.1 already carries this as the clearest instance of accepting an unexecuted claim — the round-3 addition is naming where it came from, so the lesson reads symmetrically: the doc's evidence discipline applies to reviewer findings too |

#### Carried open into the spike PR

One item is deliberately **not** closed here, and is recorded in §7.3, §9, §12.1 and this table so
it cannot be carried silently: **A3's middleware spelling**. It blocks freezing A3, which the
spike PR does; it does not block this document merging, because §7.3 now makes the E2 rule total
(forbidden) and §9's conditional cost contingent on a named, testable outcome.

#### Round 4 — the emitter spike (roadmap §4.1 term 1)

Not a review round: an **execution** round. The spike PR ran §12's plan against a throwaway
prototype and reports back. Six dispositions, all applied to this document.

| # | Finding | Disposition |
|---|---|---|
| S1 | **The open Major is closed.** §7.3/§12.1 carried A3's middleware spelling as an open Major that blocked freezing A3. Member-level was tried first, as sequenced, and **expressed R2** | **applied** — §12.4 records **MEMBER-LEVEL**. §7.3's last row does **not** flip (class/interface-level stays forbidden in E2); §9's seventh insertion point stays **conditional at zero cost**; P18 keeps `Rejected_ClassOrInterfaceLevel` |
| S2 | **The half W1c did not settle is now executed.** §12.1 said W1c was evidence about member *matching*, not row *unification* | **applied** — `after/A3-middleware-alpha.calr` binds `eff e` on the interface and `eff f` on the implementation and compiles. Rows carry binder **indices**, not names, so `fits` is alpha-equivalent by construction. §7.3, §12.4 |
| S3 | **R2 needed no carve-out.** The two `IsSubsetOf` calls in `CheckEffectVariance` became two calls to the shared `EffectRow.Fits`; there is no `if (row.IsPolymorphic)` branch anywhere in that method | **applied** — §12.4. This is §6.3's "three codes express one relation", executed |
| S4 | **G-CODEGEN does not block, and A2's caveat is named rather than smoothed over.** Five of six artifacts are byte-identical; A2 differs only inside `#line` directives, because its two rows sit on their own added lines | **applied** — §12.4's table gives both readings. All four A3 fixtures are strictly byte-identical because an inline row is written on the **same** line as its type, so nothing shifts |
| S5 | **The prototype's additivity bounds what R1 proves.** Every new code path is gated on a row being *present*, so §3.5's Calor0425-on-omission is unimplemented and a row-less function-typed position still reaches Calor0418 | **applied** — §12.4 caveat 1 states it. R1's bar is the *absence* of three codes, so it is weaker evidence than its wording suggests, and the doc now says so rather than letting a reader infer it |
| S6 | **Committing `.calr` artifacts under `docs/` collides with three frozen corpus instruments** — the demand ledger's filesystem walk, the formatter baseline's `git ls-files`, and `facts.py`'s own sweep. Found by running them, not by reading them | **applied** — §13.5(b). All three exclude `docs/design/spikes/` by path; **no count changed**, because those files were never in the 886. Round 3 solved the same problem for scratch files by moving them out of the repository; committed artifacts cannot move |

**What the spike did NOT settle**, recorded so it is not read as more than it is: §8.1's six E1
items are untouched (the prototype reads rows off the **AST**, not the bound tree, so §8.2 is
still owed); §14 Q1's residual stands — member-level `eff` works when **both** sides are Calor,
and a C#-declared interface still has no row to check against.

**Bar for a further review round.** Unchanged: APPROVE from the evidence and consistency lenses.
The spike PR's own diff is where R2 and R3 are reviewed, because §12.3 says plainly that a test
cannot re-derive them.

#### Round 4, review 1 — adversarial review of the spike PR

**NEEDS-FIXES (80%).** The verdict itself was **EARNED**: the reviewer recomputed all 29
artifacts byte-exact from the prototype commit, and G-CODEGEN (modulo `#line`), R1, R2, R3 and
R3's counter-example all reproduced, with the spike's own caveats holding. Every finding below
is therefore a **correctness-of-record** fix, not a re-adjudication. Twelve findings, **12
applied, 0 declined.**

| # | Sev | Finding | Disposition |
|---|---|---|---|
| 1 | **Critical** | **Seven transcript divergences, not three.** Unrecorded: `run.py` X9b and X9c (both go from a Calor0100 cascade to a clean compile — positions 8 and 5 doing what §3.3 promises), `run3.py` Z3 (8× Calor0100 → 1× Calor0405), and `facts.py`'s `IsSubsetOf` sweep (`EEP:533`/`:571` vanish, five `EffectSet.cs` lines appear) | **applied** — §13.5(a) is a seven-row table; the JSON carries all seven with verdicts; "exactly these three" → **SEVEN** everywhere; **P27 asserts the count and the case list agree**. The root cause is recorded in-line: the first record was read off P29's failure message, which prints the *first* difference per script and hides the rest. Confirmed by diffing all six scripts in full — `run2.py`, `facts2.py`, `compile53.py` are clean. **Row 7 is the spike's own headline claim moving a fact the harness pins**, which is exactly the kind of thing that must not be discovered by E2 |
| 2 | Major | `spike_artifacts.py` still wrote `.g.cs` after the guard rename, so it emitted untracked files beside the committed `.txt`, never refreshed the evidence, and mis-targeted its stale-file `os.remove` — while P31's failure message told the reader to run it | **applied** — the generator writes `.g.cs.txt`; re-run against the prototype it regenerates **all 29 committed files byte-identically** and leaves nothing untracked. A `.gitignore` at the spike root now ignores the bare `before/*.g.cs`/`after/*.g.cs` form so a stale copy can never commit one — which also protects the Calor-first guard |
| 3 | Major | `gCodegen.A2.diffBytes: 0` while 7 bytes differ — the field was a **length** delta, and no test read it (A1 passed with `99999`) | **applied** — split into `strictDiffBytes` (A2 = **7**, `cmp -l`) and `nonLineDirectiveDiffBytes` (0), **both asserted by P27 for every artifact**. Verified discriminating: mutating A2's value to 0 turns P27 red |
| 4 | Major | §14 Q1 was untouched though the PR body claimed it was narrowed | **applied** — Q1 rewritten to record the outcome: the ramp did not fire; a Calor implementation does **not** have to widen the interface, because it declares the *same* variable; the residual is a **C#-declared** interface, which has no row to check against, making it a manifest question (§8.4 gives the 0.15 schema no such field). No longer a ramp trigger — a scope boundary, and E2's to put in the release notes |
| 5 | Major | `ramp.R1.claim` dropped §7.5's precondition *"when every participating row is concrete and every callee resolves"* | **applied** — restored verbatim, with a note on why it matters (it is what makes R1 a claim about the four combinators rather than about the resolution ceiling) and why every A3 fixture satisfies it. **P27 asserts the clause is present** |
| 6 | Minor | P27 asserted only `Contains("E2")` on the deferral string — a joke string passed | **applied** — asserts the **exact** string; §12.5 says so |
| 7 | Minor | P31 did not cover the after-only fixtures, so `A3-middleware-alpha.g.cs.txt` could be deleted with every test green | **applied** — P31 covers all three, **and** asserts that `A3-middleware-broadening` does **not** emit, because being rejected is its evidence |
| 8 | Minor | A2's deviations omitted the dropped `where TRequest : notnull` and the three XML doc-comment blocks | **applied** — both added, with the reason: this artifact is `calor convert`'s output recorded as-is, and hand-editing it back would make it *less* faithful |
| 9 | Minor | The prototype routes `§CL{…}<eff e>` and an unbound `§E{e}` to **Calor0403**, not Calor0404 | **applied** — §12.4 caveat 5. Both are still forbidden, so it is not unsound; the prototype forbids them *incidentally* and reports a **taxonomy** error for a **scope** defect. E2 obligation: all seven §7.3 sites report Calor0404, which is what P18 asserts |
| 10 | Minor | Calor0421 prints *"interface declares: `[pure]`"* for an interface declaring `{e}` | **applied** — §12.4 caveat 6. The verdict is right, the text is not: the message is built from `EffectSet.ToDisplayString()`, which knows only the concrete part. E2 obligation: render the **row** at both variance sites — §8.3 already reserves `EffectRow.ToDisplayString()` and **P22** pins the text |
| 11 | Minor | §9's parser row still said "+1 conditional … only if the spike PR proves"; §7.5 cross-referenced "§14 Q2" | **applied** — the row resolves to **+0**, so §9's parser cost is **6 insertion points + 1 `eff` branch**; the cross-reference is now **Q1** |
| 12 | Minor | Nothing said A2's AFTER form still exits 1 | **applied** — §12.4 caveat 4: the row **removes** the Calor0418 on `§C{next}`, and the residual `Calor0410 … effect 'unknown'` is §2.2's resolution ceiling, which rows do not address |
