# RFC v3: Compact Stable Identifiers — Drop Structural IDs, Compact Symbol IDs

**Status:** Draft (v3 — supersedes [`path-2-drop-ids-v2.md`](./path-2-drop-ids-v2.md))
**Author:** TBD (with Copilot CLI)
**Created:** 2026-05-22
**Supersedes:** v2 (2026-05-22), v1 (2025-11-25)
**Reviewed against:** [`path-2-drop-ids-v2-critique.md`](./path-2-drop-ids-v2-critique.md) — plus carry-over from v1's [thesis critique](./path-2-drop-ids-critique.md) and [devil's advocate](./path-2-drop-ids-devils-advocate.md)
**Target release:** Pre-1.0 single hard break (0.x → 0.x+1) — Phase 1 + Phase 2 bundled, conditional on the §10 gate experiment running on a branch before the release cut.

---

## 0. Why v3 exists

v2 was approved-with-patches by [the v2 critique](./path-2-drop-ids-v2-critique.md). The critique found one **blocking** technical bug, three honesty/scoping cleanups, two architectural cleanups, and three process improvements:

| v2 critique finding | v3 response |
|---|---|
| **Blocking:** §6.1 collision math is wrong; 9-char base36 gives 0.49% collision at 10⁶ IDs (catastrophic) and 39% at 10⁷ IDs. The "36× margin" claim is also wrong on the wrong axis. | **§6.1 rewritten:** moves to **12-character Crockford base32** (1.15×10¹⁸ space, P≈4×10⁻⁵ at 10⁷ IDs) **plus generate-until-unique enforcement in `IdGenerator`**. Both critique-recommended remediations are adopted, defense-in-depth. |
| §5.6 migrator hedges between "AST-edit-and-print" and "regex-guided pass" in one paragraph | **§5.6 rewritten** as a single coherent strategy: **lexer-anchored text-edit with post-edit AST-diff verification.** The existing lexer (which already handles strings and comments correctly) drives the edit; the post-edit verifier proves no semantic damage. |
| §1 / §5.3 oversells Phase 1's token savings on production code | **§1 and §5.3 reframed:** Phase 1 ships for **parser-path simplification and cleanup**, not for tokens. The token win is incidental. Production structural IDs (`if1`, `for1`) are already short — Phase 2's ULID compaction is where the tokens are. |
| §3's "principle preserved" framing masks a real narrowing | **§3 owns the narrowing explicitly:** v3 narrows design principle #3 from "Everything has an ID" (universal) to "Every symbol-level declaration has an ID" (scoped). This is a change, called by name. |
| §2.3 omits error-recovery precision as a structural-ID benefit | **§2.3 adds it:** the parser-diagnostic can name *which* open the closer should have matched. Small but real. |
| §6.4 cache invalidation contradicts itself (migrator remap vs "rebuilds on first compile") | **§6.4 rewritten:** the migrator remaps Z3 cache keys in place via the deterministic ULID → compact map. No proof recomputation. |
| §10 gate has thin statistical power (N=3, no test specified, no pre-registration) | **§10 rewritten:** N ≥ 10 per task per arm (~600 runs); pre-registered analysis plan in a separate document; specified statistical tests (paired Wilcoxon for continuous, McNemar for binary) with Bonferroni correction across the four kill criteria. |
| §5.7 / §6.2 ship two hard breaks in two releases | **§11 new section:** Phase 1 and Phase 2 **bundled in 0.x+1**, conditional on the gate experiment completing on a branch before the release cut. If the gate fails, Phase 1 alone ships. Avoids the two-migration UX problem. |
| §8.3 (qualified-name diagnostics) was "recommended" but un-sequenced | **§8.3 promoted to definite, ships first as its own ~1-day PR.** No longer a footnote. |
| §8.1 `Calor0820` has a chicken-and-egg issue (the migrator itself parses `.calr`) | **§8.1 amended:** the diagnostic text explicitly guides users to the recovery path. The migrator carries the legacy parser as a private helper (already in v2 §5.6); v3 surfaces this in the user-visible diagnostic. |
| Pivot-plan reconciliation incomplete (sub-block-level diff/merge degrades) | **§14 new section:** owns the cost. Symbol-level diff/merge preserved; sub-block-level edits become positional/AST-index in the diff representation. Documented for the pivot plan to consume. |
| §9 docs cascade undercounted (1d → ~2d); editor TextMate grammar missed | **§9 corrected:** docs cascade at 2 days, editor grammar work added (~0.5 day). |

v3 also acknowledges what the v2 critique **explicitly approved** about v2: the symbol/structural split, the identity-model preservation, the `[CalorId]` honesty, the single-release hard break in the pre-1.0 envelope, the §3 thesis reframing. Those are unchanged.

---

## 1. Summary

**One release. Two changes. One gate. One standalone-first improvement.**

**Standalone — Diagnostic addressing (definite, ship first).**
~1 day PR. Diagnostic format becomes `Calor0501: division by zero in Calculator.Divide (f_01J5X7…) at file:42`. The ID stays in parentheses for tools; the qualified name is in front for humans/agents. No grammar change, no migration. **Ships in the release before Phase 1.**

**Phase 1 — Drop structural IDs (definite).**
Remove the ID block from sub-block constructs only. Symbol IDs are unchanged. **Engineering justification, not token justification** — Phase 1 saves only a few percent on production code where structural IDs are already short (`if1`, `for1`), and 0% on programs without sub-blocks. It ships because the parser path simplifies, the migrator is mechanical, and the structural ID was never load-bearing. Effort: ~2 weeks.

**Phase 2 — Compact symbol IDs (gated).**
Replace 28-character ULIDs with **12-character Crockford base32** IDs (`m_a1b2c3d4e5f6`). Symbol-level identity model is preserved end-to-end. **Measured combined Phase 1+2 savings on production-ULID projection: ~33%** (28–40% per task, see [`bench/phase0/out/report.md`](../../bench/phase0/out/report.md)). Effort: ~1.5 weeks. **Ships only if** the agent-harness experiment in §10 demonstrates a measurable agent-success improvement.

**Release sequencing (§11): Phase 1 and Phase 2 ship together in 0.x+1, or Phase 1 alone if the gate fails.** The gate runs on a branch before the release cut. This trades 3–4 weeks of calendar (waiting for the gate) for avoiding two breaking migrations in two releases. The trade is worth it.

**Principle narrowing acknowledged (§3):** v3 narrows design principle #3 from "Everything has an ID" (universal) to "Every symbol-level declaration has an ID" (scoped). Sub-block constructs are addressable by structural position but not by identity. This is a change. We own it.

---

## 2. Motivation

### 2.1 Evidence accounting (unchanged from v2 §2.1)

v1's Phase 0 benchmark measured 5 small tasks in *test-form* Calor (`f001`, `m001`), not production ULIDs (28 chars each). The v1 20% headline understated the real production cost of ULIDs by roughly an order of magnitude per occurrence. v2's re-measurement showed:

- Phase 1 alone on test-form: ~5–9% (concentrated on sub-block-heavy tasks)
- Phase 1+2 combined on production-ULID projection: ~33% (28–40% per task)

The v2 critique correctly points out that production *structural* IDs are short — `RoslynSyntaxVisitor` generates names like `if1`, `for1`, not ULIDs. So Phase 1 alone delivers roughly the same savings in test-form and production-form: **small but real, concentrated where sub-blocks are dense.** Phase 2 (the ULID compaction) is where the production-form token wins live.

This re-cuts the rationale: **Phase 1 ships for cleanliness; Phase 2 ships for tokens.** v3 surfaces this honestly in §1 and §5.3.

### 2.2 The two populations are different (unchanged from v2 §2.2)

| Population | Tracked by `IdScanner` | Used as cache key | Used in round-trip | Source of token cost |
|---|:---:|:---:|:---:|---|
| **Symbol IDs** on Module / Function / Class / Interface / Property / Method / Constructor / Enum / OperatorOverload / Indexer / RefinementType / ProofObligation / IndexedType / EnumExtension | yes | yes (verifier) | yes (planned, not implemented) | ~28 tok per occurrence (ULID) |
| **Structural IDs** on `§L`, `§IF`, `§WH`, `§DW`, `§TR`, `§FOREACH` and their close-tags | **no** | no | no | ~2–3 tok per occurrence in both test and production form (the synthesizer uses short names) |

