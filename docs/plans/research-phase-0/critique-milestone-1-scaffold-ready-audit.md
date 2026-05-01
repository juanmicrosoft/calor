# Devil's Advocate Critique: Milestone 1 — Scaffold Ready (source-audited)

**Reviewer role:** Devil's advocate. Source-code audited.
**Reviewing:** `docs/plans/research-phase-0/milestone-1-scaffold-ready.md`
**Companion review:** `docs/plans/research-phase-0/critique-milestone-1-scaffold-ready.md` (Copilot CLI — distinct concerns)
**Date:** 2026-05-01

This critique audits every factual claim in the milestone document against the actual repository state, then attacks the methodology assumptions the document inherits from the rest of `research-phase-0/`. The audit is not adversarial. The methodology critique is.

---

## TL;DR

The scaffold is real and well-built. The numbers in the milestone are *almost* honest, with one factual misstatement and several rhetorical overreaches. The bigger problems aren't in this milestone — they're in the pre-registration framework this milestone claims to satisfy. **The milestone says "Pre-registration locked" and "Five docs, all committed before any model run." Both directories (`bench/research-phase-0/` and `docs/plans/research-phase-0/`) are untracked in git as of this review.** That's not a quibble; it's the central pillar of the pre-registration discipline failing on its own headline claim.

The eight things to address before declaring the scaffold "ready":

1. **Nothing is committed.** "Locked" is an aspiration, not a status. Either commit now or rewrite the milestone.
2. **The `graders/` directory doesn't exist.** Feature-acceptance tests for T1.A/B/C are listed as "what's next" but the rubric needs them to compute `correctness`. The rubric is therefore *not yet executable* — it's locked-but-incomplete.
3. **N=5 per cell has ~30% statistical power** for the 1.5× threshold the rubric uses. A real 50% improvement could easily be missed; a sample artifact could easily be mis-counted as a 50% improvement.
4. **Mean of `score / dollar_cost` ratios is the wrong aggregator.** Means of ratios are biased toward outliers. Geometric mean or median is standard.
5. **Three-way pass criterion ("at least one of T1.A/B/C wins")** has no multiple-comparison correction. False-positive rate inflates from 5% to ~14%.
6. **The metric measures cost-effectiveness, not language quality.** Dollar cost is dominated by token pricing and prompt-cache hit rate, not by Calor-vs-C# properties.
7. **"Seeded mess is encoded with explicit `#pragma` suppressions"** — there is exactly **one** `#pragma` in the entire scaffold. Most MESS items are semantic, not lint-detectable. The doc's framing implies otherwise.
8. **The "Calor compiler chokes at 45-file scale" risk is undervalued.** This is the make-or-break gate; rated equal to "models find T1.A trivial." It's not equal.

The audit confirms the scaffold counts and contents. The methodology under it is shakier than the milestone admits.

---

## Audit results

I verified every numerical claim against the repository.

| Milestone claim | Repo reality | Status |
|---|---|---|
| 45 `.cs` files | 45 (excluding `bin/`, `obj/`) | ✅ |
| 2,160 lines of C# | 2,160 lines | ✅ exact |
| 5 projects | Domain, Infra, Services, Api, Tests | ✅ |
| 52 files total (45 .cs + Directory.Build.props + 1 .slnx + 5 .csproj) | 45 + 1 + 1 + 5 = 52 | ✅ |
| `~46` target from scaffold-spec | 45 — within tolerance | ✅ |
| `INV-1` through `INV-7` named tests | `INV1_…` through `INV7_…` | ✅ exact |
| 10 named tests in `StateTransitionTests.cs` | 10 `[Fact]` methods | ✅ |
| 41 total `[Fact]` tests | `3+3+7+5+5+10+5+3 = 41` | ✅ |
| `MESS-1` through `MESS-7` present and documented inline | All 7 found across 6 files with explanatory comments | ✅ |
| `TreatWarningsAsErrors=true` in `Directory.Build.props` | Present | ✅ |
| 5 pre-registration docs in `docs/plans/research-phase-0/` | README, scaffold-spec, scoring-rubric, t1-maintenance-prompts, cost-budget | ✅ |
| **"Five docs, all committed before any model run"** | **Untracked** (`?? docs/plans/research-phase-0/`) | ❌ **false** |
| **"Scaffold built" — committed** | **Untracked** (`?? bench/research-phase-0/`) | ❌ **false** |
| `dotnet build`: 0 warnings, 0 errors | Not re-verified in this audit; claim plausible given `TreatWarningsAsErrors` | ⚠️ unverified |
| `dotnet test`: 41/41 passing, 591ms | 41 `[Fact]` methods exist; pass status not re-verified in this audit | ⚠️ unverified |
| "9 test files" | **8 test files + 1 `TestFactory.cs`** (which is a factory, not a test) | ⚠️ minor mislabel |
| "Encoded with explicit `#pragma` suppressions" (plural) | **1 `#pragma`** in `InventoryRepository.cs` (CS1998 for MESS-5 fake-async). The other 6 MESS items are semantic. | ⚠️ misleading framing |

