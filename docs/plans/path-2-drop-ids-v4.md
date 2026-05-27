# RFC v4: Compact Stable Identifiers — Drop Structural IDs, Compact Symbol IDs

**Status:** Draft (v4 — supersedes [`path-2-drop-ids-v3.md`](./path-2-drop-ids-v3.md))
**Author:** TBD (with Copilot CLI)
**Created:** 2026-05-25
**Supersedes:** v3 (2026-05-22), v2 (2026-05-22), v1 (2025-11-25)
**Reviewed against:** [`path-2-drop-ids-v3-critique.md`](./path-2-drop-ids-v3-critique.md), [`path-2-drop-ids-v3-devils-advocate.md`](./path-2-drop-ids-v3-devils-advocate.md) — plus carry-over from earlier rounds
**Target release:** Pre-1.0 single hard break (0.x → 0.x+1) — Phase 1 + Phase 2 bundled, conditional on the §10 gate experiment running on a branch with a week-of-buffer before the release cut.

---

## 0. Why v4 exists

v3 was approved-with-fixes by both v3 reviewers — the designer-voice critique asked for 7 inline corrections; the devil's-advocate audit identified **2 hard requirements** (one factual, one spec) plus 6 calibration improvements. The hard requirements were too consequential to be patched at implementation time, so v4 incorporates them into the spec itself.

