# w5-parity-002 — PP-W5: **PASS**

**Run:** 2026-08-06, live. **Realised spend: $54.08** (cumulative across both attempts: **$92.83**).
Supersedes `w5-parity-001`, which was **VOID** — its two arms compiled through the same
`Calor.Tasks`.

**Collection: 40/40 cells, 0 invalid, 0 censored, 40/40 taskSuccess, 20 per arm**, every run
carrying an `armRepoRoot` matching one of the two pinned arms.

| | |
|---|---|
| Control | `v0.10.0`, `e24a6832`, `Calor.Tasks` `715cb5ad…` |
| Treatment | `main`, `87a783dd`, `Calor.Tasks` `9870805b…` |
| Shape | 4 N1 pairs, 5 runs/arm, `raw` both arms, **interleaved**, `claude-opus-4-8` |

## The contrast is real, and verified by execution rather than by flag

`w5-parity-001` failed because `--calor-dll` is echoed into the record but never consumed by the
agent's build. `--arm-repo-root` **is** consumed: it is `sed`-substituted into
`CalorTasksAssembly`, which is the `UsingTask AssemblyFile=` in `Sdk.targets`.

Independently confirmed in review by rebuilding all four fixtures under each arm's materialised
workspace: `obj/calor/.calor-build-state.json`, written by `Calor.Tasks` itself, records
`compilerHash` **`65ee7cf0…` (control)** vs **`40472f37…` (treatment)**. That is the compiler's
own self-report from the build path the agent runs — not an operator assertion.

**Disclosed:** on these four fixtures in their initial state the emitted C# is byte-identical
between arms (modulo `#line` paths), and no treatment run produced a diagnostic the control could
not. The contrast is real in the binary; on this population it changes little the agent sees.

## The measurement

The frozen row pins the metric to `agent.json` `usage.output_tokens`. **That field silently
under-counts** on runs that delegate to a subagent or resume after compaction — see the erratum
below. Both figures are given; the verdict is the same either way.

| pair | ratio (recorded metric) | ratio (corrected) |
|---|---:|---:|
| N1-001-string-utils | 0.470 | **0.953** |
| N1-002-inventory | 1.146 | 0.938 |
| N1-003-csv-row | 1.622 | **1.622** |
| N1-005-order-pipeline | 1.050 | 1.051 |
| **median point estimate** | **1.0984** | **1.0016** |
| one-sided 95% lower bound | 0.6531 | ≈0.81 |

Neither gate fires on either basis — the lower bound is far below 1.0 and the point far below
1.25. **PASS.** Robust across 200 bootstrap seeds (lower bound never approaches 1.0).

Iterations-to-green (observational, as the row requires): control `{1:17, 2:2, 3:1}`, treatment
`{1:18, 2:1, 4:1}`.

## Erratum — the frozen metric under-counts, and it produced a false finding

`agent.json` `usage.output_tokens` captures only the final turn when a run delegates to a
subagent or resumes after compaction. Four runs across the two epochs are affected:

| epoch | cell | recorded | actual (`modelUsage`) |
|---|---|---:|---:|
| 002 | N1-001 treatment run-4 | **543** | **30,084** (55×; `num_turns` 1, subagent) |
| 002 | N1-002 control run-3 | 13,459 | 22,792 |
| 001 | N1-002 control run-2 | 2,843 | 9,694 |
| 001 | N1-005 treatment run-3 | 6,458 | 13,859 |

**An earlier revision of this record claimed "one task cost the treatment half the tokens"
(the 0.470) and built a heterogeneity finding on it. That claim is WITHDRAWN.** The run in
question was the *most* expensive in its cell, not the least. `N1-003`'s 1.622 is real and is not
outlier-driven.

**The "sevenfold spread increase" claim is also WITHDRAWN**, on two independent grounds:

1. **The same defect is present in both epochs.** Corrected identically, the spreads are
   `w5-parity-001` **0.538** and `w5-parity-002` **0.684** — a ratio of **1.27×**, not 6.7×. The
   sevenfold figure was manufactured by a defect that happened to shrink one epoch and inflate
   the other.
2. **The spread is not a statistic with an established null.** Permuting arm labels within each
   pair (20,000 draws) puts the observed corrected spread at the **50th percentile** of the
   distribution under *no arm effect at all* (null p5 0.213 / p50 0.690 / p95 1.692). It is
   dead-centre typical of noise.

The record previously treated `w5-parity-001` as a "clean null" baseline. It was a 7th-percentile
draw, not a baseline. **No heterogeneity is claimed, and none is supported.**

The adjudicator is not at fault — it transcribed the frozen metric faithfully. The remedy is a
**§7 erratum**: the metric is defective for subagent-delegating runs, and future epochs should
pin `modelUsage[*].outputTokens` or disallow subagent spawning.

## What the PASS means

**No large tax detected.** With the corrected data the point estimate is **1.0016** — as close to
parity as the design can resolve — and the bound still admits a wide interval, so this is not
proof of equality. Within-cell CV rose from 0.20 (`m5-compare-001`, the frozen derivation's
population) to 0.42 median here, so realised power is **below** the frozen 0.33/0.62/0.87 figures.

**No mechanism is attributed.** All four N1 fixtures contain **zero contracts**, so the
D3/D12/D14 elision withdrawal cannot operate here — a claim an earlier draft made and which is
withdrawn. The `EnableTypeChecking` flip has **zero supporting instances** in the archived
journals.

## Attribution (A-1.5.6, registered results-blind)

The treatment carries **v0.11 + v0.12**: a two-release delta. The 1.25 margin and its ~1.7%
false-fail calibration were not re-derived. The on-fail isolation ladder is not triggered.

## Five apparatus defects preceded this number

Four were **silent** — data loss, or a guard verifying the wrong artifact:

1. Both arms wrote to the same directory; the treatment destroyed the control. Caught at zero spend.
2. The M5 swap guard rejected a correct parity configuration.
3. The `raw` arm never invoked the agent on bash 3.2 (empty array under `set -u`) — 40 invalid
   cells for **$0.00**.
4. **The product was never bound per arm** — voided `w5-parity-001` at **$38.75**, and the
   CLI-hash guard certified a contrast that did not exist.
5. Interleaving re-invoked `run-pair` per run, whose loop restarts at `run-1`; run 2 overwrote
   run 1. Caught at zero spend. Fixed with `--run-offset`.

A sixth — the token-accounting defect above — was found only by review, after the number existed.

## What this record does not claim

- **Nothing about Calor versus C#.** Both arms are Calor; PP-W5 is a regression gate.
- **Not parity**, and **not heterogeneity**. The data support neither.
- **Not a claim about non-neutral tasks.** N1 is deliberately neutral.
- Realised spend exceeded the authorisation's projected range; see the amended authorisation.