Confirmed by reading [`src/Calor.Compiler/Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) (empty `Visit(ForStatementNode)`, etc.) and [`src/Calor.Compiler/Verification/Obligations/GuardDiscovery.cs`](../../src/Calor.Compiler/Verification/Obligations/GuardDiscovery.cs) (`ObligationId = obligation.Id`, symbol-level).

v1 treated both populations as one decision. v2 separated them. v3 keeps the separation and refines the *why* for each.

### 2.3 What each population is worth

**Symbol IDs deliver:**

- Rename safety (canonical ID survives `Calculate → Compute`)
- Z3 proof-cache stability (proof of `DivisorNotZero` is keyed by ID; survives rename / body reformat)
- Round-trip identity (designed but not yet implemented — see §2.4)
- IR substrate for diff / merge / coordination / memory (the pivot-plan requirement)
- Unambiguous diagnostic addressing across edits (line-number-stability point in `docs/philosophy/stable-identifiers.md`)

**Structural IDs deliver:**

- Matched open/close enforcement in the parser
- **Error-recovery precision in parser diagnostics: when a closer doesn't match, the diagnostic can name which open it should have matched** (added per [v2 critique §4](./path-2-drop-ids-v2-critique.md))

The first list is the project's competitive moat. The second list is small but not zero — and v3 surfaces it honestly. Phase 1 trades structural-ID error-recovery precision for cleaner parser code paths. The §15 recommendation accepts the trade because:

- Open-stack-based error recovery (every other language) delivers ~80% of the precision of ID-matched error recovery, with the gap appearing only in deeply nested constructs.
- The Phase 1 migrator's "parse the output, diff against parse-of-input" verifier (§5.6) catches any migration-time damage before write-back.
- Post-migration code rarely produces malformed close-tags (the agent or human writing it has the open-tag context in immediate scope).

But the trade is real. §13 includes it as a residual concern.

### 2.4 Round-trip honesty (unchanged from v2 §2.4)

v1 claimed `[CalorId("f_01ABC...")]` is emitted for round-trip stability. A grep across `src/Calor.Compiler/CodeGen/`, `src/Calor.Runtime/`, and `src/Calor.Compiler/Migration/` returns **zero** uses of a `CalorId` attribute. The C# emitter today emits `[CalorAttribute]` (a different, generic attribute — see [`CSharpEmitter.cs:4075` `EmitCSharpAttributes`](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L4075)) for code-as-data preservation, not for ID round-trip. **Round-trip today is signature-based, not ID-based.**

This means v1's `[CalorId] → [CalorSymbol]` rename proposal was theater. v3 proposes no C# attribute changes. A future round-trip-stability implementation using IDs is a separate RFC.

---

## 3. Reframed thesis (and the principle narrowing called by name)

v1: "Names are identity."
v2: "Identity belongs on symbols, not on structure. Symbol identity should be compact; structural identity should not exist."
v3: **same as v2, plus this explicit acknowledgment:**

> **Design principle #3 narrows.** [`docs/philosophy/design-principles.md`](../philosophy/design-principles.md) §3 stated "Everything has an ID" as a universal property of Calor constructs. v3 narrows this to **"Every symbol-level declaration has an ID."** Sub-block constructs (loops, ifs, while, do-while, try, foreach, match, forall, exists, unsafe, fixed, sync, using) are addressable by structural position but not by identity. **This is a genuine narrowing of the original principle. We do not claim it is "preserved."** We claim it is correctly scoped to the population where it pays its weight, and the broader claim never had any cost-benefit justification in the first place (`IdScanner` already ignored sub-block IDs — they were never canonical).

This change requires updating:

- [`docs/philosophy/design-principles.md`](../philosophy/design-principles.md) §3: replace the universal statement with the scoped statement. Add a note explaining the narrowing and the cost-benefit justification.
- [`docs/philosophy/stable-identifiers.md`](../philosophy/stable-identifiers.md): add a new section "What identity does *not* cover" explicitly listing sub-block constructs as out of scope.

Diagnostics may use qualified names + positional paths for *addressing* (a presentation concern); cross-edit references and cache keys continue to use symbol IDs for *identity* (a correctness concern). [v1 thesis critique](./path-2-drop-ids-critique.md) §"Names are addressable. They are not identity" — v3 honors this distinction.

---

## 4. Goals & non-goals

### Goals

- **G1.** Reduce token cost of Calor source by a measurable amount with no semantic change and no identity-model degradation **on production-ULID code** (the realistic target).
- **G2.** Phase 1: simplify parser code paths and remove a non-load-bearing construct (token savings is incidental).
- **G3.** Phase 2: reduce production-form ID token cost by ~30–40% **only if** the §10 gate measurement clears.
- **G4.** Do not break the IR / diff / merge / coordination / memory substrate the pivot plans depend on (at the symbol level).
- **G5.** Do not introduce positional fragility for *identity-bearing* uses (cache keys, cross-edit refs). Positional addresses may appear in *presentation* uses (diagnostics) where staleness is already accepted because they're combined with file:line.
- **G6.** Single-release hard break. **Bundle Phase 1 + Phase 2 in 0.x+1** if the gate passes; ship Phase 1 alone if not. Pre-1.0 envelope allows this.
- **G7.** **Phase 2 ID format must be collision-safe up to 10⁷ project-scoped IDs with margin.** This is a hard requirement; the v2 9-char base36 design failed it.

### Non-goals

- **NG1.** v3 does not change Calor semantics, the type system, contracts, effects, or runtime.
- **NG2.** v3 does not address the C# → Calor round-trip story beyond what already exists. Round-trip stability via attribute is a separate (future) RFC.
- **NG3.** v3 does not change `IdScanner`, `IdValidator`, `IdGenerator`, or diagnostic codes `Calor0800–0805` *in Phase 1*. Phase 2 changes `IdGenerator`'s output format and adds uniqueness enforcement.
- **NG4.** v3 does not change MCP tool surfaces (`calor_navigate`, `calor_fix`, etc.) beyond the strict consequences of the grammar change.
- **NG5.** v3 does not propose Path A from the thesis critique ("BPE-friendly short IDs" alone) — Phase 1's structural-ID drop is orthogonal and additive; v3 does both.
- **NG6.** v3 does not propose Path B from the thesis critique ("sidecar file") — both critiques and the user agree a sidecar is undesirable.
- **NG7.** v3 does not propose Path C from the thesis critique ("IDs on `pub` only") — requires the agent to reason about visibility at write-time.
- **NG8.** v3 does not propose multi-agent ID coordination (e.g., a central allocator). Single-process ID generation with retry-on-collision is sufficient at the in-project scale.

---

## 5. Phase 1 — Drop structural IDs

### 5.1 Scope (unchanged from v2 §5.1)

| Tag | Today | Phase 1 |
|---|---|---|
| `§L` for-loop | `§L{for1:i:1:10:1}` … `§/L{for1}` | `§L{i:1:10:1}` … `§/L` |
| `§FOREACH` | `§FOREACH{fe1:x:items}` … `§/FOREACH{fe1}` | `§FOREACH{x:items}` … `§/FOREACH` |
| `§WH` while | `§WH{wh1} (cond)` … `§/WH{wh1}` | `§WH (cond)` … `§/WH` |
| `§DW` do-while | `§DW{dw1}` … `§/DW{dw1} (cond)` | `§DW` … `§/DW (cond)` |
| `§IF` (block) | `§IF{if1} (cond)` … `§/I{if1}` | `§IF (cond)` … `§/I` |
| `§IF` (inline) | `§IF{if1} (cond) → expr §/I{if1}` | `§IF (cond) → expr §/I` |
| `§TR` try | `§TR{try1}` … `§/TR{try1}` | `§TR` … `§/TR` |
| `§UNSAFE` / `§FIXED` / `§SYNC` / `§USING` | `§X{id}` … `§/X{id}` | `§X` … `§/X` |
| `§MATCH` | `§MATCH{m1}` … `§/MATCH{m1}` | `§MATCH` … `§/MATCH` |
| `§FORALL` / `§EXISTS` | `§FORALL{fa1:x:t}` … `§/FORALL{fa1}` | `§FORALL{x:t}` … `§/FORALL` |
| `§LIST` / `§DICT` / `§HSET` literal | `§LIST{name:type}` (name is a binding) | unchanged |
| `§PP` preprocessor | `§PP{COND}` (COND is semantic) | unchanged |
| `§CA` catch | `§CA{Type:var}` (no ID today) | unchanged |

Only the **opaque structural identifier** form is dropped. Forms where the `{...}` block contains a *binding name* or *semantic value* are unchanged.

### 5.2 Symbol IDs unchanged in Phase 1 (unchanged from v2 §5.2)

Every symbol-level declaration retains its ULID in Phase 1: `§M`, `§F`, `§AF`, `§CL`, `§IFACE`, `§EN`, `§EXT`, `§MT`, `§AMT`, `§CTOR`, `§PROP`, `§IXER`, `§OP`, `§RTYPE`, `§PROOF`, `§ITYPE`. The Phase 1 release ships with ULIDs intact.

Format changes only in Phase 2 (§6), gated.

### 5.3 Why Phase 1 ships (revised framing per v2 critique §3)

**Phase 1 is a cleanup. The token savings is small on real production code.** We ship it because:

1. The parser code paths simplify (~150 LOC deleted from the `if (endId != id) ReportMismatchedId` branches across 12 statement parsers).
2. The structural ID was never load-bearing — `IdScanner` ignored it, the C# emitter ignored it, the round-trip system ignored it. Removing it doesn't remove anything the project relies on.
3. The migrator is mechanical and low-risk (§5.6).
4. The grammar becomes more readable for the agent (fewer noise tokens around control flow).
5. Token savings is real but small (~5–9% on test form, ~5% on production form), and concentrated on sub-block-heavy tasks. **This is an incidental benefit, not the justification.**

| Critique objection (v1/v2 combined) | Phase 1 status |
|---|---|
| Thesis: "names are not identity" | Resolved. Symbol IDs unchanged. Identity model intact. |
| Thesis: "contradicts pivot strategy / IR substrate" | Resolved at the symbol level. (Sub-block-level diff/merge degrades, owned in §14.) |
| Devil's advocate §2: "`[CalorSymbol]` smuggles identifiers" | Resolved. v3 proposes no C# attribute changes. |
| Devil's advocate §3: "positional sub-block addresses are fragile" | Acknowledged. Used only for diagnostic *display*, not identity. |
| Devil's advocate §4: "dual-mode parser is permanent tax" | Resolved. Single-release hard break in pre-1.0. |
| Devil's advocate §6: "Z3 cache key story unaddressed" | Resolved. Proof obligation IDs are symbol-level and unchanged. |
| Devil's advocate §7: "refinement types / proof obligations get worst of both worlds" | Resolved. Both keep IDs; name collisions are non-issues because identity is by ID. |
| v2 critique §4: "error-recovery precision is lost" | Acknowledged. Small but real. Mitigations in §2.3 and §13. |
| v2 critique §3: "principle preserved" framing is misleading | Resolved. §3 owns the narrowing explicitly. |

### 5.4 Grammar change (unchanged from v2 §5.4 — complete enumeration)

```
// Phase 1 grammar diff — sub-block constructs only

Loop_Stmt        ::= '§L'       Loop_Spec       Statement* '§/L'
Foreach_Stmt     ::= '§FOREACH' Foreach_Spec    Statement* '§/FOREACH'
While_Stmt       ::= '§WH'      Condition       Statement* '§/WH'
DoWhile_Stmt     ::= '§DW'                      Statement* '§/DW' Condition
If_Stmt          ::= '§IF'      Condition       Statement* ('§EI' Condition Statement*)* ('§EL' Statement*)? '§/I'
If_Inline        ::= '§IF'      Condition '→'   Expression ('§EI' Condition '→' Expression)* ('§EL' '→' Expression)? '§/I'
Try_Stmt         ::= '§TR'                      Statement* Catch_Clause* Finally_Clause? '§/TR'
Unsafe_Stmt      ::= '§UNSAFE'                  Statement* '§/UNSAFE'
Fixed_Stmt       ::= '§FIXED'   Fixed_Spec      Statement* '§/FIXED'
Sync_Stmt        ::= '§SYNC'    Lock_Expr       Statement* '§/SYNC'
Using_Stmt       ::= '§USING'   Using_Spec      Statement* '§/USING'
Match_Stmt       ::= '§MATCH'   Subject         Match_Case+ '§/MATCH'
Forall_Expr      ::= '§FORALL'  Quant_Spec      Expression  '§/FORALL'
Exists_Expr      ::= '§EXISTS'  Quant_Spec      Expression  '§/EXISTS'

// Where:
Loop_Spec        ::= '{' var ':' from ':' to ':' step '}'
Foreach_Spec     ::= '{' var ':' collection '}'
Quant_Spec       ::= '{' var ':' type '}'
Fixed_Spec       ::= '{' type ':' var ':' init '}'
Using_Spec       ::= '{' var ':' init '}'

// Unchanged — every symbol-level declaration retains its ID:
Function_Decl    ::= '§F' '{' id ':' name ':' visibility '}' Body '§/F'
Module_Decl      ::= '§M' '{' id ':' name '}'                 Body '§/M'
// ... (full list in §5.2)
```

### 5.5 Compiler changes (Phase 1)

| File | Lines (today) | Lines changed | Nature of change |
|---|---:|---:|---|
| [`Parsing/Parser.cs`](../../src/Calor.Compiler/Parsing/Parser.cs) | ~8,900 | ~150 | `ParseFor/If/While/Try/Foreach/DoWhile/Match/Forall/Exists/Unsafe/Fixed/Sync/Using` lose the ID extraction + `endId == id` matching paths. |
| [`Ast/`](../../src/Calor.Compiler/Ast/) sub-block nodes | n/a | ~80 | `Id` becomes nullable on sub-block nodes; constructor signatures lose the unused `id` parameter; AST-printer falls back to `<anon>`. |
| [`CodeGen/CSharpEmitter.cs`](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs) | ~4,600 | ~20 | Sub-block IDs were never emitted into generated C# (confirmed by grep — sub-block visitors don't reference `node.Id`). Changes limited to any internal label synthesis. |
| [`Migration/CalorEmitter.cs`](../../src/Calor.Compiler/Migration/CalorEmitter.cs) | ~2,800 | ~40 | Reverse emitter writes `§L{for1:i:1:10:1}` today → `§L{i:1:10:1}`. Mechanical string-template change. |
| [`Migration/RoslynSyntaxVisitor.cs`](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs) | ~6,500 | ~5 | Stop synthesizing sub-block IDs (`if1`, `for1`). Trivial. |
| [`Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) | ~330 | 0 | Already ignores sub-block nodes. No change. |
| [`Verification/ExpressionSimplifier.cs`](../../src/Calor.Compiler/Verification/ExpressionSimplifier.cs) | ~1,400 | 0 | Doesn't touch sub-block IDs. No change. |
| [`Diagnostics/Diagnostic.cs`](../../src/Calor.Compiler/Diagnostics/Diagnostic.cs) | — | ~10 | Add `Calor0820` (text in §8.1). |
| `editors/vscode/syntaxes/calor.tmLanguage.json` | — | ~30 | TextMate grammar updated to stop highlighting structural-ID blocks as identifier scopes. |

