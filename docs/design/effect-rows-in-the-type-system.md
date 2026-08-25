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

> **Decision.** A row is written with the **existing `§E{…}` tag**. Which thing it annotates is
> decided by **line adjacency**: a `§E{…}` whose first token sits on the **same source line** as
> the last token of the type immediately preceding it is **that type's row**; a `§E{…}` on any
> **later line** is the **enclosing declaration's own row**, exactly as today. Seven positions.
> No new token, no new AST node type, no new `IAstVisitor` method.

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
claim, executed rather than asserted. It is still a **breaking change to a form that parses
today** (Y1b, X2b, Y5a all compile now and would mean something else), and the release notes must
say so; it is not a change to any form anybody has written.

The 23 files were each compiled at `82338e37` as the baseline the E2 PR re-runs
(`experiments/compile53.py`, results in `o53/baseline.json`). Today **22 of the 23 are already
compile-red for reasons unrelated to effects** — 15 `bench/mcp/tasks/*` on Calor0830 legacy
closers, 3 `benchmarks/security/*` on Calor0006/0100/0102 (the #901 stale subjects), 1 lint error
fixture on Calor0002 — which is the same set the demand ledger lists in
`notReachingEffectPass`. **Exactly one is green**:
`tests/E2E/agent-tasks/fixtures/collections-project/Collections.calr`. So the live risk surface
for the two-line `§O`/`§E` form is one file and one occurrence, and the dominant real corpus is
the 2948/471 arrow form, which the rule provably cannot reach.

### 3.3 The seven positions and the six insertion points

| # | Position | Spelling | Parses today? |
|---|---|---|---|
| 1 | Function / method declaration (its own row) | `§E{…}` on its own line | **yes** — `Parser.cs:1377-1380` (`§F`), `:8836-8840` (`§MT`), and the async variants (**Y6a** executed on `§MT`) |
| 2 | Lambda literal | `§LAM{id:x:i32} §E{…}` | **yes** — `:11392-11397`; **X7** compiles today |
| 3 | Delegate declaration | `§DEL{id:Name}` … `§E{…}` | **yes** — `:11574-11577`; **X8** compiles today |
| 4 | Parameter, tag form | `§I{Func<i32,i32>:f} §E{…}` | parses, **wrong meaning** — Y1a/Y1b |
| 5 | Parameter, inline form | `(Func<i32,i32>:f §E{…}, i32:v)` | **no** — **X9c**: `Calor0100: Expected CloseParen but found Effects` |
| 6 | Return | `§O{Func<i32>} §E{…}` / `-> Func<i32> §E{…}` | parses, **wrong meaning** — X2b, Y5a |
| 6b | Binding | `§B{f:Func<i32,i32>} §E{…} <init>` | **no** — **Y3a**: `Calor0100: Expected statement but found Effects` |
| 7 | Field | `§FLD{Action<i32>:onChange:pri} §E{…}` | **no** — **X9b**: `Calor0100: Expected TP, WHERE, EXT, IMPL, FLD, … but found Effects` |

(Draft v1's §14 Q4 closed position 7 by *reading* `ParseClassField`'s default-value guard and
concluded it already parsed. **X9b disproves that**: the class-member loop rejects `Effects`
before the guard is relevant. Corrected here; the correction is why v2 executes everything.)

**Insertion points: six, not twenty.** The evidence lens counted 9 `§I`→`ParseParameter` dispatch
arms (`Parser.cs:1369, :1565, :8271, :8826, :8971, :10137, :10300, :10390, :11566`) and 7
`§O`→`ParseOutput` arms (`:1375, :1571, :8278, :8833, :8978, :10397, :11572`) — correct, and
fatal to Draft v1's "one check per arm". **The check moves inside the shared productions
instead**, so each is written once:

| Insertion point | Covers |
|---|---|
| end of `ParseParameter()` (`Parser.cs:1740-1752`) | all **9** `§I` arms |
| end of `ParseOutput()` (`:1754-1768`) | all **7** `§O` arms |
| `TryParseInlineSignature`, after the modifier slot (`:13567`) | inline parameters |
| `TryParseInlineSignature`, after the arrow's return type (`:13620`) | `-> T §E{…}` |
| `ParseClassField()`, before the default-value branch (`:8709`) | `§FLD` |
| the `§B` production, after its attribute group | `§B` |

Positions 1–3 need **no** parser change at all. `TokenKind.Effects` is absent from
`ExpressionParsers` (`Parser.cs:15-65`, exactly 47 entries), so `IsExpressionStart()`
(`:2466-2469`) is false for it and no initializer or default-value parse can swallow a row —
which is why insertion points 5 and 6 are safe. That property is pinned (§13.2).

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

> **Decision.** `Row ::= Concrete(S) | Assumed(S, R) | Unknown`, with `S` a registry-closed effect
> set and `R` a **canonically ordered set** of reasons. Inference uses a join `⊔` (⊤ = Unknown).
> Checking uses a **separate three-valued relation** `fits` — `Fits | DoesNotFit | CannotTell` —
> **totally defined over all nine source×destination cells**. `EffectSet.IsSubsetOf` is not
> reused as `fits`.

### 4.1 The carrier, and the family/narrow gap

`S` ranges over `EffectCodes.Registry` (`EffectTypes.cs:65-109`), ordered by
`EffectSubtyping.Encompasses` (`EffectSubtyping.cs:52-66`).

**Sub-decision, executed in E2's PR:** add `database`, `network` and `environment` to
`EffectSubtyping.Subtypes` (`:14-43`) so a bare family code encompasses its narrow siblings.
Today it does not (§2), which under rows becomes visible at every binding site instead of only at
a declaration. `filesystem` has no bare code so `fs:rw` stays the filesystem top; `proc` and
`http` have no narrow siblings. This is a **widening** — nothing that compiled stops compiling —
and any Calor0410 that *disappears* is listed by name in the E2 PR body and counted by gate 5.
`Calor0401 UnusedEffectDeclaration` is declared and never reported (`Diagnostic.cs:376`), so the
widening has no 0401 blast radius.

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

**Interaction with §3.5.** A `§B{f} §LAM …` with no binding row takes the *initializer's* row,
which is the lambda's — so the common shape stays silent. Executed baseline **Y9a**: today that
shape yields `Calor0418` at the invocation (demoted to a warning under `--permissive-effects`);
after E4 it compiles, with `{}` charged. That is §13.1's rewrite of
`StrictnessBatchTests.cs:47`.

---

## 6. Decision 4 — Compatibility checking sites (E3)

> **Decision.** **Six** sites, one shared relation, and **Calor0420/0421 stay as distinct codes**
> re-implemented on top of it. The gate-1 denominator frozen by this document is **six classes,
> dropping to five if the §7.5 ramp fires**.

### 6.1 Diagnostic allocation (frozen at this doc's merge)

```
Calor0424  EffectRowMismatch    Error.   fits(...) = DoesNotFit. Never waived, any flag, any site.
Calor0425  EffectRowUnknown     Warning; Error under --strict-effects.
                                fits(...) = CannotTell. Waived by --permissive-effects.
Calor0404  EffectVariableScope  Error.   An effect variable used where §7.1 forbids it, or not in
                                scope. A declaration-shape violation, never a binding-site verdict.
```

All three free: `grep -rn "Calor042[4-9]\|Calor040[4-9]" src/ tests/` → no hits;
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
§13.1 disposes of the three existing pins that observe them. Per §4.4, a `Fits`-carrying-reasons
verdict makes the destination row `Assumed`, so the assumption survives the hop.

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
Calor0424: Argument 'Shout' has effect row [cw], which does not fit parameter
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

The third **re-words** today's Calor0419 text (`EEP:605-611`) rather than merely re-coding it —
Draft v1 claimed otherwise. The re-wording is deliberate (it names the row) and pinned.

---

## 7. Decision 5 — Rank-1 effect polymorphism

> **Decision.** Effect variables are declared with an **`eff` modifier in the existing
> type-parameter list** (`§F{f001:Map:pub}<T, U, eff e>`) and used as a **bare identifier**
> inside `§E{…}` (`§E{e}`). Not `!e`. They may appear only in a declaration's own row and its
> parameters' rows; anything else is **Calor0404**. Ships iff the §7.5 ramp does not fire.

### 7.1 Why not `!e` — executed

Draft v1 chose `!e` by inspecting `ParseValue`. Three executed results kill it:

```
CASE X3   §E{!e}          → Calor0403: Unknown effect code '!e'.
CASE X3b  §E{alloc, !e}   → Calor0403: Unknown effect code '! e'.      ← lossy
CASE Y2a  §O{str!str}     → parses; `!` is the live fallible/Result type suffix
CASE Y2b  §B{r:i32!str}   → parses (Calor0200 "not known to the type checker")
```

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
E2 extends. X6a: `eff e` is new syntax today. X6b: an identifier-shaped modifier before a type
parameter name **is an existing pattern** (`in`/`out`, `Parser.cs:7612-7621`, gated by
`Calor0119`), so `eff` is the same shape, not a new one.

**Exact changes.** (a) `ParseOptionalTypeParameterList` (`Parser.cs:7596-7639`) gains a branch
beside the variance branch: `Check(TokenKind.Identifier) && Current.Text == "eff" &&
Peek(1).Kind == TokenKind.Identifier` marks the next identifier an effect variable. The one-token
lookahead is what keeps a type parameter literally named `eff` (`<eff>`) working. (b)
`AttributeHelper.InterpretEffectsAttributes` (`:329`) resolves each code against the enclosing
declaration's in-scope effect-variable set **before** `EffectCodes.TryParseCompact`, so `§E{e}`
binds and an out-of-scope `§E{e}` raises **Calor0404**, not Calor0403. No lexer change, no new
token kind, no `IsKeyword` change.

### 7.3 Scope (Calor0404)

Permitted: the declaration's own row; a parameter's row. Forbidden, each its own Calor0404
message: a **return** row (a returned function mentioning the caller's variable is rank-2); inside
a **generic argument** (`List<Func<i32,i32> §E{e}>` — types are strings in the parser, §3);
on a **`§B`**, a **field**, or a data declaration (nothing binds the variable there). A row may
mix: `§E{cw, e}` denotes `Concrete({cw}) ⊔ e`.

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

**`Match`** binds one variable across both arms, so `e` is their join — a pure `onNone` and a
printing `onSome` give `{cw}`. **Middleware/`next`** is §7.5's decisive case, below.
**Callbacks** need **no** effect variable: a `§FLD{Action<i32>:onChange} §E{cw}` carries a
concrete row, so three of the four combinators need rank-1 and the fourth does not — if the ramp
fires, callbacks still work.

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
the interface, rank-1 rows do not compose with external interfaces. §14 Q2.

---

## 8. Decision 6 — Binder and representation

### 8.1 Recorded, not decided (E1)

Roadmap §4.1: *"`UnresolvedBoundType` → `Unknown` row, `FunctionBoundType`'s effect slot, and
symbol-identity keying are E1 decisions, made in §4.2, not design-doc decisions."* Status:
receiver-from-`BoundExpression.Type` and `_variableTypeMap` deletion **executed** (#1089);
receiver `BoundExpression` on the call nodes, binder-emitted `UnresolvedBoundType`, the
`Unknown`-row contribution, symbol-identity keying in `EffectResolver`/manifests/IL summaries, and
`BoundLambdaExpression`'s `FunctionBoundType` all **pending** (evidence in §2.2). E2 consumes all
six; roadmap §4.2's cut line already prices the risk.

### 8.2 `FunctionBoundType`

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

### 8.3 `DisplayString` — rows do not appear

> **Decision.** `FunctionBoundType.DisplayString` stays `"(p1, p2) -> ret"`
> (`BoundType.cs:224-225`). A separate `RowDisplayString` carries the row for diagnostics and
> hover.

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
(`EffectResolver.cs:596-612`). A BCL method that **returns** a delegate yields
`EffectRow.Unknown` on that return and is a frozen gate-1 residual (roadmap §4.2 DEFERRED); the
manifest schema gains no row-on-return field in 0.15. With 431 of 1248 BCL call sites unresolved,
roughly a third will produce Unknown rows — §13.4 registers the ledger that counts it.

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

---

## 9. Priced blast radius

Every row measured at `82338e37`; the command is named where it is not a plain `grep -c`.

| Bucket | Files | Evidence / note |
|---|---|---|
| `IAstVisitor` interfaces + 5 implementers | **0 forced** | 184 methods each (`grep -c "^    void Visit"` / `"^    T Visit"` on `Ast/AstNode.cs`; interfaces `:59`, `:247`); implementers `Ids/IdScanner.cs:9`, `CodeGen/CSharpEmitter.cs:88`, `Migration/CalorEmitter.cs:12`, `Verification/ExpressionSimplifier.cs:13`, `LanguageServer/Utilities/AstPositionVisitor.cs:10`. Rows add **no node type**. Counterfactual for a new node kind: both interfaces (one file, `Ast/AstNode.cs`) + 5 implementers = **6 files**, ×2 methods, plus CLAUDE.md's seven-step checklist |
| …but `CalorEmitter.cs` **does** change | **1** | round-trip fidelity: `calor fmt` and the harness must re-emit the row (§13.2 pins parse→emit→parse per position) |
| AST node classes | **4** | `ParameterNode` (`Ast/FunctionNode.cs:252`), `OutputNode` (`Ast/FunctionNode.cs:21`), `BindStatementNode` (`Ast/ControlFlowNodes.cs:161`), `ClassFieldNode` (`Ast/ClassNodes.cs:554`) |
| **`eng/ast-schema.json`** | **1** | forced by `tests/Calor.Compiler.Tests/ArchitectureTests.cs:158` `AstSchema_CoversEveryNodeDispatchAndChildRelation`; **this is also the existing "zero visitor churn" pin** — Draft v1 counted 0 here |
| Parser | **1** | `Parsing/Parser.cs`: **6** row insertion points (§3.3) + 1 `eff` branch (`:7596-7639`) |
| Lexer / `Token.cs` | **0** | no new token kind, no `IsKeyword` change (§7.2) |
| Effects subsystem | **10 existing + 1 new** | `ls src/Calor.Compiler/Effects/*.cs` → 10, all touched (incl. both `CrossModuleEffect*.cs`, §6.2), plus new `EffectRow.cs` |
| Binder | **2** | `Binding/BoundTypes/BoundType.cs`, `Binding/BoundNodes.cs` |
| Build cache | **1** | `Incremental/BuildStateCache.cs:121` `"3.0"`→`"4.0"` |
| Index / query | **2** | `Indexing/ProjectIndex.cs:145` `"3.0"`→`"4.0"`, `Commands/QueryCommand.cs` |
| Diagnostics | **1** | `Diagnostics/Diagnostic.cs`: Calor0404, 0424, 0425 |
| `.calr` goldens under `tests/TestData` | **0** | `grep -rlE 'Func<\|Action<\|Action[}:]\|Predicate<\|§DEL\|§LAM' tests/TestData --include='*.calr'` → **0 of 359**. Draft v1 said "≤8" from a whole-corpus grep |
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

AFTER — `§FLD{Action<i32>:onChange:pri} §E{cw}` (position 7; **X9b** shows this does not parse
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
Calor0424: Argument 'ReadsAndLogs' has effect row [cw, fs:r], which does not fit
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
| **A1** `tools/calor-allowlist-audit/allowlist-audit.calr` | the dogfood utility: 127 lines, 7 `§E{`, sibling `CalorAllowlistAudit.csproj`, built in CI (`.github/workflows/test.yml:181`), **no higher-order code** | **G-CODEGEN** only — the regression module | exists at `82338e37` |
| **A2** `bench/corpus/MediatR/src/MediatR/Pipeline/RequestPreProcessorBehavior.cs` | 28 lines at the pinned MediatR SHA `fb309026775ef953a64fb5339d074426c1ad2c37`: interface implementation (`:12`), delegate-typed parameter `RequestHandlerDelegate<TResponse> next` (`:20`), invocation `await next()` (`:27`). Delegate declared at `IPipelineBehavior.cs:12`, contract at `:29` | **R2**, and G-CODEGEN | submodule initialized and read; **28** lines, not 29 (Draft v1) |
| **A3** `docs/design/spikes/effect-rows/combinators/{map,match,middleware,callback}.calr` | the four §7.4 AFTER forms, as compiled fixtures | **R1** and **R3** | to be authored by the spike PR |

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

1. `before/` and `after/` — the `.calr` source and the emitted `.g.cs` for A1, A2 and each A3
   fixture, plus the compiler's full diagnostic list per file.
2. `experiments/` — `run.py`, `run2.py`, `facts.py`, `facts2.py`, `compile53.py` and
   `o53/baseline.json`: the executed cases this document quotes, so a reviewer re-runs rather
   than re-reasons.
3. **`spike-verdict.json`** — the machine-readable verdict, replacing Draft v1's prose `README`.
   Schema: `{schemaVersion, measuredCommit, artifacts:{A1,A2,A3…}, gCodegen:{artifact→PASS|FAIL,
   diffBytes}, ramp:{R1,R2,R3→PASS|FAIL, evidence}}`, with `ramp.verdict` = `VALIDATED` iff R1 ∧
   R2 ∧ R3. It follows the pattern of the two existing ledgers
   (`higher-order-demand-ledger.json`, `metadata-binding-corpus-ledger.json`) — JSON, a recorded
   commit SHA, and an **exact-equality test** (§13.2 `SpikeVerdictMatchesRecomputation`).

### 12.4 Pass/fail, per criterion per artifact

| | A1 | A2 | A3 |
|---|---|---|---|
| **G-CODEGEN** (blocking, feature-wide) | required | required | n/a |
| **R1** four combinators clean | n/a | n/a | required |
| **R2** interface/impl, no carve-out | n/a | required | n/a |
| **R3** one-line instantiation solve | n/a | n/a | required |

The verdict is **read off `spike-verdict.json`**, not argued from prose. The spike PR must merge
before E2; if it does not, E2 does not merge (§0 term 1).

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
| `:767` (`:749` `M1_ReturnedDelegateInvocation_IsError`) | Calor0418 | **rewrite** → returned value's row charged; 0425 when the `§O` carries no row |
| `:502` (`:472` `C2_DecoyNamedDelegateParameter_ShadowsFunction_IsError`) | Calor0418 | **rewrite** → the decoy parameter's row governs, not the shadowed function's. *Orphaned in Draft v1* |
| `:260` (`:245` `OverrideOfExternalBase_RoutesToAssumedChannel`) | Calor0419 | **rewrite** → Calor0425 (§6.2 retires the 0419). *Orphaned in Draft v1* |
| `:607` (`:587` `C3_ExternalInheritedImplementation_RoutesToAssumed`) | Calor0419 | **rewrite** → Calor0425. *Orphaned in Draft v1* |
| `:656` (`:640` `C4_DelegateValueArgument_ToKnownHigherOrderName_SurfacesAssumption`) | Calor0419 warning | **rewrite** → Calor0424/0425 at the argument per §10.2. *Orphaned in Draft v1* |
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

### 13.2 New pins — home and freeze

| # | Pin | Home | Freeze | Discriminating revert |
|---|---|---|---|---|
| P1 | `RowSuffix_SameLineOnI_IsParameterRow_NotDeclarationRow` — case **Y1b**: compiles today, must be Calor0410 after | `Calor.Enforcement.Tests/EffectRowSyntaxTests.cs` | with E2 | drop the `Span.Line` comparison → Y1b compiles again |
| P2 | `RowSuffix_NextLine_IsDeclarationRow` — X1b/Y6a/Y5a unchanged; the 2948/471 arrow corpus safe | same | with E2 | invert the line test → the whole corpus moves |
| P3 | `RowParses_AtEverySevenPositions` — one case per §3.3 row, incl. the three that already parse (X7, X8, Y6a), which **no test covers today** (`grep '§LAM.*§E' tests/` → 0) | same | with E2 | remove any insertion point → that row fails |
| P4 | `RowRoundTrips_ParseEmitParse` — parse → `CalorEmitter` → parse, byte-identical, **one case per position** | `Calor.Compiler.Tests/NewFeatureRoundTripTests.cs` | with E2 | drop the `CalorEmitter` row emission → 7 cases fail |
| P5 | `EffectsTokenIsNotAnExpressionStart` — `TokenKind.Effects ∉ Parser.RegisteredExpressionStartTokens` (`Parser.cs:67-68`) | `Calor.Compiler.Tests/ExpressionRegistrationTests.cs` | **before E2** | add `Effects` to `ExpressionParsers` → initializers swallow rows |
| P6 | `OmittedRow_PerSite` — declaration=pure, lambda=inferred, **binding-with-initializer=inferred**, bare binding/param/return/field=Unknown (four omitted sites, not one) | `Calor.Enforcement.Tests/EffectRowLatticeTests.cs` | with E2 | make the parameter default `Concrete(∅)` → E-3's laundering re-opens |
| P7 | `FamilyCodeEncompassesNarrowCode` — `§E{db}` admits `db:r`; `fs:rw ⊇ fs:w` regression | `Calor.Compiler.Tests/Effects/EffectSubtypingTests.cs` (exists; `:20,:29,:38` are the `fs` cases) | with E2 | remove the `database` entry → first half fails |
| P8 | `FitsIsTotalOverNineCells` — table-driven over §4.3, **including the three `Assumed`-destination cells** | `EffectRowLatticeTests.cs` | **design-doc merge** (the table is normative) | re-introduce `EffectSet.cs:100`'s `if (other.IsUnknown) return true` → `fits(Concrete, Unknown)` returns `Fits` |
| P9 | `EffectRowJoin_IsASemilattice` — associative, commutative, idempotent, identity `Concrete(∅)`, top `Unknown`, **reason sets canonically ordered** | `EffectRowLatticeTests.cs` | with E2 | make `R` a concatenated list → commutativity fails |
| P10 | `AssumedSurvivesTheDestination` — a two-hop fixture: `Assumed` source → `Fits` → destination row is `Assumed`, 0425 once per hop | `EffectRowLatticeTests.cs` | with E2 | make the destination `Concrete` → hop 2 goes silent |
| P11 | `NeverWaived_DoesNotFit_AtAllSixSites` + `PermissiveWaivesUnknown` — incl. **Y8a's flip**: 0420/0421 stop demoting | `Calor.Enforcement.Tests/StrictnessBatchTests.cs` | with E3 | route 0424 through the policy check, or restore `EEP:517-519` |
| P12 | `Calor0424_NotDefeatableByDeletingSourceE` — deleting the source `§E` yields Calor0410 on the source (§4.5) | `EffectRowLatticeTests.cs` | with E3 | skip the body check on rowed sources → one diagnostic disappears |
| P13 | `ParameterRowsAreContravariant` **and** `FunctionTypesAreInvariantInTypes` | `EffectRowLatticeTests.cs` | with E3 | flip the argument order in the parameter conjunct |
| P14 | `LambdaDeclaredRow_NarrowerThanBody_IsError`; `_CannotTell_IsCalor0425`; `_OmittedRow_IsInferred`; `_TypeCarriesDeclaredNotInferred` | `Calor.Enforcement.Tests/EffectRowLambdaTests.cs` | with E2 | restore `InferFromLambda` to ignore `lambda.Effects` |
| P15 | Six `_IsError`/`_Compiles` pairs **plus a `_CannotTell` arm each**: `RowMismatch_At{Assignment,Argument,Return,Override,InterfaceImpl,GenericInstantiation}` | `StrictnessBatchTests.cs` | **design-doc merge** — this is gate 1's frozen denominator | delete E3's rule for one site → that `_IsError` fails |
| P16 | `AllMismatchCodesShareOneRelation` — Calor0424, 0420, 0421 **and** `CrossModuleEffectEnforcementPass.cs:162` move together | `Calor.Enforcement.Tests/CrossModuleEffectTests.cs` | with E3 | give `CheckEffectVariance` its own subset test back |
| P17 | **`UnresolvedReceiver_YieldsCalor0425_NeverConcrete`** — an `UnresolvedBoundType`/unresolved receiver must produce `EffectRow.Unknown`, never a `Concrete` row. **The pin the whole design rests on**; absent from Draft v1 | `Calor.Enforcement.Tests/EffectRowLatticeTests.cs` | **before E2** | make the unresolved branch return `Concrete(∅)` → the fixture goes silent |
| P18 | `EffectVariable_*`: `Declares_EffModifier` (X6a's shape now parses); `TypeParamNamedEff_StillWorks` (the lookahead guard); `InScope_DoesNotRaise0403`; `OutOfScope_Raises0404`; `Rejected_In{Return,GenericArg,Binding,Field,Data}` (**five** rejection sites, not one); `MixedRow_IsJoin`; `InstantiatesFromArgumentRow`; `UnknownContributor_YieldsUnknown` | `Calor.Enforcement.Tests/EffectVariableTests.cs` | with E2 | **all of P18 is deleted if the ramp fires**, together with P15's site-6 pair |
| P19 | `FunctionTypesDifferingOnlyInRow_AreNotEqual` + the `GetHashCode` half + `RowsDefaultToUnknownNotPure` | `Calor.Compiler.Tests/Binding/BoundTypes/BoundTypeTests.cs` | with E2 | drop `Row` from `Equals` |
| P20 | `DisplayStringIsRowFree` — belt to `:139`/`:150`'s braces | same | with E2 | append the row → three tests fail |
| P21 | `ManifestResolutionMapsToRow` — `Resolved`/`PureExplicit`/`Unknown` → `Concrete(S)`/`Concrete(∅)`/`Unknown` | `Calor.Enforcement.Tests/EffectResolverTests.cs` | with E2 | map `Unknown` to `Concrete(∅)` → P17's sibling fails |
| P22 | `MessageTexts` — the four new strings: §10.1's `Effect row: charged by invoking …`, §10.3's two, §6.4's 0424 text. Existing pins assert `Message.Contains`; these assert the **full** new clause | `StrictnessBatchTests.cs` | with E3 | reword any clause |
| P23 | `BuildStateCacheConstants` — `"4.0"`, `CurrentCompilerSemanticsVersion` **unchanged**, `CurrentOptionsSerializerVersion` unchanged (`BuildStateCache.cs:121-123`) | `Calor.Compiler.Tests/Incremental/` | with E5 | bump the semantics stamp → fails, and G-CODEGEN is contradicted |
| P24 | `ProjectIndexFormatBumped` — `"4.0"` (`ProjectIndex.cs:145`) when the effects facet lands | same | with E5 | add the facet without the bump → gate 3's index bytes move silently |
| P25 | `EffectSummaryIsIndexIndependent` — a **fresh-clone `calor build`** with no `obj/calor` present produces a complete summary; plus a structural pin that no `Effects/` or `Incremental/` file references `ProjectIndex` | `Calor.Compiler.Tests/Incremental/` | **before E5** | derive the summary from the index → the fresh-clone build fails |
| P26 | `NoNameKeyedEffectStoreRemains` — grep pin, `EffectSummaryBuilder.cs:68,:75` keys gone | same | with E5 | re-introduce one name key |
| P27 | `SpikeVerdictMatchesRecomputation` — exact-equality against `spike-verdict.json`, the two-ledger pattern | `Calor.Compiler.Tests/Effects/SpikeVerdictTests.cs` | **spike PR** | edit a verdict field |

### 13.3 Gate rows

| Gate | Instrument | Freeze | Discriminating pin |
|---|---|---|---|
| **1** laundering, closed classes | P15's **six** `_IsError`/`_Compiles` pairs (five if the ramp fires) — the denominator is exactly those pairs | **design-doc merge** | delete E3's rule for one class |
| **2** higher-order expressiveness | `HigherOrderDemandLedgerTests.cs` re-executed at the release commit. It asserts **exact equality** on `Calor0418`, `Calor0419FunctionTyped`, `Total`, `PerFile` and `NotReachingEffectPass` (`:192-199`), so driving 0418 to zero turns it **red** — it is *not* "extended, not rewritten" as Draft v1 said. **The ledger is regenerated in the E4 PR with the delta and its cause disclosed**, which is what the test's own failure message already instructs: *"The ledger is the frozen denominator — regenerate in this PR and name the cause (§4.4 gate 2 discriminating pin)."* | ledger registration PR (#1086) | re-introduce the 0418 rejection for one class |
| **3** surface agreement | `EditScriptIdentityTests` + **ES-08**; plus three legs Draft v1 omitted: a **CLI-process** leg and a **`Calor.Sdk`** leg over the same scripts, and a test enumerating the **four entry points' default `UnknownCallPolicy`**. The F-3 supersession that had to precede ES-08 **already merged** (`b5d61e18`, PR #1085) | each leg registered **before E2** | drop ES-08 → `RegisteredScriptIdsAreStable` fails; flip one surface's default → the equivalence test fails |
| **5** compatibility over the corpus | leg (a) what CI compiles today — `tests/TestData/Benchmarks` (226, pinned `>= 200`), `samples/` (11), every `.calr` a test project compiles; leg (b) the remainder of the 886 via a **`compile-all-committed-calr` job registered before E2**. **E1-attributable firings separated**: E1 resolves callees string-guessing missed and fires new, correct Calor0410/0419, which are fixed in-corpus and counted. **0.15-specific additions**: the Calor0410s that *disappear* from §4.1's `Subtypes` widening are listed by name; the §3.2 line-adjacency baseline (`o53/baseline.json`, 23 files, 1 green today) is re-run; §4.5's 0420/0421 de-demotion is confirmed to break no committed file | branch cut | revert one in-corpus fix → leg (a) red |
| **6** resolution floor | `MetadataBinderCorpusMeasurementTests.cs:37-118`, two-sided exact equality | v0.14.3 values | the test as it stands |
| **7** index/query effects leg | `QueryGoldenTests` + the `effects` golden authored (not recorded) per `EveryGoldenStatesWhyItExists` (`:152-172`) | E5 PR | alter one expected effects answer |

### 13.4 The Calor0425 corpus ledger — a decision, not an open question

> **Decision.** `bench/phase0-agent-native/calor0425-corpus-ledger.json` is registered **before E2
> merges**, in the shape of the two existing ledgers: per subject (MediatR, Serilog,
> FluentValidation), split **by cause** — unresolved receiver / row-less function-typed
> declaration / BCL-returned delegate — with `measuredCommit`, and re-executed by an
> exact-equality test on the `compiler` shard.

Draft v1 left this as "open question 1" with the instrument being a promise that the E2 PR would
publish a count. That is the failure mode §13.3 gate 2 exists to prevent. The number matters
because 431 of 1248 BCL call sites do not resolve (§2.2): if the 0425 count per subject is in the
hundreds, rows are ergonomically *worse* than Calor0418 for converted code and
`--permissive-effects` becomes mandatory rather than exceptional — which would make §4.5's
"strictly less powerful waiver" decision land badly. Registering the ledger makes that visible
before E2 rather than after.

---

## 14. Open questions

1. **Does §7.5's R2 have a usable spelling for a foreign interface?** MediatR's
   `IPipelineBehavior` is someone else's; a Calor implementation cannot widen it. If the only
   type-checking spelling requires editing the interface, rank-1 rows do not compose with external
   interfaces. *Evidence needed:* A2's spike output. **Most likely ramp trigger.**
2. **Is `eff` safe against a type parameter literally named `eff`?** §7.2's one-token lookahead
   should make `<eff>` still work, but that is reasoning, not execution — the branch does not
   exist yet, so it cannot be run. *Evidence needed:* P18's `TypeParamNamedEff_StillWorks` case,
   which must be written before the branch is considered done.
3. **How does §4.1's `Subtypes` widening get a doc-drift guard?** `calor self-check docs` checks
   the effect-code *registry* against `effects.md`'s table (`EffectTypes.cs:134-135`,
   `Calor1323`/`Calor1324`) but has **no check for the subtyping relation**, so a `Subtypes` entry
   can drift from its documentation silently. *Evidence needed:* a decision on whether to extend
   the drift checker with a `Calor13xx` code for the relation, taken in the E2 PR.
4. **Where should Calor0425 be reported for a row-less parameter that is never invoked?** §6.2
   puts it at the parameter span, so a converted file with FluentValidation's 236 delegate-typed
   declarations would emit 236 warnings even where none is invoked. Reporting at the *invocation*
   is quieter but loses the declare-your-intent pressure. *Evidence needed:* §13.4's ledger, split
   by "declared but never invoked" vs "invoked". This draft chooses the parameter span; the
   measurement may move it before E2.

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
| `§FLD{…} §E{…}` already parses (Draft v1 §14 Q4) | Draft v1's own reasoning from `ParseClassField:8709-8719` | **X9b**: `Calor0100: Expected TP, WHERE, EXT, IMPL, FLD, … but found Effects`. It does not parse; position 7 is new syntax |
| `§E{!e, alloc}` fails at `Expect(CloseBrace)` | consistency lens C2 | **X3b**: it reaches `Calor0403: Unknown effect code '! e'`. The lens's conclusion (reject `!`) is right; the mechanism differs |
| MediatR `RequestPreProcessorBehavior.cs` is 29 lines | Draft v1 §12.2 | **28** at the pinned SHA |

---

## 15. Review record

**Round 1 (2026-08-25) on Draft v1 (PR #1093).** Three lenses, all NEEDS-FIXES: evidence 92%,
internal-consistency 88%, test-lens 88%. Exit criterion (roadmap §4.1 term 2): evidence **and**
consistency return APPROVE on a revision, or every declined finding is recorded here with its
rationale. Below: every finding, its disposition, and where it landed. **62 applied, 4 declined.**

### Evidence lens (23 findings)

| # | Finding | Disposition |
|---|---|---|
| 1 | Calor0410 message quoted four times does not exist | **applied** — real text executed as **X12b**; §2.1, §3.6, §10; §14.1 |
| 2 | `EffectResolutionStatus` is `:596-612`, not `:596-608` | **applied** — §2 table, §14.1 |
| 3 | Calor0410 path is `:377`/`:427`/`:433`/`:441`, not `:410-443` | **applied** — §2 table, §14.1 |
| 4, 22 | "~24 `MapShortTypeNameToFullName` sites" is the *pair* count | **applied** — §2 table splits 11 + 13 |
| 5 | §6.4's site-5 sample is re-worded, not merely re-coded | **applied** — §6.4 says so explicitly |
| 6 | `BindingNode` does not exist | **applied** — `BindStatementNode` (`Ast/ControlFlowNodes.cs:161`); §3.3, §9, §14.1 |
| 7 | 9 `§I` arms + 7 `§O` arms, not 5 | **applied** — §3.3 moves the check **inside** `ParseParameter`/`ParseOutput`, so **six** insertion points cover all 16 arms |
| 8, m1 | "six positions" vs seven rows vs "position 7" | **applied** — seven throughout (§3.3) |
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

### Internal-consistency lens (3 critical, 13 major, 7 minor)

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

### Test lens (8 hard defects, 3 cross-cutting, ~25 claim-table gaps)

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
| 3.3 | No test parses `§LAM … §E` or `§DEL … §E` (0 hits in `tests/`) | **applied** — **P3** covers all seven positions, including the three that already parse |
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
| 12.3 | No presence/schema test for the spike directory | **declined, with rationale** — P27's exact-equality on `spike-verdict.json` fails if the file is missing or malformed, which subsumes a separate presence test. A second test on the same artifact would have no independent discriminating revert |
| Q-D | No parse → emit → parse round-trip per position | **applied** — **P4**, seven cases |
| G-3 | CLI leg, SDK leg, default-`UnknownCallPolicy` equivalence, F-3 supersession | **applied** — §13.3 gate 3; the supersession **already merged** as `b5d61e18` (PR #1085) |
| G-5 | No gate-5 row at all | **applied** — §13.3 gate 5, with legs (a)/(b), the E1-attributable separation, and three 0.15-specific additions |

### Round 2

Pending. Bar: APPROVE from the evidence and consistency lenses.

| Round | Date | Lens | Verdict |
|---|---|---|---|
| 1 | 2026-08-25 | evidence / consistency / test | NEEDS-FIXES (92% / 88% / 88%) — all dispositioned above |
| 2 | — | evidence | pending |
| 2 | — | internal consistency | pending |
| 2 | — | test | pending |