The hard counts are accurate. The two material defects are: (a) "committed" is false, (b) the `#pragma` framing is misleading.

---

## Defect 1 — "Pre-registration locked" is the most important claim and it isn't true

The milestone's first bullet says:

> Pre-registration locked at `docs/plans/research-phase-0/`: README, scaffold-spec, scoring-rubric, t1-maintenance-prompts, cost-budget. Five docs, all committed before any model run.

`git status` says:

```
?? bench/research-phase-0/
?? docs/plans/research-phase-0/
```

Both directories are untracked. **Nothing is committed.** The README in the same directory is explicit about what "locked" means:

> 1. **Lock before you run**: scaffold-spec, scoring-rubric, and t1-maintenance-prompts are committed before T1.A first run begins. Any change after that point must be a *new* doc with a different name … never an edit to the original.

The milestone document acknowledges the gap deeper down:

> Decisions deferred to user — 1. **Commit the scaffold to git as `phase-0-baseline` tag?** I haven't committed because new commits need explicit authorization.

The headline says "all committed." The footer says "not committed." Both can't be true. **This is the single most consequential defect** because pre-registration discipline is what the entire experiment cites as defense against post-hoc rationalization. If the docs aren't committed, they can be silently edited after seeing results, and the discipline collapses.

**Fix:** Commit the directories, tag `phase-0-baseline`, then update the milestone to say "Pre-registration locked at commit `<sha>`, tag `phase-0-baseline`". Until then, the status field should read **"scaffold built / pre-registration NOT YET locked / dry-run pending / Calor port pending"** — four items, not three.

---

## Defect 2 — The `graders/` directory doesn't exist; the rubric is therefore not yet executable

The milestone's "what's next" section, item #1:

> Write feature-acceptance test files for T1.A/B/C in `graders/` (tested through API surface, not bound to model's type names) — Cost: $0 — Blocking: Required for scoring

`graders/` does not exist (`ls bench/research-phase-0/graders/: No such file or directory`). The scoring rubric defines:

> `correctness ∈ {0, 1}`: 1 iff the new tests for the requested feature pass; 0 otherwise.

If the new tests don't exist, `correctness` is undefined. The rubric is therefore locked-but-incomplete: it can't be applied to a run because half its inputs aren't written.

This matters because the milestone's confidence assessment ("**High (~85%)**") frames the remaining 15% as "empirical risk" rather than "missing prerequisites." Missing graders is not an empirical risk; it's a known gap that blocks scoring. The 85% confidence number is dressed up by counting graders as "what's next" rather than "not yet done."

**Fix:** Either lower the confidence to ~60% (scaffold built, pre-registration in progress, graders pending) or block the milestone until graders are in place and committed alongside the rest of the pre-registration.

---

## Defect 3 — N=5 statistical power is too low for the 1.5× pass bar

The rubric:

> Calor "passes" if, for at least one of (T1.A, T1.B, T1.C):
> `mean(PrimaryMetric_calor) ≥ 1.50 × mean(PrimaryMetric_csharp)` at N=5

A standard two-sample test (Welch's t on log-scores or paired t on per-prompt cells) at N=5 per group, α=0.05, has approximately:

- **30% power** to detect Cohen's d = 1.0 (large effect)
- **60% power** at d = 1.5
- **<15% power** at d = 0.5 (small/medium)

The rubric demands a 1.5× *ratio*, not an effect size. On a metric with high variance (single $25-cap-bounded run + cost denominator), the corresponding effect size is hard to predict but plausibly d ≈ 0.8–1.5.

What this means in practice:
- **False negative**: a real 50% improvement could be missed roughly 40–70% of the time.
- **False positive**: by random sampling at N=5, the cost ratio between two language conditions varies by ~30–50% even when the underlying populations are identical. A "1.5×" finding on N=5 is *one decent run away* from being a sample artifact.

The milestone doesn't address this. The cost-budget commits to N=5 because it's affordable; the rubric uses 1.5× because it's the program's "promising bar." Neither is anchored in a power calculation.

**Fix:** Either run a power calculation showing the experiment is properly powered for the 1.5× claim (which probably means N=20+, not N=5), or downgrade the milestone language: this is a *pilot* that produces a directional signal, not a confirmatory study. The README's "95% confidence Phase 0 is well-designed" target is incompatible with N=5 absent a power calculation.

---

## Defect 4 — Mean of `score / dollar_cost` is the wrong aggregator

The rubric:

```
RawScore = correctness × invariant_preservation × (1 - regression_rate)
PrimaryMetric = RawScore / dollar_cost
```

> Aggregate across N=5 trials per (language, prompt) cell using the **mean**. Report median + IQR alongside.

Means of ratios are statistically biased toward outliers in the denominator. One Calor trial that costs $1.50 (because the agent gave up early with `correctness=1`) dominates four trials at $15. The mean is no longer a population estimate — it's an outlier estimate.

Standard practice for ratio-of-quality-to-cost metrics:
- **Geometric mean** for aggregating ratios — symmetric in numerator/denominator perturbations
- **Median** as the primary, with IQR — robust to outliers
- **Per-trial t-test on log(score) − log(cost)** — converts ratio comparison to a difference test, well-behaved under normal noise

The rubric mentions "median + IQR alongside" but commits to mean as primary. With N=5 and a metric that includes a 1/cost factor, mean is the worst of the three options.

**Fix:** Make median primary; keep mean and geometric mean as secondaries. Or specify the trimmed mean (e.g., trimmed at 10%). Lock this *before* runs, because changing it after seeing data is a data-dredging risk.

---

## Defect 5 — "At least one of T1.A/B/C" inflates the false-positive rate

The pass criterion is disjunctive across three prompts. With α=0.05 per comparison:

- 1 comparison: FPR = 5%
- 3 comparisons: FPR ≈ 1 − (1−0.05)³ ≈ **14.3%**

That is, even if Calor and C# produce indistinguishable distributions on PrimaryMetric, there's a ~14% chance that at least one of the three prompts will spuriously meet the 1.5× bar. Combined with N=5 (high variance), the experiment could easily produce a "Calor passed!" outcome from pure noise.

Standard mitigations:
- **Bonferroni**: require α/3 = 0.0167 per prompt → effectively raise the bar to maintain 5% family-wise error
- **Benjamini-Hochberg**: control FDR if multiple "wins" are claimed
- **Pre-specify a single primary prompt** with the others as secondary

The rubric specifies neither. The README cites "anti-gaming clauses" — they should explicitly include multiple-testing correction.

**Fix:** Pre-register one of: a single primary prompt for the pass decision, or Bonferroni-adjusted thresholds, or a combined-metric pass bar (e.g., mean across prompts must hit 1.5×, not max).

---

## Defect 6 — The metric is mostly measuring price, not language

`PrimaryMetric = correctness × invariant_preservation × (1 − regression_rate) / dollar_cost`

The numerator is bounded in [0, 1]. The denominator is unbounded and varies by:
- **Token prices** (Opus 4.7 input $15/M, output $75/M — fixed for both languages, but)
- **Prompt cache hit rate** (varies by run; favors whichever language has more cacheable prompt prefix)
- **MCP server token overhead** (Calor calls more MCP tools; more tokens consumed by tool returns)
- **1M-context tier premium pricing** (cost-budget.md: "premium pricing — TBD on first run")
- **Re-run noise from rate limits, retries, network**

The cost-budget.md applies a `1.3×` adjustment to Calor estimates to account for MCP tool overhead. That's a quiet admission that **Calor starts at a cost disadvantage that has nothing to do with language quality**. Multiplying the score by `1/cost` then concluding "Calor is worse" is a tautology, not a finding.

The flip side: if Calor *wins* on this metric, the win could be entirely attributable to fewer turns rather than better language design. The rubric's secondary clause —

> The improvement is not driven entirely by `dollar_cost` (i.e., raw `correctness × invariant × (1 - regression_rate)` is also non-inferior, within 10%).

— is an attempt to address this, but "within 10%" on a metric capped at 1.0 with N=5 is ~0.1 raw correctness points, well within sampling noise.

**Fix:** Report two parallel scores: `Quality = correctness × invariant × (1 − regression)` and `CostEfficiency = Quality / dollar_cost`. Pre-register pass criteria for each separately. Don't conflate them into one ratio that hides which factor moved.

---

## Defect 7 — The MESS items are mostly semantic, not lint-detectable

The milestone says:

> 0 warnings on `TreatWarningsAsErrors` → seeded mess is encoded with explicit `#pragma` suppressions, not hidden under loose lint settings

Audit: there is exactly **one `#pragma`** in the entire `src/` tree:

```
bench/research-phase-0/csharp-baseline/src/WholesaleOrders.Infra/Persistence/InventoryRepository.cs:
  #pragma warning disable CS1998
  #pragma warning restore CS1998
```

That suppresses the "async method without await" warning for MESS-5. The other six MESS items don't generate compiler warnings because they're **semantic** issues (rounding inconsistency between `Order.cs` and `OrderService.cs` for MESS-2, behavioral inconsistency in logging for MESS-4, parameter-order coupling to legacy systems for MESS-3, etc.). A C# compiler can't detect any of these.

This isn't wrong per se — semantic mess is more interesting to test than lint-detectable mess, because real maintenance asks the model to *understand* code, not just satisfy a linter. But the milestone's framing **implies most messes are pragma-suppressed warnings**, which would mean the cleanliness of `dotnet build` is evidence the messes are properly tracked. They're not — the cleanliness of `dotnet build` is evidence that 6/7 messes don't trip the compiler at all.

This matters because the rubric uses `regression_rate` from existing tests to gauge whether the model "broke" something. If the messes aren't testable, an agent could "fix" MESS-2 by silently changing rounding to match one side, the tests pass, and the regression goes undetected.

**Spot-check by source location:**
- **MESS-1** (validator allows Cancel from {Draft, Submitted, Paid} but service has different rule) — likely caught by `StateTransitionTests` if cancellation is exercised. Not verified.
- **MESS-2** (rounding inconsistency between two methods) — caught by `INV1_Order_Total_Equals_LineItem_Sum`? Depends on which method that test calls. Not obvious.
- **MESS-3** (parameter-order coupling) — semantic; no obvious test catches an agent who reorders the params.
- **MESS-4** (logging inconsistency) — no test catches behavioral logging differences.
- **MESS-5** (fake async) — behavioral; no test forces actual async behavior.
- **MESS-6** (int×decimal cast to double for legacy reporting) — caught by INV1 only if the test covers the legacy code path.
- **MESS-7** (obsolete field still serialized for partner integration) — no test covers external serialization. **An agent that "tidies" by removing the obsolete field can break a partner integration with no test failure.**

**Fix:** For each MESS-N, document explicitly which test (or set of tests) detects an agent's wrong "fix." Where no test exists (likely MESS-3, -4, -7 at minimum), either (a) add a test, or (b) explicitly mark the MESS as "out of scope for regression scoring" so the rubric can't credit a score on it.

---

## Defect 8 — "Calor compiler chokes at 45-file scale" is undervalued in the risk table

The risk table:

| Risk | Mitigation in place |
|---|---|
| Per-run cost exceeds $25 cap | Budget envelope + hard cap encoded in rubric |
| Models find T1.A trivial | Three prompts at different difficulty levels |
| **Calor compiler chokes at 45-file scale** | **None yet** |
| Tests don't catch realistic regressions | 41 tests + adversarial post-hocs |

Three of these are graceful-degradation risks. **The third is the experiment dies.** If the Calor compiler can't handle a 45-file project, the entire Calor arm of the experiment doesn't run and the program goes from "Phase 0" to "we have C#-only data, please re-port to Calor."

Treating this as one of four equal-weight risks understates it. It is the binary gate. The mitigation row says "None yet" — that's correct, but the milestone proceeds as if this is a manageable open item.

**Fix:** Elevate this to a separate "blocking validation" line item with a sub-budget. Before locking pre-registration, port one moderately-complex file (e.g., `OrderService.cs`, ~250 LoC) to Calor and confirm it compiles. If it doesn't, the pre-registration framework needs to be reshaped (smaller scaffold? sample-only port? scope-down to compiler-friendly subset?). This is item #3 in "what's next" but the milestone declares "scaffold ready" before this risk is retired.

---

## Defect 9 — The hard cap harness isn't shown

`t1-maintenance-prompts.md`:

> A run completes when:
> - The model says it's done, OR
> - 60 minutes elapse, OR
> - 50 turns reached, OR
> - $25 spent (hard cap per run)

`cost-budget.md`:

> Single run exceeds $25 → Halt run, log, investigate before continuing

Where is the harness that enforces these caps? The milestone doesn't reference one. If the runs are conducted by manually starting Claude Code sessions, the operator is the cap — which means cap enforcement depends on the operator's diligence and the cost-tracking visibility of the CLI in real time (Claude Code reports cost; it doesn't hard-stop at a threshold).

This isn't necessarily fatal, but it's an unmentioned dependency. If runs occasionally exceed $25 because the operator wasn't watching, the cost-budget assumptions break and the rubric's $/run denominator gets fatter for some cells than others.

**Fix:** Either (a) ship a harness script that wraps the run and enforces caps, or (b) document the manual procedure for cap enforcement and acknowledge the operator-discretion risk.

---

## Defect 10 — Doc-precision issues that should be cleaned up

These are minor but cumulative — the things a careful reviewer flags before locking pre-registration:

- "9 test files" — there are **8 test files + 1 `TestFactory.cs`** (a factory, not a test). The 41 `[Fact]` count is correct; the file count is off by one. Trivial to fix.
- "Encoded with explicit `#pragma` suppressions" — see Defect 7. Either use singular or rephrase ("encoded as semantic patterns; one CS1998 pragma for the only lint-detectable case").
- The "what's next" table claims **#4** (running 30 trials) costs ~$575 and is the "Phase 0 main result." But cost-budget.md's Phase 0 ceiling is also $575, which means the dry-run, debugging reserve, and tooling-viability checks are inside the same envelope. So the 30 trials cost less than $575 and the sub-line-items in cost-budget.md sum correctly — but the milestone reads as if $575 buys the trials, which is ambiguous.
- Confidence claim: "**High (~85%)**" with the residual "10%" risk table. 85 + 10 = 95, not 100. Off-by-five on the math the doc itself uses to express its position.

---

## Open questions the milestone doesn't ask

These are decisions you'll hit during the dry run and confirmatory runs. Each deserves a one-paragraph answer in the next milestone:

1. **What counts as a "model says it's done" terminal signal?** Models phrase this differently across runs. Pre-register the regex/keyword set or the operator's decision rule.
2. **What does "fresh Claude Code session" mean technically?** A new shell? A clean home dir? An ephemeral container? Each gives different prompt-cache behavior, which directly affects `dollar_cost` (and therefore PrimaryMetric).
3. **Who writes feature-acceptance tests in `graders/` — the same person who wrote the scaffold?** If yes, there's a moderate "designer's blindspot" risk: the test author and the scaffold author share a mental model and may write tests that the scaffold trivially satisfies. Cross-check by having a second reviewer write half the tests blind.
4. **Is the 1M-context tier on Opus 4.7 used for runs?** If yes, the premium-pricing TBD in `cost-budget.md` could blow per-run estimates. If no, a 50K-line agent context might not fit, forcing context compaction that affects performance differently per language.
5. **What's the exit clause if the Calor port fails?** "Stop and report" is the program-level rule; what about the scaffold-level? The milestone doesn't say.
6. **Re-run policy on rate-limit / API errors.** Common cause of cost variance. Pre-register: "trial restarts from prompt-zero on hard error" vs. "trial continues from last state."

---

## What this critique does NOT say

- It does not say **the scaffold isn't real**. The audit confirms the file counts, line counts, test counts, and MESS distribution. That work happened.
- It does not say **the experimental design is wrong**. Pre-registration with a synthetic codebase, locked rubric, and frozen prompts is the right shape.
- It does not say **the budget is wrong**. $575 for Phase 0 is reasonable.
- It does not weigh in on whether the Calor program should run at all. (Strategic question; outside this critique's scope.)

It says: **the milestone overclaims its own status**. "Pre-registration locked / all committed" is false. The rubric is locked-but-incomplete (no graders). The metric is statistically under-powered for its pass bar (N=5, mean of ratios, no multiple-comparison correction). And the "scaffold ready" framing buries the one risk that can kill the entire experiment (Calor compiler at 45-file scale).

The fix is a follow-up commit + a milestone restatement, not a redesign.

---

## Punch list (in order, before T1.A first run)

1. **Commit the directories.** Tag `phase-0-baseline`. Update milestone to cite the commit SHA and tag.
2. **Restate status** as "scaffold built / pre-registration NOT YET locked / graders pending / dry-run pending / Calor port pending" until #1 is done and graders exist.
3. **Write feature-acceptance tests** in `graders/T1.A/`, `graders/T1.B/`, `graders/T1.C/`. Have a second reviewer write half blind.
4. **Run a power calculation** for N=5 vs. the 1.5× pass bar. If the experiment is under-powered, either raise N or downgrade the rubric to "directional pilot."
5. **Switch primary aggregator to median** (or geometric mean). Mean of ratios is wrong.
6. **Add multiple-comparison correction** (Bonferroni or BH) to the disjunctive pass criterion. Or pre-specify a single primary prompt.
7. **Split the metric**: report `Quality` and `CostEfficiency` separately. Pre-register pass criteria for each.
8. **Document MESS-N → test-N coverage** in a table. For uncovered messes (likely MESS-3, -4, -7), either add tests or scope them out of regression scoring.
9. **Validate the Calor compiler at scale** before locking pre-registration. Port one ~250-LoC service file and confirm `calor --input` succeeds. If not, restructure the experiment.
10. **Show the run harness** that enforces 60 min / 50 turns / $25 caps. Or document the manual procedure and accept the operator-discretion risk.

If those ten are addressed, the milestone is genuinely ready to lock and the scaffold is ready to run against. Until then, the milestone is reporting a status it hasn't earned.

---

## Audit sources

- `bench/research-phase-0/csharp-baseline/` — 45 `.cs` files (excl. `bin`/`obj`), 2,160 lines, 5 projects + Directory.Build.props + .slnx
- `tests/WholesaleOrders.Tests/` — 41 `[Fact]` methods across 8 test files + 1 factory (`TestFactory.cs`)
- `git status` — both `bench/research-phase-0/` and `docs/plans/research-phase-0/` are untracked
- `grep -rohE "MESS-[0-9]+" src/` — all 7 MESS labels present
- `grep -rE "#pragma warning" src/` — exactly 1 occurrence (CS1998 in `InventoryRepository.cs`)
- `docs/plans/research-phase-0/` — 5 docs (README, scaffold-spec, scoring-rubric, t1-maintenance-prompts, cost-budget) + this critique