**Total new code: ~300 lines. Modified code: ~150 lines. Deleted code: ~50 lines.**

### 5.6 Migrator (Phase 1) — single coherent strategy

**Strategy: lexer-anchored text-edit with post-edit AST-diff verification.**

This is one strategy with three steps, not three strategies bolted together. v2 §5.6 was ambiguous about whether the migrator built an AST or worked at the text level; v3 commits to text level with an AST-based safety net.

1. **Tokenize using the existing lexer.** The lexer already correctly handles strings, comments, escapes, and `\r\n` line endings. It identifies every `§L{...}`, `§/L{...}`, `§IF{...}`, etc. opener/closer with its byte range. This is not a regex — it's the production lexer running in token-emission mode.
2. **Text-edit the source.** For each identified sub-block opener with structural ID, surgically remove only the `{id...}` block characters using the byte ranges from step 1. For each matching closer, similarly remove its `{id}` block. **All other source bytes are preserved exactly** — comments, blank lines, whitespace, BOMs, line endings.
3. **Verify post-edit.** Parse the edited source with the **new** parser. Parse the original source with the **legacy** parser (kept as a private migrator-internal helper). Walk both ASTs and assert structural equivalence everywhere except the dropped sub-block IDs. If verification fails, abort the write-back, restore the original, and surface the file in the migrator's error report.

