# Phase 3 — Next-Session Handoff

Drop this prompt verbatim into a fresh Claude Code session pointed at `C:\Users\juanrivera\sources\repos\juanmicrosoft\calor-2`:

---

## Prompt

You are picking up Phase 3 of the autonomous Calor-vs-C# research program. Read the following in order before starting:

1. `docs/plans/research-phase-3/README.md` — what this phase tests and why
2. `docs/plans/research-phase-2/milestone-7-t3-results.md` — Phase 2 result (null) that triggered this phase
3. `docs/plans/research-phase-0/scoring-rubric-v3.md` and `research-phase-1/scoring-rubric-v4.md` — measurement rules (still authoritative; v4 is the most recent)
4. `docs/plans/research-phase-0/scaffold-spec.md` — the wholesale-orders domain
5. `docs/plans/research-phase-0/methodology-changelog.md` — full provenance of decisions

**Mandate confirmed by user:** $10,000 program budget. Currently ~$185 spent. Full control of Calor language changes. Decide and execute autonomously — do not ask permission for individual steps. Save user-feedback memories where they apply across sessions.

**Phase 3 hypothesis (not yet tested):** *Calor's machine-verified annotation regime (`§E{}`, `§Q`, `§S` enforced by the compiler) helps coding agents avoid invariant-violating bugs on multi-file maintenance tasks, where unverified annotations did not (Phases 0–2, all null primary).*

**Decision rules (from scoring-rubric-v4):** primary cell median QualityRatio_Calor / QualityRatio_bare-C#:
- ≥ 1.50× → strong → Phase 4 confirmatory at N=20
- 1.20×–1.50× → suggestive → run more cells
- 0.80×–1.20× → null → **Calor program disconfirmed at this scaffold scale; close with negative finding**
- < 0.80× → negative → close immediately

A null at this point is decisive given prior phase results.

## What's already done

- `bench/research-phase-3/csharp-base/` is a copy of `csharp-baseline` with `Money.cs` and `Sku.cs` already rewritten as plain classes with explicit ctors + equality operators (avoids CS1729). The directory currently fails to build because entity properties of type `Sku`/`Money` need updating — see Step 1 below.
- Recon confirmed plain-class idiom roundtrips cleanly through `calor convert` + `calor` compile.
- Three Calor v0.5.0 emitter bugs documented in `docs/plans/research-phase-0/milestone-2-b4-finding.md` — work around them by avoiding the breaking patterns rather than fixing the compiler.

## Step-by-step plan (~15–20 hours; do as much as fits cleanly)

### Step 1 — finish simplifying csharp-base (~2 hours)

In `bench/research-phase-3/csharp-base/src/WholesaleOrders.Domain/Entities/`:

