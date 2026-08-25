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

---

## 3. Decision 1 — Row syntax

> **Decision.** An effect row is written with the **existing `§E{…}` tag, placed immediately
> after the type it annotates**. No new token, no new punctuation, no new AST node class, no
> new `IAstVisitor` method. `§E` on a function declaration **is** the row of that function's
> own type. An omitted row means *pure* on a declaration, *inferred* on a lambda literal, and
> *Unknown* on a function-typed parameter, binding, or return.

### 3.1 Why not `Int !{db:w, throw}`

The direction doc sketches `Int !{db:w, throw}` (`calor-direction.md:23`) and calls it a
placeholder. It is rejected for three reasons, all mechanical:

1. **Types are strings in the parser.** `ReadInlineTypeToken` (`Parser.cs:13637`) accumulates a
   `StringBuilder` and hands back a type *name*; a row spelled inside the type would have to be
   parsed out of that string again downstream, in `ParameterNode.TypeName`,
   `OutputNode.TypeName`, `VariableSymbol.TypeName`, and every consumer of them. That is the
   `_variableTypeMap` mistake E1 just finished deleting.
2. **Braces collide with attributes.** `!{…}` inside a `§B{…}` / `§I{…}` group would be read by
   `ParsePositionalAttributes` (`Parser.cs:5610-5633`), which splits on `:` — so `!{db:w}`
   arrives as two positional slots and CLAUDE.md's "never re-split `attrs["_pos0"]` on `:`"
   rule is violated by construction. `§E` already owns the one place that legitimately
   re-joins colon-split codes (`AttributeHelper.cs:337-339`); putting rows anywhere else
   duplicates that machinery.
3. **`§E` already parses in three of the six positions.** `§F`/`§MT` (`Parser.cs:1377-1380`),
   `§LAM` (`:11392-11397`), `§DEL` (`:11574-11577`). Reusing the tag makes three of six sites
   a no-op at the lexer and parser, and makes the other three one `if (Check(TokenKind.Effects))`
   each.

The cost of the decision is that a function type written in a signature is *two tokens*, not
one — `Func<i32,i32> §E{cw}` rather than `Func<i32,i32>!{cw}`. That is accepted. Calor's whole
surface is tag-prefixed; a tag is the idiomatic spelling.

### 3.2 The six positions

All grammar below is stated against the existing production it extends.

| # | Position | Spelling | Parser change |
|---|---|---|---|
| 1 | Function / method declaration | `§F{id:name:vis} (…) -> T` then `§E{…}` on its own line | **none** — `Parser.cs:1377-1380` |
| 2 | Lambda literal | `§LAM{id:x:i32} §E{…}` then body | **none** — `Parser.cs:11392-11397` (today parsed and discarded; §5 makes it load-bearing) |
| 3 | Delegate declaration | `§DEL{id:Name}` … `§E{…}` | **none** — `Parser.cs:11574-11577` |
| 4 | Parameter, tag form | `§I{Func<i32,i32>:f} §E{…}` | one `if (Check(TokenKind.Effects))` after `ParseParameter()` returns, at each `§I` dispatch arm (`Parser.cs:1369-1372` and its four siblings) |
| 5 | Parameter, inline form | `(Func<i32,i32>:f §E{…}, i32:v) -> i32` | one `if (Check(TokenKind.Effects))` in `TryParseInlineSignature`, after the modifier slot (`Parser.cs:13567`) and before the `= default` check (`:13576`) |
| 6 | Return / binding | `§O{Func<i32>} §E{…}` · `-> Func<i32> §E{…}` · `§B{f:Func<i32,i32>} §E{…} <init>` | one check after `ParseOutput()` (`Parser.cs:1373-1376`), one after the arrow's `ReadInlineTypeToken` (`:13620`), one after the binding's attribute group |

**Row storage.** `EffectsNode? Row` becomes a field on `ParameterNode`, `OutputNode`, and
`BindingNode` — three existing classes. `LambdaExpressionNode.Effects`
(`Ast/LambdaNodes.cs:41`) and `DelegateDefinitionNode`'s effects field already exist. **Zero
new AST node types, therefore zero of the 184 `IAstVisitor` methods and zero of the five
implementers change** (§9).

