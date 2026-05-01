# Cost Budget — Phase 0 (and beyond)

**Living document.** Updated as we measure actuals. Estimates here are starting assumptions, not commitments.

## Opus 4.7 pricing

- Input: $15 / M tokens
- Output: $75 / M tokens
- 1M-context tier on Opus 4.7 (`claude-opus-4-7[1m]`) may have premium pricing — TBD on first run

## Per-run estimate (informed by Claude Code historical usage)

For a maintenance task on a 30–50 file codebase:

| Component | Low | Mid | High |
|-----------|-----|-----|------|
| Input tokens (cumulative across turns; cache hits) | 200K | 500K | 1.5M |
| Output tokens | 10K | 30K | 80K |
| Input $ | $3.00 | $7.50 | $22.50 |
| Output $ | $0.75 | $2.25 | $6.00 |
| **Total $/run** | **$3.75** | **$9.75** | **$28.50** |

Calor runs may be more expensive (more tool calls into MCP server, longer prompts due to syntax overhead). Adjust upward by 1.3× as a starting assumption.

## Phase 0 budget breakdown

| Item | Runs | Avg $/run | Total |
|------|------|-----------|-------|
| T1.A C# | 5 | $10 | $50 |
| T1.A Calor | 5 | $13 | $65 |
| T1.B C# | 5 | $12 | $60 |
| T1.B Calor | 5 | $16 | $80 |
| T1.C C# | 5 | $15 | $75 |
| T1.C Calor | 5 | $20 | $100 |
| Dry-run + tooling-viability checks | ~3 | $15 | $45 |
| Re-runs / debugging | reserve | — | $100 |
| **Phase 0 ceiling** | | | **~$575** |

## Hard caps

| Trigger | Action |
|---------|--------|
| Single run exceeds $25 | Halt run, log, investigate before continuing |
| Phase 0 actual > $1,000 | Stop, re-budget, send milestone |
| Cumulative program > $5,000 with no signal | Stop and report |
| Cumulative program > $9,000 | Stop regardless of state |

## Whole-program budget allocation

| Phase | Theory | Estimated $ | Cumulative |
|-------|--------|-------------|------------|
| Phase 0 | T1 (maintenance) | $600 | $600 |
| Phase 1 | T2 (adversarial-edge) | $700 | $1,300 |
| Phase 2 | T3 (compositional correctness) | $800 | $2,100 |
| Phase 3 | T4–T5 (property tests, effects) | $1,500 | $3,600 |
| Phase 4 | T6–T8 (language redesign) | $2,500 | $6,100 |
| Phase 5 | OSS port if signal exists | $2,500 | $8,600 |
| Reserve | | $1,400 | $10,000 |

This allocation assumes negative signal at each phase forces a pivot, not termination. If T1 produces strong positive signal, much of Phases 4–5 may not be needed; if T1–T3 all fail, Phases 4–5 are where the language-redesign theories live and may justify the spend.

## Sanity check (must do before locking)

Before committing N=5 × 3 prompts × 2 langs:

1. Run T1.A on the C# scaffold once, manually, with Opus 4.7. Measure actuals.
2. If actual > 2× the mid estimate ($20+), stop and re-budget.
3. If actual is in range, lock and proceed.

This dry run is itself ~$15 — under the $25 cap. Worth it as insurance.
