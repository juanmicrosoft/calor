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