**Why position 6 is unambiguous.** `TokenKind.Effects` is not in `ExpressionParsers`
(`Parser.cs:15-65`, 47 entries, no `Effects` key), so `IsExpressionStart()` (`:2466-2469`) is
false for it. A `§B{f:Func<i32,i32>} §E{cw} <init>` therefore cannot have its row swallowed by
the initializer's `ParseExpression()`: the row is consumed first, deterministically, and the
initializer parse starts at the token after `}`. **This property is load-bearing and gets a
pin** (§13): adding `TokenKind.Effects` to `ExpressionParsers` must break a test.

### 3.3 Composition with declaration-level `§E{…}`

**`§E` on a function declaration is the row of that function's own type. Yes.** One tag, one
meaning: *the effects this callable may perform.* Consequences, stated so they are checkable:

- The declared row of `§F{f001:Log:pub} (str:m) -> void` with `§E{cw}` is `{cw}`, and the
  `FunctionBoundType` the binder gives that symbol carries `Row = {cw}` (§8).
- Passing `Log` as a method-group argument no longer needs the special case at
  `EffectEnforcementPass.cs:1272-1278`; the argument's *type* carries the row and §6's
  argument rule checks it. That special case's behaviour is preserved (it charges the callee's
  declared effects, which is what "the row fits or does not fit" reduces to for a monomorphic
  destination), so no pin changes — but the mechanism moves from an ad-hoc name lookup to the
  type system, which is the whole point of TIER2D.
- The body-vs-declaration check is **unchanged and still Calor0410** (`:410-443`). Rows do not
  replace it. Calor0424/0425 are about *two rows meeting at a binding site*; Calor0410 is
  about a body exceeding its own declaration. Keeping them distinct keeps existing Calor0410
  corpus behaviour byte-stable, which gate 5 requires.

### 3.4 What an omitted row means, per site

This is the one genuinely asymmetric rule in the design, so it is stated as a table with its
reason.

