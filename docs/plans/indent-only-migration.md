# Indent-only Calor — Migration Plan

> Status: ROADMAP — Phase 0 (canonical prompt updates) in progress; Phases 1–5 pending.
> Owner: feature/indent-only branch
> Driver: H1 v3 replication (commit e8c4cf7 on rfc/phase-3-indent) showed indent form
> matches closer form on agent write pass rate (+5.7pp, p=0.61) when chain syntax is
> explicitly taught, AND saves 15–17% on tokens and source bytes. User decided to
> remove closer form entirely.

## Final state

- Calor source is INDENT-FORM only — Python-style 2-space indentation defines blocks
- No `§/F`, `§/M`, `§/I`, `§/L`, `§/CL`, `§/MT`, etc. closers anywhere in the language
- `§IF{id}` still requires `{id}` — chain continuations `§EI`, `§EL` sit at parent indent
- All in-repo `.calr` files (~1700) are in indent form
- All canonical agent prompts teach indent-only syntax
- The closer-form parser/lexer/emitter code paths are removed

## Phase 0 — Canonical prompt updates (NOT-YET-FLIPPED)

**Status:** Partially done — chain-syntax teaching added to MCP primer.

For now, the primer/CLAUDE.md still document closer form because the COMPILER still
expects closer form. The v3 chain-syntax teaching (§IF{id} required, §EL not §K, etc.)
benefits closer form too: closer pass rate went 85.7% → 91.4% in v3 vs v2.

**Done:**
- `src/Calor.Compiler/Mcp/McpMessageHandler.cs > GetPrimerContent()` — added explicit
  CHAIN STATEMENTS section, CONTRACTS clarification, EXPRESSIONS section.

**Remaining for Phase 0 (still closer-form, do later):**
- CLAUDE.md.template — add chain-syntax teaching to the quick syntax reference
- `.github/copilot-instructions.md` — same
- Root `CLAUDE.md` — same

When Phase 4 (compiler indent-only) lands, all of these will be REWRITTEN to indent form.

## Phase 1 — Parser refactor

**Goal:** parser accepts indent form (DEDENT) in lieu of explicit closers (§/F, §/M, etc.)

**Approach:** introduce two helpers in Parser.cs:
```csharp
private bool IsBlockEnd(TokenKind explicitCloser)
    => Check(explicitCloser) || Check(TokenKind.Dedent);

private Token ConsumeBlockEnd(TokenKind explicitCloser) { ... }
```

Then mechanically replace the ~152 `Expect/Check(TokenKind.EndX)` sites with calls
to these helpers. After this phase BOTH forms work — additive change, zero breakage.

**Scope:** ~30 distinct EndX token kinds across Parser.cs lines 120, 212, 338, 442,
448, 529, 630, 635, 893, 902, 914, 1159, 1172, 1210–1211, 3213, 3244, 3297, 3302,
3312, 3559, 3775, 3777, 3826, 3828, 3855, 3857, 3919, 3921, 3979, 4009, 4020, 4034,
4050, 4068, 4078, 4174, 5550, 8940, … (152 total)

**Effort:** 4–6 hours focused + verification.

**Lexer side:** also switch all 16 callers of `lexer.TokenizeAll()` to
`lexer.TokenizeWithIndentAll()`. Files: Commands/ConvertCommand, FormatCommand,
HookCommand, IdsCommand, LintCommand, EffectsCommand; Mcp/Tools/CheckTool,
FormatTool, RefineTool, CalorSourceHelper; Evaluation/Core/EvaluationContext;
Program.cs; Parser.cs (2 inner uses).

## Phase 2 — Migrate in-repo .calr files to indent form

**Goal:** every `.calr` file in the repo is indent-form.

**Counts:**
- samples/: 11
- src/: 10
- tests/Calor.Compiler.Tests: 574
- tests/Calor.Conversion.Tests: 165 (these are golden snapshots — care needed)
- tests/Calor.Enforcement.Tests: 36
- tests/Calor.Evaluation: 432
- tests/E2E: 59
- tests/TestData: 275
- Total in-repo: ~1700 files (excludes ILSpy/QuickLook/benchmarks at 191 files which
  are external corpus)

**Tooling:** `scripts/calor_indent_xform.py to_indent()` — proven 100% compile-equivalent
on baseline-compilable samples, 89.9% byte-equivalent round-trip on full corpus.

**Process:**
1. Run migrator over each directory in turn, building after each
2. For conversion snapshot tests in tests/Calor.Conversion.Tests/TestData/: regenerate
   snapshots after CalorEmitter is updated in Phase 3
3. Manual fixups for the ~10% files the migrator doesn't round-trip cleanly

**Effort:** 4–6 hours including fixups + test runs.

## Phase 3 — Update emitters

**Files:**
- `src/Calor.Compiler/CodeGen/CSharpEmitter.cs` — UNAFFECTED (emits C#, not Calor)
- `src/Calor.Compiler/Migration/CalorEmitter.cs` — currently emits closer form;
  refactor to emit indent form (no §/X closers; indent body 2 spaces per level)
- `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs` — usually delegates to
  CalorEmitter; verify and adjust

**Effort:** 3–4 hours including snapshot test rebaselines.

## Phase 4 — Subtractive: remove closer form

**Goal:** closer-form syntax becomes a parse error.

- Remove `TokenKind.End*` recognition from Lexer.cs (or keep tokens but emit
  Calor01XX diagnostic when encountered)
- Remove the closer branch of `IsBlockEnd` helper
- Add migration diagnostic: "Calor07XX: §/F is no longer supported. Use indent form."
- Remove `TokenizeAll()` (closer-form-only entry) from Lexer.cs

**Effort:** 2 hours.

## Phase 5 — Documentation pass

**Files:**
- `CLAUDE.md` (root) — rewrite syntax reference table to indent form
- `.github/copilot-instructions.md` — same
- `src/Calor.Compiler/Resources/Templates/CLAUDE.md.template` — same
- `src/Calor.Compiler/Mcp/McpMessageHandler.cs > GetPrimerContent()` —
  rewrite all examples to indent form
- `src/Calor.Compiler/Mcp/McpMessageHandler.cs > GetTagCatalogJson()` —
  replace each `"close":"§/X{id}"` with `"close":"(dedent — indent ends block)"`
- All template CLAUDE.md files under `bench/orderflow/templates/*/CLAUDE.md`
- `docs/` — syntax reference, philosophy, any guide showing Calor syntax
- `editors/vscode/` — snippets, language grammar
- `README.md` — if it has Calor examples

**Effort:** 3–4 hours.

## Phase 6 — Verification

- Full `dotnet build` green
- Full `dotnet test` green
- E2E agent task suite passes 2/3 runs (per CLAUDE.md gate)
- Re-run H1 v3 replication harness in indent-only mode to confirm no regression

## Total estimated effort

15–25 focused hours, ideally spread across 3–5 working sessions with build/test
verification at each phase boundary. Should NOT be attempted in a single autopilot
session — risk of leaving the repo in a broken intermediate state is too high.

## Branch strategy

- `feature/indent-only` — this branch, off main
- Cherry-picked from rfc/phase-3-indent:
  - cd01e48 (lexer INDENT/DEDENT pass) — DONE on this branch as 6a70d9f
- Final delivery: PR to main, single squash-merge once all 6 phases complete

## Out of scope

- Productionizing `calor fix --to-indent` / `--from-indent` as user-facing CLI commands.
  User confirmed: "this language hasn't shipped yet, we don't need to migrate any code."
  The `scripts/calor_indent_xform.py` script is used internally for Phase 2 then deleted.
