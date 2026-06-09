# Phase 3 H2 — Edit-Workload Study Results

## Verdict: TIE on every dimension

| Metric | closer | indent | Δ |
|---|---|---|---|
| Pass rate | 35/35 (100%) | 35/35 (100%) | +0.0pp |
| Total cost | $0.284 | $0.281 | -1.1% |
| Fisher exact p (two-sided) | — | — | 1.0000 (NS) |

**Indent form does not regress on edit workloads at N=35/arm.**

## Why this matters

H1 v3 measured GREENFIELD generation ("add a new function"). That left open the
question: does indent form silently fall apart when the agent has to MODIFY
existing code without corrupting structure?

Edit workloads are arguably the dominant real workload (most code is read +
modified more than it's written from scratch). Indent forms historically
struggle here because:

1. No anchor for "this block ended here" — a mistaken dedent silently changes
   semantics.
2. Diffs become harder to bound — the agent has to count spaces correctly across
   the whole touched region.
3. Copy-paste from upstream context can produce mixed indent.

H2 was designed to surface these failure modes. None of them appeared.

## Task design

7 edit tasks, each forcing a different kind of modification:

| Task | Kind | What the agent had to do |
|---|---|---|
| edit-sig-001 | signature | Add a third parameter to Add and update body |
| edit-body-001 | body | Wrap Subtract's body in a §IF{i02} chain |
| edit-pre-001 | contract | Add §Q (!= b 0) to Divide |
| edit-post-001 | contract | Add §S to Multiply referencing result |
| edit-type-001 | type | Change Add from i32 to i64 (sig + body cascade) |
| edit-chain-001 | chain | Modify the §EI branch of Sign's §IF chain |
| edit-del-001 | delete | Remove Multiply entirely, preserve all others |

Each task includes `must_contain` markers for required structure and
`must_not_contain` markers for removed code. Compilation must succeed.

## Per-task breakdown (5 trials per cell)

| Task | closer | indent |
|---|---|---|
| edit-sig-001 | 5/5 | 5/5 |
| edit-body-001 | 5/5 | 5/5 |
| edit-pre-001 | 5/5 | 5/5 |
| edit-post-001 | 5/5 | 5/5 |
| edit-type-001 | 5/5 | 5/5 |
| edit-chain-001 | 5/5 | 5/5 |
| edit-del-001 | 5/5 | 5/5 |

## What this changes about Phase 3 confidence

Before H2 we had:
- Tier 0: -15% source bytes (mechanical)
- Off-protocol read: tie (5/5 vs 5/5 small N)
- H1 v3 greenfield write: +5.7pp pass rate, -16.7% cost (p=0.61, not significant)

**Edit workload was the largest measurement gap.** It's now filled with a
strong tie signal. Indent form now has tie-or-favorable evidence on every
dimension we've measured.

Confidence update: ~70-75% → ~78-82%. The remaining gaps to 95% are:

1. **Cross-model replication** — Claude haiku only; need GPT-5, Gemini, and a
   larger Claude variant to rule out Claude's Python-trained indent affinity
   being a confound.
2. **Task variety** — 7 edit tasks all single-file, all small fixture (~25 lines).
   Need larger files (200+ lines), deeper nesting (5+ levels), multi-function
   refactors.
3. **Error recovery** — when the agent makes an indent mistake, can it fix
   itself? Inject 20 typical errors, measure self-fix rate.
4. **Compiler-level proof** — Phase 1 parser refactor lands clean, all 1700
   fixtures compile in indent form, Z3 still green.

## Methodology

- Model: claude-haiku-4-5 via Claude Code CLI
- Prompt apparatus: identical to H1 v3 (shared header includes CHAIN STATEMENTS
  teaching paragraph; only DELIM section differs between arms)
- Fixture: `scripts/h2_edit_fixture.calr` — 5-function MathLib with one §IF chain
- N: 5 trials per (task, arm) = 70 total invocations
- Total wall time: ~12 minutes
- Total cost: $0.57

## Limitations (honest)

- N=5/cell, 35/arm is still small. CI on each cell ranges roughly 48-100%.
  We can rule out catastrophic regression (≥20pp) at p<0.05, but not subtle
  regression (<10pp).
- Single model, single fixture, 7 tasks. Generalization is unproven.
- Tasks are all single-file. Multi-file refactor with cross-references untested.
- Agent had perfect chain-syntax teaching (v3 SHARED_HEADER). Real agents in
  production may not have this primer; the result here is the *ceiling*, not
  the floor.

## Raw data

- `scripts/phase3-h2-edit-results.json` — every trial with cost, duration,
  extracted code, compile stderr.
- `scripts/phase3-h2-stdout.log` — full run log.

## Recommendation

This significantly de-risks the indent-only migration. Combined with H1 v3, we
now have:

- Two independent agent-write measurements (greenfield + edit) both showing
  tie-or-indent-favorable
- Mechanical -15% byte savings
- Read comprehension parity

**Recommended next move:** proceed with Phase 1 compiler refactor (additive —
parser ignores Indent/Dedent tokens; closer form still works). After Phase 1
lands green, re-run H2 against a richer fixture set and consider cross-model
replication to close the remaining 13pp gap to 95% confidence.
