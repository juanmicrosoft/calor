# w5-parity-002 — PP-W5: **PASS**, on an instrument that cannot resolve the question

**Run:** 2026-08-06, live. **Realised spend: $54.08.** Supersedes `w5-parity-001`, which was
**VOID** — its two arms compiled through the same Calor.Tasks.

**Collection: 40/40 cells, 0 invalid, 0 censored, 40/40 taskSuccess, 20 per arm**, and every
run carries an `armRepoRoot` matching one of the two pinned arms.

| | |
|---|---|
| Control | `v0.10.0`, `e24a6832`, `Calor.Tasks` `715cb5ad…` |
| Treatment | `main`, `87a783dd`, `Calor.Tasks` `9870805b…` |
| Contrast | **verified before the run**: distinct product roots, distinct `Calor.Tasks.dll` |
| Shape | 4 N1 pairs, 5 runs/arm, `raw` both arms, **interleaved**, `claude-opus-4-8` |

## The measurement

| pair | control (mean tok) | treatment (mean tok) | ratio |
|---|---:|---:|---:|
| N1-001-string-utils | 12,238 | 5,752 | **0.470** |
| N1-002-inventory | 8,403 | 9,633 | 1.146 |
| N1-003-csv-row | 14,466 | 23,468 | **1.622** |
| N1-005-order-pipeline | 7,213 | 7,577 | 1.050 |

**Median paired ratio: point 1.0984, one-sided 95% lower bound 0.6531.**

Neither gate fires — the lower bound is far below 1.0, and 1.098 is well under 1.25.
**PASS.**

Iterations-to-green (observational, as the frozen row requires): control `{1:17, 2:2, 3:1}`,
treatment `{1:18, 2:1, 4:1}` — no material difference.

## The finding that matters more than the verdict

**The per-pair ratios range from 0.470 to 1.622.** One task cost the treatment **half** the
tokens; another cost it **62% more**. There is no consistent direction.

Compare the void epoch, which by accident ran the *same* compiler on both arms and is
therefore a clean null:

| | per-pair ratios | spread | lower bound |
|---|---|---:|---:|
| `w5-parity-001` (no contrast) | 1.128 / 0.987 / 1.159 / 1.090 | 0.17 | 0.9269 |
| `w5-parity-002` (real contrast) | 0.470 / 1.146 / 1.622 / 1.050 | **1.15** | **0.6531** |

Introducing a genuine toolchain contrast multiplied the between-pair spread by roughly
seven. That is the substantive result of this epoch, and the frozen rule was not built to
say anything about it.

**What the PASS therefore means, stated narrowly:** no *large systematic* tax was detected.
The lower bound of 0.653 means the data are consistent with anything from a ~35% improvement
to a ~10% tax. **The epoch does not establish parity, and with this dispersion it could not
have.** The row's pre-committed power statement — detection 0.33 / 0.62 / 0.87 at true
1.25× / 1.4× / 1.6× — was calibrated on the *null* variance measured at `m5-compare-001`,
and the realised variance here is far higher. Actual power against a systematic effect is
correspondingly **lower** than the frozen figures.

**No mechanism is claimed.** A prior revision of the void record asserted that the D3/D12/D14
elision withdrawal predicted a positive ratio; that was wrong on its own terms — all four N1
fixtures contain **zero contracts**. The plausible remaining channel is the
`EnableTypeChecking` default flip (#877), which changes mid-loop diagnostics. This epoch does
not isolate it, and the heterogeneity (one pair strongly negative, one strongly positive) is
not what a single systematic channel would produce. **It is left unattributed.**

## Attribution (A-1.5.6, registered results-blind)

The treatment carries **v0.11 + v0.12**, so this adjudicates a two-release delta. The 1.25
margin and its ~1.7% false-fail calibration were not re-derived. The on-fail isolation ladder
is not triggered.

## Five apparatus defects preceded this number

Recorded because four of five were **silent** — data loss or a guard verifying the wrong
artifact — and none would have appeared in the output.

1. Both arms wrote to the same directory; the treatment destroyed the control. Caught by the
   zero-spend pre-flight (4 result files where 8 were expected).
2. The M5 swap guard rejected a correct parity configuration.
3. The `raw` arm never invoked the agent on bash 3.2 (empty array under `set -u`) — a live
   attempt collecting 40 invalid cells for **$0.00**.
4. **The product was never bound per arm.** `--calor-dll` pins the CLI, which the agent never
   invokes; `__REPO_ROOT__` binds the compiler that actually builds the agent's code, and
   `run-m5-epoch.sh` never passed it. This voided `w5-parity-001` at a cost of **$38.75**, and
   the CLI-hash guard certified a contrast that did not exist.
5. Interleaving (added to fix the blocked-order confound) re-invoked `run-pair` per run, whose
   loop restarts at `run-1` — run 2 overwrote run 1. Caught by the pre-flight: 2 cells where 4
   were expected. Fixed with `--run-offset`.

The guard added for #4 now **hard-fails** a parity epoch that omits per-arm roots, and the
analyzer refuses to adjudicate unless the pins record two distinct roots with distinct
`Calor.Tasks` hashes and every run's provenance matches one of them.

## What this record does not claim

- **Nothing about Calor versus C#.** Both arms are Calor. PP-W5 is a regression gate.
- **Not parity.** See above — the instrument cannot resolve a systematic effect at this
  variance.
- **Not a claim about non-neutral tasks.** N1 is deliberately neutral; a tax appearing only on
  verification- or strictness-heavy work would not show here.
- **Realised spend exceeded projection** ($54.08 vs ~$39). The projection was drawn from the
  void epoch's N1 costs, where both arms ran the same compiler; a genuine contrast costs more.
