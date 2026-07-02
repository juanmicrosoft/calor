# Agent-Native Strategy — Gate Thresholds (Pre-Registration)

**Status:** Draft v2 (v1 revised per adversarial review, 2026-07-02 — reviewer verdict on v1: 45%, "not fit to freeze"; all CRITICAL/MAJOR findings incorporated below, dispositions in §8). **Freezes at the Phase 0 suite freeze**, after the feasibility calculation (§6) is complete. Until frozen, values may change; after freezing, only the supersession rule (§7) permits change.
**Parent:** [`agent-native-strategy.md`](agent-native-strategy.md) (v4.1). The planning envelopes in its §6 constrain every value here; a value outside its envelope requires a strategy-doc revision first.
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-07-02

---

## 0. Rules of interpretation

1. Every gate decision uses **pinned-model, simultaneous, arm-vs-arm** comparisons within one epoch. An **epoch** is one gate's complete paired run (both arms, same pinned model set, same suite version). **All gate metrics are collected every epoch on all categories** (so cross-epoch deltas always have a Δ₁). Cross-epoch difference-in-differences is permitted in exactly two places: the parity-kill clause (§4) and nowhere else — the neutral-regression gate is within-epoch by construction (§4).
2. A Calor-arm run with Z3 unavailable, effects enforcement off, or permissive mode on is **invalid** — detected by automated config check before results are examined. **Invalid, crashed (agent or harness), or API-errored runs are re-run with a fresh seed until N valid runs exist, capped at 2 re-attempts per slot; a slot still invalid after the cap counts as task failure for that arm.** Held-out tests must be deterministic, verified at authoring by 5 consecutive green runs against the reference solution; flakiness discovered after freeze is a §7 protocol defect.
3. Metrics are computed per pre-registered category, then aggregated per gate as specified. No post-hoc category creation, merging, or exclusion (quarantine under §3.4 is rule-driven and one-sided).
4. "Significant" always means the decision rule in **§6.1** — never eyeballing.

## 1. Harness configuration (pinned)

| Setting | Calor arm | C# arm |
|---|---|---|
| Compilation surface | `Calor.Sdk` MSBuild path (template `.csproj`), not bare CLI | standard `dotnet build` |
| Effects enforcement | **on** (`EnforceEffects=true`) | n/a |
| Permissive effects | **off** | n/a |
| Contract mode | `debug` (all checks) | n/a |
| Z3 | present, required | n/a |
| `.g.cs` policy | **writes blocked** (hook), reads permitted but logged | n/a |
| Analyzers | Calor defaults | **Roslyn analyzers + NRT enabled + full agent-generated evidence permitted** (tests, property-based tests, asserts, analyzer configs) |
| Agent | same agent product, same model pin, same iteration budget both arms | same |
| Held-out tests | arm-shared, black-box, **run silently by the harness after every iteration** (never surfaced to the agent) | same suite, same runner |

Model pins recorded per epoch in `bench/phase0-agent-native/epochs/<epoch-id>/pins.json` (model IDs, agent-tool version, compiler commit, suite version).

## 2. Metric definitions (machine-adjudicable)

