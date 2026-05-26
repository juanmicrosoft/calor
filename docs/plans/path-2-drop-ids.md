# RFC: Path 2 — Drop ULIDs, Names Are Identifiers

**Status:** Draft
**Author:** TBD (with Copilot CLI)
**Created:** 2025-11-25
**Target release:** Pre-1.0 breaking change (0.x → 0.x+1)
**Related:** [`bench/phase0/`](../../bench/phase0/), [`docs/philosophy/stable-identifiers.md`](../philosophy/stable-identifiers.md), [`docs/philosophy/design-principles.md`](../philosophy/design-principles.md) §3

---

## 1. Summary

Remove ULID-based identifiers from Calor source. Use **qualified names** (`Calculator.Divide`) as the canonical address for every declaration. Sub-block constructs (loops, ifs, try/catch) lose their IDs entirely — their addressing becomes positional and is computed by tooling on demand.

Repeals design principle #3 "Everything Has an ID." Replaces it with **"Everything is addressable."**

This is a breaking change to the surface syntax. The semantics, type system, contract system, effect system, Z3 verification, and runtime behavior are unchanged.

---

## 2. Motivation

### 2.1 The token tax is unjustifiably high

Phase 0 benchmark (5 small tasks, cl100k tokenizer):

| Variant | Tokens | vs zerolang |
|---|---:|---:|
| Calor today | 485 | 1.82× |
| Calor Path 2 (proposed) | 389 | 1.46× |
| zerolang | 266 | 1.00× |

**Path 2 saves 20% of Calor's tokens** (96 of 485) — purely from removing IDs. On the smallest tasks (hello, add) the savings reach 25%. On real-logic tasks like `fizzbuzz`, Path 2 brings Calor to ~parity with zerolang (1.04× tokens) while preserving Calor's effect declarations, contracts, and typed I/O.

The residual 1.46× tax represents what the ID-free Calor pays for `§Q` contracts, `§E` effects, `§I`/`§O` typed parameters, and the `§` sigil. That tax buys something — Z3 proofs, taint analysis, hosted-capability tracking — that zerolang structurally cannot offer.

The ID tax buys nothing comparable. See §2.2.

### 2.2 The economic case for ULIDs is weak

The four claimed ULID benefits (from `docs/philosophy/stable-identifiers.md`):

1. **"Unambiguous reference"** — but `Calculator.Divide` is already unambiguous within a module-scoped language. The ambiguity ULIDs solve (`Calculate` vs `validateUser` vs renamed-to-`Compute`) is a *cross-version* problem (history) not a *cross-call* problem (current code). It's solved at the tool layer (git, LSP rename, codemod), not the language layer.
2. **"Survives rename"** — this is a refactoring problem, not a programming problem. Modern LSP-based rename is atomic; you don't need IDs for atomicity, you need a tool that visits all references. Calor already has `calor fix --rename`.
3. **"Merge safety"** — name-based merge is a solved problem (every other language). The actual merge failures ULIDs prevent are exotic and rare; the daily merge cost ULIDs *add* (every block has an identifier that two branches may regenerate differently) is constant.
4. **"Traceable history"** — git already provides this at the entity level (`git log --follow`), and the symbol-name approach works as well as the ID approach in practice. Tools like Sourcegraph index by name path successfully across the entire OSS ecosystem.

### 2.3 The LLM economics make IDs hostile

Random ULIDs (`f_01J5X7K9M2NPQRSTABWXYZ12`) tokenize at roughly **one character per token** because they contain no natural-language patterns the BPE merges recognize. A 28-character ULID with prefix costs ~28 tokens vs ~3 tokens for the meaningful function name. The LLM gets zero semantic benefit from an ID it cannot reason about — it has to maintain a parallel name-to-ID mapping in its working memory.

The original argument was "the agent can edit `f_01J5X7…` unambiguously." In practice, the agent reads the file, finds the function by *name*, and then has to copy the ID character-by-character. That's not a feature; that's a tax on every edit.

### 2.4 What we keep, what we drop