| Site | Omitted row means | Why |
|---|---|---|
| Function / method / delegate **declaration** | **Pure** (`EffectSet.Empty`) | Unchanged from today (`EffectEnforcementPass.cs:473-478`). 496 of the 886 committed `.calr` files contain a `§E{`; the other 390 rely on this default. Changing it breaks the corpus and gate 5. |
| **Lambda literal** | **Inferred from the body** | The body is present and already walked (`InferFromLambda`, `:2942-2954`). Inference is sound and needs no annotation. Defaulting to pure would be *unsound* (today's `InferFromLambda` charges the body precisely because it must); defaulting to Unknown would be gratuitously lossy. |
| Function-typed **parameter, binding, or return** | **Unknown** | There is no body to infer from and no declaration to trust. Defaulting to pure would silently re-open the laundering hole PR #968 closed. This is a *new* syntactic position, so nothing in the corpus regresses: the 886-file corpus contains 4 occurrences of `Func<`/`Action<` in `.calr` in total. |

The asymmetry is defensible precisely because the three rows are three different epistemic
situations: *a promise the author made*, *a fact the compiler can compute*, and *a fact nobody
supplied*. Collapsing them to one default would have to pick pure (unsound) or Unknown
(breaks 390 files).

### 3.5 Six worked examples with their exact diagnostics

All examples are future syntax and are deliberately in `text` fences, which
`calor self-check docs` never scans (`docs/cli/self-check.md:80-86`).

**E-1 — a function-typed parameter with a row, invoked. Today: Calor0418. After: compiles.**

```text
§M{m001:Ex1}
  §F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32
    §E{cw}
    §R §C{transform} §A value §/C
```

*Today* (`82338e37`): `Calor0418` Error at the `§C{transform}` span —
`InferFromBareNameTarget` (`EffectEnforcementPass.cs:1452-1472`) sees `transform` resolve to a
parameter of declared type `Func<i32,i32>`, which `IsFunctionTypeName` (`:1939-1955`) accepts,
and reports *"Invocation of function-typed value 'transform' (type 'Func<i32,i32>') is an error
under effect enforcement…"*. Pinned by `StrictnessBatchTests.cs:29`.

*After:* no diagnostic. The invocation charges the parameter's row `{cw}` to `Apply`, whose
declared row is `{cw}`, so the Calor0410 check at `:410-443` passes.

**E-2 — the row does not fit the enclosing declaration. New charging path, unchanged code.**

```text
§M{m001:Ex2}
  §F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32
    §E{}
    §R §C{transform} §A value §/C
```

*After:* `Calor0410 ForbiddenEffect` Error at the `§E{}` span —
`"Function 'Apply' performs effect(s) [cw] not declared in its effect set"`. This is today's
Calor0410 path (`:410-443`) reached through a new charging rule, **not** a new code. Deliberate:
"the body does more than the declaration allows" already has a code and a corpus of
expectations.

**E-3 — an argument whose row is wider than the parameter's. New: Calor0424.**

```text
§M{m001:Ex3}
  §F{f001:Apply:pub} (Func<i32,i32>:transform §E{}, i32:value) -> i32
    §E{}
    §R §C{transform} §A value §/C

  §F{f002:Shout:pub} (i32:x) -> i32
    §E{cw}
    §P x
    §R x

  §F{f003:Main:pub} () -> i32
    §E{cw}
    §R §C{Apply} §A Shout §A INT:1 §/C
```

*After:* `Calor0424 EffectRowMismatch` Error at the `Shout` argument span:

```
Argument 'Shout' has effect row [cw], which does not fit parameter 'transform'
of 'Apply' (declared row: [pure]). Extra effect(s): cw. Widen 'transform''s row
to §E{cw}, or pass a function whose row fits. An effect row that does not fit is
never waived by --permissive-effects.
```

*Today:* no diagnostic at the argument site at all. `InferFromCallArguments`
(`:1272-1278`) charges `Shout`'s declared `{cw}` to `Main`, which declares `{cw}`, so the
program compiles and the laundering is invisible — `Apply` is documented pure and calls an
impure callback. **This is the class E3 closes and gate 1 counts.**

**E-4 — an argument whose row is unknown. New: Calor0425, waivable.**

```text
§M{m001:Ex4}
  §F{f001:Run:pub} (Func<i32>:make) -> i32
    §E{}
    §R §C{make} §/C
```

*After:* `Calor0425 EffectRowUnknown` Warning (Error under `--strict-effects`) at the `make`
parameter span:

```
Parameter 'make' of 'Run' is function-typed with no effect row, so its effects
are Unknown. Add §E{…} after the type to state what callers may pass, or compile
with --permissive-effects to waive this. Invoking a value whose row is Unknown
charges Unknown to 'Run'.
```

…followed by `Calor0410` at `§E{}`, because Unknown fits no declared set — the fail-closed
behaviour of `EffectSet.cs:101`, now reached through the row. Under `--permissive-effects`,
Calor0425 is suppressed and the Unknown charge is short-circuited to `Empty` exactly as
`:1427-1430` does today.

**E-5 — a lambda literal with a declared row that its body exceeds. New: Calor0410 on `§LAM`.**

```text
§M{m001:Ex5}
  §F{f001:Main:pub} () -> void
    §E{cw}
    §B{f:Func<i32,i32>} §E{} §LAM{lam1:x:i32}
      §P x
      §R x
    §/LAM{lam1}
```

*After:* `Calor0410 ForbiddenEffect` Error at the `§LAM`'s `§E{}` span —
`"Lambda 'lam1' performs effect(s) [cw] not declared in its effect set"`. *Today:* silence —
`lambda.Effects` is parsed (`Parser.cs:11392-11397`), stored
(`Ast/LambdaNodes.cs:41`, `Binding/BoundNodes.cs:2128-2129`) and never read (`InferFromLambda`,
`:2942-2954`). §5 is the decision that ends that.

**E-6 — an override broadening its base's row. Unchanged: Calor0420.**

```text
§M{m001:Ex6}
  §CL{c001:Base:pub}
    §MT{mt001:Render:pub:virt} () -> void
      §E{}
  §CL{c002:Derived:Base:pub}
    §MT{mt002:Render:pub:over} () -> void
      §E{cw}
      §P "laundered"
```

*After:* `Calor0420 OverrideEffectVariance` Error, **same code, same message shape as today**
(`EffectEnforcementPass.cs:537-545`), now computed by the shared row-fit relation instead of a
bespoke `IsSubsetOf` call. Pinned unchanged at `StrictnessBatchTests.cs:132`. §6 is the
decision to keep the code.

### 3.6 Lexing and parsing, against CLAUDE.md's rules

- *"`ParseAttributes()` already splits on `:` … never re-split `attrs["_pos0"]` on `:`"* — no
  row is ever inside a brace group except `§E{…}`'s own, where `InterpretEffectsAttributes`
  (`AttributeHelper.cs:329` onward) already owns the colon rejoin via `EffectCodes.ColonPrefixes`
  (`EffectTypes.cs:142-148`). No new re-splitting is introduced anywhere.
- *"`IsExpressionStart()` must include all new expression token kinds"* — no new expression
  token kind is introduced. `TokenKind.Effects` must stay *out* of `ExpressionParsers`
  (§3.2); that is the inverse of the usual failure and gets its own pin.
- *"Closing tags with IDs need `ParseAttributes()` after `Advance()`"* — no new closing tag.
  The only closers Calor authors write are `§/C` and `§/LAM`, both unchanged.
- *"`ParseValue()` handles `*` for pointer types and `[,]` for multi-dimensional array types"*
  — untouched; rows never reach `ParseValue`.
- **Lexer:** no new token kind, no change to `IsKeyword`'s range in `Parsing/Token.cs`, no new
  keyword-dictionary entry in `Parsing/Lexer.cs`. §7's effect variables reuse
  `TokenKind.Exclamation` (`Lexer.cs:725-734`), which the lexer already produces.

---

## 4. Decision 2 — The row lattice

> **Decision.** A row is one of `Concrete(S)`, `Assumed(S, reasons)`, or `Unknown`, where `S`
> is a set over `EffectCodes.Registry` closed under `EffectSubtyping`. Inference uses a join
> `⊔` with `Unknown` as ⊤. Checking uses a **separate three-valued relation** `fits`, whose
> third value is what distinguishes Calor0425 from Calor0424. `EffectSet.IsSubsetOf` is *not*
> reused as `fits`.

### 4.1 The carrier

`S` ranges over the 31-entry `EffectCodes.Registry` (`EffectTypes.cs:65-109`), 26 documented
plus 5 legacy aliases. The order on individual effects is `EffectSubtyping.Encompasses`
(`EffectSubtyping.cs:52-66`) — today a four-entry table (`:14-43`) relating each `*_readwrite`
to its `*_read` and `*_write`.

**Sub-decision: the family/narrow gap is closed, and it is closed in E2's PR, not later.**
§2.3 established that `db` does not encompass `db:r` today, because `Subtypes` has an entry for
`database_readwrite` but none for `database`. Under rows this becomes visible at every binding
site rather than only at a declaration, so the gap stops being academic. E2 adds entries to the
same table for the three bare family codes that have narrow siblings — `database`, `network`,
`environment` — so that e.g. `("io","database")` encompasses `database_read`, `database_write`
and `database_readwrite`. `filesystem` has no bare code (§2.3), so `fs:rw` remains the
filesystem top and needs nothing. `process` (`proc`) and `http` have no narrow siblings and are
untouched.

This is a **widening** of what compiles, never a narrowing: a program that satisfied
`§E{db:r} ⊆ §E{db}` before did so only by declaring `db:r` explicitly, and still does. Gate 5's
"E1-attributable changes separated" discipline extends to it: any Calor0410 that *disappears*
because of this table change is listed by name in the E2 PR body.

The roadmap's shorthand `fs:w ⊂ fs` remains wrong even after this change, because there is no
`fs` code. §14 records the correction.

### 4.2 The three row forms

```
Row ::= Concrete(S)              -- S a closed set of registry effects; Concrete(∅) is "pure"
      | Assumed(S, R)            -- S as above; R a non-empty list of assumption reasons
      | Unknown
```

`Assumed` is promoted from today's side table (`_assumedEffects`,
`EffectEnforcementPass.cs:34`) to a first-class row form. That promotion is the entire point:
an assumption attached to a *function id* cannot survive being passed as an argument, and an
assumption attached to a *row* can. `EffectResolutionStatus`
(`EffectResolver.cs:596-608`) gains no member — it describes manifest lookup, not rows; the
`Assumed` row is produced by the pass from an `Unknown`-or-interop resolution plus a reason,
exactly as `AddAssumption` does today.

