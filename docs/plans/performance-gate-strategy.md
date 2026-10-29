# Performance gate — strategy

> **Phases 3–4 implementation (0.29, #1115).** Memory samples now retain the
> bound module through the post-collection sample and reject nonpositive deltas.
> They measure **retained managed bytes**, not total allocation, native memory or
> peak RSS. Verification tests warm parsing, binding, taint, bug patterns and CFG
> paths in a lazy constructor prerequisite; unlike a warm-up fact, this runs before
> every measurement regardless of xUnit's class/fact order. Index tests already
> warm their own paths. The performance test host disables tiered compilation,
> using a fixed JIT tier: otherwise .NET 10's tiered escape analysis changes
> allocation counts after other classes run (a 2.7% drop reproduced during the
> aggregate gate). This standardizes the measurement environment, not the product
> runtime. Fractional milliseconds replace integer measurements.
> The two 4x-growth ratio tests are named below-quadratic smoke budgets, not linear
> guarantees. The aggregate ceiling remains 24.0s; no timing ceiling is raised.
>
> `DeterministicPerformanceTests` pins a 10-function, 50-statement generated
> module at **4,729 parser tokens, 1,351 bound nodes, 580 CFG blocks, 760 CFG edges,
> and 580 taint worklist iterations**. Five warm-up passes precede five synchronous
> current-thread allocation samples, each required within **±2%** of its build
> configuration's baseline: Debug **11,681,288 bytes**, Release **11,266,312 bytes**.
> These were measured on macOS ARM64, .NET 10.0.14. Linux x64 validation below
> confirms the Release pin within tolerance; runtime independence is not claimed.
> A separate interprocedural loop subject
> pins **11 iterations**, checks that summary-pass work is included and counters
> reset per analysis.
> Allocations complement retained-memory budgets; neither sees native Z3 memory.
>
> The remaining verification elapsed budgets are catastrophic-regression **smoke
> checks**, calibrated below from actual CI rather than relaxed from local results.
> The historical analysis and proposals below are retained.

The separate native-memory investigation (#1590) is opt-in:
`gh workflow run performance.yml --ref <branch> -f profile_compiler_memory=true`.
It runs only the diagnostic memory job, not the ordinary timing gate or the
entire Tests workflow. Artifacts include managed JIT symbol maps and procfs
mapping snapshots as well as native allocation traces. A bounded, interrupted
probe is diagnostic evidence, not a passing full-suite run or a leak proof.
The instrumented full-inventory attempt has a five-minute soft cutoff, allowing
artifact capture before the observed runner shutdown. Resource samples also
stream to the job log because an `always()` upload cannot survive runner death.
After preserving the native evidence, a separate fresh host attempts the full
unsplit inventory without profiler overhead, with a 20-minute cutoff and exact
TRX inventory validation. It still runs if the bounded profiling step fails.
Its separate TRX and resource log distinguish a complete uninstrumented result
from partial profiling; a failed profiling step remains a failed workflow.

Compiler wall-time work (#1590) moves the 67 binding-routing CLI cases and 12 W1
guide transcript cases into the already separate Tasks test lane, beside its
other CLI integration gates. Recent main measurements attributed 341.5s and
43.3s respectively to these classes. The test sources, subprocess loops,
assertions and fixtures stay intact; only their compilation owner changes.
Shared receiving-case data avoids linking a second copy of the compiler unit
tests. At relocation, inventories became Compiler 16,068 and Tasks 224, with no change to
the combined test cases or registered skips. This removes measured work from the
compiler job; a sub-30-minute p90 still requires the actual main-run window.

The bounded native trace in
[run 40916256007](https://github.com/juanmicrosoft/calor/actions/runs/40916256007)
preserves matching JIT maps and native stacks (artifact 12916582455). Managed
callers resolve repeated native PE metadata creation through
`CSharpToCalorConverter.GetBasicMetadataReferences`, not the already cached
`GeneratedCSharpCompiler.References`. The converter now shares its unchanged
runtime-only reference set through a process-wide, thread-safe `Lazy`, following
that existing cache pattern. Configured project references remain fresh, with
their aliases and complete-reference-set policy intact. Three new regressions
check reference and loaded-metadata identity, concurrent reuse and configured
reference isolation; all three reject the old per-call factory.
These bring the compiler inventory to 16,071. This removes a measured allocation
source, not proof that all anonymous RSS or the full-host failure is resolved;
the post-repair Linux investigation and main p90 remain acceptance work.

The post-converter-cache probe,
[run 40918874857](https://github.com/juanmicrosoft/calor/actions/runs/40918874857),
preserves matching symbols in artifact 12916759609: 299.14s, 1.34GB tracked peak,
7.67GB RSS including profiler overhead, and 6,706 passed cases before interruption.
The earlier five-minute probe reached 2,794 cases; different partial inventories
do not establish a causal throughput or RSS comparison. The surviving 1.30GB
metadata-allocation category resolves to repeated complete platform-reference
sets in `Issue769NamespaceTopologyTests.CreateCompilation`. All four test helpers
that construct complete platform sets now share that exact unchanged set through
one immutable, thread-safe `Lazy`, without reusing compilations or changing
diagnostics, assertions or runtime subjects. Configured references remain fresh;
the independent oracle retains its separate, bounded seven-assembly subset. One new
invariant rejects fresh references and checks loaded-metadata identity and exact
platform paths. The current compiler inventory is 16,072. Full-host memory
acceptance remains unproven until the separate unsplit verification completes.

The next symbolized probe,
[run 40921813743](https://github.com/juanmicrosoft/calor/actions/runs/40921813743),
uploaded artifact 12918334179 before uninstrumented verification: 297.44s,
1.10GB tracked peak and 3.73GB profiler RSS. Its different partial inventory
(1,945 passed cases) prevents causal RSS comparisons. The dominant 844MB native
category is now Z3 context creation through `ContractInheritanceChecker`.
That checker created a context but passed it to a borrowing prover; disposing the
checker therefore did not dispose its context. It now transfers ownership to an
internal prover constructor, while the existing public constructor still borrows.
Failed ownership transfer disposes the new context, and disposed entry points
reject calls before reaching native code. Three regressions inspect the pinned
binding's real context handle while keeping the managed wrapper rooted, proving
deterministic release, idempotence and preserved borrowed-context usability.
The inventory is now 16,075; prior full-run evidence does not validate this repair.

The post-context probe,
[run 40926105930](https://github.com/juanmicrosoft/calor/actions/runs/40926105930),
preserves artifact 12921700398 and actual testhost3640's map: 299.29s,
1.80GB tracked peak, 3.26GB profiler RSS, and 6,784 passed cases. Its remaining
1.77GB metadata category includes repeated seven-assembly runtime sets in
`Calor.Enforcement.Tests.TestHarness.ExecuteGeneratedCode`, reached through
compiler string-operation execution tests. This harness now shares exactly its
original seven references through an immutable, thread-safe `Lazy`; generated
compilations and execution remain fresh. A concurrent exact-path/resource-identity
regression brings Enforcement to 723 cases without changing compiler inventory.
The existing full-platform caches are bounded and intentionally separate from
this subset. The five-minute probe is still partial, not full-host acceptance.

### 0.29 Linux smoke calibration

After context ownership,
[run 40926109897](https://github.com/juanmicrosoft/calor/actions/runs/40926109897)
at `19cf228f0` passed 28/28 in four hosts: 11.812 / 12.156 / 11.896s,
median 11.896s below 24s, all 20 allocation samples 11,274,312 bytes.
No pin, tolerance or ceiling changed.

After the converter metadata repair,
[run 40919565888](https://github.com/juanmicrosoft/calor/actions/runs/40919565888)
at `3a3fe9c29` passed 28/28 in all four isolated hosts. Measured suite samples were
12.482 / 12.801 / 12.978s, median 12.801s below the unchanged 24s ceiling.
All 20 allocation samples remained 11,274,312 bytes, with the same exact pipeline
counters and unchanged tolerances. This is another measured runner, not proof of
a population-wide speedup.

[Performance Tests run 40908078723](https://github.com/juanmicrosoft/calor/actions/runs/40908078723)
ran one warmup and three measured isolated Release hosts on Linux x64. All 28
tests passed in every host. Aggregate measured samples were 20.060, 20.548 and
20.371 seconds: median **20.371s**, below the unchanged **24.0s** ceiling.
All 20 synchronous allocation samples were **11,274,312 bytes**, 0.071% above the
macOS Release pin and within its unchanged ±2% tolerance.

Verification timing tests now say `IsTimingSmokeTest`. Their limits round roughly
five times the three measured-host medians, capped by the previous ceiling. The
small CFG limit uses a 0.1ms floor because its output is rounded to hundredths of
a millisecond. These are four processes on one runner, not a population-wide
performance distribution. The separately registered index usability envelopes
remain unchanged.

| Operation | Measured-host median (ms) | Smoke ceiling (ms) |
|---|---:|---:|
| Parse small | 4.418 | 25 |
| Parse medium | 41.642 | 225 |
| Parse large | 431.033 | 2000 (previous tighter ceiling retained) |
| Bind small | 3.676 | 20 |
| Bind medium | 78.490 | 400 |
| Full analysis small | 27.908 | 150 |
| Full analysis medium | 374.058 | 2000 |
| Full analysis large | 4575.162 | 23000 |
| Deep taint flow | 116.272 | 600 |
| Many-function taint | 54.417 | 300 |
| Complex control-flow graphs | 4.026 | 25 |
| Nested-loop graphs | 4.145 | 25 |
| Small CFG, per-iteration average | 0.01 (rounded) | 0.1 |
| Complex CFG, per-iteration average | 0.19 (rounded) | 1 |

The first tightened Linux run,
[40909597462](https://github.com/juanmicrosoft/calor/actions/runs/40909597462),
failed the complex CFG check in its warmup host: its ten-build average was
1.78ms against the 1ms limit. This observation does not establish a compiler
regression or identify the source of the variability. Both CFG microbenchmarks
now build their exact subject once before timing and average 100 builds, rather
than ten for the complex case. The 1ms limit is unchanged; so are the aggregate,
allocation and other smoke limits.

> **Status (2026-08-27, v0.16 kickoff sweep).** This is the document from PR **#976**
> (revision 2, 2026-08-14), carried into the tree unchanged below this note and credited to
> that PR; `eng/performance-baselines.json`'s `noisePolicy` has cited it by path since #978
> merged, while the file itself sat in an open PR. What has happened since it was written:
> **Phase 1 shipped** — #978 raised the ceiling 21.0 → 24.0s with the evidence in `noisePolicy`,
> and #974 streams the gate's output. **Phase 2 (#965) has not recurred**: the v0.15.0 release
> run (`publish-nuget.yml` run 33096892494, 2026-08-27, attempt 1) was green on every job,
> including `test (performance)` (2m35s) and `release-quality` (29m); the 0.14.1–0.14.3 release
> runs on 2026-08-24 were green too. Roadmap-v0.16 §7 makes #965 release-blocking for 0.16.0
> only if it recurs. **Phases 3–4 are open** (substrate repairs; deterministic counters) and are
> tracked as **issue #1115**. #976's other two files were not carried: the baseline
> edit is already on main via #978, and the `run_performance_gate.py` change (the
> compute-subtracted alternative in §6 Phase 1) was the road not taken.

> **Revision 2.** Revision 1 proposed deleting the aggregate wall-clock ratchet.
> Four independent reviews refuted its premise, its evidence, and its remedy.
> The retraction is kept in §7 rather than deleted, because the mistakes are the
> most useful part of the record.

Sets out what the performance gate should measure, what actually blocked
v0.13.2, and the order in which to fix things.

## 1. The correction, in one paragraph

The aggregate ratchet is **not** a badly-chosen metric. It is the most stable
signal in the suite (CV ~1.6% across 18 CI samples on 6 runners) and its ceiling
was simply set at roughly the **50th percentile of the CI distribution** — a
calibration bug, not a metric bug. Meanwhile the per-test budgets that revision 1
proposed to lean on are the *least* stable measurements available (CV 24–39% at
the millisecond scale) and several assert nothing at all. And the thing that
actually blocked v0.13.2 was neither: it was the unexplained runner terminations
of #965.

## 2. What actually blocked v0.13.2

Every failing job of all four publish attempts, read from the logs:

| run | job | failure |
|---|---|---|
| 31651714822 | `release-quality` | `exit 143` / runner shutdown |
| 31651714822 | `test (performance)` | `exit 143` / runner shutdown |
| 31643889561 | both | `exit 143` / runner shutdown |
| 31634295426 | `release-quality` | `exit 143` / runner shutdown |
| 31634295426 | `test (performance)` | `exit 1`, `MSB4181: VSTestTask returned false` |
| 31624070695 | `release-quality` | `exit 143` / runner shutdown |

**Not one printed `ERROR: performance median exceeded the ratcheted maximum`.**
Retiring the ratchet would not have unblocked any of them. Note also that
`test (performance)` runs a plain `dotnet test` and **never invokes the gate
script** — so half the dying jobs are untouched by anything in this document.

The release blocker is #965 and it remains open and unexplained.

## 3. What the aggregate is, measured rather than assumed

| | suite wall-clock | startup | compute | startup share |
|---|---|---|---|---|
| dev machine | 19.871s | 0.764s | 19.107s | **3.8%** |
| CI runner | 21.348s | 1.567s | 19.781s | **7.3%** |

The aggregate is **92–96% compute**. Startup dominates the *dev-vs-CI
difference*, not the level — revision 1 conflated those and concluded the metric
measured the wrong thing. It does not.

Stability, from 18 raw CI samples across 6 runners over two days:
range 20.352–21.439s, **peak-to-peak 5.2%, CV ~1.6%**. The prior ceiling of
21.0s sits inside that range, which is the whole failure.

## 4. What the per-test budgets actually assert

Revision 1 assumed these were a sufficient fallback. Measured on CI:

| test | asserted value (CI) | budget | real headroom |
|---|---|---|---|
| `CFGConstruction_SmallFunction_Under10ms` | **0.03–0.04ms** | 10ms | **~250–300×** |
| `Binding_SmallModule_Under100ms` | **1ms** (integer ms) | 100ms | ~100× |
| `Memory_LargeModule_Under100MB` | **−0.00 MB** | 100MB | **cannot fail** |
| `Memory_SmallModule_Under10MB` | 0.00 MB | 10MB | **cannot fail** |
| `Memory_MediumModule_Under50MB` | 0.02 MB | 50MB | **cannot fail** |
| `Parsing_MediumModule_Under500ms` | 68.5ms | 500ms | ~7× |
| `FullAnalysis_LargeModule_Under30Seconds` | 5.74s | 30s | 5.2× |
| `Scalability_LinearWithFunctions` | ratio | **< 16** | permits O(n²) under a name saying "Linear" |

Three defects, none of which revision 1 knew about:

1. **The three memory tests measure nothing.** `MeasureMemory`
   (`VerificationPerformanceTests.cs:77`) takes `GC.GetTotalMemory(true)` after
   the action, by which point the module allocated inside the lambda is
   unreachable and collected. They assert against ~0 MB and **can never fail**.
2. **`CFGConstruction_SmallFunction_Under10ms`** — cited in revision 1 as the
   tight, sensitive counter-example — has ~250× headroom, not 2×.
3. **`Scalability_*` asserts `ratio < 16`** on a 4× input growth, i.e. exactly
   quadratic, under a name promising linear.

## 5. Why "tighten budgets to 2–3× observed" is not viable

Measured suite-level flake probability, if every budget were set to k × its CI
median (any one of 21 red fails the run):

| k | P(red per run) with **no code change** |
|---|---|
| 1.5× | 68.7% |
| **2×** | **12.2%** |
| 2.5× | 4.2% |
| 3× | 0% of 24 samples |

And if calibrated from a dev machine — the same mistake that set the 21.0s
ceiling — **2× gives a 93% red rate on CI**.

Two structural reasons the small tests cannot carry a tight budget:

- **Ordering.** xUnit does not fix class order; across 24 CI invocations both
  orders occur. Whichever class runs first pays first-call JIT:
  `Parsing_MediumModule` 65.7ms warm vs **117.2ms cold (1.78×)`;
  `TaintAnalysis_ManyFunctions` 1.90×. A 2× budget is consumed by a coin flip.
- **Quantization.** Sub-100ms tests assert on integer `ElapsedMilliseconds`. At a
  1ms median a 2× budget is not expressible.

## 6. Plan

Four phases, ordered so the release is unblocked without deferring the
compensating work under release pressure.

### Phase 1 — unblock, by fixing the calibration (not the metric)

Raise `maximumMedian` from 21.0s to **24.0s**, with the CI evidence recorded in
the baseline's `noisePolicy`: highest observed CI sample 21.439s, 18-sample range
20.352–21.439s, so 24.0 sits ~12% above the worst observed rather than inside the
distribution. This is the governance `docs/ci-quality-gates.md` already
prescribes and the precedent #942 already set (20.0 → 21.0, with the measured
median and spread written into the file).

Alternative, already built and 3/3 green on
`fix/perf-gate-relative-calibration`: gate on **compute** (wall-clock minus a
same-runner calibration) at 22.0s against a 19.27s CI median — ≈+14%. Either
works; the ceiling raise is one line and has precedent, so it is the
recommendation.

**This does not unblock the release on its own** — see phase 2.

### Phase 2 — root-cause #965, which is the actual blocker

The exit-143 terminations are unexplained and unreproduced since 08-13. Facts
worth carrying into that investigation:

- The kills landed at 24s, 26s, 52s, 79s and 26m — inconsistent with any single
  timeout, and `performance.yml` sets no `timeout-minutes` (360m default).
- Every `if: always()` step was **skipped**, which GitHub does on
  *cancellation*, not failure.
- Host state captured immediately pre-gate was healthy: 86G disk free, 14G RAM
  available, inodes 6%.
- `test (performance)` — which does not use the gate script — died the same way,
  so the cause is not in `run_performance_gate.py`.
- **The bisect that appeared to pin this to #938 is unreliable**: several commits
  it marked FAIL pass today. It may have been measuring a transient condition.
- Exit 143 on a hosted runner is classically an OOM kill. Calor links **Z3
  native**; a native leak is invisible to `GC.GetTotalMemory` and therefore to
  every one of the 21 budgets. Worth instrumenting RSS and cgroup
  `memory.events` rather than managed-heap deltas.

### Phase 3 — repair the substrate before tuning anything

Tuning budgets on the current substrate would re-tune noise:

1. Fix or delete the three vacuous memory tests — hold the module alive before
   measuring, or drop the assertion and stop claiming coverage.
2. Pin class order (`ITestCollectionOrderer`) and add an explicit warm-up fact so
   no test pays first-call JIT. Worth up to 1.9× on individual tests.
3. Switch every sub-100ms assertion from `ElapsedMilliseconds` to
   `Elapsed.TotalMilliseconds`.
4. Rename `Scalability_Linear*` to match what it asserts, or tighten the ratio to
   something actually linear.

### Phase 4 — add the sensitivity that no timing budget here provides

Deterministic counters, measured on this codebase (5 processes × 5 iterations,
medium module):

| signal | variance |
|---|---|
| token count (45,609) | **identical across all 25 measurements** |
| CFG block count (5,900) | **identical across all 25** |
| allocated bytes, warm | **1.20% spread** |
| allocated bytes, cold | bit-identical across processes |
| wall-clock, same operation | **123% spread** |

Assert exact-or-±2% on tokens, bound-node count, CFG blocks/edges, taint fixpoint
iterations, and `GC.GetAllocatedBytesForCurrentThread()`. Machine-independent,
order-independent, JIT-independent, diagnostic by construction, and it catches
the cumulative-regression case that timing budgets miss. It does **not** catch a
pure constant-factor slowdown at identical allocation, so it complements the
timing gate rather than replacing it. Greenfield — no use of
`GetAllocatedBytes` exists in the repo today.

Remaining timing assertions should then be reframed at ~5× observed as *smoke
tests* ("did something catastrophic happen"), not as a performance gate.

## 7. Retracted — revision 1, and what refuted it

Kept deliberately. Every claim below is mine and every refutation is measured.

| revision 1 claimed | refuted by |
|---|---|
| The aggregate blocked v0.13.2 | All four publish runs died on `exit 143`/crash; none on the threshold |
| Wall-clock is "mostly startup" | Startup is 3.8% dev / 7.3% CI; the metric is 92–96% compute |
| `CFGConstruction_Under10ms` has 2× headroom and catches 10× regressions | Actual 0.03–0.04ms → ~250–300× headroom |
| Per-test budgets are a sufficient fallback | 3 memory tests cannot fail; `Scalability_*` permits O(n²) |
| Tighten to 2–3× observed | 2× → 12.2% flake/run; dev-calibrated 2× → 93% |
| The aggregate "does not today" catch cumulative regressions | It did, in #942 — it forced a fixture-sharing optimisation before the ceiling moved |
| Per-test times are portable, "little startup" | CI/local ratio 1.7–2.0× for small tests vs **1.035×** for compute-subtracted aggregate |
| Test-count honesty survives via `check_trx.py` | `check_trx.py` is **not** run in `performance.yml`; count enforcement lives inside the script being deleted |
| Deleting touches 4 files | 8 files. `test.yml:249` runs the script on every PR, and `check_test_quality.py` — inside the **required** `calor-first-guard` check — satisfies the manifest→workflow link *only* by reading that script. Deleting it reds a required check on every PR (#949's failure mode) |
| dev suite = 19.871s | Does not reproduce; 16.674s same machine/branch/day |

The pattern worth naming: revision 1 matched "this needs three layers of
correction, so the metric must be wrong" onto a case where the metric was sound
and the *calibration* was wrong — and would have deleted the most stable signal
in the suite to fix a release it was not blocking.

## 8. Decisions

1. **Phase 1 ceiling: raise to 24.0s, or switch to compute-subtracted at 22.0s?**
   Recommendation: raise the ceiling — one line, precedented, no new machinery.
2. **Does phase 3 block phase 4?** Recommendation: yes for the timing budgets,
   no for the counters — counters are unaffected by ordering and JIT, so they can
   land first and independently.
3. **Should the aggregate stay release-blocking, or become nightly-only?**
   Recommendation: keep it blocking with an honest ceiling. It is the only signal
   covering whole-process cost, and #790's documented intent for nightly-only was
   about *per-PR* flake, not the release path.

## 9. Sequencing

1. This revision reviewed and §8 decided.
2. Phase 1 PR — ceiling raise with evidence in `noisePolicy`. Verify green over
   ≥5 CI dispatches, not one.
3. Phase 2 — #965 investigation, with RSS/cgroup instrumentation. **The release
   does not ship until this is understood**, because it is the actual blocker.
4. Phase 3 PR — substrate repairs; arm the revert on each (breach a budget, watch
   it fail).
5. Phase 4 PR — deterministic counters.
6. Re-tune the remaining timing budgets to ~5× CI median, framed as smoke tests.

## 10. Standing caution

Two claims in this document rest on single measurements and should be re-verified
before being relied on: the dev-machine column in §3 (which already failed to
reproduce once) and the counter variances in phase 4. Everything else is drawn
from repeated CI samples and is cited as such.