| v3 reviewer finding | v4 response |
|---|---|
| **HARD (devil's advocate §2): §6.4's cache-invalidation story is fictional.** The Z3 cache is content-addressed via SHA-256 of `(parameters, contract expression)`; it contains **no symbol IDs, no obligation IDs**, and there are **zero `*.calr.cache` files** in the repo. v3 inherited the error from v2 and added fabricated detail on top. | **§6.4 rewritten** to match reality (verified against [`src/Calor.Compiler/Verification/Z3/Cache/`](../../src/Calor.Compiler/Verification/Z3/Cache/)). The cache is unaffected by ID format changes. No migrator code needed for cache; no remap walk; no `*.calr.cache` files. The §10 table row about cache remapping is **deleted**. |
| **HARD (devil's advocate §3): AST-diff verifier predicate is undefined.** The migrator's entire safety argument depends on this predicate. v3 ships the safety story before defining the predicate. | **New §5.7 "AST-diff verifier specification"** enumerates ignored node kinds, trivia reattachment rules, ID references inside diagnostic text, collection-order semantics, and per-file vs per-project failure granularity. Defined before any migrator code. |
| **HARD (devil's advocate §5): Cliff's δ ≥ 0.2 is the "small effect" mark — too weak for a one-way door change.** The principle narrowing is a one-way door; a small-effect ship bar is the wrong trade. | **§10.3 raised to Cliff's δ ≥ 0.33** (medium effect). Rationale documented inline. |
| **HARD (devil's advocate §4): 6-week bundled calendar has no buffer for late gate failure.** | **§11 calendar rewritten**: gate moves from week 6 → week 4. Weeks 5–6 are reserved for buffer/contingency. Budget treats gate as a measurement instrument that may need re-running. |
| **HARD (devil's advocate §6): `IdRegistry` thread-safety and persistence unaddressed.** | **New §6.3.1** covers concurrency model (lock-per-project with `ConcurrentDictionary`), persistence semantics (rebuilt per compile, no cross-session persistence), and the generate-until-unique loop's behavior under concurrent generation. |
| **HARD (devil's advocate §7): Phase 1 ships unmeasured even by Phase 2's own evidentiary standard.** | **§10.6 new**: post-ship Phase 1 monitoring clause. Within 30 days of 0.x+1 ship, turn-counts on the agent-task corpus compared against the latest pre-release tag; Cliff's |δ| ≥ 0.2 regression triggers a Phase 1 revert. |
| **HARD (devil's advocate §8): Pre-registration retry budget missing.** | **§10.5 new**: protocol-violation retry policy. One retry covered by $900 reserve; further retries require explicit re-pre-registration. |
| **(devil's advocate §9): "the existing lexer" in §5.6 step 1 is ambiguous** between pre-Phase-1 and post-Phase-1 lexers. | **§5.6 step 1 disambiguated**: the migrator uses the post-Phase-1 lexer, which is required to accept legacy `{id:...}` attributes on sub-block constructs and discard them with a non-fatal warning. The lexer requirement is added to §5.4 (grammar / lexer change). |
| **(designer critique §1): Reverse migrator asserted but not specified.** §10.3 says "symmetric" but the forward migrator is a one-way SHA-256-truncated hash. | **§6.3 amended**: forward migrator writes `migration.log.json` with `(compact_id, original_ulid, timestamp)` triples. `calor fix --revert-compact-ids` consumes the log. New repos without the log get a clear error pointing at git-history-based revert. |
| **(designer critique §2): `IdRegistry` population ordering under-specified.** | **§6.3 amended**: `IdScanner` runs a full project pre-pass before any `IdGenerator.Generate()` call. Migration is single-threaded. |
| **(designer critique §3): Gate budget understates real cost ($450 is optimistic).** | **§9.4 honest range**: $500–$3,000 depending on model selection and per-task turn distribution. Budget the high end. Explicit per-model breakout. |
| **(designer critique §4): Token savings claim still unverified.** | **§16.F new**: tiktoken `cl100k_base` measurements on 5 real ULIDs and 5 real compact IDs. **Verified result**: ULIDs are 16–26 tokens (not "~28"), compacts are 13 tokens, savings ~10 tokens per occurrence (not "~16"). **§1 headline corrected from "~33%" to "~20–25%" project-wide reduction on production-ULID projection.** |
| **(designer critique §5): §14 dismisses sub-block diff cost as "what real diff tools do".** | **§14 last paragraph rewritten** to surface that structural matching at the sub-block level is a weeks-to-months implementation cost for the pivot plan. Explicit action item: pivot plan must either spec the algorithm or constrain `SemanticDiff` to symbol-level deltas only. |
| **(designer critique §6.1): `MaxAttempts` in `IdGenerator` retry loop unspecified.** | **§6.3 amended**: `MaxAttempts = 100`. Justified: at the 12-char base32 collision rate, 100 retries is >60 orders of magnitude of safety. |
| **(designer critique §6.2): Already-migrated detection in Phase 1 migrator unspecified.** | **§5.6 amended**: migrator tries new parser first; if it succeeds, file is already migrated, skip. Tries legacy parser only on new-parser failure. |

v4 also acknowledges what both v3 reviewers explicitly approved and should not change: symbol/structural split, 12-char base32 + generate-until-unique math, lexer-anchored text-edit migrator strategy, principle narrowing being called by name, pre-registered measurement, bundled release sequencing, and the diagnostic-addressing first-PR.

---

## 1. Summary

**One release. Two changes. One gate. One standalone-first improvement.**

**Standalone — Diagnostic addressing (definite, ship first).**
~1 day PR. Diagnostic format becomes `Calor0501: division by zero in Calculator.Divide (f_01J5X7…) at file:42`. The ID stays in parentheses for tools; the qualified name is in front for humans/agents. No grammar change, no migration. **Ships in the 0.x patch release before Phase 1.**

**Phase 1 — Drop structural IDs (definite).**
Remove the ID block from sub-block constructs only. Symbol IDs are unchanged. **Engineering justification, not token justification** — Phase 1 saves only a few percent on production code where structural IDs are already short (`if1`, `for1`), and 0% on programs without sub-blocks. It ships because the parser path simplifies, the migrator is mechanical, and the structural ID was never load-bearing. Effort: ~2 weeks. **Post-ship monitoring (§10.6) measures Phase 1's effect.**

**Phase 2 — Compact symbol IDs (gated).**
Replace 28-character ULIDs with **12-character Crockford base32** IDs (`m_a1b2c3d4e5f6`). Symbol-level identity model is preserved end-to-end. **Verified savings (§16.F): ~10 cl100k tokens per ID occurrence, ~44% reduction per ID, ~20–25% project-wide on production-ULID projection.** (v3's "~33%" was extrapolated from an incorrect "~16 tokens/ID" figure.) Effort: ~1.5 weeks. **Ships only if** the agent-harness experiment in §10 demonstrates a measurable agent-success improvement at **Cliff's δ ≥ 0.33** (medium effect — the one-way-door threshold).

**Release sequencing (§11): Phase 1 and Phase 2 ship together in 0.x+1, or Phase 1 alone if the gate fails. Gate runs in week 4 of an 8-week calendar with 2-week contingency buffer.**

**Principle narrowing acknowledged (§3):** v4 (continuing v3) narrows design principle #3 from "Everything has an ID" (universal) to "Every symbol-level declaration has an ID" (scoped). Sub-block constructs are addressable by structural position but not by identity. This is a change. We own it.

---

## 2. Motivation

### 2.1 Evidence accounting

The v1 Phase 0 benchmark measured 5 small tasks in *test-form* Calor (`f001`, `m001`), not production ULIDs. v2's re-measurement projected production-form savings. **v4's measured tokenization (§16.F) corrects v3's per-occurrence estimate from "~16 tokens" to "~10 tokens" and the headline "~33%" project-wide reduction to "~20–25%."** This is still meaningful, but smaller than earlier RFCs claimed.

Production *structural* IDs are short (`if1`, `for1`) — Phase 1 alone delivers ~5% on production form, concentrated on sub-block-heavy tasks. Phase 2 (the ULID compaction) is the larger token win.

**v4's position:** Phase 1 ships for cleanliness with post-ship monitoring (§10.6); Phase 2 ships for verified tokens with pre-registered measurement (§10).

### 2.2 The two populations are different

| Population | Tracked by `IdScanner` | Used as identity-bearing reference | Used in round-trip | Production token cost per occurrence |
|---|:---:|:---:|:---:|---|
| **Symbol IDs** on 14 declaration kinds (Module / Function / Class / Interface / Property / Method / Constructor / Enum / EnumExtension / OperatorOverload / Indexer / RefinementType / ProofObligation / IndexedType) | yes | yes | yes (planned, not implemented) | **16–26 cl100k tokens** (verified, §16.F) |
| **Structural IDs** on `§L`, `§IF`, `§WH`, `§DW`, `§TR`, `§FOREACH`, etc. | **no** | no | no | ~2–3 tokens (synthesizer uses short names like `if1`, `for1`) |

Confirmed:
- [`src/Calor.Compiler/Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) — empty `Visit(ForStatementNode)`, `Visit(IfStatementNode)`, etc.
- [`src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs`](../../src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs) and [`VerificationCache.cs:317`](../../src/Calor.Compiler/Verification/Z3/Cache/VerificationCache.cs#L317) — **cache is content-addressed via SHA-256, contains no IDs** (the v3 claim was wrong).
- The obligation-tracking `ProofObligationNode.Id` exists for in-process tracking inside the binder but **is not used as a cache key**. The Phase 2 ID format change does not affect the cache regardless.

### 2.3 What each population is worth

**Symbol IDs deliver:**

- Rename safety (canonical ID survives `Calculate → Compute`)
- Round-trip identity (designed but not yet implemented — see §2.4)
- IR substrate for diff / merge / coordination / memory (the pivot-plan requirement, **at the symbol level only** — see §14)
- Unambiguous diagnostic addressing across edits

**Symbol IDs do NOT deliver** (correcting v2/v3):

- Z3 proof-cache stability. The cache is content-hashed on `(parameters, expression)`. Symbol IDs are irrelevant to cache hits/misses today.

**Structural IDs deliver:**

- Matched open/close enforcement in the parser
- Error-recovery precision in parser diagnostics: when a closer doesn't match, the diagnostic can name which open it should have matched

The first list (symbol IDs) is the project's competitive moat. The structural list is small but not zero — §13.2 owns the diagnostic-precision cost.

### 2.4 Round-trip honesty (unchanged from v3 §2.4)

Neither `[CalorId]` nor `[CalorSymbol]` exists in source today. The C# emitter today emits `[CalorAttribute]` (a different, generic attribute) for code-as-data preservation, not for ID round-trip. **Round-trip today is signature-based, not ID-based.** A future round-trip-stability implementation using IDs is a separate RFC.

---

## 3. Reframed thesis (and the principle narrowing called by name)

Unchanged from v3:

> **Design principle #3 narrows.** [`docs/philosophy/design-principles.md`](../philosophy/design-principles.md) §3 stated "Everything has an ID" as a universal property of Calor constructs. v4 narrows this to **"Every symbol-level declaration has an ID."** Sub-block constructs (loops, ifs, while, do-while, try, foreach, match, forall, exists, unsafe, fixed, sync, using) are addressable by structural position but not by identity. **This is a genuine narrowing of the original principle. We do not claim it is "preserved."** We claim it is correctly scoped to the population where it pays its weight, and the broader claim never had any cost-benefit justification in the first place (`IdScanner` already ignored sub-block IDs — they were never canonical).

Required documentation updates:
- [`docs/philosophy/design-principles.md`](../philosophy/design-principles.md) §3: replace universal statement with scoped statement.
- [`docs/philosophy/stable-identifiers.md`](../philosophy/stable-identifiers.md): add a "What identity does *not* cover" section listing sub-block constructs as out of scope.

Diagnostics may use qualified names + positional paths for *addressing* (a presentation concern); cross-edit references continue to use symbol IDs for *identity* (a correctness concern).

---

## 4. Goals & non-goals

### Goals

- **G1.** Reduce token cost of Calor source by a measurable amount with no semantic change and no identity-model degradation on production-ULID code.
- **G2.** Phase 1: simplify parser code paths and remove a non-load-bearing construct.
- **G3.** Phase 2: reduce production-form ID token cost by ~20–25% project-wide (verified per §16.F) **only if** the §10 gate measurement clears at the medium-effect threshold.
- **G4.** Do not break the IR / diff / merge / coordination / memory substrate at the symbol level. Sub-block-level diff/merge costs surfaced in §14.
- **G5.** Do not introduce positional fragility for identity-bearing uses (cross-edit refs). Positional addresses may appear in *presentation* uses (diagnostics) where staleness is already accepted.
- **G6.** Single-release hard break. **Bundle Phase 1 + Phase 2 in 0.x+1** if the gate passes; ship Phase 1 alone if not.
- **G7.** Phase 2 ID format must be collision-safe up to 10⁷ project-scoped IDs with margin. (Met: 12-char Crockford base32 + generate-until-unique.)
- **G8.** **Post-ship monitoring for Phase 1** (§10.6) so that even un-gated changes have an evidentiary check.

### Non-goals

- **NG1.** No semantic / type-system / contract / effects / runtime changes.
- **NG2.** No C# round-trip attribute design (separate future RFC).
- **NG3.** No change to `IdScanner`, `IdValidator`, `IdGenerator` semantic API in Phase 1; Phase 2 changes `IdGenerator` output format + adds uniqueness enforcement + introduces `IdRegistry`.
- **NG4.** No MCP tool surface changes beyond grammar/format consequences.
- **NG5.** No sidecar files, no "IDs only on pub", no qualified-name-only addressing scheme.
- **NG6.** No multi-agent ID coordination (single-process generation with retry-on-collision is sufficient at project scope).
- **NG7.** **No claim that Z3 cache invalidation is part of this RFC.** The cache is content-addressed and unaffected.

---

## 5. Phase 1 — Drop structural IDs

### 5.1 Scope (unchanged from v3 §5.1)

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

### 5.2 Symbol IDs unchanged in Phase 1

Every symbol-level declaration retains its ULID in Phase 1. Format changes only in Phase 2, gated.

### 5.3 Why Phase 1 ships (cleanup framing, unchanged from v3)

Phase 1 is a cleanup. The token savings is small on real production code. We ship it because:

1. Parser code paths simplify (~150 LOC deleted across 13 statement parsers).
2. The structural ID was never load-bearing — `IdScanner` ignored it, the C# emitter ignored it, round-trip ignored it.
3. The migrator is mechanical and low-risk (§5.6 + §5.7).
4. The grammar becomes more readable for the agent.
5. Token savings is incidental (~5% on production form), not the justification.

**Phase 1 is monitored post-ship per §10.6** so that the "ship on engineering merit" position is backed by evidence within 30 days.

### 5.4 Grammar and lexer change

Grammar diff unchanged from v3 §5.4. **Lexer requirement added per devil's advocate §9:**

> The post-Phase-1 lexer MUST accept legacy `{id:...}` attribute blocks on sub-block constructs (treating them as discardable trivia with a non-fatal `Calor0820W` warning at lex-time, escalating to a `Calor0820` error at parse-time). This lets the migrator drive its edit using a single lexer, eliminates a parallel "legacy lexer" maintenance burden, and ensures every read path is identical between migration tooling and the user-facing compiler.

The two-stage diagnostic discipline (lexer warning → parser error) means: the migrator never sees the user-facing error (it runs lexer-only for its edit phase); end-users who skipped the migrator see the parser error with the recovery guidance from §8.1.

### 5.5 Compiler changes (Phase 1)

| File | Lines (today) | Lines changed | Nature of change |
|---|---:|---:|---|
| [`Parsing/Lexer.cs`](../../src/Calor.Compiler/Parsing/Lexer.cs) | ~1,400 | ~30 | Accept-and-mark legacy sub-block `{id}` attributes; emit `Calor0820W` warning token. |
| [`Parsing/Parser.cs`](../../src/Calor.Compiler/Parsing/Parser.cs) | ~8,900 | ~150 | Sub-block parsers lose ID extraction + match enforcement. When the lexer-warning token is present, parser converts to `Calor0820` error with recovery text. |
| [`Ast/`](../../src/Calor.Compiler/Ast/) sub-block nodes | n/a | ~80 | `Id` becomes nullable; constructor signatures lose unused `id` parameter; AST-printer falls back to `<anon>`. |
| [`CodeGen/CSharpEmitter.cs`](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs) | ~4,600 | ~20 | Sub-block IDs were never emitted into generated C#. Minor cleanup. |
| [`Migration/CalorEmitter.cs`](../../src/Calor.Compiler/Migration/CalorEmitter.cs) | ~2,800 | ~40 | Reverse emitter writes `§L{for1:i:1:10:1}` today → `§L{i:1:10:1}`. |
| [`Migration/RoslynSyntaxVisitor.cs`](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs) | ~6,500 | ~5 | Stop synthesizing sub-block IDs. |
| [`Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) | ~330 | 0 | No change. |
| [`Diagnostics/Diagnostic.cs`](../../src/Calor.Compiler/Diagnostics/Diagnostic.cs) | — | ~15 | Add `Calor0820` (error) + `Calor0820W` (warning); full text in §8.1. |
| `editors/vscode/syntaxes/calor.tmLanguage.json` | — | ~30 | TextMate grammar updated. |

**Total new code: ~330 lines. Modified code: ~155 lines. Deleted code: ~50 lines.**

### 5.6 Migrator (Phase 1) — strategy + workflow

**Strategy: lexer-anchored text-edit with post-edit AST-diff verification.** (Verifier predicate fully specified in §5.7.)

1. **Pre-flight: already-migrated detection.** Try parsing the source with the **post-Phase-1 parser** (strict mode — `Calor0820` becomes an error if it would have fired). If parsing succeeds and emits zero `Calor0820`/`Calor0820W`, the file is already migrated — skip with no edits and no log entry.
2. **Tokenize using the post-Phase-1 lexer** (the same single lexer that the production compiler uses, with legacy-tolerance per §5.4). It correctly handles strings, comments, escapes, and `\r\n` line endings. It identifies every `§L{...}`, `§/L{...}`, `§IF{...}`, etc. opener/closer with its byte range and emits `Calor0820W` markers for the discardable parts.
3. **Text-edit the source.** For each `Calor0820W` marker, surgically remove only the `{id...}` block characters using the byte ranges. **All other source bytes are preserved exactly** — comments, blank lines, whitespace, BOMs, line endings.
4. **Verify post-edit using the §5.7 predicate.** Parse the edited source with the post-Phase-1 parser. Parse the original source with the post-Phase-1 parser in legacy-tolerant mode. Walk both ASTs and apply the §5.7 predicate. If verification fails, abort the write-back, restore the original, and record the file in the migrator's failure report.

**Behavior guarantees:**

- **Comment preservation:** complete (text edit, no AST re-emission).
- **Formatting preservation:** complete.
- **String/comment content safety:** complete (lexer handles them).
- **`\r\n` safety:** complete (lexer is line-ending agnostic).
- **Idempotence:** files without structural IDs pass through unchanged (already-migrated detection in step 1).
- **Atomic correctness:** verification proves no semantic damage beyond the intended drop. If verification fails, *no file* is written for the entire project (per-project failure granularity — see §5.7.5).

**CLI surface:**

```
calor fix --drop-structural-ids [--dry-run] [--verify-only] [--continue-on-failure] [path]
```

- `--dry-run`: shows diffs without writing.
- `--verify-only`: runs verification against in-place source. Useful for CI.
- `--continue-on-failure`: weakens to per-file failure granularity (skip-failing-files mode). Default is per-project (any failure aborts the whole run).
- `path`: defaults to CWD recursively.

### 5.7 AST-diff verifier specification (new in v4)

**This section answers devil's advocate §3, the second hard requirement.** The verifier predicate is the migrator's safety guarantee; it must exist and be reviewed before any migrator code lands.

#### 5.7.1 Predicate signature

```
verify(original_ast: AstNode, migrated_ast: AstNode) -> VerificationResult
```

`VerificationResult` is either `Ok` or `Failed(path, original_subtree, migrated_subtree, reason)` where `path` is the structural path through the AST to the first detected difference.

#### 5.7.2 Equivalence rules — enumeration

The predicate considers two AST trees **equivalent** if-and-only-if all of the following hold:

| Aspect | Rule |
|---|---|
| **Node kind** | Every node kind must match exactly. Adding/removing a node fails verification. |
| **Symbol IDs** | All symbol-level `Id` fields (Module, Function, Class, etc.) must match exactly. Migration MUST NOT modify symbol IDs. |
| **Structural IDs** | Sub-block `Id` fields are **ignored** (target of migration). |
| **Identifiers** (parameter names, binding names, type names, etc.) | Must match exactly. Migration MUST NOT rename anything. |
| **Literal values** | Must match exactly (including string contents byte-for-byte). |
| **Expression structure** | Must match exactly (operator, operand structure, precedence). |
| **Statement order within a block** | Must match exactly (ordered comparison). |
| **Trivia / whitespace / comments** | **Ignored at the AST level** but verified at the source level (see 5.7.3). |
| **Attribute order** | Ordered for source attributes (`[Attr1] [Attr2]` ≠ `[Attr2] [Attr1]`). |
| **Effect declarations** (`§E{cw,db}`) | Order-insensitive set comparison. |
| **Diagnostic-suppression comments** | Treated as trivia. Source-level position validated (see 5.7.3). |

#### 5.7.3 Trivia reattachment verification

Comments and pragma-like markers may bind to AST nodes through anchor-based parsing. The verifier checks:

- Every comment in the original source is present in the migrated source (text + position-relative-to-nearest-AST-node-anchor).
- The "nearest AST-node-anchor" computation must yield the same anchor for each comment before and after migration. (If the anchor changed because the migration dropped the structural ID that the comment was attached to, this is a real bug — the comment now binds to a different containing construct.)

Implementation: trivia is collected during lex; each comment is paired with its anchor node by the parser's existing trivia-attachment logic; the verifier compares paired-anchor identity across the two ASTs.

#### 5.7.4 ID references inside diagnostic text and proof annotations

Some `§Q`, `§S`, `§INV` forms accept message arguments that may contain string literals referencing structural IDs (e.g., `§Q{message="invariant violated at if1"} ...`). The migrator **MUST NOT** modify string literals. The verifier checks string-literal byte-equality regardless of whether the literal happens to contain text that looks like an ID.

#### 5.7.5 Failure semantics

- **Per-file failure detection.** Each file is verified independently. A failure records the file path, the structural-path through the AST to the first divergence, both subtrees serialized, and a human-readable reason.
- **Per-project failure granularity (default).** If ANY file fails verification, NO files are written. The migrator emits a failure report listing every failed file. This prevents shipping a half-migrated codebase.
- **`--continue-on-failure` override.** Operator can elect per-file granularity, in which case verified-successful files are written and failed files are skipped (and reported).
- **No partial files.** A file is either fully written (verified) or fully untouched. The migrator uses atomic-rename (write to `.tmp.{pid}` then rename) for crash safety.

#### 5.7.6 Test plan for the verifier itself

The verifier is itself code that requires testing. Before the migrator is shipped:

- **Positive cases:** unit tests where the migrator's edit is correct; verifier must return `Ok`.
- **Negative cases:** synthetic mutations (rename a binding, swap two statements, change a literal value) that the verifier MUST catch.
- **Pathological cases:** files with comments densely attached to sub-block IDs; files with `§Q{message="..."}` containing strings that look like IDs; files with `\r\n` line endings interleaved.
- **Coverage requirement:** every clause in §5.7.2 must have at least one positive and one negative test.

This test plan is part of the Phase 1 implementation deliverable, not a separate document.

### 5.8 Phase 1 deprecation strategy (unchanged from v3 §5.7)

Hard break, single release. Pre-1.0 envelope per [`CLAUDE.md`](../../CLAUDE.md). 0.x: legacy form accepted (current). 0.x+1: legacy form rejected with `Calor0820`; migrator ships in the same release. No dual-mode parser. No multi-release window.

---

## 6. Phase 2 — Compact symbol IDs (gated)

### 6.1 What changes — corrected collision math (unchanged from v3, math re-verified)

**Format: 12-character Crockford base32**, `[0-9A-HJ-NP-TV-Z]` (excludes `I`, `L`, `O`, `U` for disambiguation). All-lowercase in source; matched case-insensitively in tooling.

| Element | Today (28 chars after prefix) | Phase 2 (12 chars after prefix) | Verified token savings (§16.F) |
|---|---|---|---|
| Function | `f_01J5X7K9M2NPQRSTABWXYZ12` (16–26 tok) | `f_a1b2c3d4e5f6` (13 tok) | **~10 tokens per occurrence** |
| Module | `m_…` | `m_…` | ~10 |
| Class | `c_…` | `c_…` | ~10 |

**Collision math (re-verified by §16.D Python script, unchanged from v3):**

| IDs (n) | 9-char base36 (v2's BUG) | 12-char base32 (v3/v4 fix) |
|---|---|---|
| 10⁵ | ≈ 5×10⁻⁵ | ≈ 4×10⁻⁹ |
| 10⁶ | ≈ 0.49% ❌ | ≈ 4×10⁻⁷ ✅ |
| 10⁷ | ≈ 39% ❌ | ≈ 4×10⁻⁵ ✅ |
| 10⁸ | — | ≈ 4×10⁻³ ✅ |

Plus `IdRegistry`-backed generate-until-unique. Defense-in-depth.

**Sortability:** ULIDs are timestamp-sortable; compact form is not. Accepted loss (display-only effect; `IdGenerator.cs` sorts only for `calor ids list`).

**Migration remap:** deterministic via `crockford32(sha256(ulid))[:12]`. The migrator detects collisions in the remap and resolves by `crockford32(sha256(ulid + ":1"))[:12]` for the second colliding ULID, `:2` for a third, etc. **Every remap is logged** to `migration.log.json` (see §6.3) for revert support.

### 6.2 Why Phase 2 is gated (unchanged from v3 §6.2)

Per v1 thesis critique, v1 devil's advocate §1 and §8: agent-UX impact must be measured before a one-way principle change ships. v4 honors this with the §10 protocol.

### 6.3 Compiler changes (Phase 2)

| File / new module | Change |
|---|---|
| [`Ids/IdGenerator.cs`](../../src/Calor.Compiler/Ids/IdGenerator.cs) | Replace `Generate()` body: `prefix + "_" + GenerateCrockford32(12)`. Add generate-until-unique loop with **`MaxAttempts = 100`**. Constructor takes `IdRegistry`. Throws `IdSpaceExhaustedException` if 100 retries fail (mathematically unreachable at any realistic scale; included for invariant correctness). |
| **New: `Ids/IdRegistry.cs`** | Per-project `ConcurrentDictionary<string, ReservationToken>` of generated IDs. Detailed concurrency + persistence model in §6.3.1. |
| [`Ids/IdValidator.cs`](../../src/Calor.Compiler/Ids/IdValidator.cs) | `UlidPatternRegex()` → `CompactPatternRegex()` (`[0-9a-hj-np-tv-z]{12}`, case-insensitive). `UlidLength` constant: 12. Test-ID regex unchanged. |
| [`Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) | Add `Populate(IdRegistry)` method. **Invariant: `Populate()` must complete a full project pass before any `IdGenerator.Generate()` call.** |
| `Verification/` proof-obligation code | **NO CHANGE.** The cache is content-addressed; ID format is irrelevant. (v2/v3 mis-described this.) |
| Migrator: `calor fix --compact-ids` | Single-threaded. Walks every `.calr`, every `[CalorAttribute]` reference, deterministically maps ULID → compact via `crockford32(sha256(ulid))[:12]`, resolves collisions per §6.1, **writes `migration.log.json`** with `{compact_id, original_ulid, file, line, column, timestamp}` quintuples. |
| Reverse migrator: `calor fix --revert-compact-ids` | Reads `migration.log.json`. Walks every `.calr` + every `[CalorAttribute]` reference. Replaces compact IDs with original ULIDs. **If `migration.log.json` is missing**, fails with `Calor0822` error pointing the user at git-history-based revert. |
| Diagnostic codes | Add `Calor0821` (text in §8.2). Add `Calor0822` (text in §8.2.1: "Reverse migration impossible without migration.log.json"). |

#### 6.3.1 `IdRegistry` concurrency + persistence

**Concurrency model: per-project lock-free with CAS retry on the generate-until-unique loop.**

```csharp
public sealed class IdRegistry {
    private readonly ConcurrentDictionary<string, byte> _ids = new();

    public bool TryReserve(string id) => _ids.TryAdd(id, 0);  // atomic
    public bool Contains(string id) => _ids.ContainsKey(id);
}

// IdGenerator.Generate(prefix):
for (int attempt = 0; attempt < MaxAttempts; attempt++) {
    var candidate = prefix + "_" + GenerateCrockford32(12);
    if (_registry.TryReserve(candidate)) return candidate;
}
throw new IdSpaceExhaustedException(prefix);
```

Two threads racing to allocate IDs both call `TryReserve`. At most one succeeds; the loser regenerates. At the 10⁶-scale collision rate (4×10⁻⁷), expected retries per allocation is < 1 in 2 million; the loop is bounded.

**Population timing.**

- **Compile-time:** `IdScanner` runs a full project pre-pass and calls `_registry.TryReserve(id)` for every seen ID. Only after the pre-pass completes does the binder begin and `IdGenerator.Generate()` calls become valid. This invariant is enforced by the compiler pipeline (the binder constructs its `IdGenerator` from a populated `IdRegistry`).
- **Migration-time:** `calor fix --compact-ids` runs **single-threaded** (no parallel file processing) so collision-detection is deterministic. The registry is built from the deterministic remap output before any source files are rewritten. This is the single-threaded rule the v3 critique demanded; it also keeps `migration.log.json` write-order deterministic.

**Persistence: NONE.** The registry is rebuilt per compile by `IdScanner`'s full project pre-pass. IDs are persisted in source (as part of declarations) and in `[CalorAttribute]` references in generated C#, not in any side-file. This means:

- No on-disk manifest. No cross-session locking concerns. No two-compile-sessions-modifying-the-same-manifest scenario.
- The registry is in-memory state of a single `calor` invocation.
- Stability across compiles is provided by source itself: a declaration's ID is in the `.calr` file; the same `.calr` file produces the same registry on every compile.

**Parallel `dotnet build`:** independent project compilations each have their own `IdRegistry`. Cross-project ID references resolve symbolically (by qualified name + ID at reference time), not via shared registry state.

### 6.4 Cache and downstream — REWRITTEN (factual correction)

**The Z3 proof cache is content-addressed on the contract expression and parameter list. Calor IDs do not appear in either, so the migration does not invalidate any cache entries.**

That is the entire correct statement. v3's claim that the migrator walks `*.calr.cache` files remapping `(symbol_id, obligation_id, body_hash)` keys was fictional and inherited from v2. Verification:

- [`Verification/Z3/Cache/ContractHasher.cs`](../../src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs): `HashPrecondition` and `HashPostcondition` compute SHA-256 over `(parameters, expression)`. Neither includes any symbol or obligation ID.
- [`Verification/Z3/Cache/VerificationCache.cs:317`](../../src/Calor.Compiler/Verification/Z3/Cache/VerificationCache.cs#L317) `GetCacheFilePath`: `Path.Combine(_cacheDirectory, prefix, hash + ".json")` — content-addressed file layout under `{ProjectDirectory}/.calor/verification-cache/` or `~/.calor/cache/z3/v1/`. **No `*.calr.cache` files exist anywhere in the repo.**
- The first compile after Phase 2 migration will hit the cache for every contract whose expression text is unchanged (which is all of them — Phase 2 changes IDs, not expressions).

**Migrator cache work: ZERO.** No walk, no remap, no invalidation. Removed from the Phase 2 migrator implementation list in §6.3.

---

## 7. Resolved questions (unchanged from v3 §7)

1. `[CalorSymbol]` footprint — resolved by deletion (not proposed).
2. Overload disambiguation — out of scope (IDs disambiguate at identity level).
3. Constructor naming — out of scope.
4. `stable-identifiers.md` — updated, not repealed.
5. Migrator in-place vs parallel — in-place with `--dry-run`.
6. Deprecation timeline — single release, hard break.

---

## 8. Diagnostics

### 8.1 Phase 1 (additive)

| Code | Description | Severity |
|---|---|---|
| `Calor0820W` | Sub-block construct has a legacy `{id:...}` attribute that is being discarded. (Lex-time warning, migrator-consumed.) | Warning |
| `Calor0820` | Sub-block constructs no longer accept `{id:...}` — run `calor fix --drop-structural-ids`. (Parse-time error after Phase 1 ships, if user skipped the migrator.) | Error |

**Full `Calor0820` diagnostic text:**

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

### 8.2 Phase 2 (additive)

| Code | Description | Severity |
|---|---|---|
| `Calor0821` | Legacy ULID format detected — run `calor fix --compact-ids` | Error |
| `Calor0822` | Reverse migration impossible: `migration.log.json` is missing | Error |

**Full `Calor0821` diagnostic text:**

```
Calor0821 (Error) at file.calr:1
  Legacy 26-character ULID format detected.
  Found:    §M{m_01J5X7K9M2NPQRSTABWXYZ12:Hello}
  Expected: §M{m_<12 chars Crockford base32>:Hello}

  Migrate this file (and any others in the project) by running:
    calor fix --compact-ids

  The migrator updates IDs in source files and [CalorAttribute] references
  in generated C#. The Z3 proof cache is content-addressed and is unaffected
  by ID format changes — no re-verification is required.
```

#### 8.2.1 `Calor0822` full text

```
Calor0822 (Error) when running 'calor fix --revert-compact-ids'
  The migration log 'migration.log.json' was not found.

  Forward migration writes 'migration.log.json' alongside the project root.
  Without it, the original ULIDs cannot be recovered deterministically.

  Recovery options:
    1. If you have committed the pre-migration state, revert via:
         git revert <commit-that-ran-forward-migration>
    2. If you have a backup of 'migration.log.json' from CI artifacts, copy
       it to the project root and re-run 'calor fix --revert-compact-ids'.
    3. Otherwise, the compact IDs become the canonical IDs going forward.
       Generate fresh ULIDs (losing identity continuity) only as a last resort.
```

### 8.3 Standalone — Diagnostic addressing (definite, ships first)

Promoted from v3 (which already promoted it from v2). **Ships in 0.x patch release before 0.x+1 dev starts.** ~1 day PR. Diagnostic format change only.

```
Today:    Calor0501: division by zero in f_01J5X7K9M2NPQRSTABWXYZ12 at file:42
Phase 0:  Calor0501: division by zero in Calculator.Divide (f_01J5X7…) at file:42
```

**Truncation rule:**
- For ULIDs (pre-Phase-2): show first 7 chars + `…`.
- For compact IDs (post-Phase-2): show full ID (12 chars; no truncation needed at that length).

This way the human-readable form is stable across the Phase 2 transition.

---

## 9. Effort & timeline

### 9.1 Standalone — Diagnostic addressing

| Task | Estimate |
|---|---|
| Update `Diagnostics/Diagnostic.cs` formatter helpers | 0.5 day |
| Snapshot test updates (~20 fixtures) | 0.3 day |
| Documentation | 0.2 day |
| **Subtotal** | **~1 day** |

### 9.2 Phase 1 (definite)

| Task | Estimate |
|---|---|
| Lexer changes (accept-and-warn legacy `{id}` on sub-block constructs) | 1 day |
| Parser changes (drop ID extraction + match enforcement) | 2 days |
| AST nullability for sub-block nodes | 1 day |
| CalorEmitter template changes | 1 day |
| Migrator (`calor fix --drop-structural-ids`) | 2 days |
| **AST-diff verifier (§5.7) — predicate + test suite** | **2 days** (new from v3) |
| Snapshot test updates (~30 fixtures, mechanical) | 2 days |
| Documentation: syntax-reference, CLAUDE.md, copilot-instructions, MCP resources, philosophy docs | 2 days |
| Editor extension TextMate grammar | 0.5 day |
| Sample migration | 0.5 day |
| **Subtotal** | **~14 days / ~2.8 weeks** |

### 9.3 Phase 2 (conditional, requires gate)

| Task | Estimate |
|---|---|
| IdGenerator / IdValidator format swap | 1 day |
| `IdRegistry` implementation + threading | 1 day |
| Migrator (`calor fix --compact-ids` + `migration.log.json`) | 2 days |
| Reverse migrator (`calor fix --revert-compact-ids`) | 1 day |
| Snapshot test regeneration | 3 days |
| Documentation & website updates | 1 day |
| **Subtotal** | **~9 days / ~1.8 weeks** |

### 9.4 Phase 2 gate experiment

| Item | Estimate |
|---|---|
| Pre-register analysis plan in `docs/plans/phase-2-measurement-protocol.md` | 0.5 day |
| Author 5–10 new realistic multi-edit tasks (top up 24 existing → ≥30) | 1.5 days |
| Implement Phase 1+2 on branch behind feature flag (overlap with §9.2/§9.3) | 0 (overlap) |
| Run the experiment (≥10 runs × ≥30 tasks × 3 arms = 900 runs minimum) | ~4–5 calendar days of agent compute |
| Analyze results, write `phase-2-measurement-results.md`, ship/no-ship decision | 1 day |
| **Subtotal** | **~3 days engineering + 4–5 days agent compute** |

**Honest gate-experiment cost range (per designer critique §3 + devil's advocate §8):**

| Model used | Per-turn cost | Avg turns/task | Total cost (900 runs) | Cost with 1 retry (1,800 runs) |
|---|---|---|---|---|
| Haiku 4.5 | ~$0.02 | 30 | ~$540 | ~$1,080 |
| Sonnet 4.6 (no thinking) | ~$0.05 | 40 | ~$1,800 | ~$3,600 |
| Sonnet 4.6 (thinking) / Opus 4.5 | ~$0.10 | 50 | ~$4,500 | ~$9,000 |

**Budget the realistic case: $2,000–$4,000 with 1 retry covered.** v3's "$450" was the Haiku-best-case figure.

### 9.5 Total release effort & calendar

| Path | Engineering days | Calendar (with gate-retry buffer) |
|---|---|---|
| Standalone diagnostic addressing | ~1 day | ~1 week (ships first, 0.x patch) |
| Phase 1 + Phase 2 + gate + buffer | ~26 days | **~8 weeks total** (see §11 for breakdown) |
| Phase 1 only (gate fails) | ~16 days | ~5 weeks |

v3 claimed 6 weeks. v4 budgets **8 weeks** to absorb the §11 buffer the devil's advocate §4 demanded. This is honest, not pessimistic — `implementation-summary-binder-class-members.md` style refactors in this repo have a track record of running over.

---

## 10. Phase 2 measurement gate — tightened

Per v2/v3 critiques: pre-registered protocol, paired tests, multiple-comparison correction, **medium-effect (not small-effect) ship threshold**.

### 10.1 Benchmark protocol

- **Tasks:** **N ≥ 30** multi-edit tasks. 24 from existing `tests/E2E/agent-tasks/fixtures/` + ≥ 6 newly authored.
- **Variants (3 arms):** today's Calor (ULID), Phase-1-only (ULID + no sub-block IDs), Phase 1+2 (compact ID + no sub-block IDs).
- **Runs:** ≥ 10 per task per arm. Total ≥ 900 task runs.
- **Metrics:** success rate (binary), turn count (integer), identity-preservation errors, edit-correctness errors, total output tokens.

### 10.2 Statistical methodology — pre-registered

**Pre-registration:** before any Phase 2 implementation code is written, `docs/plans/phase-2-measurement-protocol.md` must be committed with task list (fixture commit hashes), analysis pipeline (with seeds), thresholds, statistical tests. Once committed, no changes until the experiment concludes. Any necessary changes invalidate the run.

**Statistical tests:**
- **Continuous metrics:** paired Wilcoxon signed-rank on per-task medians.
- **Binary metric:** McNemar's test.
- **Significance threshold:** α = 0.05 per criterion, Bonferroni-corrected across 4 kill criteria → α' = 0.0125 per test.

**Effect sizes:**
- **Cliff's δ** for continuous, **odds ratio** for binary.

### 10.3 Kill criteria for Phase 2 — RAISED to medium effect

Phase 2 ships only if **all four** are true:

1. **No success-rate regression:** Phase 1+2 success rate ≥ today, McNemar p > α'.
2. **No identity-preservation regression:** Phase 1+2 identity-preservation errors ≤ today, Wilcoxon p > α'.
3. **Material agent-UX improvement on at least one of:**
   - Turn count median reduction ≥ 10% **AND** Wilcoxon p < α' **AND** **Cliff's |δ| ≥ 0.33** (medium effect), or
   - Total output tokens median reduction ≥ 15% **AND** Wilcoxon p < α' **AND** **Cliff's |δ| ≥ 0.33** (medium effect).
4. **Phase 2 is distinguishable from Phase 1 alone** on the (3) criterion, Wilcoxon p < α'.

**Why δ ≥ 0.33 (not 0.2):** The principle narrowing is a one-way door. v3 picked δ ≥ 0.2 — the lower edge of "small effect" by Cohen/Cliff convention (negligible < 0.147, small < 0.33, medium < 0.474, large ≥ 0.474). A "small" effect on a 30-turn run is ~3 turns saved. **Three turns per task at the cost of a permanent principle narrowing is a thin trade.** v4 requires medium effect (δ ≥ 0.33), i.e., the improvement must be substantively meaningful, not just statistically detectable. If N=900 is too few to detect a medium effect when one is truly present, the right answer is to add more tasks, not to lower the bar.

If any of (1)–(4) fails, Phase 2 is rejected and only Phase 1 + standalone diagnostic-addressing ship in 0.x+1.

### 10.4 Revert pathway

If Phase 2 ships and post-ship data shows regressions on (1) or (2), v4 commits to revert via `calor fix --revert-compact-ids` consuming `migration.log.json`. Symmetric, deterministic, tested in Phase 2's implementation.

If users have no `migration.log.json` (e.g., new projects post-Phase-2 ship), `Calor0822` surfaces the git-history-based recovery path.

### 10.5 Retry policy (new in v4)

Pre-registered experiments fail in two ways:

- **Gate criterion fails** — the proposal does not ship. This is the **intended** function of the gate, not a retry.
- **Protocol violation detected mid-run** — task crash, LLM provider outage, seed-isolation bug, corpus changed during run. This triggers a **retry**.

**Policy:**
- **One retry covered by an additional gate-experiment budget reserve.** Cost: another $2,000–$4,000 (per §9.4 realistic range).
- **Further retries require explicit re-pre-registration** in a new `phase-2-measurement-protocol-vN.md` document.
- **Sign-off for retry:** the maintainer.

The week-5 contingency block in §11 absorbs the calendar cost of one retry.

### 10.6 Post-ship Phase 1 monitoring (new in v4)

Per devil's advocate §7: Phase 1 ships unmeasured by Phase 2's own evidentiary standard. v4 closes this gap.

**Within 30 days of 0.x+1 ship:**
- Re-run the agent-task corpus (24+ fixtures × 10 runs) against 0.x+1's Phase-1-only build.
- Compare turn-count median against the latest 0.x pre-release tag.
- **If Cliff's |δ| ≥ 0.2 regression** (small effect; we use a lower bar for revert detection than for ship), trigger a Phase 1 revert investigation.

**Cost:** ~$200–$800 depending on model selection. Absorbed in 0.x+2 prep work.

This gives Phase 1 the same evidentiary discipline as Phase 2, applied retrospectively rather than prospectively. The asymmetry (prospective gate for Phase 2, retrospective monitoring for Phase 1) is justified because Phase 1 is reversible at lower cost than Phase 2 (no symbol ID format change to undo, no `migration.log.json` to read).

### 10.7 What the gate doesn't measure

- Long-term codebase rot at scale
- Multi-agent coordination
- Verifier performance under massive ID change rates

Honest blind spots. Phase 2 commits to *not regressing* the first two metrics; long-term effects are accepted as unmeasured risk.

---

## 11. Release sequencing — bundled in 0.x+1, with buffer

Per devil's advocate §4: v3's week-6 gate placement had no buffer for late gate failure. v4 moves the gate to week 4 and reserves weeks 5–6.

**Sequence:**

1. **0.x patch release (week 0).** Diagnostic addressing (§8.3). ~1 day work, ships in days.
2. **0.x+1 development calendar (8 weeks):**
   - **Weeks 1–3:** Phase 1 implementation on `phase-1` branch. Includes verifier (§5.7). Migrator validated against all `samples/` and `tests/`. **Pre-register §10 protocol document.**
   - **Weeks 3–4 (overlapping):** Phase 2 implementation on `phase-2` branch (rebased on `phase-1`). Tests.
   - **Week 4:** Merge `phase-1` + `phase-2` → `release/0.x+1`. **Gate experiment runs.** ~4–5 calendar days agent compute.
   - **Week 5:** Contingency. Gate retry if protocol violation detected. Final analysis. Ship/no-ship decision.
   - **Week 6:** Release prep (release notes, migrator-command messaging, sample migrations, CHANGELOG). **If gate passed:** finalize bundle. **If gate failed:** revert Phase 2 from `release/0.x+1`, re-validate Phase 1 alone, prep Phase-1-only release.
   - **Weeks 7–8:** Buffer. Release cut.

**If gate passes:** ship `release/0.x+1` as 0.x+1 (Phase 1 + Phase 2 + standalone diagnostic). User runs `calor fix --upgrade-from 0.x` (wrapper that runs both `--drop-structural-ids` and `--compact-ids`).

**If gate fails:** ship Phase 1 alone as 0.x+1. User runs `calor fix --drop-structural-ids`. Phase 2 deferred or abandoned.

**Why this beats v3's 6-week budget:**
- 2 weeks of contingency for protocol violations / late gate failures
- Realistic schedule (matches track record of comparable refactors)
- Same UX outcome (one migration for users)

---

## 12. What changed from v3

| Topic | v3 | v4 |
|---|---|---|
| **§6.4 cache invalidation** | Fictional `(symbol_id, obligation_id, body_hash)` keys + `*.calr.cache` walk | **Truth: content-addressed cache. Migration does nothing to the cache. Two sentences.** |
| **§5.7 AST-diff verifier predicate** | Asserted as safety net; predicate undefined | **Fully specified §5.7**: ignored nodes, trivia rules, ID-references-in-strings, failure granularity, test plan |
| **§10.3 Cliff's δ threshold** | 0.2 (small effect) | **0.33 (medium effect)** — appropriate for one-way-door change |
| **§11 calendar** | 6 weeks, gate in week 6, no buffer | **8 weeks, gate in week 4, weeks 5–6 contingency, weeks 7–8 buffer** |
| **§6.3.1 IdRegistry concurrency** | Not addressed | **New §6.3.1**: per-project `ConcurrentDictionary`, generate-until-unique with CAS retry, no persistence (rebuilt per compile) |
| **§10.6 Phase 1 monitoring** | None | **New §10.6**: 30-day post-ship turn-count check; revert trigger at Cliff's |δ| ≥ 0.2 |
| **§10.5 retry policy** | None | **New §10.5**: 1 retry covered ($2k–$4k reserve); further retries require re-pre-registration |
| **§5.6 lexer disambiguation** | "the existing lexer" — ambiguous | **The post-Phase-1 lexer with legacy tolerance**; lexer requirement added to §5.4 |
| **§6.3 reverse migrator** | "Symmetric, shipped in same release" — but forward is one-way hash | **`migration.log.json` format specified. Reverse migrator consumes the log. `Calor0822` for missing-log case.** |
| **§6.3 IdRegistry population order** | Mentioned in §13 as a residual concern | **Specified in §6.3**: full `IdScanner` pre-pass before any `Generate()` call. Migration single-threaded. |
| **§9.4 gate budget** | $450 (single-point, Haiku-best-case) | **$500–$4,000 range (model-dependent)**; budget the realistic case |
| **§16.F token verification** | Claimed "~16 tokens saved" without measurement | **New §16.F**: tiktoken measurement shows actual ~10 tokens saved; **headline corrected from "~33%" to "~20–25%"** |
| **§14 sub-block diff cost** | Dismissed as "what real diff tools do" | **Rewritten**: structural matching is weeks-to-months of pivot-plan work; explicit action item |
| **§6.3 MaxAttempts** | Unspecified in retry loop | **MaxAttempts = 100** (≥60 orders of magnitude safety) |
| **§5.6 already-migrated detection** | Implied by idempotence claim | **Specified**: try new parser first; legacy only on failure |
| **§5.5 line counts** | Lexer not in change list | **Lexer added** (~30 LOC for legacy-tolerance) |
| **§9.2 Phase 1 effort** | ~12 days | **~14 days** (includes verifier spec + tests) |
| **§9.5 total calendar** | ~6 weeks | **~8 weeks** (honest with contingency) |

---

## 13. Honest residual concerns

This section exists because earlier RFCs were criticized for hiding weak points in "open questions." v4 inherits prior concerns and adds new ones.

1. **Phase 1's structural-ID drop changes the "diagnostic with sub-block location" format.** Today: `Calor0501 at if1`. Phase 1: `Calor0501 at file:line (in Calculator.Divide)`. New form is more informative but breaks any external tool parsing the old form. No known external tools.

2. **Error-recovery precision degrades in Phase 1.** Today's diagnostic `Calor0101: §/I{if1} expected, got §/I{if3}` becomes `Calor0101: §/I expected, got §/M`. Open-stack-based recovery delivers ~80% of the precision. The §5.7 verifier catches migration-time damage; live edits rarely produce malformed closers; §8.3 qualified-name format softens it. Small loss, acknowledged.

3. **v3's cache fiction was itself a real defect.** This RFC trajectory v1 → v2 → v3 → v4 includes the v3 cache fiction as a worked example of how aspirational architectural claims survive multiple rounds of review until the actual source is grep'd. **Lesson:** every architectural claim in any future Calor RFC must cite an actual file + line number, and `grep`-verified before merge. The §6.4 rewrite + §16.A inventory enforce this discipline going forward.

4. **9-char ID was v2's bug; cache fiction was v2+v3's bug.** Two consecutive major errors survived multiple critique rounds. v4 reviewers: **check the source for every architectural claim**, not just the math.

5. **§5.7 verifier predicate is now spec'd but not prototyped.** The trivia-reattachment rules (5.7.3) assume the parser's existing trivia-attachment logic produces stable anchors across migration. This assumption needs prototype validation before Phase 1 ships.

6. **`IdRegistry` adds project-pre-pass invariant the compiler currently doesn't enforce.** Today `IdGenerator` can be called without a full project scan. Phase 2 changes this. Any code path that constructs an `IdGenerator` outside the standard pipeline (test harness, MCP `calor_compile` invocation, REPL) must be updated to ensure the pre-pass invariant holds. Hidden cost.

7. **`migration.log.json` is the project's revert capability for Phase 2.** It must be committed to source control. v4's `--compact-ids` command emits a clear instruction at the end of its run telling the user to commit the log. But projects that ignore this instruction lose revert capability. Documented in `Calor0822` text; further mitigation requires policy not RFC.

8. **Phase 2's `--revert-compact-ids` path is itself a one-shot operation.** If a user runs forward, then runs reverse, then makes new ULID-format edits manually, then tries reverse again, the migration log is now stale and the second reverse fails. v4 accepts this as a degenerate operator workflow; users shouldn't manually round-trip ID formats.

9. **No multi-agent coordination measurement exists today.** Phase 2's gate measures single-agent task performance. Multi-agent risks (two agents editing the same module with different IDs being generated) are not in the gate. Mitigated by: (a) `IdRegistry` collision detection, (b) project-scoped uniqueness, (c) standard merge-conflict resolution.

10. **The sub-block structural-matching algorithm for `SemanticDiff` is not in v4's scope.** §14 surfaces this as a real pivot-plan implementation cost. v4 does not solve it.

11. **The §16.F token-count measurement was on synthetic ULID/compact pairs.** Real per-occurrence savings depends on the project's tokenizer (most agents use cl100k or o200k variants). The numbers should be re-measured on actual project source as part of the §10 gate to confirm the projection.

---

## 14. Pivot plan reconciliation — what symbol vs structural means for diff/merge

Per v2 critique §9 and v3 critique §5: the v2 claim "pivot-plan conflict resolved" is true at the symbol level but glosses a real cost at the sub-block level.

**The pivot plan's `Calor.SemanticDiff` design** promises structured deltas over the IR keyed on stable IDs. v4 preserves this at the symbol level: an agent that rewrites a method body but keeps the method declaration produces a `SymbolDelta(method_id=f_a1b2c3d4e5f6, body_change={...})`. The method's identity survives.

**At the sub-block level**, v4 trades identity for positional/AST-index addressing. An agent that rewrites an `if` block inside a function produces `SubBlockDelta(parent_id=f_a1b2c3d4e5f6, path="body[3].if", change={...})`. The "path" is fragile: if the agent also added a statement at `body[1]`, the same `if` is now at `body[4].if`.

**Implementation cost the pivot plan must absorb (clarified per v3 designer critique §5):** Sub-block-level structural matching is **weeks-to-months of implementation work** for a robust algorithm — comparable to `git diff -M` move detection or AST-diff tools like `gumtree`. The precision/recall tradeoffs are non-trivial. v4 does not dismiss this as "what real diff tools do." It is a real cost that v4 transfers from the Calor compiler (which can no longer key sub-block deltas on IDs) to the pivot-plan IR (which must now do structural matching).

**Pivot plan action item:** the pivot plan's next revision must either:
1. **Specify the sub-block structural-matching algorithm** with implementation estimate (likely 4–8 weeks), or
2. **Constrain `SemanticDiff` to symbol-level deltas only** and not promise sub-block delta identity. Sub-block changes appear as opaque diffs inside symbol-level deltas, not as structured sub-block deltas.

Option (2) is the smaller commitment and likely the right answer for a v1 of `SemanticDiff`. The pivot plan owns this choice.

---

## 15. Recommendation

**Standalone diagnostic addressing (§8.3): ship as small PR in 0.x patch release.** ~1 day. Near-zero risk.

**Phase 1 + Phase 2 bundled in 0.x+1, conditional on §10 gate:** Implement both on branches; merge to `release/0.x+1`; run gate in week 4. Ship together if the gate passes (Cliff's δ ≥ 0.33 on turns OR tokens, no regressions on success rate or identity-preservation). Ship Phase 1 alone if not. Weeks 5–6 are reserved for contingency; weeks 7–8 for release prep + buffer.

**Pre-register §10 protocol** before any Phase 2 implementation code lands.

**Pre-prototype the §5.7 verifier predicate** with positive + negative test cases before Phase 1 migrator code lands.

**Verify every architectural claim in this RFC against the actual source** before merge. Specific claims to audit: §6.4 cache (already verified), §6.3.1 IdRegistry (no IdRegistry exists today; pure forward design), §5.7 trivia reattachment (depends on parser's existing trivia logic; needs prototype).

**Mark [`path-2-drop-ids-v3.md`](./path-2-drop-ids-v3.md) as superseded.** Keep v1, v2, v3, all four critiques, and v4 in tree. The trajectory is itself a case study and earns the document corpus.

---

## 16. Appendices

### 16.A — What survives, what dies (updated from v3)

**Survives:** `IdScanner` (14 symbol IdKinds), `IdValidator`, `IdGenerator` (format changes; generator stays + adds uniqueness check), `RefinementType.Id`, `IndexedType.Id`, cross-module symbol-ID resolution, the (narrowed) principle "Every symbol-level declaration has an ID," `docs/philosophy/stable-identifiers.md` (updated, not repealed).

**Survives unchanged:** Z3 proof cache (content-addressed; no ID dependency to begin with; no migrator work). [Verified against [`Verification/Z3/Cache/`](../../src/Calor.Compiler/Verification/Z3/Cache/).]

**Dies (Phase 1):** ID block on `§L`, `§FOREACH`, `§WH`, `§DW`, `§IF`, `§TR`, `§UNSAFE`, `§FIXED`, `§SYNC`, `§USING`, `§MATCH`, `§FORALL`, `§EXISTS`, and close tags. `if (endId != id)` enforcement paths. Synthesized sub-block IDs in `RoslynSyntaxVisitor`.

**Dies (Phase 2, gated):** 26-char ULID format. Replaced by 12-char Crockford base32. The `IdRegistry` is new (didn't exist).

**Out of scope:** `[CalorId]` / `[CalorSymbol]` attribute design; positional sub-block addressing as identity; MCP qualified-name lookups beyond §8.3; C# → Calor round-trip stability improvements; sidecar files; "IDs only on pub" rules; Z3 cache invalidation (because there is none).

### 16.B — Side-by-side examples (with verified token counts)

Same as v3 §16.B, with the headline corrected from "~33%" to **"~20–25% project-wide on production-ULID projection"** (per §16.F). Per-occurrence savings: ~10 tokens (not v3's "~16").

### 16.C — Calor0820 / Calor0821 / Calor0822 diagnostic text

Full text in §8.1, §8.2, §8.2.1.

### 16.D — Collision math verification (unchanged from v3)

```python
import math
def birthday(n, N): return 1 - math.exp(-(n**2) / (2*N))

# 12-char Crockford base32 (v3/v4 choice):
N = 32**12  # 1,152,921,504,606,846,976
for n in [1e6, 1e7, 1e8]:
    print(f"n=10^{int(math.log10(n))}: P = {birthday(n, N):.2e}")
# n=10^6: P = 4.34e-07
# n=10^7: P = 4.34e-05
# n=10^8: P = 4.34e-03
```

Plus generate-until-unique in `IdGenerator` with `MaxAttempts = 100` provides defense-in-depth.

### 16.E — Pre-registration template (for §10 protocol)

The pre-registration document at `docs/plans/phase-2-measurement-protocol.md` must include:

1. Task list with commit hashes of each fixture.
2. New task authoring artifacts (prompts + acceptance criteria) committed before pilot runs.
3. Implementation flag — exact branch + commit hash for each arm.
4. Run protocol — agent harness invocation, model version (pinned), random seed.
5. Data schema — fields recorded per run.
6. Analysis pipeline — exact statistical functions, library versions, seed.
7. Pass/fail thresholds (per §10.3, including **δ ≥ 0.33**) plus Bonferroni correction.
8. Reporting commitment — `docs/plans/phase-2-measurement-results.md` written from analysis output regardless of pass/fail.
9. Retry policy reference (§10.5).

### 16.F — Token-count verification (NEW in v4)

Verified with `tiktoken` (`cl100k_base` encoding) on 2026-05-25:

```python
import tiktoken
enc = tiktoken.get_encoding('cl100k_base')

ulids = [
    'f_01J5X7K9M2NPQRSTABWXYZ12',
    'm_01HZJK2N3P4Q5R6S7T8V9W0XY',
    'c_01J9K4M2N5P7Q8R3S6T1V4W7X',
    'i_01J8H7G6F5E4D3C2B1A0Z9Y8X',
    'rt_01J2K3L4M5N6P7Q8R9S0T1V2W',
]
compacts = [
    'f_a1b2c3d4e5f6', 'm_q9r8s7t6u5v4', 'c_p3k7m2n9q5r1',
    'i_t6v9w2x5y8z3', 'rt_b4n7p2r5s8t1',
]
```

| ID | Tokens |
|---|---:|
| `f_01J5X7K9M2NPQRSTABWXYZ12` | 16 |
| `m_01HZJK2N3P4Q5R6S7T8V9W0XY` | 23 |
| `c_01J9K4M2N5P7Q8R3S6T1V4W7X` | 26 |
| `i_01J8H7G6F5E4D3C2B1A0Z9Y8X` | 26 |
| `rt_01J2K3L4M5N6P7Q8R9S0T1V2W` | 26 |
| **ULID mean** | **23.4** |
| `f_a1b2c3d4e5f6` | 13 |
| `m_q9r8s7t6u5v4` | 13 |
| `c_p3k7m2n9q5r1` | 13 |
| `i_t6v9w2x5y8z3` | 13 |
| `rt_b4n7p2r5s8t1` | 13 |
| **Compact mean** | **13.0** |

**Per-occurrence savings: ~10 tokens.** Per-ID reduction: **44%** (10/23.4). Project-wide reduction depends on ID occurrence density (declarations + close tags + cross-edit refs). For typical Calor source: **~20–25%** project-wide. (v2 and v3 projected "~33%" from an incorrect "~28 tok/ULID" assumption; the correct figure is ~16–26 tok/ULID with mean ~23.)

**Note:** cl100k tokenization of mixed-case + digit strings is not one-token-per-character. The BPE merges some digit/letter sequences. Real ULIDs encode to fewer tokens than naive length implies. Future RFCs should not extrapolate token counts from string length without measurement.

The §10 gate experiment will produce real measurements on real source, replacing these synthetic estimates with project-actual numbers.

---

## 17. Decision

To be made by repo maintainer.

**Recommended action:**

1. **Approve standalone diagnostic addressing (§8.3) for immediate ship in 0.x patch.** ~1 day.
2. **Approve v4 Phase 1 + Phase 2 for bundled implementation in 0.x+1**, conditional on the §10 gate experiment running on the release branch in week 4 with Cliff's δ ≥ 0.33 threshold. If gate passes, ship both; if not, ship Phase 1 alone.
3. **Approve §10 pre-registration as a hard requirement** before any Phase 2 implementation code merges.
4. **Approve §5.7 verifier predicate as a hard requirement** before any Phase 1 migrator code merges.
5. **Mark [`path-2-drop-ids-v3.md`](./path-2-drop-ids-v3.md) as superseded by this document.** Keep v1, v2, v3, v4 + all critiques in tree.

---

*v4 path: `docs/plans/path-2-drop-ids-v4.md`*
*Status: ready for adversarial review. Reviewers: please attack the §5.7 verifier predicate enumeration, the §6.3.1 concurrency model, the §10.3 medium-effect threshold rationale, and the §11 8-week calendar. In particular: verify every architectural claim against actual source (§6.4 is the worked example of why this matters). If v4 survives, it ships; if it does not, v5.*