| Surface element | Today | Path 2 |
|---|---|---|
| Module declaration | `§M{m001:Calculator}` | `§M{Calculator}` |
| Function declaration | `§F{f001:Add:pub}` | `§F{Add:pub}` |
| Class declaration | `§CL{c001:User}` | `§CL{User}` |
| Method declaration | `§MT{mt001:save:pub}` | `§MT{save:pub}` |
| Constructor | `§CTOR{ctor001:pub}` | `§CTOR{pub}` |
| Loop | `§L{for1:i:1:10:1}` | `§L{i:1:10:1}` |
| Conditional | `§IF{if1} (cond) … §/I{if1}` | `§IF (cond) … §/I` |
| Try block | `§TR{try1}` … `§/TR{try1}` | `§TR` … `§/TR` |
| Refinement type | `§RTYPE{rt001:PosInt}` | `§RTYPE{PosInt}` |
| Proof obligation | `§PROOF{pf001:DivisorNotZero}` | `§PROOF{DivisorNotZero}` |
| Effect declaration | `§E{cw,db}` | `§E{cw,db}` — unchanged |
| Precondition | `§Q (> x 0)` | `§Q (> x 0)` — unchanged |
| Postcondition | `§S (>= result 0)` | `§S (>= result 0)` — unchanged |

**Names are required where they exist today.** Modules, functions, classes, methods, properties, enums, refinement types must still have a name — that's already how the code is identified by humans and LLMs. Path 2 just makes the name the *sole* identifier, removing the redundant ULID.

**Sub-blocks are nameless.** Loops, ifs, try blocks were never named, only ID'd. Their addressing becomes positional and is computed by tooling.

---

## 3. Goals & non-goals

### Goals

- **G1.** Reduce token cost of Calor source by 15–25% with zero semantic change.
- **G2.** Eliminate the cognitive tax on LLMs of maintaining name-to-ULID mappings.
- **G3.** Eliminate IdScanner / IdValidator / IdGenerator as required code paths in the compile pipeline. Their presence becomes optional metadata (see §6.4).
- **G4.** Preserve full backward compatibility for *reading* existing Calor with IDs during a transition period; the parser accepts both forms.
- **G5.** Ship an automatic migrator (`calor fix --drop-ids`) that converts an existing codebase to Path 2 syntax in one command.

### Non-goals

- **NG1.** This RFC does not change the semantics, type system, contract system, effect system, Z3 verification, runtime, or generated C# in any user-visible way.
- **NG2.** This RFC does not introduce optional IDs as an ongoing supported feature (see §11 for why). The transition period is finite.
- **NG3.** This RFC does not change C# → Calor migration output beyond removing IDs from emitted Calor.
- **NG4.** This RFC does not address the broader "should Calor compete with C# at all?" question (separate discussion).

---

## 4. Current state

### 4.1 Where IDs exist

Today the `IdScanner` (`src/Calor.Compiler/Ids/IdScanner.cs`) recognizes IDs on these declaration kinds (the `IdKind` enum):

```
Module, Function, Class, Interface, Property, Method, Constructor,
Enum, EnumExtension, OperatorOverload, Indexer,
RefinementType, ProofObligation, IndexedType
```

These are all **symbol-level** declarations — things you would put in a symbol table.

Additionally, the AST carries IDs on sub-block constructs (loops, ifs, try blocks) but **the IdScanner does not collect them**. They were never canonical identifiers; they're purely structural.

### 4.2 ID format

`IdValidator` enforces:

- **Production IDs:** `{prefix}_{ULID}` where ULID is 26 chars of Crockford Base32. Example: `f_01J5X7K9M2NPQRSTABWXYZ12`.
- **Test IDs:** `{prefix}{digits}` in files under `tests/`, `docs/`, `examples/`. Example: `f001`.

The prefix table (from `IdGenerator`): `m_`, `f_`, `c_`, `i_`, `p_`, `mt_`, `ctor_`, `e_`, `op_`.

### 4.3 Diagnostics tied to IDs

`Diagnostic.cs` defines diagnostic codes `Calor0800–0805`:

- `Calor0800` — missing ID
- `Calor0801` — invalid format
- `Calor0802` — wrong prefix for declaration kind
- `Calor0803` — duplicate ID
- `Calor0804` — test ID used in production
- `Calor0805` — ID churn (existing ID was modified)

