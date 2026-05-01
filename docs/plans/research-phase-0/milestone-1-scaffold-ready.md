# Milestone 1 — Scaffold Ready

**Date:** 2026-05-01
**Phase:** 0
**Status:** scaffold ✅ / dry-run pending / Calor port pending

## What's done

- **Pre-registration locked** at `docs/plans/research-phase-0/`: README, scaffold-spec, scoring-rubric, t1-maintenance-prompts, cost-budget. Five docs, all committed before any model run.
- **C# wholesale-orders scaffold built** at `bench/research-phase-0/csharp-baseline/`:
  - 45 `.cs` files + Directory.Build.props + 1 .slnx + 5 .csproj = 52 files
  - 2,160 lines of C#
  - 5 projects (Domain, Services, Infra, Api, Tests)
  - `dotnet build` clean with `TreatWarningsAsErrors=true`: **0 warnings, 0 errors**
  - `dotnet test`: **41/41 passing, 591ms**
- **Invariants encoded** as 7 named tests (INV-1 through INV-7) in `InvariantTests.cs`
- **State machines encoded** as 10 named tests in `StateTransitionTests.cs`
- **Seeded realistic mess** present: all 7 of MESS-1 through MESS-7 implemented and documented inline

## Confidence assessment

Confidence on Phase 0 design and scaffold: **High (~85%)**. Up from Medium-High because:

- Scaffold builds clean → architecture is sound
- 41 tests pass → invariants and state machines are testable, not just paper claims
- 0 warnings on `TreatWarningsAsErrors` → seeded mess is encoded with explicit `#pragma` suppressions, not hidden under loose lint settings
- File count (52 incl. project files, 45 `.cs`) matches spec target (~46)

The remaining 10% is empirical risk:

| Risk | Mitigation in place | Open |
|------|---------------------|------|
| Per-run cost exceeds $25 cap | Budget envelope + hard cap encoded in rubric | Cost not measured |
| Models find T1.A trivial (no differentiation between langs) | Three prompts at different difficulty levels (T1.A/B/C) | Need to see one run to gauge |
| Calor compiler chokes at 45-file scale | None yet | Need port to find out |
| Tests don't catch realistic regressions | 41 tests + adversarial post-hocs in rubric | Need to see what models break |

## What's next

| # | Task | Cost est. | Blocking |
|---|------|-----------|----------|
| 1 | Write feature-acceptance test files for T1.A/B/C in `graders/` (tested through API surface, not bound to model's type names) | $0 | Required for scoring |
| 2 | Run T1.A dry-run on Opus 4.7 (one trial) — measure turns/tokens/$ | $5–25 | Validates cost envelope |
| 3 | Convert C# scaffold to Calor (mechanical port via `calor_convert`) | $0 model + my time | Validates Calor tooling at scale |
| 4 | Lock the rubric (commit), then run N=5×3×2 = 30 trials | ~$575 | Phase 0 main result |

## Decisions deferred to user

1. **Commit the scaffold to git as `phase-0-baseline` tag?** I haven't committed because new commits need explicit authorization. Recommend committing now so subsequent runs branch off a stable base.
2. **Should the dry-run be a sub-agent in this conversation, or a fresh Claude Code session?** A fresh session gives cleaner cost measurement. A sub-agent is faster but conflates token accounting.

## Files added in this milestone

```
docs/plans/research-phase-0/
  README.md
  scaffold-spec.md
  scoring-rubric.md
  t1-maintenance-prompts.md
  cost-budget.md
  milestone-1-scaffold-ready.md   ← this file

bench/research-phase-0/csharp-baseline/
  Directory.Build.props
  WholesaleOrders.slnx
  src/
    WholesaleOrders.Domain/        4 enums + 2 value objects + 7 entities + .csproj
    WholesaleOrders.Infra/         5 repos + 1 dbcontext + 1 logger + .csproj
    WholesaleOrders.Services/      5 services + 3 validators + .csproj
    WholesaleOrders.Api/           4 controllers + 2 middleware + Program + DepReg + .csproj
  tests/
    WholesaleOrders.Tests/         9 test files + .csproj
```
