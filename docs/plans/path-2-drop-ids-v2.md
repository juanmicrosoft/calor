# RFC v2: Compact Stable Identifiers — Drop Structural IDs, Compact Symbol IDs

**Status:** Draft (v2 — supersedes [`path-2-drop-ids.md`](./path-2-drop-ids.md))
**Author:** TBD (with Copilot CLI)
**Created:** 2026-05-22
**Supersedes:** v1 (2025-11-25)
**Reviewed against:** [`path-2-drop-ids-critique.md`](./path-2-drop-ids-critique.md), [`path-2-drop-ids-devils-advocate.md`](./path-2-drop-ids-devils-advocate.md)
**Target release:** Pre-1.0 hard break (0.x → 0.x+1) for Phase 1. Phase 2 gated on measurement.

---

## 0. Why v2 exists

v1 was rejected by two independent critiques on three grounds:

1. **Thesis error.** v1 claimed "names are identity." The critiques showed this conflicts with the project's identity model (the IR/diff/merge/coordination/memory substrate per the pivot plans) and is internally inconsistent with v1's own round-trip proposal (`[CalorSymbol]`) which silently re-introduces identifiers under a different name. ([thesis critique](./path-2-drop-ids-critique.md) §"Path 2 contradicts the project's own strategic direction", [devil's advocate](./path-2-drop-ids-devils-advocate.md) §2.)

2. **Evidence error.** v1 anchored on a 5-program micro-benchmark that used *test-form* IDs (`f001`, `m001`), not production ULIDs (`f_01J5X7K9M2NPQRSTABWXYZ12`). It then generalized "20% savings" to all programs without measuring on realistic codebases or against any of the rejected alternatives. ([thesis critique](./path-2-drop-ids-critique.md) §"The token math is a benchmark sleight-of-hand", [devil's advocate](./path-2-drop-ids-devils-advocate.md) §1.)

3. **Scope error.** v1 lumped two populations together: (a) **symbol IDs** that `IdScanner` tracks and the verifier uses as cache keys, and (b) **structural IDs** on sub-block constructs (`§L`, `§IF`, `§WH`, `§TR`) that `IdScanner` ignores and that exist only for open/close matching. The two have different costs, different benefits, and warrant different decisions. v1 did not separate them. ([devil's advocate](./path-2-drop-ids-devils-advocate.md) §3, §6, §7.)

v2 separates the populations, drops only what is unambiguously noise, and gates the harder change behind an explicit measurement. The thesis is reframed.

---

## 1. Summary

Two changes, sequenced:

**Phase 1 — Drop structural IDs (definite).**
Remove the ID block from sub-block constructs only. Symbol IDs are unchanged. No deprecation period. Hard break (pre-1.0 envelope). No `[CalorId]` impact, no Z3 cache impact, no round-trip impact. **Measured savings (Phase 0, N=5): ~5–9% on test-form sources, concentrated on programs with sub-blocks** (16–18% on `fizzbuzz`/`is_prime`, 0% on `hello`/`add`/`divide`). Effort: ~1 week.

**Phase 2 — Compact symbol IDs (gated).**
Replace 28-character ULIDs with ~9-character compact base32 IDs (`m_a1b2c3d4`). Symbol-level identity model is preserved end-to-end (`IdScanner`, `IdValidator`, `IdGenerator` continue to work; cache keys and refs continue to function; round-trip remains stable). **Measured combined Phase 1+2 savings on production-ULID projection: ~33%** (28–40% per task, see [`bench/phase0/out/report.md`](../../bench/phase0/out/report.md)). Effort: ~2 weeks. **Ships only if** the agent-harness experiment in §10 demonstrates a measurable agent-success improvement.

**No change to the design principle.** "Everything has an ID" is preserved at the symbol level (where it pays). Sub-block IDs were never actually canonical (`IdScanner` does not record them — confirmed in [`src/Calor.Compiler/Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs)) and removing them does not amount to repealing the principle.

---

## 2. Motivation

### 2.1 Evidence accounting

The v1 Phase 0 benchmark measured 5 small tasks in *test-form* Calor:

```
§M{m001:Hello}
§F{f001:Main:pub}
```

`m001` tokenizes to ~2 cl100k tokens. Real production code uses ULIDs:

```
§M{m_01J5X7K9M2NPQRSTABWXYZ12:Hello}
§F{f_01J5X7K9M2NPQRSTABWXYZ12:Main:pub}
```

Each ULID is 28 characters and tokenizes to approximately one token per character (~28 tokens). The v1 20% headline measured on test IDs **understates** the real production cost of ULIDs by roughly an order of magnitude per occurrence.

This cuts both ways. It makes the *opportunity* larger (a compact format is a much bigger win than v1's measurement suggested) and the *risk* of "v1's number doesn't generalize" smaller for the symbol-ID compaction question — but it also makes the v1 *recommendation* (drop IDs entirely) less defensible, because v1 over-stated the relative cost of structural IDs and under-stated the absolute cost of symbol IDs.

**v2's position:** the v1 benchmark is a directional signal, not a justification. It identifies that ID token cost is non-trivial. It does not justify a particular response. Phase 2 (the larger change) is therefore gated on a real measurement (§10), and Phase 1 (the smaller change) is recommended on its own merits because it has near-zero downside.

### 2.2 The two populations are different

| Population | Tracked by `IdScanner` | Used as cache key | Used in round-trip | Source of token cost |
|---|:---:|:---:|:---:|---|
| **Symbol IDs** (`m_`, `f_`, `c_`, `i_`, `p_`, `mt_`, `ctor_`, `e_`, `op_`) on Module / Function / Class / Interface / Property / Method / Constructor / Enum / OperatorOverload / Indexer / RefinementType / ProofObligation / IndexedType / EnumExtension | yes | yes (verifier) | yes (planned [CalorId]) | ~28 tok per occurrence (ULID) |
| **Structural IDs** on `§L`, `§IF`, `§WH`, `§DW`, `§TR`, `§FOREACH` and their close-tags | **no** | no | no | ~3-8 tok per occurrence (test form is shorter) |

Confirmed by reading [`src/Calor.Compiler/Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs):

> The `Visit(ForStatementNode)`, `Visit(WhileStatementNode)`, `Visit(IfStatementNode)`, `Visit(TryStatementNode)` methods are all empty bodies. **Structural IDs exist in the AST but are not collected as canonical identity entries.** Their only function is matched open/close enforcement in the parser ([`Parsing/Parser.cs:4052`](../../src/Calor.Compiler/Parsing/Parser.cs#L4052) `if (endId2 != id) ReportMismatchedId`).

Confirmed by reading [`src/Calor.Compiler/Verification/Obligations/GuardDiscovery.cs`](../../src/Calor.Compiler/Verification/Obligations/GuardDiscovery.cs):

> `ObligationId = obligation.Id` — the Z3 verifier uses the *symbol-level* `ProofObligationNode.Id` as its cache key. Removing that ID would break the proof cache. Removing structural IDs would not.

v1 treated both populations as one decision. v2 treats them as two.

### 2.3 What each population is worth

**Symbol IDs deliver:**
- Rename safety (canonical ID survives `Calculate → Compute`)
- Z3 proof-cache stability (proof of `DivisorNotZero` is keyed by ID, survives function rename / body reformat)
- Round-trip identity (designed but not yet implemented — see §2.4)
- IR substrate for diff / merge / coordination / memory (the pivot-plan requirement called out by [thesis critique](./path-2-drop-ids-critique.md))
- Unambiguous diagnostic addressing across edits (the line-number-stability point in `docs/philosophy/stable-identifiers.md` §"Code Identity is Fragile")

**Structural IDs deliver:**
- Matched open/close enforcement in the parser
- Nothing else

The first list is the project's competitive moat. The second list is the parser's local convenience and can be achieved by indentation, nesting depth, or stack-based matching — none of which need an ID. **Dropping structural IDs costs nothing the project actually uses; compacting symbol IDs costs only their format.**

### 2.4 Round-trip honesty

v1 claimed `[CalorId("f_01ABC...")]` is emitted on every C# member for round-trip stability. **This is aspirational, not implemented.** A grep across `src/Calor.Compiler/CodeGen/`, `src/Calor.Runtime/`, and `src/Calor.Compiler/Migration/` returns **zero** uses of a `CalorId` attribute. The C# emitter today does *not* preserve IDs in generated C#. The round-trip stability claim in [`docs/philosophy/stable-identifiers.md`](../philosophy/stable-identifiers.md) §"Round-Trip Stability" describes a design that does not exist.

The pre-existing round-trip infrastructure is signature-based, not ID-based. Today, when C# → Calor → C# happens, the converter matches members by name+signature. **The system already operates without ID-based round-trip.** This is important context the v1 RFC did not surface and the critiques did not have access to.

This means:

- v1's `[CalorId] → [CalorSymbol]` rename proposal was theater — neither attribute exists.
- The devil's-advocate concern that `[CalorSymbol]` "silently re-introduces identifiers" applies to a strawman of v1's own making.
- A *future* round-trip-stability implementation should use ID, not name. v2 is consistent with that future; v1 was not.

v2 does not propose any C# attribute changes. The round-trip story is what it is today (signature-based, brittle on rename), and improving it is out of scope.

---

## 3. Reframed thesis

v1: "Names are identity."
v2: **"Identity belongs on symbols, not on structure. Symbol identity should be compact; structural identity should not exist."**

This:

- Preserves the principle "Everything has an ID" *at the level where it pays* (modules, functions, classes, methods, properties, enums, operator overloads, refinement types, proof obligations, indexed types) — every population that `IdScanner` already tracks.
- Removes the principle from sub-block constructs (loops, ifs, while, do-while, try, foreach) where it never paid (`IdScanner` already ignores them).
- Makes no claim that addressing equals identity. Diagnostics may use qualified names + positional paths for *addressing* (a presentation concern); cross-edit references and cache keys continue to use symbol IDs for *identity* (a correctness concern). [Thesis critique](./path-2-drop-ids-critique.md) §"Names are addressable. They are not identity" applies to v1's confusion of these two; v2 does not make that confusion.

---

## 4. Goals & non-goals

### Goals

- **G1.** Reduce token cost of Calor source by a measurable amount with no semantic change and no identity-model degradation.
- **G2.** Phase 1: definite reduction (~10% on realistic programs) by removing pure-noise structural IDs.
- **G3.** Phase 2: conditional reduction (additional 5–15%) by compacting symbol ID format. Ships only if measured agent-success gain meets a pre-declared bar.
- **G4.** Do not break the IR / diff / merge / coordination / memory substrate the pivot plans depend on.
- **G5.** Do not introduce positional fragility for *identity-bearing* uses (cache keys, cross-edit refs). Positional addresses may appear in *presentation* uses (diagnostics) where staleness is already accepted because they're combined with file:line.
- **G6.** Single-release hard break. Pre-1.0 envelope allows this (CLAUDE.md is explicit). No dual-mode parser. No multi-release deprecation window. The migrator is a one-shot operation.

### Non-goals

- **NG1.** v2 does not change Calor semantics, the type system, contracts, effects, or runtime.
- **NG2.** v2 does not address the C# → Calor round-trip story beyond what already exists. Round-trip stability via attribute is a separate (future) RFC.
- **NG3.** v2 does not change `IdScanner`, `IdValidator`, `IdGenerator`, or diagnostic codes `Calor0800–0805` *in Phase 1*. Phase 2 changes `IdGenerator`'s output format only.
- **NG4.** v2 does not change MCP tool surfaces (`calor_navigate`, `calor_fix`, etc.) beyond the strict consequences of the grammar change.
- **NG5.** v2 does not propose Path A from the thesis critique ("BPE-friendly short IDs" alone) because Phase 1's structural-ID drop is orthogonal and additive — v2 does both.
- **NG6.** v2 does not propose Path B from the thesis critique ("sidecar file") because both critiques and the user agree that a sidecar is undesirable.
- **NG7.** v2 does not propose Path C from the thesis critique ("IDs on `pub` only") because it requires the agent to reason about visibility at write-time (the "middle path" cognitive tax that v1's §11.2 correctly identified, and that has nothing to do with the broader thesis flaws in v1).

---

## 5. Phase 1 — Drop structural IDs

### 5.1 Scope

Remove the ID block (and matching close-tag ID block) from:

| Tag | Today | Phase 1 |
|---|---|---|
| `§L` for-loop | `§L{for1:i:1:10:1}` … `§/L{for1}` | `§L{i:1:10:1}` … `§/L` |
| `§FOREACH` | `§FOREACH{fe1:x:items}` … `§/FOREACH{fe1}` | `§FOREACH{x:items}` … `§/FOREACH` |
| `§WH` while | `§WH{wh1} (cond)` … `§/WH{wh1}` | `§WH (cond)` … `§/WH` |
| `§DW` do-while | `§DW{dw1}` … `§/DW{dw1} (cond)` | `§DW` … `§/DW (cond)` |
| `§IF` (block) | `§IF{if1} (cond)` … `§/I{if1}` | `§IF (cond)` … `§/I` |
| `§IF` (inline) | `§IF{if1} (cond) → expr §/I{if1}` | `§IF (cond) → expr §/I` |
| `§TR` try | `§TR{try1}` … `§/TR{try1}` | `§TR` … `§/TR` |
| `§CA` catch | `§CA{Type:var}` (no ID today) | unchanged |
| `§UNSAFE` | `§UNSAFE{u1}` … `§/UNSAFE{u1}` | `§UNSAFE` … `§/UNSAFE` |
| `§FIXED` | `§FIXED{fx1}` … `§/FIXED{fx1}` | `§FIXED` … `§/FIXED` |
| `§SYNC` lock | `§SYNC{s1}` … `§/SYNC{s1}` | `§SYNC` … `§/SYNC` |
| `§USING` block | `§USING{u1}` … `§/USING{u1}` | `§USING` … `§/USING` |
| `§PP` preprocessor | `§PP{COND}` … `§/PP{COND}` | unchanged — the condition is semantic, not an ID |
| `§MATCH` | `§MATCH{m1}` … `§/MATCH{m1}` | `§MATCH` … `§/MATCH` |
| `§FORALL`/`§EXISTS` quantifier | `§FORALL{fa1:x:t}` … `§/FORALL{fa1}` | `§FORALL{x:t}` … `§/FORALL` |
| `§LIST` literal | `§LIST{name:type}` … `§/LIST{name}` | unchanged — `name` is the binding name, not an ID |
| `§DICT` literal | `§DICT{name:k:v}` … `§/DICT{name}` | unchanged — `name` is the binding name |
| `§HSET` literal | `§HSET{name:type}` … `§/HSET{name}` | unchanged — `name` is the binding name |

**Critical distinction:** elements where the `{...}` block contains a *binding name* (e.g., `§LIST{counts:i32}` — `counts` is the variable being bound, used elsewhere by name) are unchanged. Only elements where the `{...}` block contains an *opaque structural identifier* (`if1`, `for1`) are simplified.

### 5.2 Symbol IDs unchanged in Phase 1

Every entry in this table is **unchanged** in Phase 1:

| Tag | Today | Phase 1 |
|---|---|---|
| `§M` module | `§M{m_01J5X…:Name}` | unchanged |
| `§F` function | `§F{f_01J5X…:Name:vis}` | unchanged |
| `§AF` async function | `§AF{f_01J5X…:Name:vis}` | unchanged |
| `§CL` class | `§CL{c_01J5X…:Name}` | unchanged |
| `§IFACE` interface | `§IFACE{i_01J5X…:Name}` | unchanged |
| `§EN` / `§ENUM` enum | `§EN{e_01J5X…:Name}` | unchanged |
| `§EXT` enum extension | `§EXT{ext_01J5X…:EnumName}` | unchanged |
| `§MT` method | `§MT{mt_01J5X…:Name:vis}` | unchanged |
| `§AMT` async method | `§AMT{mt_01J5X…:Name:vis}` | unchanged |
| `§CTOR` constructor | `§CTOR{ctor_01J5X…:vis}` | unchanged |
| `§PROP` property | `§PROP{p_01J5X…:Name:type:vis}` | unchanged |
| `§IXER` indexer | `§IXER{ixer_01J5X…:type:vis}` | unchanged |
| `§OP` operator overload | `§OP{op_01J5X…:token:vis}` | unchanged |
| `§RTYPE` refinement type | `§RTYPE{rt_01J5X…:Name}` | unchanged |
| `§PROOF` proof obligation | `§PROOF{pf_01J5X…:Description}` | unchanged |
| `§ITYPE` indexed type | `§ITYPE{it_01J5X…:Name}` | unchanged |
| `§FLD` field | `§FLD{type:Name:vis}` (no ID today) | unchanged |

Phase 1 preserves: the IR substrate, Z3 proof-cache keys (proof obligations still have IDs), rename safety, cross-edit reference stability, every guarantee in `docs/philosophy/stable-identifiers.md`.

### 5.3 Why this is safe

| Critique objection | Phase 1 status |
|---|---|
| Thesis critique: "names are not identity" | Resolved. Symbol IDs unchanged. Identity model intact. |
| Thesis critique: "contradicts pivot strategy / IR substrate" | Resolved. IR substrate is keyed on symbol IDs, which are unchanged. |
| Thesis critique: "parity with zerolang is the loss condition" | Phase 1 alone does not approach zerolang parity. It removes a clear non-load-bearing tax. |
| Devil's advocate §2: "`[CalorSymbol]` smuggles identifiers" | Resolved. v2 does not propose any C# attribute change. |
| Devil's advocate §3: "positional sub-block addresses are fragile" | Acknowledged. Sub-block addressing in diagnostics uses `file:line + parent-symbol qualified name + indent path`, which is what today's diagnostics already do *visually* (line numbers change too). No identity-bearing system depends on sub-block addresses. |
| Devil's advocate §4: "dual-mode parser is a permanent tax" | Resolved. Hard break in 0.x → 0.x+1. No dual-mode. |
| Devil's advocate §6: "Z3 cache key story unaddressed" | Resolved. Proof obligation IDs are symbol-level and unchanged. Cache keys unaffected. |
| Devil's advocate §7: "refinement types and proof obligations get the worst of both worlds" | Resolved. Both keep their IDs. Name collisions don't matter because identity is by ID. |
| Devil's advocate §5: "effort estimate wrong by 2x" | Phase 1 is small enough that 2x of "1 week" is still <2 weeks. The migrator is mechanical (delete a single `{…}` block per tag). |

### 5.4 Grammar change (complete enumeration)

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
Function_Decl    ::= '§F'       '{' id ':' name ':' visibility '}'  Body '§/F'
Module_Decl      ::= '§M'       '{' id ':' name '}'                 Body '§/M'
// ... (full list in §5.2)
```

### 5.5 Compiler changes (Phase 1)

| File | Lines (today) | Lines changed | Nature of change |
|---|---:|---:|---|
| [`src/Calor.Compiler/Parsing/Parser.cs`](../../src/Calor.Compiler/Parsing/Parser.cs) | ~8,900 | ~150 | `ParseForStatement`, `ParseIfStatement`, `ParseWhileStatement`, `ParseTryStatement`, `ParseForeachStatement`, `ParseDoWhileStatement`, `ParseMatchStatement`, `ParseForallExpression`, `ParseExistsExpression`, `ParseUnsafeBlock`, `ParseFixedStatement`, `ParseSyncBlock`, `ParseUsingStatement` each lose the `attrs["_pos0"]` ID extraction and the `endId == id` matching code path. Replace with a no-attributes-on-open-tag / no-id-on-close-tag form. |
| [`src/Calor.Compiler/Ast/`](../../src/Calor.Compiler/Ast/) (sub-block nodes) | n/a | ~80 | `ForStatementNode`, `IfStatementNode`, `WhileStatementNode`, etc.: `Id` field becomes nullable. Constructor signatures lose the `id` parameter where unused. AST-printer / debug-dump code that prints `node.Id` becomes `node.Id ?? "<anon>"`. |
| [`src/Calor.Compiler/CodeGen/CSharpEmitter.cs`](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs) | ~4,600 | ~20 | These visitors never emitted the sub-block ID into generated C# (sub-blocks are translated to C# `for`/`if`/`while` which have no Calor-ID concept). Confirmed by reading: no `[CalorId]` attribute is emitted today (see §2.4). Changes are limited to internal label generation if any sub-block currently uses its ID as a synthesized label name. Grep target: `node.Id` in `Visit(ForStatementNode)`, `Visit(IfStatementNode)`, etc. |
| [`src/Calor.Compiler/Migration/CalorEmitter.cs`](../../src/Calor.Compiler/Migration/CalorEmitter.cs) | ~2,800 | ~40 | The reverse emitter writes `§L{for1:i:1:10:1}` today. Change to `§L{i:1:10:1}`. Same for IF/WH/TR/etc. — mechanical string-template change. |
| [`src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs`](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs) | ~6,500 | ~5 | When converting C# `for`/`if`/`while` to Calor, today the visitor would generate a synthetic ID. Phase 1: stop generating synthetic IDs for sub-blocks. Trivial. |
| [`src/Calor.Compiler/Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) | ~330 | 0 | Already ignores sub-block nodes. No change. |
| [`src/Calor.Compiler/Verification/ExpressionSimplifier.cs`](../../src/Calor.Compiler/Verification/ExpressionSimplifier.cs) | ~1,400 | 0 | Doesn't touch sub-block IDs. No change. |
| [`src/Calor.Compiler/Diagnostics/Diagnostic.cs`](../../src/Calor.Compiler/Diagnostics/Diagnostic.cs) | — | ~10 | Add `Calor0820` ("Removed sub-block ID is no longer accepted — use `calor fix --drop-structural-ids` to migrate."). |

**Total new code: ~300 lines. Modified code: ~150 lines. Deleted code: ~50 lines (the matching enforcement paths).**

### 5.6 Migrator (Phase 1)

`calor fix --drop-structural-ids [path]`:

1. Parse every `.calr` file under `path` using the *legacy* parser (kept as a private migrator-internal helper for exactly this purpose).
2. Walk the AST. For every sub-block node with an `Id`, set it to null.
3. Re-emit using the *new* `CalorEmitter`.
4. Write back in place.

**Behavior guarantees (addressing [devil's advocate §9](./path-2-drop-ids-devils-advocate.md)):**

- **Comment preservation:** Phase 1 uses an *AST-edit-and-print* migrator strategy, not full re-emit. The migrator operates on the source text directly: locate each opening sub-block tag, locate its matching ID-bearing close tag, and surgically remove only the `{…}` block in each. Other source bytes (comments, whitespace, blank lines, ordering) are preserved exactly. This is implementable as a regex-guided pass anchored on tokens from the lexer, not a parse-and-re-emit pass.
- **Formatting preservation:** see above. No reformatting.
- **Diagnostic suppressions:** grep returns zero `calor-suppress` directives in the codebase today. None to handle.
- **External references:** docs and samples are in-tree; the migrator handles them. External docs (website, archived discussions) become stale; this is an expected one-time cost and the cleanup is mechanical.
- **Idempotence:** files without sub-block IDs pass through unchanged.

### 5.7 Phase 1 deprecation strategy

**Hard break, single release.** Pre-1.0 envelope per [`CLAUDE.md`](../../CLAUDE.md):

- 0.x: legacy form accepted (current).
- 0.x+1: legacy form rejected with `Calor0820`; error message includes the exact `calor fix --drop-structural-ids` command to run. Migrator ships in the same release.
- No dual-mode parser. No multi-release window.

This is what [devil's advocate §10](./path-2-drop-ids-devils-advocate.md) recommended and what `CLAUDE.md` allows. v1's three-release deprecation was importing 1.0+ etiquette into a pre-1.0 project.

---

## 6. Phase 2 — Compact symbol IDs (gated)

### 6.1 What changes

Replace 28-character ULIDs with a compact format that preserves uniqueness and ordering properties at lower tokenizer cost:

| Element | Today (28 chars after prefix) | Phase 2 (proposal: 9 chars after prefix) | Per-occurrence token savings |
|---|---|---|---|
| Function | `f_01J5X7K9M2NPQRSTABWXYZ12` | `f_a1b2c3d4e` | ~20 tokens |
| Module | `m_01J5X7K9M2NPQRSTABWXYZ12` | `m_a1b2c3d4e` | ~20 tokens |
| Class | `c_01J5X7K9M2NPQRSTABWXYZ12` | `c_a1b2c3d4e` | ~20 tokens |
| (etc.) | | | |

**Format proposal:** 9-character alphanumeric (lower-case + digits, no ambiguous characters): `[a-z0-9]{9}`. Alphabet size 36, total IDs = 36⁹ ≈ 1.0×10¹⁴. Collision probability for 10⁶ IDs ≈ 5×10⁻³. Acceptable for an internal codebase; not acceptable for a global registry. (Since Calor IDs are project-scoped — there is no cross-project ID resolution today — a collision space of 10¹⁴ is more than sufficient.)

**Why not 8 chars:** the alphabet [`0-9a-z`] = 36 chars; 8 chars = 36⁸ ≈ 2.8×10¹² which is still fine but Birthday-bound collision starts to matter for very large monorepos (>10⁷ IDs). 9 chars gives a 36× margin at a 1-token cost.

**Why not Crockford Base32 (32 chars):** the existing ULID alphabet would tokenize slightly better than `[a-z0-9]` in BPE because base32 characters appear in more BPE merges. But the savings is marginal vs the migration cost of maintaining an alternate alphabet. The 9-char `[a-z0-9]` form is simpler and the token cost is dominated by *length*, not *alphabet*.

**Sortability:** ULIDs are sortable by creation time. The compact form drops this property. Today, very little code depends on ID sortability (`IdGenerator` sorts internally only for display in CLI listings). Acceptable loss.

**Migration:** deterministic. Each existing ULID maps to a deterministic compact form via `hash(ulid)[:9]`. Repeated runs produce the same output. Existing-ID-aware tools (verifier proof cache) get a one-time invalidation; rebuild on first compile.

### 6.2 Why Phase 2 is gated

Three of the v1 critiques specifically demand measurement before identity-format changes:

- [Thesis critique](./path-2-drop-ids-critique.md) §"The 'cognitive tax' claim is unmeasured": no in-repo data shows agents fail more with long IDs.
- [Devil's advocate](./path-2-drop-ids-devils-advocate.md) §1 ("savings number is a marketing figure"): N=5 micro-benchmark doesn't generalize.
- [Devil's advocate](./path-2-drop-ids-devils-advocate.md) §8 ("agent UX claim asserted, not measured"): no agent-harness comparison.

v2 honors this. Phase 2 ships **if and only if** the experiment in §10 demonstrates a measured improvement.

### 6.3 Compiler changes (Phase 2)

| File | Change |
|---|---|
| [`src/Calor.Compiler/Ids/IdGenerator.cs`](../../src/Calor.Compiler/Ids/IdGenerator.cs) | Replace `Generate()` body: `prefix + GenerateCompactBase36(9)`. Helper computes from `RandomNumberGenerator`. |
| [`src/Calor.Compiler/Ids/IdValidator.cs`](../../src/Calor.Compiler/Ids/IdValidator.cs) | Update `UlidPatternRegex()` → `CompactPatternRegex()`. `UlidLength` constant: 9. Test-ID regex unchanged. |
| [`src/Calor.Compiler/Ids/IdScanner.cs`](../../src/Calor.Compiler/Ids/IdScanner.cs) | No change. Format-agnostic. |
| [`src/Calor.Compiler/Verification/Obligations/`](../../src/Calor.Compiler/Verification/Obligations/) | No change. Format-agnostic. |
| Migrator: `calor fix --compact-ids` | Walk every `.calr`, every `[CalorAttribute]` reference, every `*.calr.cache` — deterministically map ULID → compact. |
| Diagnostic codes | Add `Calor0821` ("Legacy ULID format detected — run `calor fix --compact-ids` to migrate."). |

### 6.4 Round-trip and downstream

- Project-internal: deterministic remap, one-shot.
- Z3 proof cache: invalidated once. Rebuilds on first compile (the cache is already disk-local and the invalidation is one cold cache).
- External tooling that hard-codes ULID format expectations: would break. Today there is none. If any appears between v2 acceptance and Phase 2 ship, the gate (§10) is the place to evaluate it.

---

## 7. Resolved questions (formerly "Open")

v1 §12 listed six open questions. v2 resolves them:

1. **`[CalorSymbol]` footprint.** Resolved by deletion: v2 does not propose this attribute. (No `[CalorId]` exists either today.)
2. **Overload disambiguation syntax.** Out of scope: symbol IDs still distinguish overloads at the identity level. Qualified-name addressing in diagnostics uses signature suffix only when ambiguity is observed (`Calculator.Add(i32,i32)` only if there's an ambiguity to resolve).
3. **Constructor naming.** Out of scope: `§CTOR` keeps its ID. Diagnostic display uses `Calculator.User.ctor` (matches existing `IdKind.Constructor` → display).
4. **Delete or repeal `stable-identifiers.md`.** Neither. Update it: the doc remains correct about *symbol-level* identity. Add a section "What identity does *not* cover" pointing out that sub-block constructs are structural-only.
5. **Migrator in-place vs parallel tree.** In-place with `--dry-run` flag. Same as the v1 recommendation; carry over.
6. **Deprecation timeline.** Single release, hard break. (Pre-1.0 envelope.)

---

## 8. Diagnostics

### 8.1 Phase 1 (additive)

| Code | Description | Severity |
|---|---|---|
| `Calor0820` | Sub-block constructs no longer accept `{id:...}` — run `calor fix --drop-structural-ids` | Error |

No existing diagnostic codes are removed. The diagnostic message for `Calor0820` includes the exact command and a one-line example of the new form.

### 8.2 Phase 2 (additive)

| Code | Description | Severity |
|---|---|---|
| `Calor0821` | Legacy ULID format detected — run `calor fix --compact-ids` | Warning (or Error, depending on gate decision) |

### 8.3 Diagnostic addressing (standalone improvement, recommended for both phases)

**Standalone change recommended by [thesis critique](./path-2-drop-ids-critique.md) §"Things the RFC gets right" #3:** the qualified-name diagnostic format is more readable than the ID form regardless of any other change.

```
Today:    Calor0501: division by zero in f_01J5X7K9M2NPQRSTABWXYZ12 at file:42
Better:   Calor0501: division by zero in Calculator.Divide (f_01J5X7…) at file:42
```

The ID is retained in parentheses for tooling, the qualified name is shown for humans/agents. ~1-day change. Recommended to ship before Phase 1 as its own small RFC.

---

## 9. Effort & timeline

### 9.1 Phase 1 (definite)

| Task | Estimate |
|---|---|
| Parser changes (~150 LOC delta across ~12 statement parsers) | 2 days |
| AST nullability for sub-block nodes (~80 LOC) | 1 day |
| CalorEmitter template changes | 1 day |
| Migrator (`calor fix --drop-structural-ids`, regex-guided edit) | 2 days |
| Snapshot test updates (~30 fixtures, mechanical) | 2 days |
| Documentation: `docs/syntax-reference/*`, `CLAUDE.md`, `.github/copilot-instructions.md`, MCP resources | 1 day |
| Sample migration: run migrator over `samples/` | 0.5 day |
| **Subtotal** | **~9.5 days / ~2 weeks** |

### 9.2 Phase 2 (conditional, requires gate)

| Task | Estimate |
|---|---|
| IdGenerator / IdValidator format swap | 1 day |
| Migrator (`calor fix --compact-ids`, AST-walk deterministic remap) | 2 days |
| Snapshot test regeneration (every test that asserts a specific ID format) | 3 days |
| Z3 cache invalidation regression testing | 1 day |
| Documentation & website updates | 1 day |
| **Subtotal** | **~8 days / ~1.5 weeks** |

### 9.3 Honest accounting on v1's 3-week estimate

v1 claimed 3 weeks for a much larger change. The [devil's advocate](./path-2-drop-ids-devils-advocate.md) §5 argued the real number was 6–10 weeks. Both are probably right about v1: v1 underestimated because it pretended `[CalorId]` existed and ignored the snapshot churn.

v2's Phase 1+2 combined estimate is **~3.5 weeks**, but the scope is genuinely smaller than v1 (no `[CalorSymbol]` work, no dual-mode parser, no positional sub-block addressing infrastructure, no MCP rewrites for qualified-name lookups). The reduced scope makes the lower estimate plausible. Bake in 1 week buffer per phase for snapshot churn and incidental cleanup → **~6 weeks total**, of which **~3 weeks (Phase 1 + buffer) is committed** and Phase 2 is gated.

---

## 10. Phase 2 measurement gate

Per [devil's advocate §15](./path-2-drop-ids-devils-advocate.md) and [thesis critique recommendation #3](./path-2-drop-ids-critique.md), Phase 2 ships only after a measured agent-success improvement on a realistic benchmark.

### 10.1 Benchmark protocol

- **Tasks:** N ≥ 20 multi-edit tasks drawn from `tests/E2E/agent-tasks/` plus 5–10 newly-authored realistic tasks (e.g., "rename method `Add` to `Plus` and update all callers", "extract a helper from `Process` into `ProcessPart`", "add a new field to `Order` and update constructors and serialization"). The mix must include single-edit, multi-edit, and refactor tasks.
- **Variants:** today's Calor (ULID), Phase-1-only Calor (ULID + no sub-block IDs), Phase 1+2 Calor (compact ID + no sub-block IDs). Three arms.
- **Runs:** ≥ 3 runs per task per arm. Total ≥ 180 task runs.
- **Metrics:**
  - Success rate (binary: did the agent produce a passing solution?)
  - Turn count (median + 90th percentile)
  - Identity-preservation errors (agent edited the wrong member; agent created a duplicate)
  - Edit-correctness errors (agent's edit had a syntax / type / contract error fixed in a later turn)
  - Total output tokens (the proxy for what actually costs money per [devil's advocate §1.2](./path-2-drop-ids-devils-advocate.md))

### 10.2 Kill criteria for Phase 2

Phase 2 ships only if **all four** are true:

1. Success rate on Phase 1+2 ≥ today's success rate (no regression).
2. Identity-preservation errors on Phase 1+2 ≤ today's count (no regression on the property symbol IDs exist to protect).
3. Either: turn count median on Phase 1+2 < today's median by ≥ 10%, **or** total output tokens median on Phase 1+2 < today's median by ≥ 15%.
4. Phase 1+2 result is statistically distinguishable from Phase-1-only (otherwise: ship Phase 1 only and stop).

If any of (1)–(4) fails, Phase 2 is rejected. v2 commits to revert Phase 2 if shipped and the post-ship data shows regressions on (1) or (2).

### 10.3 What the gate doesn't measure

The gate measures **first-write and multi-edit agent performance.** It does not measure:

- Long-term codebase rot at scale (months-long agent sessions, thousands of files)
- Multi-agent coordination (two agents editing the same module)
- Performance of the verifier under massive ID change rates

These are open questions for future work and not blockers for Phase 2. Phase 2 commits to *not regressing* the first two metrics; long-term effects are accepted as unmeasured risk.

---

## 11. What changed from v1

| Topic | v1 | v2 |
|---|---|---|
| Core thesis | "Names are identity" | "Symbol identity stays; structural identity goes; format compacts" |
| Population treatment | Lumps symbol + structural together | Separates: symbol IDs preserved, structural IDs dropped |
| Round-trip story | `[CalorSymbol]` replacing `[CalorId]` | Acknowledges neither attribute exists today; no proposal |
| Diagnostics addressing | Qualified-name only | Qualified-name + ID in parentheses (standalone improvement) |
| Positional sub-block addressing | Proposed as identity-bearing | Used only for diagnostic *display*, not identity |
| Deprecation strategy | 3-release dual-mode | Single-release hard break (pre-1.0) |
| Z3 cache key story | Not addressed | Preserved: proof obligations keep IDs |
| Refinement type / proof obligation names | Required unique by name | Unchanged: IDs disambiguate |
| Effort estimate | 3 weeks (under-counted) | 3.5 weeks (Phase 1 + Phase 2 separately budgeted) |
| Evidence requirement | None | Phase 1: zero-risk, ship on engineering merit. Phase 2: measurement gate (§10) |
| Pivot-plan conflict | Triggered | Avoided (IR substrate intact) |
| Rejected alternatives | 5, dismissed by argument | Reframed as "v2 already incorporates what was correct in two of them" |

---

## 12. Honest residual concerns

This section exists because v1 was justly criticized for hiding its weak points in "open questions." v2 surfaces its weak points here.

1. **Phase 1 measurement is still not done.** Phase 1 ships on engineering merit (zero identity impact, mechanical change, near-zero rollback cost). It is not proven to improve agent success. If the Phase 2 gate measurement (§10) shows Phase 1 *itself* regresses agent UX, v2 should be reverted. The repository should add agent-harness measurement to its CI before any further ID-related changes.

2. **9-char compact ID collision space is not infinite.** ~10¹⁴ is fine for any realistic project. Phase 2 should ship with a `calor ids check` that detects collisions (already exists today; format-agnostic). The diagnostic surface is in place.

3. **The pivot plan (semantic IR for agents) needs IDs to be *stable across edits*, not just *short*.** Phase 2 preserves stability (a generated ID is permanent for the life of the declaration). What it loses is *sortability* by creation time. The pivot plan should be audited for any reliance on sortability before Phase 2 ships.

4. **Migrator regex-guided edit (Phase 1, §5.6) is not parse-perfect.** Multi-line tags split across `\r\n` boundaries, tags inside string literals (rare but possible), and tag-like sequences in comments are edge cases the regex must handle correctly. The migrator should include a "parse the output, diff against parse-of-input" sanity check before writing.

5. **No multi-agent coordination measurement exists today.** Phase 2's gate measures single-agent task performance. Multi-agent risks (two agents editing the same module with different IDs being generated) are not in the gate. This is an honest blind spot.

6. **Phase 1's structural-ID drop changes the "diagnostic with sub-block location" format.** Today: `Calor0501 at if1`. Phase 1: `Calor0501 at file:line (in Calculator.Divide)`. The new form is more informative but breaks any external tool that was parsing the old form. There are no such known tools, but the absence of evidence is not evidence of absence.

---

## 13. Recommendation

**Phase 1: accept and proceed.** It is small, mechanical, low-risk, and addresses every concrete objection in both critiques. It does not require an experiment to justify because it does not change anything load-bearing.

**Phase 2: accept the design, gate the ship.** Implement Phase 2 on a branch behind a feature flag. Run the §10 experiment. Ship only if the gate passes.

**Standalone (§8.3 diagnostic addressing): ship independently.** This is a 1-day change that delivers most of the perceived diagnostic-UX win at zero risk. Recommend as a separate PR before Phase 1 ships, so the diagnostic format is in place by the time the structural-ID change lands.

---

## 14. Appendix A — what survives, what dies

### A.1 What survives (the project's identity moat)

- `IdScanner` scanning all 14 symbol IdKinds with stable IDs
- `IdValidator` validating IDs (format changes in Phase 2; the validator stays)
- `IdGenerator` producing IDs (output format changes in Phase 2; the generator stays)
- Z3 proof cache keyed on `ProofObligation.Id`
- `RefinementType.Id`, `IndexedType.Id` used by the dependent-type subsystem
- Cross-module references resolving by symbol ID
- The principle "Everything [that's a symbol] has an ID"
- `docs/philosophy/stable-identifiers.md` (updated to clarify scope, not repealed)

### A.2 What dies (the pure-noise tax)

- ID block on `§L`, `§FOREACH`, `§WH`, `§DW`, `§IF`, `§TR`, `§UNSAFE`, `§FIXED`, `§SYNC`, `§USING`, `§MATCH`, `§FORALL`, `§EXISTS`
- ID block on the corresponding close tags
- The `if (endId != id) ReportMismatchedId` paths in `Parsing/Parser.cs`
- Generation of synthetic sub-block IDs in `Migration/RoslynSyntaxVisitor.cs`

### A.3 What changes format (Phase 2 only, gated)

- ULID `01J5X7K9M2NPQRSTABWXYZ12` (26 chars) → compact `a1b2c3d4e` (9 chars)
- Display in `calor ids check` output and tooling
- Internal hash table key length (negligible perf impact)

### A.4 What is explicitly out of scope

- `[CalorId]` / `[CalorSymbol]` attribute design (neither exists today; round-trip-via-attribute is a future RFC)
- Positional sub-block addressing as an identity mechanism
- MCP qualified-name lookups
- Naming-as-identity philosophy
- C# → Calor → C# round-trip stability improvements
- Sidecar files
- "IDs only on `pub`" calibrated rules

---

## 15. Appendix B — side-by-side examples

### B.1 Hello

**Today (test form, 50 cl100k tokens):**
```calor
§M{m001:Hello}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F{f001}
§/M{m001}
```

**Phase 1 (test form, ~46 cl100k tokens; -8%):**
```calor
§M{m001:Hello}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F{f001}
§/M{m001}
```
*(`hello` has no sub-blocks, so Phase 1 alone shows no change here. The savings appear on programs with sub-blocks.)*

**Phase 1+2 (test form, ~46 cl100k tokens; -8%):**
```calor
§M{m_a1b2c3d4e:Hello}
§F{f_a1b2c3d4e:Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F{f_a1b2c3d4e}
§/M{m_a1b2c3d4e}
```
*(Test files keep the short numeric form `m001`. Phase 2 compacts production ULIDs only.)*

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

**Phase 1+2 (production form, ~62 cl100k tokens; -46%):**
```calor
§M{m_a1b2c3d4e:Hello}
§F{f_a1b2c3d4e:Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F{f_a1b2c3d4e}
§/M{m_a1b2c3d4e}
```

### B.2 IsPrime (sub-blocks, nested ifs)

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
*Symbol IDs (`m001`, `f001`) preserved. Sub-block IDs (`if1`, `for1`, `if2`, `if3`) dropped. Total drop: 4 open IDs + 4 close IDs = 8 ID blocks, ~25 tokens.*

**Phase 1+2 (production form, would drop ~120 tokens from today's production-form measurement):**
```calor
§M{m_a1b2c3d4e:IsPrimeDemo}
§F{f_a1b2c3d4e:IsPrime:pub}
  §I{i32:n}
  §O{bool}
  §IF (< n 2) → §R BOOL:false §/I
  §L{i:2:n:1}
    §IF (== (% n i) 0) → §R BOOL:false §/I
    §IF (> (* i i) n) → §R BOOL:true §/I
  §/L
  §R BOOL:true
§/F{f_a1b2c3d4e}
§/M{m_a1b2c3d4e}
```

### B.3 Divide with contract (where ID identity matters most)

**Today (test form, 81 cl100k tokens):**
```calor
§M{m001:DivideDemo}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q{message="divisor must not be zero"} (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
```

**Phase 1 (test form, unchanged from today — no sub-blocks):**
```
[same as today]
```

**Phase 1+2 (production form, ~50% reduction on the IDs):**
```calor
§M{m_a1b2c3d4e:DivideDemo}
§F{f_a1b2c3d4e:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q{message="divisor must not be zero"} (!= b 0)
  §R (/ a b)
§/F{f_a1b2c3d4e}
§/M{m_a1b2c3d4e}
```

The `§Q` precondition is unchanged. The Z3 cache key for proving `(!= b 0)` is `f_a1b2c3d4e:Q1` instead of `f_01J5X7K9M2NPQRSTABWXYZ12:Q1` — same structure, shorter string, **identity preserved.**

---

## 16. Decision

To be made by repo maintainer.

**Recommended action:**

1. **Approve v2 Phase 1 for immediate implementation** (single-release hard break, ~2 weeks, no measurement required).
2. **Approve v2 Phase 2 as gated** (implement on a branch behind a flag, run the §10 experiment, ship if-and-only-if the gate passes).
3. **Approve §8.3 (diagnostic addressing) as a separate small RFC** to ship before Phase 1.
4. **Mark v1 [`path-2-drop-ids.md`](./path-2-drop-ids.md) as superseded** by this document. Keep v1 in tree as the historical record (with both critiques) — it documents a real design exploration and the reasoning that produced v2.

---

*v2 path: `docs/plans/path-2-drop-ids-v2.md`*
*Status: ready for adversarial review. Reviewers: please attack this document the way the critique authors attacked v1. If v2 survives, it ships; if it does not, v3.*