`Unknown` replaces the `(EffectKind.Unknown, "*")` sentinel (`EffectSet.cs:20`, `:200-203`) *at
the row level*. `EffectSet.Unknown` itself stays, because `EffectSet` remains the type the
Calor0410 body-vs-declaration check uses, and changing it would move that check's behaviour.

### 4.3 The join `⊔` — used by inference

```
Concrete(A)      ⊔ Concrete(B)      = Concrete(A ∪ B)
Concrete(A)      ⊔ Assumed(B, R)    = Assumed(A ∪ B, R)
Assumed(A, R₁)   ⊔ Assumed(B, R₂)   = Assumed(A ∪ B, R₁ ++ R₂)
X                ⊔ Unknown          = Unknown          (absorbing, either side)
```

Set union is `EffectSet.Union` (`EffectSet.cs:83-91`), which is already absorbing on Unknown
(`:86`). Reason lists concatenate and dedupe, preserving the existing "first three shown, then
`; and N more`" presentation (`:453-455`). `⊔` is associative, commutative, idempotent, with
identity `Concrete(∅)` and top `Unknown` — a genuine join-semilattice.

### 4.4 The relation `fits` — used by checking

`fits(src, dst)` is **three-valued**: `Fits | DoesNotFit | CannotTell`.

```
fits(Concrete(A),    Concrete(B))  = Fits          if A ⊆_enc B
                                   = DoesNotFit    otherwise
fits(Assumed(A, R),  Concrete(B))  = Fits          if A ⊆_enc B   [carries R → Calor0425]
                                   = DoesNotFit    otherwise      [Calor0424 wins over 0425]
fits(Unknown,        Concrete(B))  = CannotTell                   [Calor0425]
fits(X,              Unknown)      = CannotTell                   [Calor0425 at the dst site]
fits(Unknown,        Unknown)      = CannotTell
```

