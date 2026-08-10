# #762 binder rebuild — scoping (v0.13 spine)

**Date:** 2026-08-10
**Status:** Draft v2 — adversarial review round 1 applied (verdict 65%; both CRITICALs and all
Majors/minors addressed; dispositions in §8)
**Governing inputs:** issue #762 (DoD: 100% of accepted expression kinds structurally bind),
`v0.13-freeze-registrations.md` F-1 (the 61-class tier assignment — 55 registered / 6 residual —
frozen before this work; the denominator of roadmap §2.5 gate 1) and F-2 (the measurement corpus),
`roadmap-v0.13-v0.15.md` §2.1/§2.5. Per the freeze discipline, this doc merges before the work it
scopes.

## 1. Current state (measured; corrected by review round 1)

- `Binder.BindExpression` (`Binding/Binder.cs:299`) has **18 class-matched arms** over **61**
  concrete `ExpressionNode` classes — and two of the 18 fall back *conditionally*
  (`ThisExpressionNode` in static context; `TypeOperationNode` for non-Cast/Is/As ops). Everything
  else reaches `BindFallbackExpression` (`Binder.cs:514`), a **zero-child** `BoundCallExpression`
  named `<unsupported:TypeName>` typed `"OBJECT"`, and — important — **silent**: the fallback
  reports no diagnostic. The fallback's comment records the class of harm an earlier
  `BoundIntLiteral(0)` fallback caused (div-by-zero false positives on every unhandled divisor).
- **Types are strings** (`"INT"`, `"OBJECT"`, …); `BoundNodes.cs` holds **43 bound classes**
  (40 concrete + 3 abstract; 14 are `BoundExpression` subclasses). `DecimalLiteralNode` binds as
  `BoundFloatLiteral((double)value)` — #762 item 4's defect, visible in the switch.
- **Where binding runs — the load-bearing correction from review:** `new Binder(` has exactly
  three production sites. (a) `Analysis/VerificationAnalysisPass.cs:155` — analyze-mode only
  (`EnableVerificationAnalyses`, i.e. `--analyze`/MCP), and it binds into a **throwaway
  DiagnosticBag**: binder diagnostics never reach the user there today. (b)
  **`Calor.LanguageServer/State/DocumentState.cs:105` — the LSP binds every open document with
  the LIVE diagnostics bag.** (c) Tests. Binding does **not** run in a normal compile.
  Consequences: the analysis layer AND the LSP are the blast radius; any new binder diagnostic is
  editor-visible immediately; and the incomplete-fraction instrument cannot be read from the
  analyze path until the discarded-bag defect is fixed (§5).
- Top-level function registration ignores `TryDeclare` failures (`Binder.cs:65`) while class
  members use `DeclareOverload` (`:742`) — #762 items 5–6.