All six become irrelevant under Path 2; see §9.

### 4.4 ID-related CLI surfaces

- `calor ids assign` — generates ULIDs for declarations that lack them
- `calor ids check` — validates IDs, detects churn, duplicates, missing
- `calor format --ids` — alternate spelling, same purpose
- `calor hook validate-ids` — pre-write hook

### 4.5 Round-trip via [CalorId] attribute

The C# emitter writes `[CalorId("f_01ABC…")]` on every generated member. The Roslyn → Calor converter reads it back. This is how identity survives `calr → cs → calr` round-trips today.

---

## 5. Proposed state

### 5.1 Addressing model

The canonical address of any declaration is its **dotted qualified name** from the module root:

```
Calculator                       // module
Calculator.Divide                // function
Calculator.User                  // nested class
Calculator.User.save             // method
Calculator.User.Email            // property
Calculator.PosInt                // refinement type
Calculator.Divide.DivisorNotZero // proof obligation (named)
```

Sub-block addresses are **computed paths**, not source-visible identifiers:

```
Calculator.Divide.body[3]                  // the 4th statement
Calculator.Divide.body[3].then.body[0]     // the first statement of its then-branch
Calculator.Main.body[0].loop.body[2]       // 3rd statement of the loop body
```

Tooling (LSP, MCP `calor_navigate`) accepts both. Diagnostics emit both: `Calor0501 at Calculator.Divide.body[3] (file:line)`.

### 5.2 Uniqueness rules

- Within a module, **all sibling declarations must have unique names**. (Already true today modulo overload resolution.)
- Method overloads continue to use signature-based disambiguation: `Calculator.Add(i32,i32)` vs `Calculator.Add(f64,f64)`. The fully-qualified-with-signature form is used in tooling output when needed.
- Constructors disambiguate by signature: `Calculator.User.ctor(str)` vs `Calculator.User.ctor()`.
- Operator overloads address by token: `Calculator.Vector.op_plus`.

### 5.3 Cross-module references

A reference from module `Reports` to a function in module `Calculator` uses the qualified path:

```
Calculator.Divide
```

There is no global namespace beyond modules. Module names must be unique within a project (already enforced).

### 5.4 Rename refactoring

The MCP/LSP layer ships `calor fix --rename --from Calculator.Add --to Calculator.Plus`. This:

1. Parses every `.calr` file in the project.
2. Finds the declaration matching the `--from` qualified name.
3. Renames the declaration and *every reference to it* in one atomic operation.
4. Writes all files. The operation is all-or-nothing (uses a temp directory + atomic swap).

This is functionally what `calor fix --rename` does today, except it operates on names instead of ULIDs. The implementation cost is approximately the same; the agent's UX is better because it can issue rename commands in natural-language form.

### 5.5 Git history & blame

Names instead of ULIDs means git follows entities by name. `git log --follow path.calr` works. When a function is renamed, the rename refactoring should produce a *single commit* whose message records both names; tooling can encourage this with a commit hook (`calor fix --rename --commit`).

This is the same behavior every other language has used successfully for decades.

---

## 6. Detailed changes

### 6.1 Grammar changes

The lexer rule for declaration-opening tags becomes:

```
§M    { Name }
§F    { Name : visibility }
§CL   { Name }
§IFACE{ Name }
§MT   { Name : visibility }
§PROP { Name : type }
§CTOR { visibility }
§EN   { Name }
§OP   { token : visibility }
§RTYPE{ Name }
§PROOF{ Name }
```

Sub-block opening tags drop the ID block entirely (the loop variable spec moves to position 0):

```
§L  { var : from : to : step }
§IF (cond) → …
§WH (cond) →
§TR
```

Closing tags become bare:

```
§/M  §/F  §/CL  §/IFACE  §/MT  §/CTOR
§/L  §/I  §/W  §/TR
```

(No ID block follows. Indentation and tag-nesting determine the match; the parser already tracks the open stack.)

### 6.2 Lexer / parser changes