`⊆_enc` is subset-modulo-`Encompasses`, i.e. exactly `EffectSet.IsSubsetOf`'s loop body
(`EffectSet.cs:103-118`) **without** its two Unknown special cases at `:100-101`. Restating the
three properties the design needs, each as a sentence:

- **A concrete row never fits into a narrower one.** `Concrete({cw})` into `Concrete(∅)` is
  `DoesNotFit`, because `⊆_enc` fails. That is E-3.
- **Unknown fits nothing and is fitted by nothing except Unknown, and even then only as
  `CannotTell`.** There is no rule producing `Fits` with `Unknown` on either side. In
  particular the `if (other.IsUnknown) return true` rule of `EffectSet.cs:100` — "everything is
  a subset of unknown" — is **not** carried into `fits`. That rule is sound for its caller
  (a *computed* set checked against a *declared* set that can never be Unknown, because
  `§E{unknown}` is not writable: `unknown` is not in `EffectCodes.Registry` and
  `ParseEffects` reports Calor0403 for it, `Parser.cs:1778-1781`). It is not sound for rows,
  where a *destination* row can be Unknown by omission (§3.4).
- **Assumed fits like its underlying row and propagates Calor0425.** The assumption travels
  with the value; a `Fits` verdict on an `Assumed` row is a conditional acceptance, and
  Calor0425 is what makes the condition visible. A `DoesNotFit` verdict on an `Assumed` row is
  a hard Calor0424: if the assumed set already exceeds the destination, no further assumption
  could rescue it.