- **Eight** AST classes carry no-op `default!` `Accept`s (the #874 null-injection hazard class):
  `ElseIfClauseNode`, `OutputNode`, `EffectsNode`, `KeywordArgNode`, `FieldDefinitionNode`,
  `VariantDefinitionNode`, `TypeReferenceNode`, `FieldAssignmentNode` — **only one of which
  (`KeywordArgNode`) is an `ExpressionNode`**. #762 item 8 therefore has an expression half
  (F-1's Tier B pair) and a larger non-expression half; both are scheduled (§3, B8).

## 2. Design decisions

**D1 — One authoritative dispatch table.** The `switch` becomes a
`IReadOnlyDictionary<Type, Func<Binder, ExpressionNode, BoundExpression>>` built once. A
**reflection completeness test** asserts every concrete `ExpressionNode` subclass has exactly one
entry — Tier A classes map to real binders, Tier B classes map to an *explicit*
`BindIncomplete` with a stated reason. Why a Type-keyed table rather than a new
`IAstVisitor<BoundExpression>` implementation, recorded so B1's reviewer doesn't relitigate it:
`IAstVisitor` carries ~236 methods across 4+ implementers (the roadmap's change-amplification
complaint, #791), and eight classes have broken `Accept` dispatch — visitor traversal is exactly
the mechanism that cannot be trusted here until item 8 lands. Parser expression-start
classification (#762 item 7) derives from the same source of truth in B8 — one table, two
projections.

**D2 — `BoundIncompleteExpression` replaces the fallback, in two phases.** The node carries the
node-type name, an explicit stable type, and an *analysis incomplete* diagnostic (new code in the
0200 band). **Phase semantics, stated precisely (review M2):**
- **B1 ships it zero-child** — traversal-identical to today's fallback, which is what makes B1's
  "zero checker behavior change" exit criterion true by construction rather than aspiration.
- **Children arrive with each family's real binder** (a family PR replaces the incomplete node
  with a real bound node — children come from the real arm, not from a generic extractor).
- **Classes that remain incomplete at B8** (Tier B residuals) get explicit per-class child
  extractors there, with children **marked non-evaluated** (`IsDeferredContext`) so dataflow/
  taint/uninitialized checkers do not treat lambda bodies or `?.`/`??` right-sides inside an
  unsupported wrapper as evaluated in-line — the residual false-positive route review M2 named.
  #762 item 3 ("retain children") is thus satisfied at end state, with the interim disclosed.
  There is no generic child-enumeration mechanism and none is assumed.
- Opacity-at-the-node is preserved throughout (the div-by-zero lesson): the node itself is an
  opaque non-constant value to every checker.

**D3 — Types stay strings in 0.13; they become explicit and non-null.** The single constructor
choke point is the base `BoundExpression` constructor (verified viable by review). The 0.14 typed
representation (roadmap §3.2) replaces the *representation*; widening into metadata-backed typing
here is a fenced non-goal (§4).

**D4 — SymbolIds and exact spans (#762 item 9), corrected for the review's two defects.**
- **Module ids are not unique** (nothing enforces it; nine sample files all use `§M{m001`), so a
  bare `<module-id>/<declaration-id>` collides corpus-wide. `SymbolId` therefore =
  `<project-relative-file-path>::<module-id>/<declaration-id>` for declarations. (Enforcing
  module-id uniqueness instead would break existing corpora; the path component is
  collision-free by construction and what the index needs anyway.)
- **Locals/parameters are keyed by declaration order, not per-name ordinal**:
  `…/<function-id>/local#<declaration-index>` (name carried as display metadata). A per-name
  ordinal renumbers the *surviving* symbol when a same-named shadowing local is renamed — an
  untouched symbol changing identity under exactly the rename-gate oracle (F-3 ES-06) that
  consumes these ids. Declaration order is invariant under rename.
- `VariableSymbol`/`FunctionSymbol` gain `SymbolId` + the **exact identifier token span**. This is
  the substrate roadmap gates 3/4 instrument against, and what retires the #879/#893
  mutable-cursor class properly (the emitter follow-up is separate work).
- **Scheduled in B1** (plumbing on symbols, no consumers yet) — review C1's unassigned-item fix.

**D5 — Overload sets for top-level functions (#762 items 5–6).** Reuse the class-member
machinery; `TryDeclare` failures become diagnostics. **Pass-interaction note (review m2):** this
changes *pass 1* (symbol registration, `Binder.cs:57–66`) while bodies bind in pass 2 — the
overload-set switch must land with a test that pass-2 call resolution sees the full set
regardless of declaration order, which is the property two-pass binding exists to provide.

**D6 — Decimal and conversion fidelity (#762 item 4).** `BoundDecimalLiteral` (no double
downcast); `BoundConversionExpression` preserving the target type; `is`/pattern tests bind
structurally instead of folding to `true`.

**D7 — Item-8 disposition lands in B8, full scope.** The expression half (`GenericTypeNode`,
`KeywordArgNode`) resolves with its F-1 amendment (additive promotion or subtractive
reclassification with supersession). The **seven non-expression no-op-Accept classes** get real
dispatch or a reclassification out of `AstNode`, plus the **AstNode-wide** reflection test #762's
regression list requires (the ExpressionNode-only test ships in B1; B8 widens it).

## 3. PR slicing (each PR: suite green, adversarial review, merge)

| PR | Scope | Exit criterion |
|---|---|---|
| **B1 — rails** | Dispatch table + ExpressionNode reflection completeness test; `BoundIncompleteExpression` (zero-child phase) + 0200-band diagnostic; **diagnostic routing** (§5: analyze path stops discarding the binder bag; LSP severity decision applied); **D4 SymbolId + span plumbing** on symbols; **the F-2 instrument** (§5) with its discriminating pin; **F-2/roadmap marker amendments** (§5); baseline checker-verdict snapshot + direct-Binder test audit (22 files instantiate `Binder` directly) | All 61 classes flow through the table; baseline incomplete-fraction measured and published; checker verdicts byte-identical to the snapshot; LSP noise decision in effect |
| **B2 — core 9** | `Ok/Err/Some`, `ExpressionCallNode`, `AnonymousObjectCreationNode`, `RecordCreationNode`, `WithExpressionNode`, `ThrowExpressionNode` (+`SelfRefNode` per its F-1 dormant rule) | F-1 core row fully live |
| **B3 — arrays/indexes + collections** | 13 classes; biggest checker payoff | Family binds; checker deltas disclosed |
| **B4 — string family** | `StringOperationNode`, `InterpolatedStringNode`, `StringBuilderOperationNode`, `CharOperationNode` | Family binds |
| **B5 — conversion + type/pattern + decimals** | `TypeOperationNode` full ops, `IsPatternNode`, `TypeOfExpressionNode`, D6 | Casts keep operand+target; patterns stop folding to `true` |
| **B6 — control-value forms** | `NullCoalesceNode`, `NullConditionalNode`, `MatchExpressionNode`, `LambdaExpressionNode`, `AwaitExpressionNode` | Family binds; deferred-evaluation marking exercised |
| **B7 — quantifiers** | `ForallExpressionNode`, `ExistsExpressionNode`, `ImplicationExpressionNode` | Family binds; no verification-pipeline interaction |
| **B8 — closure** | Interop structural binding; D5 overload sets; expression-start unification + **the token-context test matrix** (#762: every expression-start token in return/bind/argument/nested contexts); **diagnostic-seed suite** (a seed nested inside every expression wrapper, proven reachable); D7 full item-8 scope (all 8 no-op Accepts + AstNode-wide reflection test) + F-1 amendment; Tier-B child extractors with `IsDeferredContext`; **gate 1 evaluation** | Zero Tier A `incomplete` on the F-2 corpus; every #762 DoD bullet green |

Every #762 "Required implementation" item and regression-coverage bullet now maps to a row:
items 1–3 (B1–B8 families), 4 (B5), 5–6 (B8), 7 (B8), 8 (B1 expression-half test + B8 full),
9 (B1); regression list: AstNode reflection test (B8), diagnostic seed (B8), cast/type/decimal
(B5), overloads (B8), token contexts (B8), unsupported-node diagnostics (B1).

## 4. Explicitly out of scope

- **Metadata-backed .NET typing** — the 0.14 entry spike. This rebuild must not grow into it.
- **Effect-resolution contracts**: `BoundCallExpression.Target`-string consumers keep their
  string surface until 0.14/0.15.
- **Emitter/codegen changes** (verified: neither emitter consumes bound nodes).
- **Checker rewrites** (#786, 0.15) — checkers get more input; their logic is untouched.

## 5. Instrumentation, diagnostics routing, and the frozen marker

- **Diagnostic routing (new, from review C2):** `VerificationAnalysisPass` binds into a throwaway
  bag today — B1 routes binder diagnostics into the real bag (or the instrument cannot see them),
  and decides LSP presentation: the incomplete diagnostic ships at **Info severity until B8**
  (37 Tier A classes are incomplete at B1 — Warning would flood every editor; the severity
  promotion to Warning is B8's, when the count is ~0 and a warning means something).
- **Instrument mechanism (named, from review M3):** diagnostic-count based — the CI leg runs the
  analyze pipeline over both F-2 corpus legs and counts the 0200-band incomplete code per file;
  fraction = incomplete diagnostics ÷ bound expressions (a counter the binder emits). No
  bound-tree serialization exists and none is invented for this.
- **The `<unsupported:` marker is frozen in three places** (`v0.13-freeze-registrations.md`
  Tier A header, gate restatement, F-2 instrument; `roadmap-v0.13-v0.15.md` gate 1). B1 carries
  **additive amendment entries** in the freeze doc (and the roadmap wording) naming the new
  marker/diagnostic code, argued as an additive rename — the denominator (Tier A classes must not
  silently degrade) is unchanged and the instrument strictly improves (silent fallback → counted
  diagnostic); it is not a weakening, and the argument appears in B1's PR description per the
  amendment rules.
- **Discriminating pin** (F-2's requirement): a fixture whose Tier A construct is temporarily
  routed to `BindIncomplete` must turn the CI leg red; runs as a test, continuously.
- **Checker-delta disclosure** per family PR. **Mechanism (named in B1, per its review Major 1,
  before any family PR): the analysis-layer test suite IS the verdict baseline** — ~1,500
  analysis tests pin checker verdicts, and the ratchet pins the corpus-level incomplete counts;
  a family PR's checker-delta disclosure = the suite diff (any analysis-test change it required,
  each explained) plus the baseline movement. No separate serialized verdict-snapshot artifact
  exists; if a family PR changes checker behavior the suite does not pin, that is a test GAP to
  close in that PR, not a silent delta. (B1's direct-Binder audit, corrected count: **14**
  pre-existing test files instantiate `Binder` directly — not 22 as round-1 review estimated —
  all green post-B1 with one updated pin, `Binder_FallbackExpression_ReturnsOpaqueExpression`.)
- **CI cost budget (review m2):** the corpus leg must stay under **5 minutes**; B1 measures and
  publishes the actual cost; the conversion leg's migrate outputs are cached per pinned subject
  commit (they are deterministic at a frozen commit).

## 6. Risks

1. **Checker behavior movement** — five checkers key on the fallback shape; family PRs change
   their input distribution. Mitigation: B1's frozen baseline snapshot + per-family disclosure +
   the D-S2.3 negative-corpus precedent.
2. **LSP visibility** — the binder runs live in editors (review C2); the Info-until-B8 severity
   decision is the mitigation, and LSP snapshot tests are part of B1's audit.
3. **Parser coupling** — deferred to B8 deliberately.
4. **Direct-Binder tests** — 22 test files instantiate `Binder` and assert diagnostic sets; B1
   audits and updates them alongside the routing change (not just goldens, which are
   diagnostics-blind).
5. **Scope creep toward 0.14** — §4 is the fence; reviews police it.

## 7. What "zero checker behavior change" means in B1 (review C2/M2)

Binding runs only under analyze and in the LSP. B1's criterion is therefore: (a) analyze-mode
checker verdicts over the F-2 corpus are byte-identical to the pre-B1 snapshot; (b) LSP
diagnostics gain only the new Info-severity incomplete code; (c) normal compiles are bit-identical
(binding still doesn't run there). The incomplete-fraction is measured in analyze mode — stated so
the instrument's scope is honest about where binding executes.

## 8. Review record — round 1 (2026-08-10)

Verdict 65%, NEEDS-FIXES. Applied: **C1** (SymbolIds scheduled in B1; item 8's non-expression
half — 7 classes — plus the AstNode-wide reflection test, diagnostic-seed suite, and
token-context matrix all assigned to B8; full DoD→row map added) · **C2** (consumer map corrected:
LSP binds with the live bag, analyze discards its bag; diagnostic routing + Info-severity decision
added to B1; §7 defines the exit criterion against where binding actually runs) · **M1** (SymbolId
gains a file-path component — module ids demonstrably collide — and locals key by declaration
order, rename-invariant) · **M2** (two-phase D2 semantics: B1 zero-child, children with real
binders, Tier-B extractors with `IsDeferredContext` at B8; no generic child enumeration assumed) ·
**M3** (marker frozen in three places; B1 carries the additive amendments with the classification
argued; instrument mechanism named as diagnostic-count based) · **M4** (counts corrected: 18 arms
with 2 conditional, 43 bound classes, 8 no-op Accepts) · **m1** (D1 justification recorded) ·
**m2** (CI budget, two-pass note on D5, direct-Binder test audit). Declined: none.
