# w5-parity-001 — PP-W5 toolchain parity: **PASS**

**Run:** 2026-08-05, live. **Realised spend: $38.75** (projected ~$33; the projection came
from summing realised `total_cost_usd` over the 37 archived N1 runs of `m5-compare-001`).
**Collection: 40/40 cells, 0 invalid, 0 censored, 40/40 taskSuccess, 20 per arm.**

| | |
|---|---|
| Control | `v0.10.0` tag build, `e24a6832` |
| Treatment | `main` at epoch time, `87a783dd` |
| Pairs | the four registered N1 neutral pairs |
| Shape | 5 runs/arm, `raw` edit mechanism **both** arms, model `claude-opus-4-8` |
| Rule | frozen at A-1.4 tranche 1; adjudicator written **results-blind** |

## The measurement

| pair | control (mean tok) | treatment (mean tok) | ratio |
|---|---:|---:|---:|
| N1-001-string-utils | 7,002 | 7,897 | **1.128** |
| N1-002-inventory | 5,307 | 5,237 | 0.987 |
| N1-003-csv-row | 10,076 | 11,679 | **1.159** |
| N1-005-order-pipeline | 6,381 | 6,956 | **1.090** |

**Median paired ratio: point 1.1090, one-sided 95% lower bound 0.9269.**

PP-W5 fails iff **both** the lower bound exceeds 1.0 **and** the point estimate exceeds
1.25. Neither fires — the lower bound is **below** 1.0, and 1.109 is well under 1.25.

## Verdict: PASS — and what that does *not* mean

**It does not mean "no difference".** The point estimate is a **~11% increase in
output-tokens-to-green**, and the direction is consistent in **3 of 4 pairs** (the fourth
is 0.987, essentially flat). A reader who takes "PASS" as "the toolchain got no more
expensive" is reading something the data does not say.

What the rule actually establishes is narrower: **no LARGE tax was detected.** The
one-sided lower bound sits at 0.927, so a true ratio of 1.0 is not excluded — the observed
11% is not distinguishable from parity at this sample size. Equally, the design bounds only
large regressions: measured detection is **0.33 / 0.62 / 0.87** at true 1.25× / 1.4× / 1.6×.
At 4 clusters × 5 runs, an 11% real tax is exactly the size this epoch cannot resolve.

**So the honest summary is: the point estimate leans mildly against the treatment, the gate
is not close to firing, and the instrument could not have told a real 11% tax from noise.**

## Attribution (A-1.5.6, registered results-blind)

The treatment carries **v0.11 + v0.12**, so this adjudicates a **two-release** delta, not a
strictness-batch-only one. The frozen row's sentence claiming otherwise is recorded stale.
The 1.25 margin and its ~1.7% false-fail calibration were **not** re-derived; the treatment
simply carries more change, so a pass is correspondingly stronger.

Worth naming, since it was flagged before the run rather than after: v0.12 added work that
could plausibly tax this loop — type checking now runs on **every** compile (#877), and the
D3/D12/D14 demotions withdraw elision from a large class of contracts, leaving more runtime
checks in the emitted C#. A mild positive ratio is what one would expect from that, and the
epoch is consistent with it without being able to attribute it.

No isolation ladder is triggered: that branch is pre-committed for a FAIL.

## Three apparatus defects, all found before any number existed

Recorded because two of them would have produced a *plausible* result rather than an
obvious failure.

1. **The arms overwrote each other.** `run-pair` writes to `$OUT/$PAIR/$ARM_LABEL/run-N`,
   and M5's arms only stay apart because they differ in edit mechanism, which run-pair
   appends to the label. PP-W5 is `raw` on both arms, so both landed on `calor` and the
   treatment silently destroyed the control — the null pre-flight produced **4 result files
   where 8 were expected, every one the treatment build**. Caught at **zero spend**.
2. **The swap guard rejected a correct configuration.** It asserts arm A lacks the WS2
   `--root` path — right for M5's baseline, wrong for v0.10.0, which has it. Now scoped to
   `--kind m5-comparison`, replaced by a general one: the arms must be different *binaries*
   by content hash. Not by `--version`: v0.10.0 and today's main both report `0.10.0`, since
   the version only moves at release.
3. **The raw arm never invoked the agent.** bash 3.2 (macOS's `/bin/bash`) treats
   `"${arr[@]}"` on an empty array as unbound under `set -u`; the raw arm registers no MCP
   server, so the subshell died *before* `claude` ran. The first live attempt collected
   **40 cells, all invalid, $0.00 spent**. Benign in the only way that matters — the harness
   reported honestly instead of emitting plausible zeros, and the validity floor would have
   returned REPORTED-NOT-ADJUDICATED.

A fourth was avoided by reading the frozen row rather than the runner's defaults: the M5
runner hardcodes arm B to `mcp-file`, which would have measured the **tooling** delta and
reported it under PP-W5's name.

## What this record does not claim

- **Nothing about Calor versus C#.** Both arms are Calor. PP-W5 is a regression gate, not a
  thesis gate — untouched by Call S retiring the real-scale venue.
- **Not that the toolchain is free.** See above: a mild positive point estimate that this
  instrument cannot resolve.
- **Not a claim about non-neutral tasks.** The N1 set is deliberately neutral; a tax that
  appears only on verification-heavy or strictness-heavy work would not show here.