Two relations, not one, is itself a decision. `⊑` is not defined as a single order because
`Unknown` has to be ⊤ for inference (an unresolved callee poisons the join) and ⊥-incomparable
for checking (an unresolved callee proves nothing). Forcing both into one order is where a
single-relation design would silently re-admit laundering.

### 4.5 `--permissive-effects`

> **Decision.** `--permissive-effects` waives **Calor0425 only**. **Calor0424 is never
> waived — by any flag.**

This executes roadmap §4.5's row for the flag verbatim. Mechanically: the `CannotTell` verdict
is suppressed and the row is treated as `Concrete(∅)` for charging (mirroring
`EffectEnforcementPass.cs:1427-1430`, which already returns `EffectSet.Empty` under
`UnknownCallPolicy.Permissive`); the `DoesNotFit` verdict reports at Error severity regardless
of policy. The flag remains `--permissive-effects` (`src/Calor.Compiler/Program.cs:81`) and
keeps its existing effect on Calor0410 and Calor0411.

One consequence is worth naming: **`--permissive-effects` becomes strictly less powerful in
0.15 than in 0.14.** Today it demotes Calor0418 to a warning
(`EffectEnforcementPass.cs:1457-1459`, pinned at `StrictnessBatchTests.cs:64`) and thereby lets
any higher-order code through. After E4 there is no Calor0418, and the rows that used to hide
behind the waiver split: the ones that are Unknown stay waivable, the ones that genuinely
mismatch stop being. That is intentional — a waiver for "we don't know" is honest, a waiver for
"we know it's wrong" is not — and it is a behaviour change the release notes must carry.

### 4.6 Subtyping for function types

> **Decision.** Rows on function types are **covariant in the callee's own effects**;
> parameters' rows are **contravariant**; parameter *types* and the return *type* stay
> **invariant** in 0.15.

For `F₁ = (P₁ᵢ !ρ₁ᵢ …) -> T₁ !ρ₁` and `F₂ = (P₂ᵢ !ρ₂ᵢ …) -> T₂ !ρ₂` (writing `!ρ` for a row):

```
fits(F₁, F₂) = Fits   iff   arity(F₁) = arity(F₂)
                      and   P₁ᵢ ≡ P₂ᵢ for all i          (parameter types invariant)
                      and   T₁ ≡ T₂                       (return type invariant)
                      and   fits(ρ₁,  ρ₂)  = Fits         (own row: COvariant)
                      and   fits(ρ₂ᵢ, ρ₁ᵢ) = Fits ∀ i     (parameter rows: CONTRAvariant)
```

with `CannotTell` propagating (any `CannotTell` conjunct makes the whole verdict
`CannotTell`) and `DoesNotFit` dominating `CannotTell`.

**Why parameter types stay invariant.** `FunctionBoundType.Equals` is structural equality over
`ParameterTypes` and `ReturnType` (`Binding/BoundTypes/BoundType.cs:228-231`). Making parameter
types contravariant is a *generics* change — variance, constraints, higher-rank — which
`calor-direction.md:57-60` explicitly defers ("Generics with constraints, variance,
higher-rank … Deferred because the type-system work for effect rows is more foundational").
0.15 changes what rows do, not what types do.

**Contravariant parameter rows, worked.** Destination:

```text
§F{f001:RunTwice:pub} (Func<i32,i32>:g §E{cw}) -> void
```

`RunTwice` promises its caller: *I will hand you a `g` that is allowed to print.* A source
value whose own parameter row is `§E{}` — a `RunTwice` variant that only accepts pure `g` —
**does not fit**, because it accepts strictly fewer functions than the destination promises to
supply. A source whose parameter row is `§E{cw,fs:w}` **does** fit: it accepts everything the
destination will hand it, and more. Formally `fits(ρ₂ᵢ, ρ₁ᵢ)`: the *destination's* parameter
row must fit into the *source's*. That is the flip, and it is the one place where reading the
rule aloud is worth the ink.