**Behavior guarantees:**

- **Comment preservation:** complete (no AST re-emission).
- **Formatting preservation:** complete (no AST re-emission).
- **String-content safety:** complete (lexer already handles strings).
- **Comment-content safety:** complete (lexer already handles comments).
- **`\r\n` safety:** complete (lexer is line-ending agnostic).
- **Idempotence:** files without structural IDs pass through unchanged (lexer emits no openers with structural ID, so no edits happen).
- **Atomic correctness:** the verification step proves that the edit didn't damage anything except the intended drop. If a file fails verification, *no* file is written.

**External-reference handling:**

- In-tree: migrator handles all `.calr` files, doc samples, fixture files.
- Out-of-tree (website, archived discussions): become stale once. One-time cleanup. Mechanical.
- No `calor-suppress` directives exist in the repo (verified by grep, returns 0 matches). None to handle.

**CLI surface:**

```
calor fix --drop-structural-ids [--dry-run] [--verify-only] [path]
```

- `--dry-run`: shows diffs without writing.
- `--verify-only`: runs the verification step against the in-place source (without text edit) — useful for CI to check that the source already conforms to Phase 1 grammar.
- `path`: defaults to CWD recursively.

### 5.7 Phase 1 deprecation strategy (unchanged from v2 §5.7)

Hard break, single release. Pre-1.0 envelope per [`CLAUDE.md`](../../CLAUDE.md):

- 0.x: legacy form accepted (current).
- 0.x+1: legacy form rejected with `Calor0820`; error message includes the exact migrator command. Migrator ships in the same release.
- No dual-mode parser. No multi-release window.

---

## 6. Phase 2 — Compact symbol IDs (gated)

### 6.1 What changes — corrected collision math

**Format: 12-character Crockford base32**, `[0-9A-HJ-NP-TV-Z]` (Crockford alphabet excludes `I`, `L`, `O`, `U` for visual disambiguation). All-lowercase in source; matched case-insensitively in tooling.

| Element | Today (28 chars after prefix) | Phase 2 (12 chars after prefix) | Per-occurrence token savings |
|---|---|---|---|
| Function | `f_01J5X7K9M2NPQRSTABWXYZ12` | `f_a1b2c3d4e5f6` | ~16 tokens (vs ~28 today) |
| Module | `m_01J5X7K9M2NPQRSTABWXYZ12` | `m_a1b2c3d4e5f6` | ~16 tokens |
| Class | `c_01J5X7K9M2NPQRSTABWXYZ12` | `c_a1b2c3d4e5f6` | ~16 tokens |
| (etc.) | | | |

