# E1a — attribution experiment: **H1 KILLED**

Pre-registered per `docs/plans/machine-zone.md` §1 (H1), §9 (E1a row), and the
§12.4 re-registration (baseline-relative threshold, n = 30/arm/pair). Protocol
and decision rule were fixed in `pins.json` before the first live run; the
harness change (`run-pair.sh --exemplar`) and the exemplar sheet
(`bench/phase0-agent-native/exemplar.md`, sha256 pinned) were committed before
running. 2 pairs x 3 arms x 30 sequential live runs = 180 runs, all on
compiler commit `0f205e05`, agent `2.1.204 (Claude Code)`, default model pin.

## Results (iterations-to-green, mean; censored runs carry budget+1 = 11)

| Pair | csharp | calor | calor+exemplar | R_base | R_ex | reduction (R_base−R_ex)/(R_base−1) |
|---|---|---|---|---|---|---|
| N1-003-csv-row | 1.00 | 2.50 | 2.23 | 2.50 | 2.23 | 0.18 |
| W3-003-kv-journal | 1.00 | 2.27 | 1.00 | 2.27 | 1.00 | 1.00 |
| **Pooled** | **1.00** | **2.38** | **1.62** | **2.38** | **1.62** | **0.55** |

## Decision rule applied (§12.4)

- Pooled R_base = 2.38 ≥ 1.3 → the wave-2 baseline effect **reproduced**
  concurrently (no regression-to-the-mean escape: the historical 2.7x was
  measured at n=3/arm; at n=30 it is 2.38x pooled).
- Reduction fraction = (2.38 − 1.62) / (2.38 − 1) = **0.55 ≥ 0.30** →
  **H1 is KILLED**: a ~60-line in-context syntax exemplar removed the
  majority of the Calor green-field iteration cost. The cost is dominated by
  **thin training prior / missing in-context syntax reference**, not by a
  text-serialization tax that structured edits would eliminate.
- Plan consequences (§9, §12.2c): M2's rationale falls; M1's
  canonicalizer-as-write-path portions descope to the identity/keying subset;
  the spec-surface/evidence work stands on its own.

## Distributions (iterations-to-green: count)

| Arm | N1-003-csv-row | W3-003-kv-journal |
|---|---|---|
| csharp | 1:30 | 1:30 |
| calor | 1:9, 2:6, 3:13, 4:1, 11:1 | 1:10, 2:4, 3:14, 4:2 |
| calor+exemplar | 2:23, 3:7 | 1:30 |

## Anomalies and honesty notes

1. **The exemplar itself injected one deterministic error on N1-003.** The
   sheet shows `§B{lines:[str]}` (correct for `File.ReadAllLines`, an array);
   29/30 N1 exemplar-arm transcripts pattern-matched `[str]` into their
   `JoinRow`/`SplitRow` signatures, which emits `string[]` against the pinned
   `List<string>` surface — a guaranteed first-build failure (note the
   exemplar arm's floor at 2 with zero 1-iteration runs, vs 9/30 for baseline
   calor). W3-003, whose surface has no list-typed signature, collapsed to
   perfect parity (30/30 at 1). The N1 reduction (0.18) is therefore an
   **underestimate** of what a corrected exemplar would achieve; the flaw
   biases *against* killing H1, and H1 died anyway. The sheet is left as run
   (sha-pinned); fixing it and re-running would only strengthen the verdict.
2. **One invalid slot** (N1-003/calor/run-1): three consecutive API-error/
   overloaded attempts; per the gates-doc §0.2 rule it counts as censored
   task failure (itg = 11). Sensitivity: excluding it, N1 R_base = 2.21 and
   the N1 reduction is ~0 (2.21 vs 2.23 — the flawed exemplar bought nothing
   on this pair), pooled R_base = 2.24, pooled reduction = 0.50 — **verdict
   unchanged**. Three other slots (N1 csharp run-5, N1 calor runs 2 and 9)
   hit one API error each and recovered on auto-retry; `invalid.txt` logs
   are archived in those run dirs.
3. **Zero escaped bugs in any arm** (180/180 runs with heldout_fail = 0 at
   final state except the one invalid slot) — consistent with the
   hardness-check finding that these pairs cannot power the 2a benefit gate.
4. C# arm was a constant 1.0 on both pairs (60/60 first-try green), so the
   ratios are driven entirely by the calor arms' distributions.

## Transcripts and archive

All 180 agent transcripts (`agent.json`), journals, invalid-run logs, per-run
`result.json`, `pins.json` (protocol + exemplar sha), the archived driver
(`run-driver.sh`, `driver.log`), the pre-registered analysis script
(`analyze.sh`), and its outputs (`analysis.json`, `per-pair.json`) live in
this epoch directory. Total agent tokens: ~0.88M in / ~1.75M out.
