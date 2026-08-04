# v0.12 S1 Kickoff — WS-S0.5 Funnel Probe

**Status:** Kickoff, **revision 2** — adversarial review applied (verdict on revision 1: **40%**, three blocking findings; dispositions in §6). Decisions below are priced and recorded **before** the work they govern.
**Parent:** [`substrate-plan-v0.12.md`](substrate-plan-v0.12.md) (Draft v2.2). Governing gates: [`agent-native-gates.md`](agent-native-gates.md), Annex A at A-1.4 tranche 2.
**Created:** 2026-08-04
**Milestone:** S1 — the plan's §3 step 1.

---

## 0. Why S1 exists

v0.11's measurement half died of task-supply starvation, and the close-out named three candidate causes. **None has been measured against the others.** WS-S0.5 raises *n* from 3 toward ~30 and reports where in the funnel candidates actually die, so WS-S1's L-sized fidelity box is scoped from a table rather than a prior.

The funnel the harness implements, labelled honestly:

```
native files → candidates sited → clause (a): survives mutation natively
             → clause (b): observable + arms agree → addressable ( ≡ eligible )
```

**`addressable` and `eligible` are the same population** for the expressible stratum: `EligibilityPredicate` applies the addressability clause last, with no filter between it and `Eligible = true`. Revision 1 tried to seal the eligible count while publishing `addressable`; that was incoherent, and it is why this revision reorders the freeze instead (§1 D-0).

At n = 3 all observed mortality was in clause (b) — but 0/3 is also consistent with a 25% true pass rate (~42% of the time). S1 resolves that ambiguity as cheaply as it can be resolved.

---

## 1. Priced decisions

### D-0 — A-1.5 freezes BEFORE the probe runs (supersedes revision 1's seal)

The plan's §3 constraint (a) forbids setting a bar after its value is visible. Revision 1 satisfied it by sealing the probe's eligible count. That does not work: `addressable ≡ eligible`, the count is the residual of the openly-published exclusion counts, and the harness broadcasts it via console, report, JSON, `bundles/` directories, and the **process exit code** (`Program.cs:281`) — with no seal mode in existence.

- **Decision:** freeze A-1.5 **before** the probe, from inputs that are already public or that require no eligibility evaluation:
  - per-project `NativeFraction` (published, `bench/corpus/README.md:122–124`);
  - the `3 candidates / 3 addressable / 0 eligible` baseline (published, gates A-1.4 tranche 2);
  - the dry-run variance, for M-S3's power derivation (`w4-dryrun-001/VERDICT.md`);
  - the **enumeration-only pre-pass** ceiling (D-1).
- The probe then runs **after** the freeze and informs **box scoping** — a resourcing decision, not a bar.
- **No blindness is claimed and none is needed.** This is an ordering control, which arithmetic cannot defeat, rather than a concealment control, which it can.

### D-1 — Enumeration-only pre-pass first (free, and it de-risks everything downstream)

`ExpressibleMutationOperators.Enumerate` is pure over source text: no builds, no tests. Running enumeration alone across the corpus yields the **candidate-supply ceiling** per project, per operator, per file — in minutes.

- **Decision:** run it first, publish it, and use it for (i) A-1.5's supply ceiling, (ii) the plan §4 go/no-go, and (iii) sizing the probe's cap against reality rather than against a guess.
- It also settles whether n ≈ 30 is even reachable: `EffectViolation` requires a block-bodied method with a **predefined** `int`/`long` return type, and the guard-removal operators require a wrapping `if` with no `else` over a bare identifier compared against `0`/`null`/`.Length`. On a mediator, a logger, and a validator, fewer than 10 enumerable candidates per project is entirely plausible.

### D-2 — `--stratum expressible`, not `both`

The cap applies to the **merged** cross-stratum list in lexicographic order (`TaskGenerator.cs:118–124`), so at `both` the logic stratum consumes slots and the expressible *n* stays small. The expressible stratum is the thesis-testing one (plan §0.1); the logic stratum is retired as a thesis channel (plan §9).

### D-3 — Cap 10/project, no early stop — with the clustering caveat that revision 1 dropped

