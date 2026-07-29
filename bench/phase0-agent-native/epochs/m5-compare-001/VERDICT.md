# m5-compare-001 — M5 comparison-epoch VERDICT

**Loop plan v0.9 milestone M5.** One simultaneous A/B epoch over two calor
toolchain builds, adjudicating **PP-L5** (does the WS2+WS3 loop tooling reduce
convergence cost?) and **PP-L6** (did loop work corrupt the science?), then
**Call 2** (program go/no-go). Design + frozen rules pinned in
`docs/plans/loop-m5-comparison.md` and `agent-native-gates.md` (Annex A, §6.1).

## Pins / provenance

| Field | Value |
|-------|-------|
| Model | `claude-opus-4-8` |
| Arm A | `4f235cdc` (`loop-baseline-ws1`, WS1-only) — `--edit-mechanism raw`, cold feedback |
| Arm B | `73e2519a` (main = baseline + WS2/WS3 isolation) — `--edit-mechanism mcp-file`, warm feedback |
| Design | 9 warm pairs (W2×5 + W3×4) + 4 neutral (N1) × 2 arms × 5 runs = **130 runs**, simultaneous, identical tasks/pins |
| Started | 2026-07-28T22:59Z |

Arm B's `src/` is byte-identical to main and differs from arm A only by the
WS2/WS3 compiler delta (MCP write path + parse-tolerant mode + warm session
state + instrumentation); Calor.Tasks/Sdk/Runtime and codegen are unchanged
between arms, so both compile identically and the delta is attributable to the
WS2/WS3 tooling (loop plan §10 C2). The build-pin routing was verified per arm.

## Result

### PP-L5 — HIT ✅ (WS2+WS3 tooling reduces tokens-to-green)

| | value |
|---|---|
| **Median paired tokens-to-green ratio (B/A)** | **0.6465** (~**35% reduction**) |
| Threshold | ≤ 0.85 |
| One-sided 95% CI upper bound | 0.7957 |
| P(bootstrap median ≥ 1.0) | 0.0000 |
| §6.1 pass rule | point ≤ 0.85 ✓ **AND** one-sided 95% CI excludes zero effect (1.0) ✓ |
| Decidable | yes (9 warm pairs; warm censored 0%/0%, ≤5pp diff) |

Per-pair B/A (all 9 < 1.0): W2-001 0.614, W2-002 0.647, W2-003 0.684,
W2-004 0.625, W2-005 0.927, W3-001 0.550, W3-002 0.625, W3-003 0.674,
W3-004 0.871. The write-heavy W3 tasks benefit most (W3-001 0.55); the two
heaviest tasks (W2-005 scheduler 0.93, W3-004 sync-folder 0.87) improve least
but still reduce. The reduction is large and consistent, and the one-sided
cluster bootstrap (runs nested within pairs, α=0.05) excludes zero effect
decisively.

### PP-L6 — PASS ✅ (loop work didn't corrupt the science; release blocker cleared)

- **Neutral iterations-to-green parity:** median paired ratio B/A = 1.000, one-sided
  95% lower bound 0.889 → **no significant regression**.
- **Censored caps:** neutral censored arm A 10% / arm B 10% — within the 40%
  absolute cap and the 5pp differential.
- **Config invariance:** enforced per-run by the harness `check_pins` (gates §0.2);
  no drift.

## Effort / cost

- Tokens (valid runs): arm A 514,475 out / arm B 400,309 out; input 2,854 total.
  Mean iterations-to-green (valid runs): arm A 1.06, arm B 1.02 — both near the
  floor, which is why the primary measure is tokens-to-green, not iterations
  (loop plan §4). (Invalid-inclusive it is 1.37 / 1.32, inflated by the four
  N1-005 cap-exhausted runs recorded at the budget ceiling; those feed no
  adjudicated quantity.)
- Spend: **≈ $22.88** (`claude-opus-4-8` @ $5/$25 per 1M) — far under the
  $1,500/epoch ceiling.

## Run integrity

- **130/130 runs collected.** The **warm set (PP-L5) is 100% clean** — 0
  censored, 0 invalid across all 90 warm runs.
- **4 cap-exhausted (invalid) runs, all in the single neutral pair
  N1-005-order-pipeline** (2 arm A + 2 arm B), from a localized transient
  Anthropic API cluster in that pair's window; 11 transient blips total were
  absorbed by the invalid-run retry cap. N1-005 retained 3 valid runs per arm,
  and the neutral censored fractions (10%/10%) stay within the 40%/5pp caps, so
  PP-L6 is validly adjudicated. Recorded per the frozen A.1 semantics.
- The #814 write_invalid_result fix (calorDll provenance) held — no invalid run
  aborted the epoch.

## Call 2 (loop plan §6.2) — PROCEED

Call 2 is adjudicated on **PP-L5 + PP-W1 together**, PP-W1 carrying more weight.

- **PP-W1** (WS5 / `ws5-probe-001`): **HIT**, Calor − C# catch delta = **+5**
  (calor 9/9, C# 4/9) — not zero-vs-zero, so the pre-committed kill signal is
  not triggered.
- **PP-L5**: **HIT** (~35% tokens-to-green reduction, significant).

Per the pre-committed rule ("PP-W1 hit + PP-L5 any → proceed"), **the program
proceeds on the thesis into v0.10.** With PP-L5 also a hit, the loop program
continues with the same discipline (the PP-L5-miss path — freeze loop
investment pending transcript analysis — does not apply). PP-L6 passing clears
the release blocker.

M5 closes on this record's merge. The v0.9 loop program's proof points are
resolved: PP-L1 PASS (M4), PP-L5 HIT + PP-L6 PASS (here), PP-W1 HIT (WS5);
PP-L3 retired, PP-L4 reported-not-adjudicated.
