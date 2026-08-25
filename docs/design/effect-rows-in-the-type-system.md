# Effect Rows in the Type System (TIER2D)

**Status:** Draft v2
**Date:** 2026-08-25 (v1 2026-08-25; review round 1 applied — §15)
**Measured against:** `main` @ `82338e37` (v0.14.3 + PR #1089 E1 slice 1 + PR #1090)
**Governing inputs:** `docs/design/calor-direction.md` (`:23` TIER2D, `:33` generics deferral,
`:57` the three worked examples, `:90-120` the postscript); `docs/plans/roadmap-v0.13-v0.15.md`
§4.0–§4.5 (Draft v4); `docs/plans/v0.14-metadata-binding-scoping.md` §2 D2 (`:90-101`), D5
(`:166-171`), D6 (`:173-181`), §3 S6 (`:301-317`);
`bench/phase0-agent-native/higher-order-demand-ledger.json`;
`bench/phase0-agent-native/metadata-binding-corpus-ledger.json`.

**Evidence discipline (new in v2).** Every claim about how the compiler behaves today is backed
by an **executed** experiment against a compiler built from this worktree
(`dotnet build src/Calor.Compiler` → `src/Calor.Compiler/bin/Debug/net10.0/calor.dll`), not by
reading source. Cases are labelled `X*`/`Y*` and their **verbatim** output is quoted. Reproduce
with `docs/design/spikes/effect-rows/experiments/run.py`. Structural claims (line numbers, file
counts) carry `file:line` and the command that produced them. Where a governing input disagrees
with the source, the source wins and the disagreement is recorded in §14.1.

---

## 0. Entry-gate checklist (roadmap §4.1)

| # | Term | Status |
|---|---|---|
| 1 | **Emitter spike producing actual compiler output** on named, frozen artifacts | **NOT MET AT THIS DOC'S MERGE, and honestly so.** §12 freezes the artifacts, the schema, and the verdict format. The spike output lands in a **follow-up PR that MUST merge before E2**; if it does not, E2 does not merge. This doc's merge still freezes gate 1's class list and gate 2's ledger, exactly as roadmap §4.4 specifies — the spike PR adjudicates only the §7.5 ramp. |
| 2 | **External critique cycle with a pass bar** | **Round 1 complete** — evidence 92%, consistency 88%, test-lens 88%, all NEEDS-FIXES. Every finding is dispositioned in §15. Round 2 pending; the bar is APPROVE from the evidence and consistency lenses. |
| 3 | **Priced blast radius in the doc** | **§9**, one table, each row with the command that produced its number. |
| — | **Demand denominator registered before the doc opens** | **DONE** (PR #1086). D-A **3**, D-B **3121**, total **3124**, floor **25**. §1. |
| — | **E1 permitted to start before this doc merges** | Slice 1 executed (PR #1089); slice 2 pending. §8.1 records both. |
| — | **Diagnostic allocation frozen at design-doc merge** | **Calor0424 `EffectRowMismatch`**, **Calor0425 `EffectRowUnknown`**, **Calor0404 `EffectVariableScope`**. All three verified free: `grep -rn "Calor042[4-9]\|Calor040[4-9]" src/ tests/` → no hits; `Diagnostic.cs:378` is `Calor0403`, `:381` `Calor0410`, `:437` `Calor0423`, `:440` `Calor0500`. |

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
followed by a call-chain line. §3.5, §5.3 and §10 quote this shape; where they show a *new*
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