- **Decision:** `--max-candidates 10 --target 0`, subject to D-1's ceiling.
- **`.Take(10)` is a lexicographic prefix, not a sample.** The evaluated candidates come from the alphabetically-first native files — possibly one or two per project. `NoObservableDefect` is a property of *that file's* test coverage and `ArmsDiverge` of *that file's* conversion, so intra-cluster correlation is high by construction. **n ≈ 30 is 3 clusters of ~10, not 30 draws.** The close-out recorded this verbatim ("a biased slice of the candidate space, not a representative truncation of it"); revision 1 cited it in D-2 and dropped it in D-3, where it undercut the power claim. That selective carry-forward is the exact failure the caveat-propagation lesson was supposed to have fixed.
- **Restated power claim, honestly:** pooled and iid, 30 draws would put `P(0 | p=0.25)` at 1.8×10⁻⁴. But D-S3.4 forbids pooling, and at the per-project level (n=10) `P(0 | p=0.25) = 5.6%`, `P(0 | p=0.10) = 35%`. Under intra-file correlation ρ ≈ 0.5 the effective n is ~5–6 and a true 25% rate still yields 0/30 about **20%** of the time. **The probe rules out a high clause-(b) rate weakly and cannot rule out a 10% rate at all.** It is worth running because it is cheap and strictly better than n=3 — not because it is decisive.
- If D-1 shows enough supply, prefer a **seeded uniform sample** or ≤2-per-file stratification over the lexicographic prefix; the seed is then registered in §3.

### D-4 — `NegateCondition` stays disabled (and is inert here)

The filter at `TaskGenerator.cs:58` sits inside the `Logic && Injected` branch, which `--stratum expressible` never enters — so this disposition has **no effect on the probe**. It binds v0.12 logic-stratum supply only. The code's recorded rationale (`:54–56`) is a scoping convenience — "keep the demo focused on point mutations" — and revision 1 upgraded it to a measurement-validity claim the code does not make. Recorded as scoping.

### D-5 — Region-granularity clause (a): decided by a **pre-committed numeric rule**, on a statistic that can actually answer it

Revision 1 deferred this to "look at clause-(a) mortality, then decide". Two problems: that is not a trigger, and **clause-(a) mortality cannot answer the question**. Under `--stratum expressible`, candidates are enumerated only over files that **already converted natively** (`TaskGenerator.cs:38,65–74`), so the only reachable clause-(a) exclusions are "the mutation itself broke nativeness". D-S0.5.4's actual concern — one interop block excluding every site in a file — manifests *upstream*, as candidates never enumerated, and appears nowhere in the clause-(a) column. Run 1 would therefore have reported ≈0 clause-(a) mortality, and the reading "clause (a) kills almost nothing → the change is unnecessary" would have been produced by the instrument rather than the corpus — while being the outcome that saves the most work.

- **Decision, pre-committed now:** D-1's pre-pass additionally enumerates (does **not** evaluate) expressible candidates over `ConvertedWithLosses` files, reporting `S_withloss` beside `S_native`.
  - **If `S_withloss / S_native ≥ 0.5`** → site-level clause (a) is **accepted** and priced into WS-S1's box.
  - **Below 0.5** → **rejected**, and the rejection is final for v0.12.
- Enumeration-only, so this costs one extra pass and no build time.

### D-6 — MediatR submodule must be initialized

`bench/corpus/MediatR` is uninitialized locally. Initialize it, or the probe reports **two** projects and says so — a silently changed denominator is the failure mode this program keeps finding.

### D-7 — `--native-bar 0` is set, and it does almost nothing (correcting revision 1)

Revision 1 called this "the decision that makes the probe possible at all", on the premise that the fidelity gate excludes below-bar projects from contributing tasks. **That premise is false.** `options.Fidelity` is read in exactly one place — `TaskGenRunner.cs:25`, *after* the per-project generation loop — `TaskGenerator.cs` never references it, and `ExclusionReason.FidelityGateBelowBar` is **declared and counted but never assigned**. The phrase "contributes no tasks" is an interpolated string in a report (`FidelityGate.cs:78`), not enforced behaviour.

- **What `--native-bar 0` actually changes:** the report's gate table, `FidelityGateResult.Signal`, and `MeetsDefinitionOfDone`. The funnel, the dispositions, the addressability rates, and the bundles written are **bit-identical** either way.
- **Decision:** set it anyway, so the run report does not read as a fidelity verdict — and record that it is cosmetic.
- **Recorded as a defect:** the harness/report divergence (a gate that reports exclusion it does not perform) is a real trap for future readers and a candidate fix alongside D-S1.4.

### D-8 — The funnel measures today's native surface, and is a biased-optimistic estimator for WS-S1's

