# Performance gate — strategy change

Proposes retiring the aggregate wall-clock ratchet added by #938 and returning
the performance gate to per-test budgets, then strengthening those budgets.

Written because the aggregate gate has blocked the v0.13.2 release, has never
passed reliably in CI, and required three successive layers of statistical
correction — each of which either failed or made things worse. That pattern is
the argument, not the individual failures.

This document fixes the strategy and the evidence behind it. It does not
re-tune every per-test budget; that lands with the follow-up in §6.

## 1. What the gate is for

One question: **did a change make the Calor compiler slower?**

A useful answer has to be *actionable* (name what regressed), *portable* (mean
the same on a laptop and a shared runner), and *sensitive* (fire on a real
regression, stay quiet otherwise). The current arrangement fails all three.

## 2. What exists today

Two gates, stacked, measuring overlapping things:

**(a) Per-test budgets — 21 tests, each asserting its own.** The budget is in
the test name: `Parsing_MediumModule_Under500ms`,
`CFGConstruction_SmallFunction_Under10ms`, `Memory_LargeModule_Under100MB`. A
failure names the operation that regressed.

**(b) An aggregate wall-clock ratchet** (`scripts/run_performance_gate.py` +
`eng/performance-baselines.json`), added by #938. Runs the suite 4× and caps the
median at `maximumMedian` (21.0s).

(b) is the subject of this document. (a) stays either way.

## 3. Why the aggregate is the wrong measurement

Not "badly tuned" — measuring the wrong quantity.

### 3.1 It is dominated by the term we do not care about

The suite's wall-clock is mostly process start, assembly load, JIT and test-host
overhead. Measured 2026-08-14, same commit:

| | suite wall-clock | startup (calibration) | compute |
|---|---|---|---|
| dev machine | 19.871s | 0.764s | 19.107s |
| CI runner | 21.348s | 1.567s | 19.781s |

**CI startup is 2.05× the dev machine; CI compute is 1.07×.** The gate's number
moves with the runner, not with the compiler.

### 3.2 It is undiagnostic

`median: 22.391s (maximum 21.000s)` does not say what got slower. Every
investigation starts from zero. A per-test failure starts from the answer.

### 3.3 It is insensitive exactly where the per-test budgets are tight

A 10× regression in a 5ms operation adds 45ms to a ~21,000ms total — invisible.
`CFGConstruction_SmallFunction_Under10ms` catches it on the first run. So the
aggregate misses small-operation regressions *and* fires on runner noise: the
worst of both.

### 3.4 The corrections needed to rescue it are the evidence against it

Three attempts, in order, all measured rather than argued:

| approach | dev | CI | cross-machine spread |
|---|---|---|---|
| raw seconds (#938, shipped) | 19.871 | 21.348 | 7.4% |
| ÷ same-runner calibration | 26.009 | 13.623 | **47.6%** |
| − same-runner calibration | 19.107 | 19.781 | 3.5% |

Dividing was **worse than no normalisation at all**. Subtraction works, but it
is a third layer of machinery to make an aggregate mean something — and it still
cannot say what regressed.

## 4. Evidence that the aggregate has never worked

- **#938 (`301d7a30`) created this gate** and replaced the nightly's single
  `dotnet test` with it. Landed 10:56 UTC 2026-08-12.
- **The eleven green nightlies people cite predate it** and ran the *old* single
  invocation. They are not evidence this gate works.
- **Its own record:** nightly 08-13 failed; nightly 08-14 passed; six controlled
  dispatches on 08-14 gave main 2/3 and the candidate branch 2/3 — **identical**,
  with both failures being threshold failures at 22.391s and 21.209s against a
  21.0s ceiling.
- **It blocked v0.13.2.** `publish` depends on `release-quality` and
  `test (performance)`; both run this gate. The release has been unpublishable
  since 08-12.
- **Its ceiling was calibrated on a dev machine** (~19.8s) and never validated on
  a runner. Observed CI medians: 20.618, 20.692, 21.209, 22.391; samples to
  22.962. The baseline's own `noisePolicy` records ~0.5s spread — wider than the
  headroom it left.

Separately and still open: runner terminations on 08-13
(`shutdown signal` / exit 143) at 24s, 26s, 52s, 79s and 26m. **Not explained,
not reproduced since**, and not what this document addresses. #965 stays open for
it. Note the bisect that appeared to pin those to #938 is suspect — several
commits it marked FAIL pass today, so it may have been measuring a transient
condition rather than code.

## 5. Proposal

**Retire the aggregate ratchet. Keep and strengthen the per-test budgets.**

1. **Delete** `scripts/run_performance_gate.py` and
   `eng/performance-baselines.json`; drop the gate step from `performance.yml`
   and from `release-quality` in `publish-nuget.yml`.
2. **Run the suite once** — `dotnet test tests/Calor.Performance.Tests`. One
   invocation (~21s) instead of nine (~100s).
3. **Keep test-count honesty**, which is independent of the ratchet:
   `eng/test-manifest.json` pins 21 tests and `check_trx.py` enforces it, so a
   silently-skipped performance test still fails CI.
4. **Then tighten the per-test budgets** (§6) — this is where the lost
   sensitivity is recovered, and where it becomes diagnostic.

### Cost

| | today | proposed |
|---|---|---|
| invocations per run | 9 (5 calibration + 4 suite) | 1 |
| wall-clock | ~100s | ~21s |
| config files | `performance-baselines.json` | none |
| failure output | "median exceeded" | the test that regressed |

## 6. What is lost, and how it is recovered

**Lost: detection of many small regressions that each stay inside their budget
but sum.** Real. The aggregate could in principle catch it.

But it does not today, because the per-test budgets are loose — 2× to 16×
headroom:

| test | observed | budget | headroom |
|---|---|---|---|
| `Binding_SmallModule_Under100ms` | 6ms | 100ms | 16× |
| `Parsing_MediumModule_Under500ms` | 54ms | 500ms | 9× |
| `Memory_LargeModule_Under100MB` | 18MB | 100MB | 5× |
| `FullAnalysis_LargeModule_Under30Seconds` | 6s | 30s | 5× |
| `CFGConstruction_SmallFunction_Under10ms` | 5ms | 10ms | 2× |

A 30% regression passes every one of them, and the aggregate — with its 1.5%
headroom over CI noise — cannot distinguish that from a busy runner.

**Recovery: tighten budgets to ~2–3× observed.** This is portable in a way the
aggregate is not: per-test times are small, compute-dominated, and carry
proportionally little startup. It is also diagnostic by construction.

**Follow-up, separate PR:** re-measure all 21 on both a dev machine and a runner,
set each budget from the CI figure with a stated multiplier, and record the
multiplier and its rationale next to each assertion.

## 7. Alternatives considered

| alternative | why not |
|---|---|
| Raise `maximumMedian` to ~25s | Makes red go green without making the number mean anything. The metric is still dominated by startup and still undiagnostic. |
| Keep the ratchet, subtract calibration | Works numerically (3.5% spread) but adds permanent machinery to an aggregate that still cannot name a regression. Complexity without diagnosis. |
| Run the aggregate only on the quiet nightly | Better than today, and matches #790's documented intent. But it leaves a metric nobody can act on, and the per-test budgets remain loose. Viable fallback if §5 is rejected. |
| Benchmark harness (BenchmarkDotNet) with statistical comparison | The genuinely rigorous answer for micro-regressions, and worth considering for 0.15. Far more than is needed to unblock a release, and it does not belong in a test-suite gate. |

## 8. Decisions required

1. **Retire the aggregate ratchet?** (§5) — reverses part of #938's intent.
2. **Tighten per-test budgets now or as a follow-up?** Recommendation: follow-up,
   so unblocking the release is not coupled to re-tuning 21 numbers.
3. **Does `release-quality` keep any performance gate at all?** Recommendation:
   yes — the suite itself, with its per-test budgets, since that is a real gate
   that names failures.

## 9. Sequencing

1. This document reviewed and §8 decided.
2. PR: retire the aggregate; `performance.yml` and `release-quality` run the
   suite once. Verify green on CI **and** confirm the suite still fails when a
   budget is deliberately breached — arm the revert, do not trust green
   (`docs/` convention; see the rename and edit-script corpora for prior art).
3. Unblock the v0.13.2 publish; verify at the registry, not by a green workflow.
4. Follow-up PR: re-measure and tighten the 21 budgets.
5. #965 stays open for the unexplained 08-13 runner terminations.

## 10. Open question

The 08-13 terminations remain unexplained. If they recur after the aggregate is
retired, the cause was never the gate and this change will not have addressed
it — it will still be worth making on its own merits, but the investigation must
continue rather than be considered closed by a green pipeline.