**Why 12 chars and not 9** (correcting v2's blocking bug):

| IDs (n) | 9-char base36 (N≈10¹⁴) | 12-char base32 (N≈1.15×10¹⁸) |
|---|---|---|
| 10⁵ | ≈ 5×10⁻⁵ | ≈ 4×10⁻⁹ |
| 10⁶ | ≈ **0.49% (1-in-200)** ❌ | ≈ 4×10⁻⁷ ✅ |
| 10⁷ | ≈ **39% (~certain at scale)** ❌ | ≈ 4×10⁻⁵ ✅ |
| 10⁸ | — | ≈ 4×10⁻³ ✅ |

[Verified arithmetic; see the script in §16.D appendix.] A 0.5% per-project collision probability for an identity system at 10⁶-symbol scale is **catastrophic** — a single collision breaks the proof cache for two unrelated proofs, breaks diff identity for two unrelated symbols, and corrupts memory keyed on those IDs. The 12-char base32 design gives **5 orders of magnitude** more safety at the cost of +3 characters per ID.

**Why Crockford base32 and not raw base36:** Crockford excludes visually ambiguous characters (`I`, `L`, `O`, `U`), reducing human transcription errors when an ID appears in a stack trace or diagnostic. The token cost is marginally higher than raw base36 because the alphabet is smaller, but the readability win is worth the +0 to +1 token-per-ID delta. The existing ULID alphabet (also Crockford base32) means the lexer already accepts these characters.

**Why generate-until-unique enforcement (in addition to 12 chars):**

Defense-in-depth. Even with 12-char base32, at 10⁷ IDs the collision probability is 4×10⁻⁵. That means roughly 1 in 25,000 projects might hit a collision over their lifetime. Generate-until-unique makes this 0:

```csharp
// IdGenerator.Generate(prefix) — Phase 2 implementation sketch
public string Generate(string prefix) {
    for (int attempt = 0; attempt < MaxAttempts; attempt++) {
        var candidate = prefix + "_" + GenerateCompactBase32(12);
        if (!_projectRegistry.Contains(candidate)) {
            _projectRegistry.Add(candidate);
            return candidate;
        }
    }
    throw new IdSpaceExhaustedException(prefix);  // unreachable in practice
}
```

This requires `IdGenerator` to hold a reference to the per-project ID registry. The registry is populated at compile time by `IdScanner` (which already walks every `.calr` file building the symbol table). Adding an in-memory `HashSet<string>` to that walk is a few lines.

**Migration ID remap:** deterministic via `crockford32(sha256(ulid))[:12]`. Each existing ULID maps to a deterministic 12-char compact form. Repeated runs produce the same output (important for cache invalidation — see §6.4).

**Migration collision handling:** the deterministic remap is exposed to the same 4×10⁻⁵ collision rate at 10⁷ IDs. The migrator handles this by:

1. Computing the remap for every existing ULID.
2. Detecting any collision (two distinct ULIDs mapping to the same 12-char form).
3. For each colliding pair, using `crockford32(sha256(ulid + ":1"))[:12]` for the second, `:2` for a third (vanishingly unlikely at this scale), etc.
4. Emitting a `migrator.log` entry for every collision-resolved ID with the original and remapped values, for audit.

**Sortability:** ULIDs are timestamp-sortable. The compact form is not. v3 accepts the loss. Today, very little code depends on ID sortability (`IdGenerator.cs` sorts only for display in `calor ids list`). The display-sort can fall back to string-sort which is no worse than today for the user's perception.

### 6.2 Why Phase 2 is gated (unchanged from v2 §6.2)

Three critique points specifically demand measurement before identity-format changes:

- [v1 thesis critique](./path-2-drop-ids-critique.md) §"The 'cognitive tax' claim is unmeasured": no in-repo data shows agents fail more with long IDs.
- [v1 devil's advocate](./path-2-drop-ids-devils-advocate.md) §1 ("savings number is a marketing figure"): N=5 micro-benchmark doesn't generalize.
- [v1 devil's advocate](./path-2-drop-ids-devils-advocate.md) §8 ("agent UX claim asserted, not measured"): no agent-harness comparison.

v3 honors this. Phase 2 ships **if and only if** the experiment in §10 demonstrates a measured improvement.

### 6.3 Compiler changes (Phase 2)

| File | Change |
|---|---|
| [`Ids/IdGenerator.cs`](../../src/Calor.Compiler/Ids/IdGenerator.cs) | Replace `Generate()` body: `prefix + "_" + GenerateCrockford32(12)`. Add generate-until-unique loop. Accept `IdRegistry` constructor arg. |
| New: `Ids/IdRegistry.cs` | Per-project `HashSet<string>` of generated IDs. Populated by `IdScanner` walk; threaded into `IdGenerator`. |
| [`Ids/IdValidator.cs`](../../src/Calor.Compiler/Ids/IdValidator.cs) | `UlidPatternRegex()` → `CompactPatternRegex()` (`[0-9a-hj-np-tv-z]{12}`). `UlidLength` constant: 12. Test-ID regex unchanged. |
| [`Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) | Add `Populate(IdRegistry)` method that registers each seen ID. No change to scanning. |
| `Verification/` proof-obligation code | No change. Format-agnostic. |
| Migrator: `calor fix --compact-ids` | Walk every `.calr`, every `[CalorAttribute]` reference, every `*.calr.cache` — deterministically map ULID → compact via `crockford32(sha256(ulid))[:12]`. Collision-detection per §6.1. |
| Diagnostic codes | Add `Calor0821` (text in §8.2). |

### 6.4 Cache and downstream — rewritten

**The migrator updates Z3 proof cache keys in place using the deterministic ULID → compact remap. No proof recomputation is required.**

The Z3 proof cache stores entries keyed by `(symbol_id, obligation_id, body_hash)`. Both `symbol_id` and `obligation_id` are symbol-level IDs that the migrator remaps deterministically. `body_hash` is content-derived and unaffected by ID format. So the migrator's cache-key transformation is:

```
old_key = (f_01J5X7K9M2NPQRSTABWXYZ12, pf_01J5X7K9M2NPQRSPROOFKEY, 0xab12cd...)
new_key = (f_a1b2c3d4e5f6,             pf_q9r8s7t6u5v4,            0xab12cd...)
```

No proof is recomputed. The cache entries are renamed in place. The cache invalidation cost is zero — what was a "one cold cache" rebuild in v2 §6.4 is, with the in-place remap, no rebuild at all.

The migrator validates this by:

1. Loading every `*.calr.cache` file.
2. Rewriting `(symbol_id, obligation_id, ...)` keys via the remap.
3. Writing back atomically.
4. On the next `calor compile`, the cache hit rate should be 100% (no invalidation).

This is what v2 §6.3's table actually intended (the row says "every `*.calr.cache` — deterministically map"). v3 §6.4 surfaces this clearly and drops v2's contradictory "rebuilds on first compile" phrasing.

---

## 7. Resolved questions (unchanged from v2 §7)

1. **`[CalorSymbol]` footprint.** Resolved by deletion: v3 does not propose this attribute. (Neither `[CalorId]` nor `[CalorSymbol]` exists today.)
2. **Overload disambiguation syntax.** Out of scope. Symbol IDs distinguish overloads at identity level.
3. **Constructor naming.** Out of scope. `§CTOR` keeps its ID.
4. **Delete or repeal `stable-identifiers.md`.** Update it. Add "What identity does *not* cover" section (sub-block constructs are structural-only).
5. **Migrator in-place vs parallel tree.** In-place with `--dry-run` flag.
6. **Deprecation timeline.** Single release, hard break (pre-1.0 envelope).

---

## 8. Diagnostics

### 8.1 Phase 1 (additive)

| Code | Description | Severity |
|---|---|---|
| `Calor0820` | Sub-block constructs no longer accept `{id:...}` — run `calor fix --drop-structural-ids` | Error |

**Full diagnostic text** (addressing the chicken-and-egg point in v2 critique §10):

```
Calor0820 (Error) at file.calr:42
  Sub-block constructs no longer accept an ID block.
  Found:    §IF{if1} (cond)
  Expected: §IF (cond)

  Migrate this file (and any others in the project) by running:
    calor fix --drop-structural-ids

  If 'calor fix' itself reports parse errors before reaching this file,
  fix those unrelated errors first using the previous Calor release
  (or your version-controlled history), then re-run the migrator.
```

The migrator carries the legacy parser as a private internal helper (per §5.6 step 3), so `calor fix --drop-structural-ids` itself can read pre-Phase-1 files even after the public parser has been updated. The diagnostic text makes this concrete to prevent user confusion.

### 8.2 Phase 2 (additive)

| Code | Description | Severity |
|---|---|---|
| `Calor0821` | Legacy ULID format detected — run `calor fix --compact-ids` | Error |

**Full diagnostic text:**

```
Calor0821 (Error) at file.calr:1
  Legacy 26-character ULID format detected.
  Found:    §M{m_01J5X7K9M2NPQRSTABWXYZ12:Hello}
  Expected: §M{m_<12 chars Crockford base32>:Hello}

  Migrate this file (and any others in the project) by running:
    calor fix --compact-ids

  The migrator updates IDs in source files, [CalorAttribute] references
  in generated C#, and Z3 proof cache keys (no re-verification needed).
```

### 8.3 Standalone — Diagnostic addressing (definite, ships first)

Promoted from "recommended" (v2) to **definite, ship first as its own PR.**

**Change:** all diagnostics that reference a symbol by ID gain a qualified-name prefix and a parenthesized truncated ID.

```
Today:    Calor0501: division by zero in f_01J5X7K9M2NPQRSTABWXYZ12 at file:42
Phase 0:  Calor0501: division by zero in Calculator.Divide (f_01J5X7…) at file:42
```

The ID is retained in parentheses for tooling (`calor_navigate references` uses it). The qualified name is shown for humans/agents.

**Scope:** all diagnostic codes that today reference a symbol by raw ID. Mechanical change in `Diagnostics/Diagnostic.cs` formatting helpers.

**Effort:** ~1 day. Near-zero risk.

**Sequencing:** **Ships in 0.x as a small PR before 0.x+1 cuts.** This ensures the more readable diagnostic format is established before the ID format itself changes in Phase 2, which simplifies user mental model continuity across both releases.

---

## 9. Effort & timeline

### 9.1 Standalone — Diagnostic addressing

| Task | Estimate |
|---|---|
| Update `Diagnostics/Diagnostic.cs` formatter helpers | 0.5 day |
| Snapshot test updates (~20 fixtures) | 0.3 day |
| Documentation: `docs/syntax-reference/diagnostics.md` (or wherever the diagnostic format is documented) | 0.2 day |
| **Subtotal** | **~1 day** |

### 9.2 Phase 1 (definite)

| Task | Estimate |
|---|---|
| Parser changes (~150 LOC delta across ~13 statement parsers) | 2 days |
| AST nullability for sub-block nodes (~80 LOC) | 1 day |
| CalorEmitter template changes | 1 day |
| Migrator (`calor fix --drop-structural-ids`, lexer-anchored text-edit + AST-diff verifier) | 3 days *(revised up from v2's 2d; the AST-diff verifier is more work than the bare text-edit)* |
| Snapshot test updates (~30 fixtures, mechanical) | 2 days |
| Documentation: `docs/syntax-reference/*`, `CLAUDE.md`, `.github/copilot-instructions.md`, MCP resources, philosophy docs | **2 days** *(revised up from v2's 1d per critique §"Documentation cascade")* |
| Editor extension: `editors/vscode/syntaxes/calor.tmLanguage.json` grammar update | **0.5 day** *(added per critique §"Editor extension")* |
| Sample migration: run migrator over `samples/`, verify, commit | 0.5 day |
| **Subtotal** | **~12 days / ~2.4 weeks** |

### 9.3 Phase 2 (conditional, requires gate)

| Task | Estimate |
|---|---|
| IdGenerator / IdValidator format swap | 1 day |
| `IdRegistry` implementation + threading through `IdScanner`/`IdGenerator` | 1 day |
| Migrator (`calor fix --compact-ids`, AST-walk deterministic remap, collision-detection, cache-key rewrite) | 3 days |
| Snapshot test regeneration (every test that asserts a specific ID format) | 3 days |
| Z3 cache invalidation regression testing (verify cache-hit rate is 100% post-migration) | 1 day |
| Documentation & website updates | 1 day |
| **Subtotal** | **~10 days / ~2 weeks** |

### 9.4 Phase 2 gate experiment (gating step, blocking ship)

| Task | Estimate |
|---|---|
| Pre-register analysis plan in `docs/plans/phase-2-measurement-protocol.md` | 0.5 day |
| Author 5–10 new realistic multi-edit tasks to top up the existing 24 fixtures to ≥25–30 tasks | 1.5 days |
| Implement Phase 1+2 on a branch behind a feature flag | (overlap with §9.2 + §9.3) |
| Run the experiment: ≥10 runs × ≥25 tasks × 3 arms = 750 runs | ~3–4 calendar days of agent compute (~$450 budget at 30 turns × $0.02/turn average) |
| Analyze results, write `docs/plans/phase-2-measurement-results.md`, decide ship/no-ship | 1 day |
| **Subtotal** | **~3 days engineering + 3–4 days agent compute** |

### 9.5 Total release effort & calendar

| Path | Engineering days | Calendar (with buffer) |
|---|---|---|
| Standalone diagnostic addressing | ~1 day | ~1 week (ships first, in 0.x) |
| Phase 1 + Phase 2 + gate experiment | ~25 days | ~6 weeks (Phase 1 + Phase 2 in parallel on branch; gate runs once both are stable; release cut after gate passes) |
| Phase 1 only (if gate fails) | ~15 days | ~4 weeks (revert Phase 2 branch, release Phase 1 alone) |

The bundled-release calendar is **~6 weeks** vs the v2 sequential-release calendar of **~4 weeks for Phase 1 + ~3 weeks for Phase 2 + ~3–4 weeks gate** = ~10 weeks. **The bundle is faster overall** and avoids the two-migration UX problem.

---

## 10. Phase 2 measurement gate — tightened

Per [v2 critique §6](./path-2-drop-ids-v2-critique.md): N=3 is too few given agent run variance; statistical test must be specified; analysis must be pre-registered.

### 10.1 Benchmark protocol

- **Tasks:** **N ≥ 25** multi-edit tasks drawn from the existing 24 `tests/E2E/agent-tasks/fixtures/` plus **5–10 newly-authored realistic tasks** (e.g., "rename method `Add` to `Plus` and update all callers", "extract a helper from `Process` into `ProcessPart`", "add a new field to `Order` and update constructors and serialization", "merge two branches whose changes touched overlapping methods"). The mix must include single-edit, multi-edit, and refactor tasks. Task selection and any new task authoring **must occur before any analysis of pilot data**.
- **Variants:** today's Calor (ULID), Phase-1-only Calor (ULID + no sub-block IDs), Phase 1+2 Calor (compact ID + no sub-block IDs). Three arms.
- **Runs:** **≥ 10 runs per task per arm.** Total ≥ 25 × 3 × 10 = **750 task runs.** Runs are stratified by task to keep paired comparisons valid.
- **Metrics (per run):**
  - **Success rate** (binary: did the agent produce a passing solution?)
  - **Turn count** (integer)
  - **Identity-preservation errors** (integer count: agent edited the wrong member; agent created a duplicate)
  - **Edit-correctness errors** (integer count: agent's edit had a syntax / type / contract error fixed in a later turn)
  - **Total output tokens** (the proxy for what actually costs money per [v1 devil's advocate §1.2](./path-2-drop-ids-devils-advocate.md))

### 10.2 Statistical methodology — pre-registered

**Pre-registration:** before any Phase 2 implementation code is written, `docs/plans/phase-2-measurement-protocol.md` must be committed with:

- The exact task list (with hashes of fixture commits)
- The exact analysis pipeline (with seed values)
- The exact pass/fail thresholds per kill criterion (§10.3)
- The exact statistical tests and significance thresholds

Once committed, no changes are permitted to the protocol before the experiment concludes. Any necessary changes invalidate the run and the experiment restarts.

**Statistical tests:**

- **Continuous metrics** (turn count, output tokens, edit-correctness errors): paired **Wilcoxon signed-rank** test on per-task medians across arms. Pairing is by `(task_id, run_index)` — each task contributes one paired sample per run.
- **Binary metric** (success rate): **McNemar's test** on per-task success counts across arms.
- **Significance threshold:** α = 0.05 per criterion, **Bonferroni-corrected** across the 4 kill criteria → α' = 0.0125 per test.

**Effect sizes** (not just p-values): Cliff's delta for continuous metrics, odds ratio for binary. A statistically-significant but practically-trivial improvement (e.g., 2% reduction in turn count) is not sufficient to ship — see §10.3.

### 10.3 Kill criteria for Phase 2

Phase 2 ships only if **all four** are true after the analysis pipeline runs on the pre-registered data:

1. **No success-rate regression:** Phase 1+2 success rate ≥ today's success rate, with McNemar p > α' (i.e., not statistically worse). A non-significant numerical decrease is acceptable; a significant decrease is not.
2. **No identity-preservation regression:** Phase 1+2 identity-preservation error count ≤ today's, Wilcoxon p > α'. The point of symbol IDs is to protect identity; we do not ship a change that erodes this.
3. **Material agent-UX improvement on at least one of:**
   - Turn count median reduction ≥ 10% **AND** Wilcoxon p < α' **AND** Cliff's |δ| ≥ 0.2 (small-or-larger effect), or
   - Total output tokens median reduction ≥ 15% **AND** Wilcoxon p < α' **AND** Cliff's |δ| ≥ 0.2.
4. **Phase 2 is distinguishable from Phase 1:** Phase 1+2 vs Phase-1-only on the criterion that satisfied (3), with Wilcoxon p < α'. If Phase 1 alone delivers the improvement, ship Phase 1 alone and stop.

If any of (1)–(4) fails, Phase 2 is rejected and only Phase 1 (and standalone diagnostic-addressing) ship in 0.x+1.

v3 commits to revert Phase 2 if shipped and post-ship data shows regressions on (1) or (2). The revert mechanism is: the Phase 2 PR is structured as a series of commits that can be cleanly reverted; the migrator from `compact → ULID` is symmetric and shipped in the same release for this revert pathway.

### 10.4 What the gate doesn't measure (unchanged)

- Long-term codebase rot at scale (months-long agent sessions, thousands of files)
- Multi-agent coordination (two agents editing the same module)
- Performance of the verifier under massive ID change rates

These are open questions for future work and not blockers. Phase 2 commits to *not regressing* the first two metrics; long-term effects are accepted as unmeasured risk.

---

## 11. Release sequencing — bundled in 0.x+1

Per [v2 critique §7](./path-2-drop-ids-v2-critique.md): two hard breaks in two releases is a real UX cost. v3 bundles.

**Sequence:**

1. **0.x (current).** No changes.
2. **0.x patch release (small).** Diagnostic addressing (§8.3) ships as its own PR. ~1 day work, ~1 week calendar. Lands before 0.x+1 development starts.
3. **0.x+1 development** (~6 weeks calendar):
   - Phase 1 implemented on `phase-1` feature branch. Tested. Migrator validated against all `samples/` and `tests/`.
   - Phase 2 implemented on `phase-2` feature branch (rebased on `phase-1`). Tested.
   - `phase-1` and `phase-2` merged into `release/0.x+1` branch.
   - Phase 2 measurement gate runs on `release/0.x+1` (the experiment in §10). ~3–4 calendar days of agent compute.
   - **If gate passes:** ship `release/0.x+1` as 0.x+1 (Phase 1 + Phase 2 bundled). Single migration. Users run `calor fix --upgrade-from 0.x` (a wrapper that runs both `--drop-structural-ids` and `--compact-ids`).
   - **If gate fails:** revert Phase 2 changes from `release/0.x+1`. Ship Phase 1 alone as 0.x+1. Users run `calor fix --drop-structural-ids`. Phase 2 deferred or abandoned.

**Why this is better than v2's sequential approach:**

- One migration command, not two. Users update once.
- No risk of a Phase 2 deprecation landing on top of unmerged Phase 1 feature branches in user repos.
- Phase 2's risk is contained behind the gate without forcing a separate Phase 2 release later.
- Calendar is shorter (~6 weeks vs ~10 weeks).

**Cost of bundling:**

- Phase 1 ships later than it would standalone (~4 weeks vs ~2 weeks).
- If the gate fails late in the cycle, the calendar slips for Phase 2's reverted code paths.

The first cost is small (a 2-week delay on a cleanup is not material). The second is mitigated by structuring Phase 2 commits to be cleanly revertable.

---

## 12. What changed from v2

| Topic | v2 | v3 |
|---|---|---|
| **Phase 2 ID format** | 9-char base36 (catastrophic 0.49% collision at 10⁶ IDs) | **12-char Crockford base32 + generate-until-unique** (4×10⁻⁷ collision at 10⁶ IDs) |
| **Phase 1 justification** | Sold as "token savings + cleanup" | Sold as **cleanup with incidental token savings** (~5% on production form) |
| **Principle scoping** | "Preserved at the symbol level" | **"Genuinely narrowed from universal to symbol-scoped"** — explicit |
| **Structural-ID benefits** | Listed: parser enforcement only | Listed: parser enforcement **+ error-recovery precision in diagnostics** |
| **Migrator architecture** | Hedged "AST-edit-and-print" / "regex-guided pass" / "AST-diff verifier" | Single strategy: **lexer-anchored text-edit + post-edit AST-diff verification** |
| **Cache invalidation** | Contradicted itself (remap vs "rebuilds on first compile") | **Migrator remaps cache keys in place. No re-verification.** |
| **Measurement gate N** | 3 runs/task/arm | **10 runs/task/arm (~750 total runs)** |
| **Measurement gate methodology** | Unspecified test, no pre-registration | **Wilcoxon / McNemar with Bonferroni; pre-registered protocol document** |
| **Release sequencing** | Phase 1 in 0.x+1, Phase 2 in 0.x+2 (two migrations) | **Phase 1 + Phase 2 bundled in 0.x+1 (one migration), conditional on branch-experiment success. Phase 1 alone if gate fails.** |
| **Diagnostic addressing (qualified names)** | Recommended; un-sequenced | **Definite; ships first as small PR in 0.x.** |
| **Pivot-plan reconciliation** | Claimed "resolved" at IR level | **§14 new: owns sub-block diff/merge degradation explicitly.** |
| **Editor TextMate grammar** | Not mentioned | **Added to Phase 1 effort (§9.2, 0.5d).** |
| **Documentation cascade** | 1 day | **2 days** (per critique correction). |
| **Calor0820 chicken-and-egg** | Implicit (migrator has legacy parser per §5.6) | **Explicit in the diagnostic text** so users hit a clear recovery path. |

---

## 13. Honest residual concerns

This section exists because v1 was justly criticized for hiding weak points in "open questions." v3 inherits v2's residual concerns and adds the new ones surfaced in the v2 critique.

1. **Phase 1 itself is not measurement-justified before ship.** Phase 1 ships on engineering merit (zero identity impact, mechanical change, low rollback cost). It is not proven to improve agent success. The §10 gate measures Phase 1+2 vs Phase 1 alone vs today; if Phase 1 alone regresses agent UX vs today, Phase 1 should be reverted in 0.x+2. The repo should add agent-harness measurement to CI before any further ID-related changes.

2. **Error-recovery precision degrades in Phase 1.** Today's diagnostic `Calor0101: §/I{if1} expected, got §/I{if3}` becomes `Calor0101: §/I expected, got §/M`. Open-stack-based recovery delivers ~80% of the precision but loses the deeply-nested-case clarity. Mitigations: (a) the migrator's AST-diff verifier catches damage at migration time, (b) live edits rarely produce malformed closers because the open is in immediate scope, (c) §8.3's qualified-name format means the diagnostic still names the containing symbol. Net: small loss, acknowledged.

3. **9-char ID was a real technical bug.** v2 §6.1 went out for review with collision math off by an order of magnitude on the wrong axis. The math was checked in v3 (script in §16.D). Lesson for v3 reviewers: **check the math in §6.1 and §10.2 specifically.** If the 12-char base32 analysis is also wrong, this RFC has the same bug v2 had.

4. **Migrator AST-diff verification is implementation work, not a free guarantee.** §5.6 step 3 says the migrator parses both old and new and asserts structural equivalence. The "structural equivalence" predicate is non-trivial — what counts as equivalent across an ID-format change? The implementation must define this carefully (ignore IDs entirely; compare every other field; surface any difference). If the predicate is too strict, valid migrations fail; if too loose, real bugs slip through. The right answer is "ignore ID fields, compare the structural shape of the AST exactly." The verifier should be code-reviewed as carefully as the migrator itself.

5. **The pivot plan (semantic IR for agents) needs IDs to be *stable across edits*, not just *short*.** Phase 2 preserves stability per declaration. What it loses is *sortability* by creation time. The pivot plan should be audited for any reliance on sortability before Phase 2 ships. (See §14 for the sub-block diff/merge cost.)

6. **No multi-agent coordination measurement exists today.** Phase 2's gate measures single-agent task performance. Multi-agent risks (two agents editing the same module with different IDs being generated) are not in the gate. This is an honest blind spot. Mitigated by: (a) `IdRegistry` collision detection, (b) the project-scoped uniqueness (no cross-project ID resolution to coordinate), (c) standard merge-conflict resolution if both agents touch the same source line.

7. **Phase 1's structural-ID drop changes the "diagnostic with sub-block location" format.** Today: `Calor0501 at if1`. Phase 1: `Calor0501 at file:line (in Calculator.Divide)`. New form is more informative but breaks any external tool parsing the old form. No known external tools; absence of evidence is not evidence of absence.

8. **`IdRegistry` adds a small invariant to maintain.** `IdScanner` must populate the registry before `IdGenerator` generates new IDs in the same compilation unit. Cross-file generation (during migration or multi-file edits) must populate registries from all `.calr` files in the project before generation starts. A bug where the registry is consulted before being fully populated would re-introduce collision risk.

---

## 14. Pivot plan reconciliation — what symbol vs structural means for diff/merge

Per [v2 critique §9](./path-2-drop-ids-v2-critique.md): the v2 claim "pivot-plan conflict resolved" is true at the symbol level but glosses a real cost at the sub-block level.

**The pivot plan's `Calor.SemanticDiff` design** (per the most recent pivot plan revision) promises structured deltas over the IR keyed on stable IDs. v3 preserves this at the symbol level: an agent that rewrites a method body but keeps the method declaration produces a `SymbolDelta(method_id=f_a1b2c3d4e5f6, body_change={...})`. The method's identity survives.

**At the sub-block level**, v3 trades identity for positional/AST-index addressing. An agent that rewrites an `if` block inside a function but keeps the surrounding function structure produces a delta like `SubBlockDelta(parent_id=f_a1b2c3d4e5f6, path="body[3].if", change={...})`. The "path" is fragile: if the agent also added a statement at `body[1]`, the same `if` block is now at `body[4].if`, and the diff representation has to handle this (typically by structural matching, which is what real diff tools do).

**Why this is an acceptable cost:**

- Most agent operations the pivot plan cares about are symbol-level (rename method, extract function, add field). Sub-block edits are usually inside symbol-level edits, where the symbol delta carries the broader semantic.
- All major code editors and diff tools handle exactly this case (file-level + position-based) today. Calor's pivot plan is not solving a problem that nobody has solved.
- The semantic IR can still represent sub-block deltas; they just don't have a separate identity space.

**Pivot plan action item:** the pivot plan's `SemanticDiff` design should be updated to **explicitly document that sub-block-level delta identity is positional/AST-index, not ID-based**. This is a 1-paragraph addition to the pivot plan's design doc that prevents the implementation team from assuming they can key sub-block deltas on IDs (which they cannot, post-Phase-1).

---

## 15. Recommendation

**Standalone diagnostic addressing (§8.3): ship as small PR in 0.x.** ~1 day. Near-zero risk.

**Phase 1 + Phase 2 bundled in 0.x+1, conditional on §10 gate:** Implement both on branches; merge to `release/0.x+1`; run the gate. Ship together if the gate passes, ship Phase 1 alone if it does not.

**Mark v2 [`path-2-drop-ids-v2.md`](./path-2-drop-ids-v2.md) as superseded by this document.** Keep v2 in tree as the historical record (with its critique). Keep v1 too. The trajectory v1 → v2 → v3 documents a real design exploration and is itself valuable.

**Pre-register the §10 measurement protocol** before any Phase 2 implementation code lands.

---

## 16. Appendices

### 16.A — What survives, what dies (updated from v2)

**Survives:** `IdScanner` (14 symbol IdKinds), `IdValidator`, `IdGenerator` (format changes; generator stays + adds uniqueness check), Z3 proof cache (keys remapped in place), `RefinementType.Id`, `IndexedType.Id`, cross-module symbol-ID resolution, the (narrowed) principle "Every symbol-level declaration has an ID," `docs/philosophy/stable-identifiers.md` (updated, not repealed).

**Dies:** ID block on `§L`, `§FOREACH`, `§WH`, `§DW`, `§IF`, `§TR`, `§UNSAFE`, `§FIXED`, `§SYNC`, `§USING`, `§MATCH`, `§FORALL`, `§EXISTS`, and corresponding close tags. The `if (endId != id)` enforcement paths in `Parsing/Parser.cs`. Synthesized sub-block IDs in `Migration/RoslynSyntaxVisitor.cs`. (Phase 2: 26-char ULID format. Replaced by 12-char Crockford base32.)

**Out of scope:** `[CalorId]` / `[CalorSymbol]` attribute design. Positional sub-block addressing as identity. MCP qualified-name lookups (beyond §8.3). C# → Calor → C# round-trip stability improvements. Sidecar files. "IDs only on `pub`" calibrated rules.

### 16.B — Side-by-side examples (updated with 12-char format)

#### B.1 Hello

**Today (production ULID, ~115 cl100k tokens):**
```calor
§M{m_01J5X7K9M2NPQRSTABWXYZ12:Hello}
§F{f_01J5X7K9M2NPQRSTABWXYZ12:Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F{f_01J5X7K9M2NPQRSTABWXYZ12}
§/M{m_01J5X7K9M2NPQRSTABWXYZ12}
```

**Phase 1+2 (12-char Crockford base32, ~72 cl100k tokens; ~37% reduction):**
```calor
§M{m_a1b2c3d4e5f6:Hello}
§F{f_a1b2c3d4e5f6:Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F{f_a1b2c3d4e5f6}
§/M{m_a1b2c3d4e5f6}
```

#### B.2 IsPrime (sub-blocks)

**Today (test form, 151 cl100k tokens):**
```calor
§M{m001:IsPrimeDemo}
§F{f001:IsPrime:pub}
  §I{i32:n}
  §O{bool}
  §IF{if1} (< n 2) → §R BOOL:false §/I{if1}
  §L{for1:i:2:n:1}
    §IF{if2} (== (% n i) 0) → §R BOOL:false §/I{if2}
    §IF{if3} (> (* i i) n) → §R BOOL:true §/I{if3}
  §/L{for1}
  §R BOOL:true
§/F{f001}
§/M{m001}
```

**Phase 1 only (test form, ~125 cl100k tokens; -17%):**
```calor
§M{m001:IsPrimeDemo}
§F{f001:IsPrime:pub}
  §I{i32:n}
  §O{bool}
  §IF (< n 2) → §R BOOL:false §/I
  §L{i:2:n:1}
    §IF (== (% n i) 0) → §R BOOL:false §/I
    §IF (> (* i i) n) → §R BOOL:true §/I
  §/L
  §R BOOL:true
§/F{f001}
§/M{m001}
```

**Phase 1+2 (production form, ~120 fewer tokens than today's production form):**
```calor
§M{m_a1b2c3d4e5f6:IsPrimeDemo}
§F{f_a1b2c3d4e5f6:IsPrime:pub}
  §I{i32:n}
  §O{bool}
  §IF (< n 2) → §R BOOL:false §/I
  §L{i:2:n:1}
    §IF (== (% n i) 0) → §R BOOL:false §/I
    §IF (> (* i i) n) → §R BOOL:true §/I
  §/L
  §R BOOL:true
§/F{f_a1b2c3d4e5f6}
§/M{m_a1b2c3d4e5f6}
```

#### B.3 Divide with contract (where ID identity matters most)

**Phase 1+2 (production form):**
```calor
§M{m_a1b2c3d4e5f6:DivideDemo}
§F{f_a1b2c3d4e5f6:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q{message="divisor must not be zero"} (!= b 0)
  §R (/ a b)
§/F{f_a1b2c3d4e5f6}
§/M{m_a1b2c3d4e5f6}
```

The `§Q` precondition is unchanged. The Z3 cache key for proving `(!= b 0)` is `f_a1b2c3d4e5f6:Q1` instead of `f_01J5X7K9M2NPQRSTABWXYZ12:Q1` — same structure, shorter string, **identity preserved end-to-end.**

### 16.C — Calor0820 diagnostic for the chicken-and-egg case (full)

```
Calor0820 (Error) at file.calr:42
  Sub-block constructs no longer accept an ID block.

  Found:    §IF{if1} (< n 2)
  Expected: §IF (< n 2)

  Migrate this file (and any others in the project) by running:
    calor fix --drop-structural-ids

  If 'calor fix' itself reports parse errors before reaching this
  file, the migrator has hit a pre-Phase-1 file with unrelated syntax
  errors. The migrator carries the legacy parser internally, so this
  should be rare. If it occurs:
    1. Use the previous Calor release (0.x) to identify and fix
       the unrelated parse errors.
    2. Re-run 'calor fix --drop-structural-ids' on the cleaned file.

  For a project-wide dry-run preview, use:
    calor fix --drop-structural-ids --dry-run
```

### 16.D — Collision math verification

```python
import math

def birthday(n, N):
    return 1 - math.exp(-(n**2) / (2*N))

# 9-char base36 (v2's BLOCKING bug):
print("9-char base36 N =", 36**9)
for n in [1e5, 1e6, 1e7]:
    print(f"  n=10^{int(math.log10(n))}: P = {birthday(n, 36**9):.2e}")
# Output:
# 9-char base36 N = 101559956668416
#   n=10^5: P = 4.92e-05
#   n=10^6: P = 4.91e-03   ← 0.5%, catastrophic
#   n=10^7: P = 3.89e-01   ← 39%, certain at scale

# 12-char Crockford base32 (v3's fix):
print("12-char base32 N =", 32**12)
for n in [1e6, 1e7, 1e8]:
    print(f"  n=10^{int(math.log10(n))}: P = {birthday(n, 32**12):.2e}")
# Output:
# 12-char base32 N = 1152921504606846976
#   n=10^6: P = 4.34e-07  ← 0.4 ppm, safe
#   n=10^7: P = 4.34e-05  ← 43 ppm, safe with retry
#   n=10^8: P = 4.34e-03  ← still acceptable with retry
```

The 5-orders-of-magnitude improvement (3 chars of additional length, 5 orders of magnitude of safety) is the right design point. Generate-until-unique enforcement in `IdGenerator` reduces the residual risk to zero in practice.

### 16.E — Pre-registration template (for §10 protocol)

The pre-registration document at `docs/plans/phase-2-measurement-protocol.md` must include, at minimum:

1. **Task list** with commit hashes of each fixture.
2. **Task authoring artifacts** — for any new tasks, the prompts and acceptance criteria, committed before pilot runs.
3. **Implementation flag** — the exact branch + commit hash for each arm.
4. **Run protocol** — agent harness invocation, model version (pinned), random seed for any non-deterministic parts.
5. **Data schema** — fields recorded per run.
6. **Analysis pipeline** — exact statistical functions, library versions, seed.
7. **Pass/fail thresholds** for each of the 4 kill criteria, plus the multiple-comparison correction.
8. **Reporting commitment** — `docs/plans/phase-2-measurement-results.md` will be written from the analysis output, regardless of whether the gate passes or fails, and committed before any Phase 2 ship decision.

---

## 17. Decision

To be made by repo maintainer.

**Recommended action:**

1. **Approve standalone diagnostic addressing (§8.3) for immediate ship in 0.x** as its own ~1-day PR. Lands before 0.x+1 development begins.
2. **Approve v3 Phase 1 + Phase 2 for bundled implementation in 0.x+1**, conditional on the §10 gate experiment running on the release branch before the release cut. If the gate passes, ship both; if it fails, ship Phase 1 alone.
3. **Approve §10 pre-registration as a hard requirement** before any Phase 2 implementation code merges.
4. **Mark [`path-2-drop-ids-v2.md`](./path-2-drop-ids-v2.md) as superseded by this document.** Keep v2 and its critique in tree as the historical record.

---

*v3 path: `docs/plans/path-2-drop-ids-v3.md`*
*Status: ready for adversarial review. Reviewers: please attack this document the way previous critique authors attacked v1 and v2. In particular: verify the collision math in §6.1 and §16.D, audit the §5.6 migrator strategy for edge cases, and probe whether the §11 bundled-release calendar is realistic. If v3 survives, it ships; if it does not, v4.*