The probe measures clause-(b) pass rates on code that is native **now**. WS-S1's purpose is to make *more* code native — and the plan's own M-S5 rationale argues that increment is "the marginal, hardest cases, which are the likeliest to diverge semantically", i.e. `ArmsDiverge` should be **higher** there. So the probe's clause-(b) rate is an optimistic estimator for the population WS-S1 creates. Recorded because the alternative — scoping WS-S1 from it silently — is scoping from the wrong population without saying so.

---

## 2. Pre-pass and probe: what runs, in order

1. **Enumeration-only pre-pass** (D-1, D-5) → supply ceiling + `S_withloss / S_native`.
2. **A-1.5 registration frozen** (D-0), including the **frozen mutation-operator set** (D-S0.5.2) that PP-S2's anti-tautology guard depends on.
3. **Funnel probe** at the pinned configuration below.
4. **Box confirmation / re-scope** for WS-S1 / WS-S2 / WS-S3.

## 3. The pinned configuration (probe)

```
dotnet run -c Release --project tools/Calor.RoundTrip.Harness -- \
  gen-tasks MediatR Serilog FluentValidation \
  --stratum expressible \
  --max-candidates 10 \
  --target 0 \
  --native-bar 0 \
  --output bench/phase0-agent-native/epochs/s1-funnel-001
```

- Corpus pins: MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
- `--projects-dir` is **omitted deliberately**: `ProjectConfigs.DefaultCorpusDir` already resolves `bench/corpus` from the repo root cwd-independently, whereas the flag resolves against process cwd.
- `--source` is **omitted**: the expressible branch checks `Strata` only and ignores `Sources`, so it would be inert and misleading.
- Also pinned, because they materially affect the result and revision 1 left them implicit: **`RequireIdenticalSignature = true`** (not CLI-exposed; it produces the `ArmsDiverge` half of the clause-(b) split, and the close-out deliberately refused to relax it), the `--dotnet` resolution, each project's `ExcludePatterns` (the plan's own "cheapest gaming route"), and the compiler commit.
- **Bundles are written**, contrary to revision 1's claim: every eligible candidate triggers `WriteBundleAsync`, a fourth full conversion+build cycle, producing `bundles/<taskId>/`. **Pre-committed:** probe bundles are quarantined under the epoch directory and never recycled into an epoch; they carry no marker distinguishing them from bar-passing bundles, which is itself worth fixing.

## 4. Cost, stated two-sided

Per candidate reaching the Calor arm: up to ~14 `dotnet build` and ~4 full-suite runs (recovery alone iterates up to 5 builds), against suites of 157 / 811 / 866 tests. **~3–6 h if clause-(b) mortality is near-total; 15–25 h+ if it is not** — note the perverse structure: **the probe is cheapest exactly when its answer is "≈0% pass", and expensive exactly when the hypothesis it exists to test is true.** There is no `--build-timeout` wired into `gen-tasks` (it exists only for `run`), so there is no CLI lever to bound this. **Pre-committed:** on exceeding a 12 h wall-clock budget the run is stopped and the partial funnel is reported **with its realized denominator disclosed**.

## 5. Exit criteria

- Enumeration ceiling + `S_withloss / S_native` published; D-5 resolved by its numeric rule.
- **A-1.5 frozen**, including the mutation-operator set.
- Funnel table committed with the pinned configuration, realized cap, and any time-budget truncation disclosed.
- D-S0.5.3 baseline zero committed as a versioned artifact — **this is the baseline (n = 3) run's accounting**, whose `0/3` is already public at gates A-1.4 tranche 2, not the probe's.
- WS-S1/S2/S3 boxes confirmed or re-scoped.

## 6. Deviations and dispositions

| Item | From | Disposition |
|---|---|---|
| A-1.5 freezes before the probe | Plan v2.1's seal | v2.1's remedy refuted (`addressable ≡ eligible`); replaced by ordering (D-0) |
| Region-granularity decided by `S_withloss/S_native ≥ 0.5` | D-S0.5.4 ("before the numbers are seen") | Pre-committed numeric rule on a statistic that can answer the question; clause-(a) mortality cannot (D-5) |
| Probe is a **precursor** to D-S0.5.1, not D-S0.5.1 itself | Plan D-S0.5.1 | Run 1 has neither region-granularity nor new operator breadth; recorded rather than substituted silently |
| `--native-bar 0` retained but reclassified as cosmetic | Revision 1's D-1 | Its premise was false about the harness (D-7) |
| Funnel is optimistic for the post-WS-S1 population | — | Recorded, not corrected (D-8) |