- **Iteration:** one harness-observed build-or-test invocation following ≥1 workspace edit. A build with no preceding edit does not count. A task-level timeout mid-iteration fails that iteration; the task continues or censors per remaining budget.
- **Declared-done:** the agent's first terminal non-edit action (stops, asks a question, refuses) or budget/timeout exhaustion. A non-compiling final state counts as **all held-out tests failing**.
- **Task success:** all arm-shared held-out tests pass within the iteration budget (**10 iterations, fixed; the pair schema pins it as a constant — per-pair deviations require pre-freeze registration with rationale**).
- **Escaped bugs:** held-out test failures at declared-done. **Unit of analysis: the per-pair mean over runs, then the per-pair arm delta** (never per-category sums, which weight pairs by test count).
- **Iterations-to-green:** iterations until held-out tests first all pass (harness-observed, silent); never-green tasks count at budget+1 (censored). **Censored fraction is reported per arm; a gate is invalid if either arm exceeds 40% censored on neutral tasks** (a ratio of mutual failure is not a pass).
- **Median ratio:** the **median of paired per-pair ratios** (each pair's per-arm mean first, then the ratio) — not a ratio of arm medians.
- **Tokens:** total input+output per task, both arms — **recorded, not gated**.
- **`.g.cs` dead-end rate (Phase 1 only):** fraction of Calor-arm runs where either (a) the agent attempts a blocked `.g.cs` write, or (b) ≥1 wasted iteration occurs on a `.g.cs`-located error — *wasted* = two consecutive compiles with identical errors (identity = error code + message normalized by stripping paths/line numbers) and no `.calr` edit between; for runtime errors, (b) additionally requires the agent's subsequent action to reference the `.g.cs` frame (a stack trace merely containing generated frames is not by itself a dead-end).
- **Retained-check overhead (recorded, not gated):** wall-clock delta of the held-out suite under contract mode `debug` vs `off`, Calor arm only.

## 3. Phase 0 — fairness gates (suite construction)

3.1 **Difficulty-equivalence band (C# arm):** a pair is out-of-band only if its C#-arm success rate's **90% CI lies wholly outside ±20pp ⚠ of the leave-one-out category mean** (point-estimate banding at feasible N flags pairs on binomial noise alone; the band value freezes from §6 dry-run variance). Out-of-band pairs are re-authored (max **M = 2** rounds) or dropped.
3.2 **Kill threshold:** if strictly more than **N = 25%** of pairs in any gate-bearing category remain out-of-band after M rounds (at minPairs=4: ≥ 2 pairs), the Phase 0 kill row fires (publish and stop).
3.3 **Calor-side symmetry (authoring-time):** each pair authored from the same behavioral spec; declaration count and cyclomatic-complexity sum within ±30% across arms; no Calor fixture may omit spec-required functionality. Recorded in `pair.json`.
3.4 **Calor-side symmetry (measured):** at every epoch where a category's Calor-arm success is non-degenerate (≥ 30% category success — the check applies per category, only where that category itself is non-degenerate), run the band check on the Calor arm. **Only high-side (Calor-easy) outliers are quarantined from advantage evidence; low-side outliers stay in.** Pooled gate results are reported with and without quarantine. (One-sided by design: symmetric quarantine would remove Calor failures from gate evidence — selection in Calor's favor.)
3.5 **Wedge fixtures** are authored against [`../verification-modeled-forms.md`](../verification-modeled-forms.md); a wedge fixture whose intended contracts fall outside the whitelist is invalid at authoring time.

## 4. Phase gate thresholds

Envelope references are to strategy §6. Values marked ⚠ are provisional pending §6 and freeze with it.

| Gate | Metric | Threshold | Envelope |
|---|---|---|---|
| **Phase 1** | `.g.cs` dead-end rate | ≤ **5%** of Calor-arm runs | 0–10% |
| **Phase 1** | Neutral iterations-to-green | Calor ≤ **125%** of C# arm (median ratio, §2) | 115–140% |
| **Phase 1** | Neutral task success (non-inferiority companion) | Calor within **15pp ⚠** of C# arm | — (guards the degenerate both-arms-censored pass) |
| **Phase 2a** | Escaped bugs, pooled wedge (W1+W2+W3) | ≥ **30%** relative reduction ⚠, per §6.1 decision rule | 20–40% |
| **Phase 2a** | Iterations-to-green, pooled wedge | Calor ≤ **120%** of C# arm (appeasement allowance — bounded cost, not an advantage requirement) | 110–125% |
| **Phase 2a** | Neutral regression (within-epoch) | 2a-epoch neutral median ratio ≤ **137.5%** (= Phase 1 threshold 125% × 1.10 tolerance) | tolerance 5–15% |
| **Phase 2b** | Same as 2a, on 2b-exercising categories | same values | same |

**Pooling constraint:** the gate pool is W1+W2+W3 with **no category contributing more than 40% of pooled pairs**; exact per-category pair counts freeze in `categories.json` at suite freeze. Per-category results are reported; the gate is the pooled result.

**Parity-kill clause (the only cross-epoch rule):** sign convention — a positive delta means Calor better. Kill the current phase iff **(a)** the pooled-wedge escaped-bugs point estimate Δ₂ ≤ Δ₁ across two consecutive epochs, **and (b)** Δ₂'s one-sided 80% upper confidence bound is below the frozen 2a threshold. (Condition (a) alone is a hair-trigger at feasible N; condition (b) alone is a dead letter; the conjunction means "not improving and not plausibly near passing.")

**Adopter gate (parallel with Phase 1):** named adopter with ≥1 non-maintainer reviewer, agreed in writing, by the Phase 1 gate date. Failure routes to the 2a kill *action* with conclusion "demand unproven."

## 5. Category registry (pre-registered)

Defined in [`../../bench/phase0-agent-native/categories.json`](../../bench/phase0-agent-native/categories.json): gate-bearing wedge **W1** contract-preserving refactors, **W2** contract-dense algorithmic, **W3** first-order effects; deferred-eligibility **W4/W5** (post 2a-item-4 + 2b-item-1); context **C1–C3** (C#-favored); neutral **N1**. Proportions: W1–W3 ≥ 4 pairs each (≤ 40% pool share each), N1 ≥ 8, C1–C3 ≥ 2 each. No new categories after freeze.

## 6. Feasibility (power) requirement

Before freezing, a dry run (≥ 3 runs per arm on ≥ 5 pairs — a floor; the power calculation must use the **upper confidence bound of the estimated variance**, not the point estimate) determines the frozen N (runs per task per arm per epoch). N must give ≥ 80% power under the §6.1 decision rule for the frozen escaped-bugs threshold within the epoch API budget ceiling of **$1,500 ⚠**. If the numbers don't close, reduce category count or raise the threshold *before* freezing — never after.

### 6.1 Decision rule (gate-time)

- **Unit of analysis:** the per-pair arm delta (per-pair means over runs first).
- **Test:** cluster bootstrap over pairs (runs nested within pairs), **one-sided** — every gate is directional, so two-sided α would be miscalibrated. α = 0.05.
- **Pass rule, stated in full:** a threshold gate passes iff the point estimate meets the threshold **and** the one-sided 95% CI excludes zero effect. (Point-passes-but-not-significant = gate fails; significant-but-below-threshold = gate fails. No third outcome.)
- Ratio gates (iterations, neutral regression) use the same bootstrap on paired per-pair ratios against their threshold constant.

## 7. Supersession rule

After freezing, this document may be superseded only for a **documented empirical defect in the measurement protocol itself** (metric shown to be noise, harness bug invalidating runs) — the `phase-2-measurement-protocol` v1→v2 standard. A successor must contain a written defect analysis. **Threshold changes after seeing arm results are never a valid supersession.** Residual: at bus factor 1 the defect judgment is self-made; the written-analysis requirement and the envelopes are the constraint. Disclosed, not solvable by a document.

## 8. Revision log

**v1 → v2 (2026-07-02, adversarial review: 45% as written → est. 85% after fixes).** All findings accepted: §6.1 decision rule added (C1); equivalence band made interval-based with leave-one-out mean (C2); neutral-regression gate made within-epoch, resolving the §0.1 self-contradiction (C3); parity-kill made decidable via the two-condition conjunction, with all-metrics-every-epoch collection and sign convention (C4); median-ratio defined as paired per-pair ratios + non-inferiority companion + censoring cap (M5); quarantine made one-sided high-only, reported with/without, every epoch (M6); 40% pool-share cap (M7); iteration budget pinned as schema constant, timeout rule added (M8); invalid/crash/flake handling added (M9); `.g.cs` metric operationalized — write-block pinned in §1, error-identity normalization, runtime-frame condition (M10); iteration/declared-done/silent-test-execution defined (M11); integer arithmetic stated (m12); non-degeneracy scoped per-category (m13); variance upper-bound rule (m14); tokens marked recorded-not-gated (m15).
