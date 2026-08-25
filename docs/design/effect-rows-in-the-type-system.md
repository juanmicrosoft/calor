# Effect Rows in the Type System (TIER2D)

**Status:** Draft v1
**Date:** 2026-08-25
**Measured against:** `main` @ `82338e37` (v0.14.3 + PR #1089 E1 slice 1 + PR #1090)
**Governing inputs:**

- `docs/design/calor-direction.md` — the TIER2D commitment (`:23`, `:57`) and the
  2026-04-22 postscript that tightened the terms after TIER1A failed (`:90-120`).
- `docs/plans/roadmap-v0.13-v0.15.md` §4.0–§4.5 (Draft v4) — the measured inventory,
  the entry gate, the ship tiers, the release gates, the carried debt.
- `docs/plans/v0.14-metadata-binding-scoping.md` §2 D2 (`:90-101`, six `BoundType`
  kinds), D5 (`:166-171`), D6 (`:173-181`), §3 S6 (`:301-317`).
- `bench/phase0-agent-native/higher-order-demand-ledger.json` (the demand denominator).
- `bench/phase0-agent-native/metadata-binding-corpus-ledger.json` (the resolution floor).

Every factual claim about the source in this document carries a `file:line` citation
taken at the commit above. Where a claim in a governing input disagrees with the source,
the source wins and the disagreement is recorded (§14).

---

## 0. Entry-gate checklist (roadmap §4.1)

The direction doc's postscript sets three conditions. Their status **at the moment this
draft opens**:

| # | Term (roadmap §4.1) | Status |
|---|---|---|
| 1 | **Emitter spike producing actual compiler output** on two named, frozen modules | **NOT YET RUN — this document precedes it.** §12 names the two modules, freezes them, states what is committed and the binary pass/fail criterion. The spike runs on a throwaway rows branch and does not gate E1. |
| 2 | **External critique cycle with a pass bar** — at minimum evidence, internal-consistency and test lenses; exit when evidence *and* consistency return APPROVE on a revision, or every declined finding is recorded with rationale | **Pending.** No review record exists yet; §15 is the empty record this draft opens with. |
| 3 | **Priced blast radius in the doc itself** | **§9**, with per-bucket file counts measured at `82338e37`. |
| — | **Demand denominator registered before the doc opens** (its own tautology guard) | **DONE.** `bench/phase0-agent-native/higher-order-demand-ledger.json` merged in PR #1086, `measuredCommit` `4e636d51533fde27e59fae32be149b313b3afdfb`. Numbers quoted in §1. |
| — | **E1 permitted to start before this doc merges** | **Slice 1 executed** (PR #1089). Slice 2 pending; §8 records both as E1 decisions, not design-doc decisions. |
| — | **Diagnostic allocation frozen at design-doc merge** | **Calor0424 `EffectRowMismatch`, Calor0425 `EffectRowUnknown`** — verified free: `grep -rn "Calor042[4-9]" src/ tests/` returns nothing, and the effect band ends at `Calor0423 AccessorEffectContractUnavailable` (`src/Calor.Compiler/Diagnostics/Diagnostic.cs:437`), with `Calor0500 NonExhaustiveMatch` next (`:440`). §6 allocates them. |

The six decisions this document must settle (roadmap §4.1, "Decisions the design doc must
settle, each named so its absence is visible") are §3–§8, one section each. Each opens with
a **Decision** line. None is a menu.

---

## 1. The demand denominator

TIER2D's postscript reframed effect rows as "architectural elegance … not a new user-facing
capability" (`calor-direction.md:114`). That reframing is testable, but only against a
denominator that a Calor-shaped corpus cannot supply — because **Calor0418 rejects the idiom
outright**, so the corpus is written around it. That circularity is §2(b) of the postscript,
the reading that killed TIER1A. The roadmap's answer is two denominators, frozen before this
doc opened.

### D-A — Calor-native demand: **3**

From `higher-order-demand-ledger.json`, `dA`:

```
"calor0418": 1,
"calor0419FunctionTyped": 2,
"total": 3,
"fileCount": 886,
"filesNotReachingEffectPass": 45
```

Three sites across the whole committed `.calr` corpus (886 files, 45 of which die before the
effect pass and are listed by name in the ledger rather than dropped):

| File | 0418 | 0419 (function-typed) |
|---|---|---|
| `bench/phase0-agent-native/fixtures/d-s1.5/conditional-declaration/expected.calr` | 1 | 0 |
| `tests/Calor.Conversion.Tests/Snapshots/05-02.approved.calr` | 0 | 1 |
| `tests/Calor.Conversion.Tests/Snapshots/05-03.approved.calr` | 0 | 1 |

**D-A is near zero because the language rejects the idiom, not because the idiom is
unwanted.** The ledger's own scope note records the direction of the residual error: Calor0419
is counted per diagnostic with at most three reasons shown, so a function with more than three
assumptions whose function-typed reason is truncated out of the message is *under*-counted —
"a known floor, not a ceiling". Corroborating counts at `82338e37`: 9 `§LAM` occurrences and 3
`§DEL` occurrences in the entire 886-file corpus. Calor code does not contain higher-order code
because Calor code cannot contain higher-order code.

### D-B — C#-shaped backstop: **3121**

From `higher-order-demand-ledger.json`, `dB.aggregate` — a Roslyn syntax count over the three
pinned conversion subjects at their pinned SHAs, independent of Calor's rejection:

```
"filesScanned": 364,
"lambdas": 2676,
"anonymousMethods": 0,
"delegateDeclarations": 2,
"delegateTypedDeclarations": 311,
"delegateInvocations": 132,
"total": 3121
```

Per subject: MediatR 92 (36 files), Serilog 171 (112 files), FluentValidation 2858 (216
files). FluentValidation dominates on `lambdas` (2524) because its entire public API is a
lambda-taking builder; that is the shape Calor must express, not an artifact.

### The floor, and what gate 2 re-counts

`demandTotal` is **3124** (D-A 3 + D-B 3121). The pre-registered floor is **25**
(`"floor": 25`), with the rule frozen in the ledger:

> if `dA.total + dB.aggregate.total` is below 25 sites, §4.4 gate 2 adjudicates
> NOT-ADJUDICATED, never HIT. The floor is frozen with the ledger's registration PR and is
> not re-tuned after the design doc opens.

3124 ≫ 25, so gate 2 is adjudicable. **What gate 2 re-counts at the release commit** (roadmap
§4.4 gate 2): the ledger is re-executed, and the bar is that Calor0418 firings *on the
registered classes* go to zero without `--permissive-effects` and without interop wrapping,
with the residual count and its classes published. The residual is frozen at this doc's merge
as the DEFERRED list of roadmap §4.2: reflection / `DynamicInvoke` / `dynamic`-receiver
dispatch, event-handler subscription (`+=` of a function value to a .NET event), and
BCL-returned delegates. Those are named in the release notes as "not closed", never as "no
callback can".

**Honest reading of the denominator.** D-B is the number that carries the argument, and D-B is
a count of *C# syntax in C# files*, not of Calor programs anybody wrote. It establishes that
the shape is pervasive in the .NET code Calor migrates from; it does not establish that agents
writing Calor would reach for it. The only instrument that could establish the latter is
D-A after the ceiling is removed — which is why gate 2 re-executes the ledger rather than
retiring it.

---

## 2. Today's effect system, precisely

This section is the evidence baseline. Every claim is cited; §3–§8 build on it.

### 2.1 Pass architecture — an AST walk with a binder side-channel

`EffectEnforcementPass` (`src/Calor.Compiler/Effects/EffectEnforcementPass.cs`, 2993 lines) is
an **SCC-based interprocedural pass over the AST**, not over the bound tree
(`:10-15`). `Enforce(ModuleNode)` builds a call graph (`:88`), indexes module shape for
delegate detection and variance checks (`:93-120`), then processes SCCs in reverse topological
order.

The call graph is where the binder enters, and it enters as a **side channel**:
`CallGraphAnalysis.Build` calls `ResolveBoundCallSites(ast, functions)`
(`src/Calor.Compiler/Analysis/CallGraphAnalysis.cs:424-425`), which constructs a *second*
`Binder` over the same AST inside a `try` (`:492-495`), scrapes `ResolvedSymbols` /
`ReceiverSymbol` off `BoundCallStatement` / `BoundCallExpression` / `BoundNewExpression`
(`:521-549`), maps bound symbols back to legacy AST ids by matching `DefinitionSpan`
(`:588-600`), and **discards the binder's diagnostics** (`:494`, `:496-501` keeps only the
overload-failure spans). If binding throws, the whole thing returns
`(resolved, boundCallSites, false)` and the pass falls back to name resolution (`:578-583`).

Consequence for rows: **the effect pass does not have a bound tree; it has a lookup table
keyed by `(callerId, targetString, spanStart, spanEnd)`** (`AstCallKey`, `:436`, `:554`).
Effect rows live on types, and types live on the bound tree. This is the single largest
structural fact in the design, and it is what E1 exists to change.

### 2.2 The string-keyed resolver

`EffectResolver.Resolve(string fullyQualifiedType, string methodName, params string[]
parameterTypes)` (`src/Calor.Compiler/Effects/EffectResolver.cs:48`) is string-keyed end to
end. Its cache key is a built signature string (`:59`), its layered resolution order is
documented at `:5-14` (specific signature → method on declaring type → wildcard → type default
→ namespace default → Unknown), and `ResolveExtension` returns
`new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown")` on miss
(`:100`).

`EffectResolutionStatus` is three-valued: `Resolved | PureExplicit | Unknown`
(`EffectResolver.cs:596-608`). There is no `Assumed` status.

Inside the enforcement pass, receiver types are recovered by a **lexical AST search**:
`ResolveLocalValueType(name)` (`:1719-1744`) walks the current function's parameters, then
local `§B` declarations, then `§FOREACH` variables, then the owner class's fields, returning
the declared *type name string*. `MapShortTypeNameToFullName` (`:830`) then expands it. There
are 20+ call sites of that pair (`:1269, :1342, :1454, :1532, :1560, :2022, :2092, :2157,
:2225, :2339, :2384`, plus the `MapShortTypeNameToFullName` sites at `:820, :1996, :2025,
:2065, :2069, :2118, :2189, :2291, :2325, :2372, :2406, :2829, :2899`).

Function-typedness is likewise a **string test**: `IsFunctionTypeName(string typeName)`
(`:1939-1955`) trims `?`, strips generic arguments, checks the module's declared `§DEL` names,
then pattern-matches `Action`, `Action<`, `Func<`, `Predicate<`, `Comparison<`, `Converter<`,
`Delegate`, `MulticastDelegate`, `EventHandler`, `EventHandler<`. A `Func` reached through a
type alias, a generic type parameter, or a metadata return type is invisible to it.

### 2.3 The `EffectSet` lattice and the `Unknown` sentinel

`EffectSet` (`src/Calor.Compiler/Effects/EffectSet.cs`) is a `HashSet<(EffectKind, string)>`
(`:9`) with:

- `Empty` — the pure set (`:14`).
- `Unknown` — a **sentinel member**, `(EffectKind.Unknown, "*")` (`:20`, `:200-203`), detected
  by `IsUnknown` (`:73`). It is a distinguished element of the same set type, not a separate
  lattice node.
- `Union` — join; absorbing on Unknown (`:83-91`, `:86`).
- `IsSubsetOf` — the fit test (`:97-119`), with two special rules:
  - `if (other.IsUnknown) return true;` — "Everything is subset of unknown" (`:100`).
  - `if (IsUnknown) return false;` — "Unknown is not subset of anything else" (`:101`).

The second rule is the fail-closed guarantee PR #968 shipped: an unresolved callee yields
`EffectSet.Unknown` (`EffectEnforcementPass.cs:1434`, `:2928`, `:2939`), which fits no declared
set and therefore forces Calor0410. `--permissive-effects` is the explicit waiver, and it works
by short-circuiting earlier: `if (_context.Policy == UnknownCallPolicy.Permissive) return
EffectSet.Empty;` (`:1427-1430`, `:2935-2936`).

**The subtyping hierarchy is not colon-derived.** `EffectSubtyping.Subtypes`
(`src/Calor.Compiler/Effects/EffectSubtyping.cs:14-43`) is a hand-written table with exactly
four entries, all of the same shape: `*_readwrite` encompasses `*_read` and `*_write`, for
`filesystem`, `network`, `database`, `environment`. `Encompasses` (`:52-66`) checks exact match
then that one table.

Two consequences that governing inputs get wrong and this document must not repeat:

1. **There is no `fs` code.** `EffectCodes.Registry`
   (`src/Calor.Compiler/Effects/EffectTypes.cs:65-109`) has `fs:w`, `fs:r`, `fs:rw` and no bare
   `fs`. The direction doc's sketch and the roadmap's shorthand `fs:w ⊂ fs` describes a
   relation that does not exist.
2. **A bare family code does not encompass its narrow codes.** `db` is
   `("io","database")`; `db:r` is `("io","database_read")`. `Subtypes` has no entry for
   `database`, only for `database_readwrite`. So `§E{db}` does **not** admit a `db:r` effect
   today. Same for `net` vs `net:r`/`net:w`, and `env` vs `env:r`/`env:w`.

§4 decides what to do about that.

The registry has 31 entries, of which 5 are `Legacy: true` (`fw`, `fr`, `fd`, `dbr`, `dbw` —
`:75-77`, `:90-91`), excluded from the documented surface (`:134-135`).

### 2.4 `Assumed` is a side table, not a state

Assumption provenance lives in `Dictionary<string, List<string>> _assumedEffects`
(`EffectEnforcementPass.cs:34`), keyed by function id, populated by `AddAssumption` /
`RecordAssumption`, and surfaced once per function as Calor0419 after the SCC pass:

```
EffectEnforcementPass.cs:448-463
  if (_assumedEffects.TryGetValue(function.Id, out var reasons) && reasons.Count > 0)
      … $"Effects of '{function.Name}' are ASSUMED, not verified: {shown}. "
        + "The declared effect set is accepted as an assumption, not a proof; …"
```

Severity is Warning, or Error under `--strict-effects` (`:450-452`). At most three reasons are
shown, with `"; and {n} more"` for the rest (`:453-455`) — the truncation the demand ledger
calls out as an under-count. A second Calor0419 site covers interface implementations
satisfied through an external base (`:602-611`).

The set carried by an assumed function is a perfectly ordinary `EffectSet`; nothing in the
lattice records that it is assumed. **`Assumed` is metadata attached to a function id, not a
property of a value's type** — which is exactly why it cannot cross a binding site today.

### 2.5 The four enforcement diagnostics, with what each actually does

**Calor0418 `DelegateInvocation`** — two report sites, both unconditional errors under
enforcement, demoted to warnings only under `--permissive-effects`:

- `:1452-1472` (`InferFromBareNameTarget`): a bare-name call target that resolves lexically to
  a parameter, `§B` binding, or field of the enclosing class. Message text at `:1466-1469`;
  the rule's rationale is stated in the doc comment at `:1437-1450`, which names the escape
  hatch explicitly: *"There is no annotation escape hatch: effect-annotated function types are
  a Phase 3 design."* Returns `EffectSet.Empty` (`:1471`) — the invocation is rejected, so
  nothing is charged.
- `:2679-2697` (`InferFromExpressionCall`, `CallExpressionNode or ExpressionCallNode` arm):
  invoking the result of a call, `GetF()()`. Message at `:2691-2694`.

Pins: `tests/Calor.Enforcement.Tests/StrictnessBatchTests.cs:29`
(`DelegateInvocation_FunctionTypedParameter_IsError`), `:47`
(`_LambdaBoundLocal_IsError`), `:749` (`M1_ReturnedDelegateInvocation_IsError`), and the
waiver pin `:64` (`_UnderPermissiveEffects_IsWaivedToWarning`).

Not everything function-shaped reaches 0418. A bare name the pass *cannot* see routes to the
unknown-call chain instead (`:1483-1500`, Calor0411) — pinned by
`tests/Calor.Enforcement.Tests/EffectEnforcementTests.cs:354`
(`DelegateInvocation_SingleWordTarget_FailsClosed`) and `:378`
(`DelegateInvocation_InStrictMode_IsError`). Those two pins are about **Calor0411**, not 0418;
§13 keeps them that way.

**Calor0419 `AssumedEffects`** — §2.4. The function-typed flavour is raised by
`InferFromCallArguments` (`:1259-1288`): for each bare-identifier argument, if
`ResolveLocalValueType` returns a type and `IsFunctionTypeName` says it is function-typed and
the call target is dotted (external), record

```
EffectEnforcementPass.cs:1282-1284
  $"passes function-typed value '{reference.Name}' to '{callTarget}', "
  + "which may invoke it with unverifiable effects"
```

That is the string the demand ledger's `dA.calor0419FunctionTyped` class matches on. The same
method's *other* branch is the method-group rule: a bare-name argument that resolves to an
internal function has that function's declared effects charged at the passing site
(`:1272-1278`) — the conservative closure of the `ConvertAll`/`Select` laundering path.

**Calor0420 `OverrideEffectVariance`** (`:537-545`) and **Calor0421
`InterfaceEffectVariance`** (`:584-593`) — both live in `CheckEffectVariance` (`:515-616`) and
are already **declaration-local typing rules**: `overrideDeclared.IsSubsetOf(baseDeclared)`
(`:533`) and `implDeclared.IsSubsetOf(ifaceDeclared)` (`:571`). Severity is Error, demoted to
Warning under `--permissive-effects` (`:517-519`). An external base routes to the Assumed
channel instead (`:548-553`, `:596-611`).

Pins: `StrictnessBatchTests.cs:132` / `:176` for 0420 (error / compiles), `:199` / `:221` for
0421. A fifth pin, `:155` (`GenericOverrideWithAlphaEquivalentTypeParameters_IsMatchedForVariance`),
covers alpha-equivalent generic overrides.

**Missing `§E` means pure.** `GetDeclaredEffects(EffectsNode?)` (`:473-502`) returns
`EffectSet.Empty` when the node is null or empty (`:475-478`), with the doc comment stating the
rule: *"A missing declaration is the empty (pure) set — consistent with per-function
enforcement, where an undeclared function may not exhibit any effect."* This matters for §3's
omitted-row decision.

**`await` is transparent.** `AwaitExpressionNode await_ => InferFromExpression(await_.Awaited)`
(`:2538`) — awaiting contributes exactly the awaited expression's effects and nothing else.

**Lambdas are charged by body, declaration ignored.** `InferFromLambda` (`:2942-2954`) returns
`InferFromExpression(lambda.ExpressionBody)` or `InferFromStatements(lambda.StatementBody)`.
`lambda.Effects` is never read.

### 2.6 What E1 slice 1 changed (PR #1089), and what slice 2 still owes

**Changed.** `Effects/ExternalCallCollector.cs` no longer carries `_variableTypeMap`:
`grep -rn "_variableTypeMap" src/` at `82338e37` returns hits only in
`Migration/RoslynSyntaxVisitor.cs` (`:64, :7506, :7510, :7516, :7522, :12498, :12505, :12558`) —
the C#→Calor converter, a different subsystem. The grep pin exists at
`tests/Calor.Enforcement.Tests/EffectsSuggestTests.cs:148-157`.

Receivers now come from the bound tree. `IndexBoundCallReceivers`
(`ExternalCallCollector.cs:287-328`) builds
`Dictionary<(int Start, int End, string Target), BoundReceiver>` (`:103`) from
`BoundCallStatement.ReceiverSymbol` / `ReceiverTypeSymbol` / `ResolvedTypeName`
(`:296-314`), and `TryAddCall` consults it before anything else (`:254-283`). Three honesty
guards report *unresolved* rather than guessing (`:62-67`): the binder's `OBJECT` fallback on
an inferred local, a member chain through the variable (`a.b.M`), and a function-typed value.
`CollectedCall` gained `bool ReceiverResolved = true` (`:34-38`), documented at `:23-32`: when
false, `TypeName` is *the receiver exactly as written in source — never a guessed type*. If
binding throws, every dotted receiver is unresolved (`:81-82`, `_indexingFailed` at `:107`).

**Still owed by slice 2** — named in the file's own header comment (`:66-68`) and by roadmap
§4.2's E1 bullet:

| Owed item | Evidence it is not done |
|---|---|
| A receiver `BoundExpression` on the call nodes | `ExternalCallCollector.cs:66-68` — *"A receiver `BoundExpression` on the call nodes, and `UnresolvedBoundType` emitted by the binder (scoping §D6), are slice 2."* |
| Binder-emitted `UnresolvedBoundType` | Same cite. The type exists (`Binding/BoundTypes/BoundType.cs:248-250`) but the binder does not emit it on failed metadata lookup. |
| Enforcement-pass string resolvers deleted | `ResolveLocalValueType` (`EffectEnforcementPass.cs:1719-1744`) and `IsFunctionTypeName` (`:1939-1955`) are untouched; the ~24 `MapShortTypeNameToFullName` sites listed in §2.2 remain. |
| `EffectResolver` keys on symbol identity | `EffectResolver.Resolve(string, string, params string[])` (`EffectResolver.cs:48`) is intact. Roadmap §4.2's E1 exit pin (c) — "no `EffectResolver.Resolve(string, string, …)` overload remains" — is unmet. |
| Lambda `FunctionBoundType` | `BoundLambdaExpression.Type` is `new NominalBoundType($"{(isAsync ? "ASYNC_" : "")}LAMBDA({signature})->{returnTypeName}")` (`Binding/BoundNodes.cs:2178`), a string. |
| Resolution reaches only 65.46% | `metadata-binding-corpus-ledger.json`: `aggregateResolved` 817 of `aggregateCandidates` 1248; MediatR 129/226 (57.08%), Serilog 104/113 (92.04%), FluentValidation 584/909 (64.25%). A third of BCL call sites will reach the row system as Unknown. |

`FunctionBoundType` itself exists and its doc comment already reserves the slot:

```
Binding/BoundTypes/BoundType.cs:209-211
  /// <summary>Kind 5: function type (for lambdas / delegates). Effect rows
  /// attach here in 0.15 (§4.2); the kind ships in 0.14 so downstream analyses
  /// have the shape ready.</summary>
```

It carries `ParameterTypes`, `ReturnType`, and a structural `DisplayString`
`"(p1, p2) -> ret"` (`:214-226`); `Equals` is structural over parameters and return (`:228-231`).

### 2.7 Where effects are persisted

- **Incremental build cache.** `BuildFileEntry.EffectSummary`
  (`src/Calor.Compiler/Incremental/BuildStateCache.cs:52`), a plain JSON POCO
  (`Effects/EffectSummary.cs:14-16`: *"This type is a plain POCO (no domain types like
  EffectSet) for JSON compatibility"*). Cache format version is `"3.0"`
  (`BuildStateCache.cs:121`), with a separate `CurrentCompilerSemanticsVersion`
  (`:122`) and `CurrentOptionsSerializerVersion = "compile-inputs-v3"` (`:123`); a mismatch on
  either invalidates globally (`:676-678`).
- **Keys are names.** `EffectSummaryBuilder` keys callers by `function.Name` (`:68`) and by
  `$"{cls.Name}.{method.Name}"` (`:75`). `EffectFunctionSummary` carries `Name` +
  `ClassName?` (`EffectSummary.cs:59-60`), never a `SymbolId`.
- **The project index holds no effect facts.** `ProjectIndex` is format `"3.0"`
  (`src/Calor.Compiler/Indexing/ProjectIndex.cs:145`), and `calor query` exposes exactly six
  facets — `symbol | callers | callees | impact | contracts | assumptions`
  (`src/Calor.Compiler/Commands/QueryCommand.cs:26-34`). No `effects`.

### 2.8 Surface syntax as it parses today

- `§E{…}` is `ParseEffects()` (`src/Calor.Compiler/Parsing/Parser.cs:1770-1784`): expect the
  `Effects` token, `ParseAttributes()`, then `AttributeHelper.InterpretEffectsAttributes`
  (`Parsing/AttributeHelper.cs:329`), which **re-joins** codes the attribute parser split on
  `:` using `EffectCodes.ColonPrefixes` (`AttributeHelper.cs:337-339`, derived from the
  registry at `EffectTypes.cs:142-148`). Unknown codes raise Calor0403 (`Parser.cs:1778-1781`).
- `ParseAttributes` (`:5589-5608`) reads brace groups; `ParsePositionalAttributes`
  (`:5610-5633`) splits on `:` into `_pos0`, `_pos1`, … plus `_posCount`. **This is the rule
  CLAUDE.md warns never to re-split.**
- Inline signatures: `TryParseInlineSignature` (`:13518-13623`). Parameters are
  `type:name[:modifiers]` (`:13540-13567`), optional `= default` (`:13576-13581`), and an
  optional `-> type` return (`:13603-13622`). Types are read as *strings* by
  `ReadInlineTypeToken` (`:13637-...`).
- Function headers: `§F{id:name:vis}` → `ParseOptionalTypeParameterList` (`:1331`, defined
  `:7596`) → `TryParseInlineSignature` (`:1336`) → a section loop that accepts `§I`, `§O`,
  `§E`, `§Q`, `§S`, … (`:1358-1400`; `§E` at `:1377-1380`).
- `§LAM` already parses an optional `§E`: `ParseLambdaExpression` (`:11330`), header attributes
  with `maxGroups: 1` (`:11335`), then `if (Check(TokenKind.Effects)) effects = ParseEffects();`
  (`:11392-11397`). The node field is `LambdaExpressionNode.Effects`
  (`src/Calor.Compiler/Ast/LambdaNodes.cs:41`); the bound node carries it as `Effects` +
  `DeclaredEffects` (`Binding/BoundNodes.cs:2128-2129`). Nothing consumes either.
- `§DEL{id:name}` already parses an optional `§E`: `ParseDelegateDefinition` (`:11541`), `§E`
  arm at `:11574-11577`. Lexed at `Parsing/Lexer.cs:173-174`.
- `IsExpressionStart()` is `ExpressionParsers.ContainsKey(Current.Kind)` (`:2466-2469`) — a
  dictionary of expression-starting token kinds. `TokenKind.Effects` is not in it, and §3
  depends on that staying true.
- `!` lexes to `TokenKind.Exclamation` (`Lexer.cs:725-734`), currently only produced for the
  unary-not/`!=` path. §7 uses it.