- `Parsing/Lexer.cs`: no token-kind changes (the tag tokens are unchanged); the attribute parser inside `ParseAttributes` no longer reserves the `_pos0` slot for an ID on these tags.
- `Parsing/Parser.cs`: for each declaration tag, `ParseAttributes` is called with a new `expectsId: false` flag (or equivalent). The Name moves from `_pos1` to `_pos0`.
- For backward compatibility during transition: parser accepts both forms. If `_pos0` looks like an ID (prefix matches `m_`, `f_`, etc.), it's parsed as the legacy form and the Name is at `_pos1`. Otherwise the new form. The parser emits `Calor0806` (deprecation warning) on legacy form.

### 6.3 AST changes

- Every node that today has an `Id` string field keeps the field — but it becomes **nullable** (`string?`).
- For new code, IDs are always null. For legacy code parsed during transition, IDs are populated as before.
- Binder treats `null` IDs as expected (no diagnostic).

### 6.4 IdScanner / IdValidator / IdGenerator

These three classes become **optional metadata producers**:

- `IdScanner.Scan(...)` still works but returns only entries for nodes with non-null IDs.
- `IdGenerator.Generate()` is exposed via `calor ids assign --legacy` for users who still want ULIDs for some reason (e.g., proprietary tooling that depends on them). Off by default. Removed entirely in a later release.
- `IdValidator` is only invoked when `calor ids check` is called. The `Calor0800–0805` diagnostics are still defined but emitted only by that command, not by the default compile pipeline.

### 6.5 Visitor implementations

Per `CLAUDE.md` §"Adding New AST Nodes" — every IAstVisitor implementer must handle the nullability:

- `CodeGen/CSharpEmitter.cs` — when emitting `[CalorId(...)]`, skip the attribute when ID is null. (See §6.6 for the alternative.)
- `Migration/CalorEmitter.cs` — never emit IDs in new code. The `EmitCalor` settings drop `IncludeIds` support; it's hard-coded false.
- `Ids/IdScanner.cs` — already skips nodes with null/empty IDs (the existing pattern for RefinementType / ProofObligation works).
- `Verification/ExpressionSimplifier.cs` — no change (doesn't touch IDs).

### 6.6 Round-trip strategy: `[CalorId]` → `[CalorSymbol]`

The C# emitter today writes `[CalorId("f_01ABC...")]` to enable round-trip identity preservation. Under Path 2:

**Option A (recommended):** Replace `[CalorId]` with `[CalorSymbol("Calculator.Divide")]`. The Roslyn → Calor converter uses the qualified name to restore the original Calor structure (e.g., to know which `using` block / module a class belongs to). This is enough for round-trip stability because names are the new identity.

**Option B:** Drop the attribute entirely; rely on the file structure (one module per file by convention, type names match Calor class names). Simpler but loses some round-trip information for complex layouts.

This RFC recommends **Option A**. The attribute is cheap (one line per emitted member) and the migration tooling already depends on it.

### 6.7 MCP tool changes

| Tool | Today | Path 2 |
|---|---|---|
| `calor_navigate` "definition" | accepts ID `f_01ABC...` | accepts qualified name `Calculator.Divide` |
| `calor_navigate` "references" | finds by ID | finds by qualified name |
| `calor_navigate` "find" | search by name (already) | unchanged |
| `calor_fix` "ids" | regenerate missing IDs | removed (or repurposed as `--drop-ids` migrator) |
| `calor_format` action="ids" | format/assign IDs | removed |
| All diagnostic-bearing tools | emit IDs in diagnostics | emit qualified names |

The change is mostly *removing* features. Net MCP surface shrinks.

### 6.8 Sample migration

All files under `samples/` are auto-migrated by running `calor fix --drop-ids samples/` once. The diff is mechanical (remove `m001:`, `f001:`, etc.) and reviewable.

### 6.9 Documentation changes

- **Repeal:** `docs/philosophy/design-principles.md` §3 "Everything Has an ID" → rewrite as "Everything is addressable."
- **Repeal:** `docs/philosophy/stable-identifiers.md` → keep as historical document with prominent "Repealed" header pointing to this RFC, or delete entirely.
- **Update:** `docs/syntax-reference/` — strip ID columns from every tag table.
- **Update:** `docs/ids.md`, `docs/cli/ids.md` — mark as deprecated/legacy.
- **Update:** `CLAUDE.md`, `.github/copilot-instructions.md` — remove the "ID prefix table" and the rule about adding IDs; replace with the simpler "names are identifiers" rule.
- **Update:** `calor://primer`, `calor://id-prefixes`, `calor://tags` MCP resources — same treatment.
- **Update:** `.claude/skills/calor-language/SKILL.md` (if it exists) — update to Path 2 syntax.
- **Add:** `docs/migration/v0.x-drop-ids.md` — how-to-migrate guide.

---

## 7. Migration plan

### 7.1 Deprecation timeline (proposed)

| Release | Behavior |
|---|---|
| **0.x** (Path 2 ships) | Parser accepts both forms. Path 2 form is the documented default. Legacy form emits `Calor0806` deprecation warning. `calor fix --drop-ids` migrator ships. All samples migrated. |
| **0.x + 1** | Legacy form is opt-in via `--allow-legacy-ids` flag. Default compile rejects it with `Calor0807`. |
| **1.0** | Legacy form removed. Parser rejects it unconditionally. `IdScanner`, `IdValidator`, `IdGenerator` deleted. `[CalorId]` attribute deleted. |

Three releases gives downstream users time to migrate. Because Calor is pre-1.0, this is well within the "breaking changes allowed" envelope per `CLAUDE.md`.

### 7.2 Migrator

`calor fix --drop-ids [path]`:

1. Parses every `.calr` file under `path`.
2. For each AST node with an ID, sets the ID to null.
3. Re-emits with `CalorEmitter` (which under Path 2 never writes IDs).
4. Writes the file back.

The migrator is idempotent. Files already in Path 2 form pass through unchanged.

### 7.3 Round-trip migration

For projects that consume the generated C# via `[CalorId]` attributes (e.g., test runners that map ID → test), the C# emitter still writes `[CalorSymbol("…")]` under §6.6 Option A. Consumers must update their attribute reads from `CalorId` → `CalorSymbol`.

This is a one-line code change in any consumer. Provide a `[CalorId]` shim attribute that forwards to `[CalorSymbol]` for one release for safety.

---

## 8. Compiler change scope

Estimated effort (single engineer, no help): **3–4 weeks**.

### Phase A — Parser & AST (week 1)

- `Parsing/Parser.cs`: dual-mode declaration parsing; deprecation warning on legacy form. ~200 line delta.
- AST nodes: make `Id` properties nullable. Mechanical; impacts ~14 node types. ~50 line delta per type ≈ 700 line delta total.
- Snapshot tests under `tests/Calor.Compiler.Tests`: many tests check exact source/AST output. Each needs a "Path 2 variant" snapshot. ~50 snapshot files affected.

### Phase B — Visitors & emitters (week 2)

- `CodeGen/CSharpEmitter.cs`: switch `[CalorId]` → `[CalorSymbol]`. ~20 line delta.
- `Migration/CalorEmitter.cs`: stop emitting IDs. Drop the `IncludeIds` option. ~30 line delta.
- `Migration/RoslynSyntaxVisitor.cs`: read `[CalorSymbol]` instead of `[CalorId]` for round-trip. ~15 line delta.
- `Ids/IdScanner.cs`: short-circuit when ID is null (already mostly does this). ~10 line delta.
- `Verification/ExpressionSimplifier.cs`: no change.

### Phase C — Diagnostics & tooling (week 3)

- `Diagnostics/Diagnostic.cs`: add `Calor0806` (deprecation warning) and `Calor0807` (error in deprecation period 2). Keep `Calor0800–0805` but only fire them in `calor ids check`.
- `Migration/FeatureSupport.cs`: no change (no new tag kinds; only attribute slots changed).
- `calor fix --drop-ids` migrator: new code path, ~200 lines.
- `calor ids assign` → `calor ids assign --legacy`: flag rename and deprecation message. ~20 line delta.
- MCP server tool registrations: update `calor_navigate`, `calor_fix`, `calor_format` argument schemas to accept qualified names. ~100 line delta.

### Phase D — Samples, docs, instructions (week 4)

- Run `calor fix --drop-ids samples/` and review diff.
- Run `calor fix --drop-ids docs/` and review diff (some docs intentionally show ID syntax — keep those tagged with `<!-- legacy -->`).
- Rewrite `docs/philosophy/design-principles.md` §3.
- Mark `docs/philosophy/stable-identifiers.md` as repealed.
- Update `CLAUDE.md`, `.github/copilot-instructions.md`, MCP resource files.
- Update `editors/vscode/` syntax highlighting grammar.
- Update website (`website/content/`).

### Risks

- **R1.** **Snapshot test churn.** ~50 golden files need new variants. Mitigate by writing a snapshot updater script that re-runs `dotnet test --update-snapshots` after parser dual-mode lands.
- **R2.** **MCP/LSP regressions.** Address-by-name has slightly different ambiguity properties than address-by-ID. Mitigate by writing a comprehensive resolver test suite in `Calor.Semantics.Tests`.
- **R3.** **Downstream consumers of `[CalorId]`.** Mitigate with the shim attribute (§7.3) for one release.
- **R4.** **Performance.** Resolving by qualified name requires a symbol table lookup vs an ID dictionary lookup. Both are O(1). No measurable impact expected.
- **R5.** **Cross-version git diffs.** Migrating an existing codebase produces a large mechanical diff. Mitigate by scheduling the migration as a single commit on its own PR, separate from any logic changes.

---

## 9. Diagnostics rework

### 9.1 Repurposed codes

| Code | Today | Path 2 |
|---|---|---|
| `Calor0800` Missing ID | Error | Removed (or only `calor ids check`) |
| `Calor0801` Invalid format | Error | Removed |
| `Calor0802` Wrong prefix | Error | Removed |
| `Calor0803` Duplicate ID | Error | Removed |
| `Calor0804` Test ID in production | Error | Removed |
| `Calor0805` ID churn | Error | Removed |

### 9.2 New codes

| Code | Description | Severity |
|---|---|---|
| `Calor0806` | Legacy ID syntax used (deprecated) | Warning |
| `Calor0807` | Legacy ID syntax used (no longer supported) | Error (post-deprecation period) |
| `Calor0808` | Duplicate sibling declaration name | Error |
| `Calor0809` | Ambiguous overload (signature-disambiguated reference required) | Error |

### 9.3 Diagnostic addressing

Today: `Calor0501: division by zero in f_01J5X7K9M2NPQRSTABWXYZ12 at line 42`

Path 2: `Calor0501: division by zero in Calculator.Divide at line 42`

The qualified name is shorter, semantically meaningful, and copy-pasteable into the LLM's next message.

---

## 10. Effort summary

| Phase | Scope | Estimate |
|---|---|---|
| A | Parser, AST nullability, snapshot tests | 5 days |
| B | Visitors, emitters, round-trip | 4 days |
| C | Diagnostics, migrator, CLI, MCP | 4 days |
| D | Samples, docs, agent instructions | 3 days |
| **Total** | | **~3 weeks** |

Plus 1 week buffer for snapshot churn, downstream feedback, and the deprecation-shim work.

**One PR strategy:** ship Phases A–C in a single PR (compiler change with dual-mode parser). Ship Phase D in a follow-up PR (samples + docs migration). This keeps the compiler change reviewable and lets samples migrate after the migrator is verified.

---

## 11. Rejected alternatives

### 11.1 Sidecar file (`*.calr.ids.json`)

Keep IDs out of source; store them in a sibling JSON file. Reduces source token cost the same as Path 2 but introduces a new problem: every operation that creates/moves/renames a declaration must update the sidecar, and the sidecar must be checked in. Identity preservation through cut-paste becomes unreliable.

**Rejected because:** the user explicitly disliked this option (preferred no extra file). Also: it doesn't reduce LLM cognitive load — the LLM still has to be aware of the parallel ID structure.

### 11.2 Optional IDs (hybrid)

IDs become optional. The lint policy decides when to add them ("only on `pub` declarations referenced cross-file"). The compiler auto-generates them on demand.

**Rejected because:** the rule "when do I need an ID?" is itself a tax — every time the agent adds or moves a declaration it must reason about visibility, cross-file references, and lint policy. This is *worse* than mandatory IDs (always present) or no IDs (never present). The middle path is the worst path.

### 11.3 Compiler-generated inline IDs

The compiler auto-assigns and inlines ULIDs after the agent writes ID-less code. The agent writes `§F{Add:pub}` and the file on disk becomes `§F{f_01J5X7...:Add:pub}` after the next save.

**Rejected because:** this trains the agent to ignore IDs, then forces the agent to re-read them whenever it edits. The IDs become source-code noise that everyone — humans and agents — learns to ignore. If everyone ignores them, why are they there?

### 11.4 Keep IDs, focus on tokenizer-friendly format

Replace ULIDs with shorter, more BPE-friendly IDs (e.g., 8-char alphanumeric). Cuts token cost ~70% per ID while preserving "everything has an ID."

**Rejected because:** the cognitive tax is independent of the token tax. The LLM still has to maintain a name-to-ID mapping for editing. Even a 4-character ID costs more cognition than zero.

### 11.5 Status quo

Keep ULIDs. Accept the 1.82× token tax. Argue that the verification dividend justifies it.

**Rejected because:** Phase 0 evidence shows the verification dividend is only on `divide`-like programs (preconditions). The token tax applies to *all* programs including ones that have no contracts. Most programs do not benefit from ULIDs at all.

---

## 12. Open questions

1. **Q1.** Should `[CalorSymbol]` be emitted on every member, or only on members the round-trip migration needs? Smaller attribute footprint vs guaranteed round-trip. **Recommendation:** all members for one release; revisit.
2. **Q2.** Method overload disambiguation in qualified names — use C# syntax (`Calculator.Add(i32,i32)`) or Calor-native (`Calculator.Add#2`)? **Recommendation:** C# syntax for consistency with `nameof()` and LSP tooling.
3. **Q3.** Constructor naming — `Calculator.User.ctor()` or `Calculator.User.new()` or `Calculator.User`? **Recommendation:** `ctor()` to match the existing IdKind.
4. **Q4.** Do we delete `Stable Identifiers` from `docs/philosophy/` or keep it with a "Repealed" header? **Recommendation:** keep with header (preserves history of the design decision).
5. **Q5.** Does `calor fix --drop-ids` overwrite source files in place, or output to a parallel tree? **Recommendation:** in-place with `--dry-run` flag.
6. **Q6.** Should the deprecation period 2 release be 0.x+1 or 0.x+2? Faster removal = less code in tree; slower removal = more downstream goodwill. **Recommendation:** 0.x+1 because we're pre-1.0 and the migrator makes upgrade trivial.

---

## 13. Appendix: side-by-side examples

### A. Hello

**Today (50 cl100k tokens):**
```calor
§M{m001:Hello}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F{f001}
§/M{m001}
```

**Path 2 (38 cl100k tokens, −24%):**
```calor
§M{Hello}
§F{Main:pub}
  §O{void}
  §E{cw}
  §P "hello"
§/F
§/M
```

### B. Divide with precondition

**Today (81 cl100k tokens):**
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

**Path 2 (69 cl100k tokens, −15%):**
```calor
§M{DivideDemo}
§F{Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q{message="divisor must not be zero"} (!= b 0)
  §R (/ a b)
§/F
§/M
```

### C. IsPrime with nested ifs and a loop

**Today (151 cl100k tokens):**
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

**Path 2 (114 cl100k tokens, −25%):**
```calor
§M{IsPrimeDemo}
§F{IsPrime:pub}
  §I{i32:n}
  §O{bool}
  §IF (< n 2) → §R BOOL:false §/I
  §L{i:2:n:1}
    §IF (== (% n i) 0) → §R BOOL:false §/I
    §IF (> (* i i) n) → §R BOOL:true §/I
  §/L
  §R BOOL:true
§/F
§/M
```

The `is_prime` example is where the structural improvement is clearest: nested ifs and loops lose 6 ID blocks (3 opens + 3 closes) totaling 24 tokens of pure structural noise.

---

## 14. Decision

To be made by repo maintainer. Recommended: **accept** and proceed with Phase A in a feature branch.