- `InventoryItem.cs`, `OrderLineItem.cs`, `StockReservation.cs`: change `public Sku Sku { get; init; }` to `public required Sku Sku { get; init; }` (C# 11 `required` modifier). Same fix for the other non-nullable reference type properties (Customer fields, etc).
- `Payment.cs`: same fix for `public Money Amount { get; init; }`.
- `Order.cs`: rewrite `LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice)` as a `foreach` loop accumulating into a `decimal sum`. Same fix for `OrderService.cs RecalculateTotal` and `EstimateTotal` (avoids CS8917 lambda hoisting).

Verify: `dotnet build` clean (0 warnings, 0 errors with `TreatWarningsAsErrors`); `dotnet test` 42/42 pass.

Commit: `research: phase 3 step 1 — simplify scaffold to Calor-compatible idioms`

### Step 2 — create the bare-C# arm for Phase 3 (~10 min)

```bash
cp -r bench/research-phase-3/csharp-base bench/research-phase-3/csharp-bare
# Strip annotation comments (// PURE / // EFFECTS / // PRECONDITION / // POSTCONDITION) using same pattern as Phase 0
```

Use the Python regex pattern from milestone-3-pivot to strip annotation comment lines. Verify `csharp-bare` still builds and tests pass.

Commit: `research: phase 3 step 2 — bare arm`

### Step 3 — convert csharp-base to Calor (~3 hours)

```bash
cp -r bench/research-phase-3/csharp-base bench/research-phase-3/calor-arm
cd bench/research-phase-3/calor-arm
calor migrate src --skip-verify    # converts each .cs to .calr alongside
```

Then for each .calr in src/, manually:

1. Add `§E{...}` to every method declaring its effects:
   - Pure methods: `§E{}`
   - Throws: `§E{throw}`
   - Repository reads: `§E{db:r}`
   - Repository writes: `§E{db:w}`
   - Logging: `§E{log}`
   - Combinations: `§E{db:r,db:w,log,throw}`
2. Add `§Q (precondition)` and `§S (postcondition)` where the C# `// PRECONDITION:` / `// POSTCONDITION:` comments said anything specific.
3. Use `calor compile <file>.calr -o <file>.g.cs --enforce-effects` to validate. **Effect violations should error here** — that's the point of this phase.
4. Once each file compiles cleanly under `--enforce-effects`, delete the corresponding `.cs` file.

Set up `.calor-effects.json` at the project root declaring effects of external dependencies (DB, logger). See `samples/Contracts/` in this repo for a working example.

Verify: all 42 tests pass through the .g.cs files. `dotnet build && dotnet test`.

Commit: `research: phase 3 step 3 — Calor variant with §E/§Q/§S declarations`

### Step 4 — write a Phase 3 grader (~30 min)

Use `bench/research-phase-2/grade_run_t3.sh` as a template. Adapt for the Calor pipeline (need to run `calor` to regenerate .g.cs before `dotnet test` after each model edit).

Pick **T2.B (partial reservation release)** as the Phase 3 primary prompt — it was the cleanest-graded prompt across phases. Reuse `bench/research-phase-1/graders/T2.B/AcceptanceTests.cs`.

### Step 5 — run trials (~3 hours wall clock, parallel)

N=5 × 1 prompt × 2 arms = 10 trials. Just T2.B. (Adding A/C variants is an optimization for later.)

For each trial workspace, the agent prompt is the same T2.B prompt from `docs/plans/research-phase-1/phase-1-prompts.md`, with one addition for the Calor arm: tell the agent it's working on a Calor codebase and must keep the .calr files as source of truth. The agent runs `calor compile` to regenerate .g.cs and then `dotnet test`.

Sample prompt block to pass to each Calor-arm agent:

> You are a coding agent working on a Calor wholesale order processing service... [paste T2.B prompt]
> 
> Important: This codebase uses Calor (.calr) source files that compile to C# (.g.cs). Edit the .calr files only. After each edit, run `calor compile <changed-file>.calr -o <same>.g.cs --enforce-effects` to regenerate. The compiler will reject changes that violate the declared effects (§E{}) — fix those errors before proceeding. Build with `dotnet build` and test with `dotnet test`.

### Step 6 — grade and report (~1 hour)

Apply the T2.B grader. Compute median Quality ratio Calor / bare-C#. Apply v4 decision rules:

- ≥ 1.50×: write `milestone-8-phase3-strong-positive.md`. Recommend Phase 4 confirmatory study (N=20, ~$1,500).
- 1.20×–1.50×: write `milestone-8-phase3-suggestive.md`. Run T2.A and T2.C variants in same shape (~$120 more) before deciding.
- 0.80×–1.20×: write `milestone-8-phase3-null-program-closed.md`. Stop the program. Calor's full proposition disconfirmed at this scale.
- < 0.80×: write `milestone-8-phase3-negative.md`. Stop. Document that Calor introduces friction without benefit.

In all cases, commit and end the program for now.

## What NOT to do

- Don't fix Calor compiler bugs. The user authorized "language changes" but the time budget is for research, not compiler debugging. Work around the bugs by simplification.
- Don't expand to T1, T2.A, T2.C in Phase 3 unless T2.B shows ≥1.20× median ratio. Avoid proliferating cells when the primary cell is null.
- Don't run more than ~$500 of Phase 3 trials. If costs exceed that, halt and reassess.
- Don't skip the `--enforce-effects` flag. The whole point is testing enforcement.

## Memory to update on completion

Append to `~/.claude/projects/.../memory/MEMORY.md` once Phase 3 result is in:
- A reference memory pointing to `docs/plans/research-phase-3/milestone-8-*.md` describing the program's final outcome.
- If null/negative: a project memory noting the Calor program was tested across 4 phases and produced no signal at the 1.5× threshold.

## Open questions to resolve as they arise

1. **`.calor-effects.json` manifest format.** I haven't authored one for this scaffold. Look at `samples/` for a working example or run `calor` with `-v` to see what manifest entries it expects.
2. **MCP tool latency in agent runs.** The Calor compile cycle adds ~1–3 seconds per edit. May increase agent turn counts vs C# arm — track and report; if it becomes a confound, document in milestone.
3. **Cost asymmetry.** Per-trial cost on Calor arm could be 1.5–2× higher than C# bare due to the compile cycles. v4 rubric's CostEfficiency split handles this; report both Quality ratio and CostEfficiency ratio.

## End-of-session honest assessment

If you can't get Step 1 + Step 2 done cleanly, stop and write a milestone summarizing what was tried. Don't push through Steps 3–5 with a broken scaffold — that's the trap I avoided in the previous session, and it's the right call here too.
