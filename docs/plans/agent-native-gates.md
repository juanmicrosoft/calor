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
3.6 **Hardness (uniform-ceiling) check** *(added after the wave-1 feasibility dry run, which found both arms at ~100% success — a state §3.1's relative band is structurally blind to)*: before freeze, a live hardness epoch (≥3 runs/arm on candidate pairs) must show pooled **C#-arm** success on each gate-bearing wedge category strictly below a ceiling (planning envelope **70–90%** ⚠, frozen with the rest). A category at or above the ceiling cannot detect an escaped-bugs advantage at any N and must be re-authored harder before freeze. Results recorded per category in the epoch directory.
3.7 **Determinism evidence** (§0.2's 5-consecutive-green rule) is produced at authoring and **committed** under `bench/phase0-agent-native/epochs/authoring-validation-*/` — uncommitted validation claims do not count.
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

---

## Annex A — Instrument metrics (loop plan v0.9, D4.4)

**Annex version: A-1.11 (additive A-1.11, 2026-08-25 — registers **PP-E1**, the 0.15 effect-rows
fixture-scale probe: freeze event named, five spike fixtures and ten injectable mutations frozen
with an honest-timing disclosure, a four-valued outcome with an exhaustive decidability fallback,
and a cost-leg margin re-derived from `w5-parity-002` (null p95 1.321 → point margin 1.35,
conjoined with bootstrap lower bound > 1.0); with it roadmap M1 is complete and E2 may merge;
additive A-1.10, 2026-08-25 — mechanical freeze guard, first half of the 0.15 amendment: `scripts/check-annex-freeze.py`, run by `experiment-registry-tamper-check.yml`, byte-freezes every A.2 PP row and table header and every A.3 entry against `main`, additions only, every annex change logged, the guard run from `main` not from the PR; no PP registered — the 0.15 PP row is the pending second half; additive A-1.9, 2026-08-06 — PP-W5 adjudicated PASS on epoch w5-parity-002 (w5-parity-001 VOID: both arms shared a compiler), point 1.098 recorded / 1.0016 after a metric erratum; registered as "no large tax detected", explicitly not parity and with the earlier heterogeneity claim withdrawn; additive A-1.8, 2026-08-05 — PP-A1 adjudicated: all nine PASS, item 6 only after six audits and three merged fixes (#872/#876/#878), item 9 delivered outside its registered window (#877); additive A-1.6, 2026-08-05 — PP-W2 restated to instrument scope + PP-S1 disposition; additive A-1.7, 2026-08-05 — Call S adjudicated: PP-S3 = MISS, PP-S1 = MISS, PP-S4 = PASS, venue retired; thresholds frozen at A-1.0, 2026-07-24; additive
clarification A-1.1, 2026-07-25; additive PP-W1/M-W1 registration A-1.2, 2026-07-27;
additive M-G*/PP-G3/PP-G4 registration A-1.3, 2026-07-29 — guarantees plan
D-G5.1, frozen before the Guarantees probe epoch; additive PP-W5 registration
A-1.4 tranche 1, 2026-08-01 — wedge plan D-W5.1, frozen before any WS-W2
strictness-batch code merges; **additive A-1.4 tranche 2, 2026-08-04 — registers
PP-W2 = NOT ADJUDICATED; no threshold frozen, no epoch run**; **additive A-1.5, 2026-08-04 —
v0.12 substrate registration: M-S1…M-S5, PP-S1…PP-S4, the pinned measurement configuration and
frozen mutation-operator set, the §4 go/no-go (does not fire), D-5 (rejected), and the PP-W5
additive note**).** This annex is **additive-only
with respect to the main document**: no §1–§7 machine-adjudicable gate
criterion references any metric defined here, and nothing here alters the
C#-vs-Calor gate decisions those sections govern. The annex's own proof-point
thresholds (A.2) ARE adjudicated pass/fail criteria — for the **loop program's
tooling-investment decisions** (the loop plan's PP-L*), a separate question
from the language gates. Stated precisely so "observational" is not
overclaimed (review of #795 item 2). It carries its own version counter and
revision log; changes to this annex never constitute supersession of the main
document. Pre-registered per the loop plan's D4.4 discipline: thresholds through
A-1.1 were frozen from the `loop-feasibility-dry-002` variance epoch
**before** any treatment build exists; the A-1.2 PP-W1 row froze by the
feasibility-by-determinism argument recorded in its Basis column (a
disclosed D4.5 deviation — supersession entry in `loop-plan-v0.9.md` §10),
before the probe epoch runs. **Merge-order dependency**: the governing plan
(`loop-plan-v0.9.md`, PR #747) must be on main before or with this annex, or
its §-references dangle and the pre-registration claim is unverifiable from
the repo (review of #795 item 1).

### A.1 Instrument metric definitions

**v0.12 substrate metrics (A-1.5).** Full definitions, bars, and derivations in the A-1.5 entry (§A.3):
- **M-S1** — per-project `ConversionCoverage.NativeFraction` at the pinned `ExcludePatterns`.
- **M-S2** — candidates verification-addressable via the #856 differential. **Reported, not adjudicated.**
- **M-S3** — expressible-stratum tasks passing the D-W4.1 predicate and the D-S5.1 determinism screen.
- **M-S4** — converter/harness changes raising M-S3 while reducing faithfulness. CI-enforced via A-1.5.7.
- **M-S5** — per-project `ArmsDiverge` count ÷ candidates reaching clause (b).

Telemetry source: `loop-telemetry/2` records (normative schema:
`bench/phase0-agent-native/loop-telemetry-schema.md`). Iteration semantics,
pairing, and censoring follow §2 of this document.

- **M-E1 envelope coverage** — % of the envelope-schema denominator
  (`docs/cli/envelope-schema.md` tables) emitting schema v1.1. Measured in CI
  by the conformance suite. Status: 100 % as of #757 (WS1 exit).
- **M-E2 counterexample attach rate** — over the D1.5 outcome corpus: % of
  `refuted` scalar obligations whose envelope carries a concrete model.
- **M-E3 cliff visibility** — over the D1.5 corpus: % of obligations
  reporting one of the five proof statuses; the choke-point bypass test is
  the structural guarantee.
- **M-L1 feedback latency** — `feedback_latency_ms` P50/P99: wall time from
  agent-visible invocation start to agent-visible completion, explicitly
  excluding the harness's silent held-out observation and telemetry
  bookkeeping (arm-fairness; see schema doc).
- **M-L2 first-apply validity** — % of edited iterations whose build exits 0,
  split by `edit_mechanism`. **Heal accounting (A-1.1)**: on the `mcp-file`
  mechanism the per-attempt stream (`mcp-writes.jsonl`, schema doc) is the
  source, and `calor_file_write` auto-heals before checking — so
  `applied/attempts` is first-apply-**after-autoheal** validity, crediting
  the tool for slips it repaired. The stream journals `healApplied` per
  record and the run summary carries `appliedUnhealed`; cross-mechanism
  comparisons against raw arms must report both forms.
- **M-L3 diagnostic actionability** — of failing edited iterations whose
  envelope named ≥1 `declarationId`, % where the next edited iteration's
  `edit_target_ids` intersects the named set. **Adjudication floor: 20
  qualifying events per epoch**; below it, reported-not-adjudicated.
- **M-L5 tokens-to-green** — per-run agent output tokens (`agent.json`
  `usage.output_tokens`); per-pair means, then median of paired per-pair
  ratios per §2. Iterations-to-green remains **recorded, observational**.
- **M-W1 defect catch rate (A-1.2)** — per arm: the fraction of the D5.1
  injected defects **absent at declared-done**, each defect adjudicated by
  its dedicated held-out probe test (fails iff the defect is present) and
  aggregated by per-defect majority across exactly 3 runs/arm/pair.
  **Telemetry source is NOT `loop-telemetry/2`**: the per-run signal is the
  `defect {id, class, probeTest, caught, smokeTampered}` object in the
  run's `result.json` (run-pair.sh D5.1 support). Semantics, frozen with
  the row: the probe test lives in its own project compiled against the
  **starting** public surface, so `caught` measures the defect and not
  task completion (an agent that fixes the defect without finishing the
  feature still scores a catch); `caught` requires the probe to have run
  and passed against the declared-done build; a non-compiling final state
  counts as not-caught, consistent with §2's all-failing rule; a slot
  still invalid after §0.2's retry cap contributes **not-caught** for its
  run (task failure ⇒ the defect was not shown absent); a run with
  `smokeTampered: true` (the arm-shared smoke suite was modified) is
  invalid for M-W1 and re-run per §0.2. PP-W1 consumes the Calor − C#
  delta of this rate.

- **M-G1 depth-corpus proven rate (A-1.3)** — CI metric, no agent: % of
  contracts in the committed outcome corpus
  (`tests/TestData/Verification/Outcomes/*.calr`) expected-proven fixtures
  reporting non-vacuous `proven`, reported per tier (sound-core:
  `proven.calr`/`proven-with-result.calr`; depth: `proven-with-binding.calr`).
  **Disclosed narrowing (#825 review M4):** the guarantees plan's
  multi-module tier is deferred — no multi-module corpus fixtures exist yet
  (D-G4.2's seed lives in the CLI test suite); the tier registers by
  re-baseline when its fixtures land. Corpus additions re-baseline; they
  never silently move the rate.
- **M-G2 verdict honesty (A-1.3)** — CI metric: 100 % of corpus outcomes in
  the closed seven-status vocabulary (whitelist-conformance-backed,
  `ModeledFormsTests`); **zero refutations on known-proven fixtures** (#807
  regression pin) and **zero elisions without a ∀-proof** (#755 pin, incl.
  vacuous and assumed never eliding). Enforced by
  `Calor.Verification.Tests`; a red run is a PP-G1/PP-G2 regression.
- **M-G3 catch earliness (A-1.3)** — per injected defect (the frozen A-1.2
  D5.1 set, unchanged fixtures), the earliest channel that surfaced it per
  arm, ordered **`build-proof` > `build-block` > `runtime-guard` >
  `caught-unattributed` > `missed`** (#825 review C2 — the taxonomy must be
  total and must not conflate the surfacing channel with the adjudication
  artifact): `build-proof` = a journal `diagnostics[]` entry with code ∈
  {`Calor0711`, `Calor0712`} whose `declarationId` is the seeded declaration,
  BEFORE declared-done; `build-block` = a journal `diagnostics[]`
  `Calor0410`-class error on that declaration, before declared-done;
  `runtime-guard` = `ContractViolationException` in the agent-visible
  transcript's test output (`agent.json`), before declared-done —
  **explicitly excluding `.probe_final.txt`** (the probe runs only at
  declared-done and is the adjudication artifact, not a surfacing channel: a
  MISSED W5-B defect also throws there); `caught-unattributed` = defect
  absent at declared-done with no captured surfacing event (e.g. fixed by
  reading the code); `missed` = defect **present at declared-done**,
  regardless of what the failing probe output contains. **Aggregation: per
  defect per arm, majority across exactly 5 runs** (odd by construction — no
  tie rule); an invalid slot (retry cap exhausted, A-1.2 semantics) counts as
  `missed` for aggregation, mirroring A-1.2's invalid ⇒ not-caught.
  M-W1 (catch/no-catch, A-1.2 semantics) is computed alongside from the same
  runs for cross-epoch continuity; its exactly-3 aggregation is superseded by
  exactly-5 **for this epoch's continuity copy only** — the adjudicated A-1.2
  PP-W1 result stands untouched. **Configuration registration:** each arm runs
  its NATIVE registered config — the v0.9 control arm (tag
  `guarantees-baseline-v0.9` = `ce7708af`) per A-1.2 (static-verify channel
  excluded), the v0.10 arm with the verify gate on. This is declared config
  variance, not drift; identical tasks, fixtures, and model pins. The
  earliness comparison is therefore a **single-arm property of the v0.10
  configuration** (the v0.9 arm-C mold); only M-W1 totals compare across arms.
  **The verify gate, concretely (#825 review C1):** the v0.10 arm's gate is
  compile-path `--verify`, wired in TWO places so refutations are both
  journaled and agent-visible — the harness's silent envelope compile gains
  `--verify` (journal `diagnostics[]` carries the Calor0711/0712 events
  M-G3 reads), and `Calor.Tasks.CompileCalor` gains a `Verify` property set
  in the workspace template csproj (the agent sees refutations as build
  diagnostics; Warning severity, the build still succeeds, so smoke and
  `.smokehash` mechanics are unchanged). **Instrumentation to build
  pre-epoch, results-blind — frozen as part of this registration** (a
  registered metric with no measurement path is unfalsifiable, and A-1.2's
  standard was a named per-run artifact): (1) `Calor.Tasks` `Verify`
  property + template wiring; (2) `--verify` on the v0.10 arm's envelope
  compile; (3) a `verify` field in the arm config checked by the §0.2
  `check_pins` machinery — a v0.10-arm run without the gate is INVALID;
  (4) per-run archival of the seeded declaration's final source at
  declared-done (M-G4's input); (5) the M-G4 diff+implication harness step
  (a small CLI entry over the repo's implication prover). None change
  fixture content; all must pass a null-agent shakedown plus one live
  single-run probe before the epoch (the smokeTampered lesson).
- **M-G4 prover-appeasement incidence (A-1.3)** — on the contract-carrying
  pairs (the W5-B trio): (a) iterations-to-green per §2 semantics; (b)
  **contract-weakening incidence, decided mechanically — never by
  inspection**, at the CONJUNCTION level (#825 review M1 — per-contract
  textual matching would let the deleted-vs-modified branch decide the
  verdict), compared as **two separate legs** (#826 review C2 — a single
  mixed §Q∧§S conjunction scores an ADDED §Q as a strengthening, hiding the
  canonical prover-appeasement move of restricting inputs until the
  refutation disappears): per eligible run, take the seeded declaration's
  final `§Q`/`§S` sets from the source archived at declared-done
  (instrumentation item 4) and the fixture's frozen sets; the declaration is
  matched by its function node ID — a renamed/removed declaration, a changed
  signature, an unparseable final source, or an empty final contract set is
  **weakened**; otherwise conjoin each side's `§Q` set and `§S` set
  separately (an empty set conjoins to `true`) and the run is weakened iff
  **either** (§S leg) `conjS(frozen) ⇒ conjS(final)` proves AND the converse
  disproves, **or** (§Q leg) `conjQ(final) ⇒ conjQ(frozen)` proves AND the
  converse disproves — i.e. the postcondition was relaxed or the
  precondition was strengthened (both directions via the repo's implication
  prover — a semantically equivalent rewrite is NOT weakened; a §S
  strengthening or §Q relaxation is NOT weakened). The check additionally
  emits **`intactOrStrengthened`** := `conjS(final) ⇒ conjS(frozen)` proven
  AND `conjQ(frozen) ⇒ conjQ(final)` proven (#826 review M3) — this field,
  not `¬weakened`, is PP-G3 leg-b's intact-or-strengthened predicate: a
  gutted incomparable contract is not-weakened under the asymmetry rule yet
  earns no leg-b credit. `unknown`/`timeout`/`unsupported` on any needed
  direction marks the run **mechanical-check-indeterminate** unless one leg
  already determinately shows weakening (a proven weakening cannot be
  un-weakened by the other leg's indeterminacy); indeterminates count as
  NOT-weakened and REMAIN in the denominator (#825 review m3 — no
  asymmetric shrinkage). Integer literals outside the signed 32-bit domain
  are refused by the translator rather than silently wrapped (#826 C4
  follow-through; wrap corrupted verdicts in both polarities) — such
  contracts yield indeterminate, never a false determinate verdict. Eligible runs = all VALID runs on
  the W5-B trio in both arms — 3 pairs × 5 runs × 2 arms = **30 ≥ the
  20-run floor** by design; invalid slots leave the denominator (#825
  review m2), and if either arm's realized valid count falls below 12 the
  weakening leg is reported, not adjudicated. **Decidability fallback,
  pre-registered:** if > 20 % of eligible runs are
  mechanical-check-indeterminate, the weakening leg is likewise reported,
  not adjudicated (the PP-L4 pattern).

### A.2 Frozen instrument proof-point thresholds

<!-- annex-freeze (A-1.10): every table row in this section whose first cell names a PP id is byte-frozen against main by scripts/check-annex-freeze.py (CI: experiment-registry-tamper-check.yml). Rows may be added, never edited or removed. -->

| Proof point | Threshold (frozen) | Basis |
|:---|:---|:---|
| PP-L1 (warm latency) | P50 ≤ 300 ms, P99 ≤ 1 s on the D3.3 fixture | unchanged from the loop plan; toolchain metric, not epoch-dependent |
| PP-L2 (machine-actionable failures) | M-E1 = 100 %; M-E2 ≥ 90 %; M-E3 = 100 % | met at WS1 exit (#754/#757); regression = CI failure |
| PP-L3 (node vs file edits) | **retired unrun** | machine-zone E1 killed H1 (55 % pooled); pre-committed scope gate |
| PP-L4 (diagnostics steer the agent) | **reported-not-adjudicated** | dry-run found 3 qualifying M-L3 events across 35 runs — floor (20) unreachable at authorable-fixture scale |
| PP-L5 (loop tooling pays off) | **≥ 15 % relative reduction in median per-pair tokens-to-green**, arm A (`loop-baseline-ws1`) vs arm B (baseline + WS2/WS3 isolation build), simultaneous epoch, ≥ 7 pairs × ≥ 5 runs/arm, §6.1 adjudication | dry-run: iterations-to-green floor-bound (median 1, 94 % at floor → undetectable at any N); tokens MDE at 80 % power ≈ 15 % at the stated N — a **design-stage simulation estimate over only 7 clusters** (400 sims × 200-resample cluster bootstrap), so the MDE itself carries wide uncertainty; if the M5 epoch's realized variance is materially higher, the miss is reported as underpowered rather than adjudicated as a clean miss. Pre-registered fallback applied **before** freezing |
| PP-W1 (enforcement catches seeded defects, A-1.2) | **M-W1 delta (Calor − C#) ≥ 3/9 defects** on the D5.1 set (N = 9; class definitions frozen here: **W5-A** = undeclared side effect via a named static manifest-covered call inside a pure-declared function; **W5-B** = violated scalar `§S` postcondition caught by the Debug runtime guard on arm-shared smoke-test inputs; **W5-C** = effect laundered through a covered **intra-module** call chain where the caller's declaration is violated and every callee's is honest). Catch = defect **absent at declared-done** per its held-out probe test, aggregated per defect by majority across **exactly 3** runs/arm/pair (odd by construction — no tie rule needed). **Adjudication is the raw aggregated count — no §6.1 significance test** (9 binary clusters would be degenerate under bootstrap). **Zero-vs-zero, precisely: both arms catch 0/9 after majority aggregation = the pre-committed Call 2 kill signal** (loop plan §6.2). A negative delta (C# catches more) is a clean miss and thesis-adverse — reported with the same prominence as a kill. **Decidability fallback, pre-registered:** if per-defect run outcomes are not unanimous for ≥ 7 of 9 defects in either arm, PP-W1 is **reported, not adjudicated** (the PP-L4 pattern) | feasibility by determinism, replacing a D4.5 dry-run — a recorded deviation from the loop plan's letter (supersession entry in `loop-plan-v0.9.md` §10; rationale in `loop-m4b-ws5.md` §2). Honest scope of the argument: it covers the *surfacing* channel (W5-A/C are deterministic `Calor0410` build blocks; W5-B's guard throws deterministically on the smoke inputs), **not** agent fix-behavior, which is what "absent at declared-done" also depends on — the unanimity fallback in the threshold column is the guard against that residual. **Scope guards, frozen with the threshold:** the delegate-invocation and override/dispatch effect-laundering holes are excluded and listed (strategy §1.1; pinned by `DelegateInvocation_*` enforcement tests) — a defect requiring them disqualifies its fixture; the static-verify catch channel is excluded until #807 (unconstrained-`result` refutations) is fixed — W5-B catches via the Debug-mode runtime guard only, and W5-B contracts are postcondition-only (a `Proven` precondition elides its guard, #755). Measures detection capability, not organic incidence; no overlap with machine-zone §7 (spec-diff review detection on the dogfood module, §12-amended to absolute detection of externally-authored blinded injections — different subject, corpus, and comparator) |
| PP-G3 (verification depth converts the wedge, A-1.3) | Two legs, both required for a hit. **(a) Cross-arm, paired:** the v0.10 arm's M-W1 total is **not below** the v0.9 control arm's (ceiling note: the control scored 9/9 at `ws5-probe-001`, so this leg is a no-regression bar, not a delta). **(b) Single-arm, v0.10 configuration** (product-configuration claim, NOT cross-arm attribution): **≥ 2 of the 3 W5-B defects earn leg-b credit**, where credit is a per-run JOINT predicate aggregated per defect by majority (≥ 3 of the 5 slots; an invalid slot cannot satisfy the predicate) — the predicate, frozen (#825 review C3): a `build-proof` surfacing event occurred **∧** the defect is absent at declared-done (its probe passes) **∧** the seeded contract is intact-or-strengthened per the M-G4 mechanical check with a DETERMINATE result (mechanical-check-indeterminate counts as not-satisfying — conservative). An agent that ignores the refutation (defect present) or deletes/weakens the contract earns no credit for that run, however loudly the defect surfaced. **Adjudication preconditions:** PP-G1/M-G2 green on the treatment build (an unsound refute-everything verifier maximizes M-G3); raw aggregated counts, no §6.1 test (3 binary items are degenerate under bootstrap — the PP-W1 precedent). **D-G3.1 restate-check, recorded:** the guarantees plan required restating this threshold if D-G3.1 was descoped; D-G3.1 SHIPPED (#824 — all three W5-B contract shapes prove, corpus-pinned), so the threshold stands as planned | feasibility by determinism, disclosed limit included (the A-1.2 pattern): with M-G1/M-G2 green, the W5-B defective bodies encode to genuinely-SAT obligations, so the *surfacing* half of leg (b) is deterministic-by-construction; what the epoch adjudicates is the **agent-behavior residual** (does the agent act on a build-time refutation or delete the contract) plus leg (a)'s no-regression bar. Guarantees plan §5 [P] resolved here, results-blind, before the epoch |
| PP-G4 (depth didn't buy prover-appeasement, A-1.3) | **(a) Iterations leg:** no significant iterations-to-green regression, v0.10 arm vs control: one-sided §6.1 cluster bootstrap (α = 0.05) over **all 9 pairs** on the median paired ratio (v0.10/control), direction convention frozen — a REGRESSION is ratio > 1, and the leg fails iff the one-sided 95 % CI lower bound exceeds 1.0. Disclosed widening of the plan's "contract-carrying tasks" wording: 3 clusters are degenerate under bootstrap (the PP-W1 precedent); the trio's per-pair medians are additionally reported. **Power honesty (the PP-L5 pattern):** iterations-to-green is floor-bound on these fixtures (median 1), so a pass at this N bounds only LARGE regressions; the all-identical degenerate case (every ratio 1.0) is a pass and means "no detectable movement at the floor", not "proven equal" — stated so the blocker's pass cannot be overread. **(b) Weakening leg:** v0.10-arm weakening incidence (M-G4 mechanical decision) exceeds the control arm's by **at most 3 runs, an ABSOLUTE excess** (≈ 20 pp at the designed 15/arm; the run-count margin is the frozen quantity when denominators shrink toward the 12-run floor) — frozen with small-sample honesty: at n = 15/arm, differences below ~3 runs are indistinguishable from noise, so a smaller margin would adjudicate coin flips; 0-vs-0 passes trivially. Release blocker regardless of PP-G3 (guarantees plan §5) | margin and method frozen results-blind before the epoch; the mechanical weakening procedure (implication asymmetry) dogfoods the product and removes the eyeballing this program's gates §0 forbids on adjudicated quantities; the M-G4 indeterminate fallback (> 20 %) guards prover-blind-spot inflation |
| PP-W5 (strictness didn't tax the loop, A-1.4 tranche 1) | **Fails iff BOTH**: the one-sided 95 % cluster-bootstrap lower bound of the **median paired per-pair output-tokens-to-green ratio** (v0.11 treatment / v0.10.0 control) exceeds **1.0**, AND the point estimate exceeds **1.25**. Epoch shape: the four registered N1 neutral pairs (N1-001-string-utils, N1-002-inventory, N1-003-csv-row, N1-005-order-pipeline), **5 runs/arm**, simultaneous, same pinned model; control arm = the immutable `v0.10.0` release tag build, treatment = main at epoch time. **Attribution, stated honestly (#839 review M2): by plan §3 the parity epoch runs at W5, so the treatment carries ALL v0.11 plan work (W1–W4), and this row adjudicates the v0.11-toolchain-vs-v0.10.0 RELEASE question, not a strictness-batch-only attribution.** On a FAIL, diagnosis before any rollback decision uses a `v0.10.0 + WS-W2-only` isolation build (the M5 arm-B recipe) to attribute the tax to the batch vs other v0.11 work — pre-committed here so the on-miss branch cannot blame the batch without isolating it. **Identical harness configuration both arms** (the gates §1 pinned config — enforcement on — so the delta is toolchain behavior, not config variance); edit mechanism `raw` both arms. Iterations-to-green recorded observational (floor-bound); §2 censoring caps apply. **Validity floor (#839 review M3, the M-G4 pattern):** a cell (pair×arm) with < 2 valid runs after §0.2's retry cap drops its PAIR from adjudication with the drop disclosed; if fewer than 3 pairs survive, or either arm's total valid runs fall below 12, PP-W5 is **reported, not adjudicated** — never silently computed over degenerate cells. **Power honesty (the PP-L5/PP-G4 pattern):** at 4 clusters × 5 runs this rule bounds only LARGE regressions — measured detection 0.33/0.62/0.87 at true 1.25×/1.4×/1.6× regressions — so a pass means "no large tax detected", never "proven equal". **Release blocker** per wedge plan §5 | Per-run artifact: output tokens per A.1 M-L5 = `agent.json` `usage.output_tokens`, mirrored as `result.json` `tokens.output` (verified equal on every valid archived N1 run). Frozen results-blind before any WS-W2 batch code merges (wedge plan §6.1: register-then-merge, or the comparison is unfalsifiable in the wrong direction). Margin and rule derived from the existing `m5-compare-001` N1 cells (the correct population — same 4 pairs, Calor arms, neutral tasks): within-cell output-token CV median 0.20, max 0.73 (population-sd convention; the max is the N1-005 calor cell, which contains a 113-token near-empty passing run — the derivation used the full realized data, outlier included); null-simulation point-estimate p95 = 1.247 (the 1.25 margin sits AT the null p95, so the point test alone would false-fail ~5 %); the bootstrap-bound conjunction calibrates the measured null false-fail to ≈ 1.7 %. The PP-L6(b) precedent already adjudicated neutral parity on this same 4-pair N1 set via the §6.1 bootstrap. N1-005 contributed 3 valid runs/arm at m5 (its API-cluster cap-exhaustion, recorded there); the derivation used the realized data |
| PP-E1 (effect rows close the frozen laundering cells, without a large loop tax, A-1.11) | **Two legs, one four-valued outcome (hit / miss / underpowered / not-adjudicated), adjudicated at the 0.15.0 release commit.** **Leg A — detection, deterministic, no agent.** *Fixture set, frozen here* — the five spike artifacts at `f7cd1c46` by path and blob SHA: `docs/design/spikes/effect-rows/after/A2.calr` (`93ecdf16`), `A3-map.calr` (`0885b3dd`), `A3-match.calr` (`c1ce7517`), `A3-middleware.calr` (`e5ee81e2`), `A3-callback.calr` (`05ddc23d`). *Defect classes, frozen here as injectable mutations* — one named mutation per (class, fixture) cell, each a diff against the frozen source: **L5 interface implementation** — broaden the implementing member's row by `cw` against the interface member's — `L5-A2` (A2 `§AMT{m008:Handle}` `§E{e}` → `§E{e, cw}`; expect **Calor0421** at `m008`) and `L5-MID` (A3-middleware `§MT{mt002:Handle}` likewise; expect **Calor0421** at `mt002`); **L6 rank-1 generic instantiation** — launder an effect through an instantiated `eff e` into a caller that does not declare it (design §7.4's own `UseImpure` mechanism) — `L6-MAP` (A3-map `f004:UsePure`'s argument `Double` → `Announce`; expect **Calor0410** naming `cw` at `f004`), `L6-MATCH` (A3-match `f005:BothPure`'s `Identity` → `Shout`; expect **Calor0410** naming `cw` at `f005`), `L6-MID` (A3-middleware `f001:RunTwice` `§E{e}` → `§E{e, cw}`; expect **Calor0410** naming `cw` at `mt002`); **L7 row erasure — invoke a row-less value** — delete the row from a function-typed position and invoke it (§3.5: parameter/field with no row ⇒ `Unknown`) — `L7-A2` (A2's `RequestHandlerDelegate<TResponse>:next`; expect **Calor0425** at `§C{next}` in `m008`), `L7-MAP` (`Func<i32,i32>:f`; at `§C{f}` in `f001`), `L7-MATCH` (`Func<i32,i32>:onSome`; at `§C{onSome}` in `f001`), `L7-MID` (`Func<i32>:g`; at `§C{g}` in `f001`), `L7-CB` (A3-callback `§FLD{Action<i32>:onChange:pri}`; at `§C{onChange}` in `mt001`). **Denominator = exactly these 10 cells** (3 classes × 5 fixtures = 15 grid cells; 10 applicable, and the applicability is frozen with the list — no cell is added, dropped, re-expressed or re-priced at adjudication). **Leg-A HIT bar: 10/10 detected, plus a clean negative control** — every unmutated fixture compiles (exit 0) with **zero** diagnostics of the rejection codes {`Calor0405`, `Calor0410`, `Calor0420`, `Calor0421`, `Calor0424`}; its `Calor0419`/`Calor0425` counts (legitimate on A2's external `Task`/`ConfigureAwait` surface) are recorded as the baseline, not barred, and an `L7` mutant is detected only if the `Calor0425` count **rises** at its registered declaration. Detection = the registered code at the registered declaration; a mutant rejected at that declaration under a *different* effect-family code is **detected-with-code-drift** — it counts in the numerator (the class is closed) and every drift is published by name against design-doc §6.1's allocation, the reason §13.3 gate 1 likewise puts diagnostic *span* outside its denominator. **Leg B — cost, "no large loop tax".** **Fails iff BOTH** the one-sided 95 % cluster-bootstrap lower bound of the **median paired per-pair output-tokens-to-green ratio** (0.15.0 release build / `v0.14.3` release tag) exceeds **1.0**, AND the point estimate exceeds **1.35**. Epoch `e1-rows-parity-001`: the four registered N1 neutral pairs (N1-001-string-utils, N1-002-inventory, N1-003-csv-row, N1-005-order-pipeline), **5 runs/arm**, interleaved, same pinned model, identical harness configuration (gates §1), edit mechanism `raw` both arms, per-arm `--arm-repo-root` with two distinct `Calor.Tasks` hashes recorded in `pins.json` and in every run's provenance (the `w5-parity-001` void is what this pin exists for). Per-run figure = `result.json` `tokens.output` **as derived by `bench/phase0-agent-native/token-usage.py`** (A-1.9.1); the naive `usage.output_tokens` is archived for audit and never adjudicated. §2 censoring caps apply; iterations-to-green recorded observational. **Outcome map, frozen:** **HIT** = leg A 10/10 with a clean negative control **and** leg B does not fail; **MISS** = leg A below 10/10 on a valid harness, **or** leg B fails (both its conditions); **UNDERPOWERED** = leg A 10/10 but leg B's point estimate exceeds 1.35 with the bound leg not firing, **or** leg B's realized median within-cell CV exceeds **0.66** (= 1.5 × the 0.439 the margin was calibrated on — above it the calibration no longer holds), the PP-L5 pattern; **NOT-ADJUDICATED** — the pre-registered decidability fallback, exhaustive, **any route not listed here is a MISS** — (a) any of the five fixtures fails to compile on the release compiler, mutated or not (a defect cannot be injected into a program that does not build); (b) the §4.1 higher-order demand ledger re-executed at the release commit puts `dA.total + dB.aggregate.total` below its frozen floor of **25** (registration-time value 3 + 3121 = 3124), so the demand premise this probe assumes is unproven — roadmap §4.4 gate 2's own floor rule, inherited; (c) the harness is invalid — the PP-W5 validity floor (a cell with < 2 valid runs drops its pair, disclosed; fewer than 3 surviving pairs, or either arm below 12 valid runs), or the two arms fail to record distinct repo roots with distinct `Calor.Tasks` hashes, or either arm exceeds §2's 40 % censoring cap; (d) E2/E3 do not ship in 0.15.0 (roadmap §4.2's schedule abort) — there is no rows checker to adjudicate. **If the design-doc §7.5 ramp fires** and rank-1 is cut from E2, the three `L6` cells leave the denominator, which becomes **7/7**, disclosed in the ledger — a denominator reduction, not a not-adjudicated route. **Instrument that reads the outcome:** `bench/phase0-agent-native/effect-rows-probe-ledger.json` (per cell: mutation id, class, fixture blob SHA, expected code, observed code, detected, drift; plus leg B's point estimate, bound, realized CV, and the four-valued verdict), re-executed by **exact equality** in `EffectRowsProbeLedgerTests.PpE1LedgerMatchesRecomputation` (`tests/Calor.Compiler.Tests/Effects/EffectRowsProbeLedgerTests.cs`, `compiler` shard) — the `Calor0425CorpusLedgerTests` shape of design §13.4. Leg B's adjudicator is `bench/phase0-agent-native/ppe1-analyze.py` writing `epochs/e1-rows-parity-001/ppe1-analysis.json`. Both are **registered here, not built here**. PP-E1 is the instrument of roadmap §4.4 gate 4; its verdict — hit, miss, underpowered or not-adjudicated — is published in the 0.15.0 release notes whatever it is | **Freeze event: this row is it.** Registered 2026-08-25, before any effect-row implementation merges; verified at registration by `grep -rn "Calor0424" src/` returning empty at `f7cd1c46` (E2's `DoesNotFit` code does not exist in the tree), so roadmap M1's "no E2 merges before M1" holds at registration time. **Honest timing (the A-1.2 pattern):** the five fixtures were authored **before** this registration, in the emitter-spike PR #1096 (merged 2026-08-25, `f7cd1c46`), by the same workstream and the same author — they are not blind to the implementer, and two of their executions are pre-existing evidence rather than blind: `after/A3-middleware-broadening.calr` (`37b7e5af`) is the `L5-MID` mutant and was already rejected with Calor0421 under the spike's **throwaway** prototype, and the negative control's clean compiles are that spike's own G-CODEGEN result. What remains results-blind, and is the property this registration protects: the mutation catalogue, the 10-cell denominator, the 10/10 bar, the four-valued map, the decidability fallback and leg B's margin were fixed with **zero output of the shipping rows checker observed** — none exists to run them against. **Coverage, stated so leg A is not overread:** the frozen set has **zero** applicable cells for four of roadmap §4.4 gate 1's classes (assignment, monomorphic argument, return, virtual override) — no fixture contains such a site — so PP-E1 covers 2 of gate 1's 6 classes plus one class gate 1 does not enumerate (`L7`, E4's Calor0425 leg); the other four stay with gate 1's `_IsError`/`_Compiles` pairs and this row claims nothing about them. **Margin derivation (the PP-W5 method: 300 null simulations × 400-resample two-level cluster bootstrap, re-run at registration):** *method reproduction* — on PP-W5's own population (`m5-compare-001` N1 cells, 8 cells) with the naive field that row named, this re-run gives null point p95 **1.257** against the **1.247** registered and the #839 reviewer's independent **1.243**: a transcription, within Monte-Carlo noise. *Population for PP-E1* — `w5-parity-002`, the realized parity epoch: same four N1 pairs, 5 runs/arm, both arms Calor, the same two-toolchain shape leg B will run. On its 40 valid runs with **corrected** tokens (A-1.9.1): within-cell CV **median 0.439, max 0.804** (population-sd convention), against the 0.20 / 0.73 PP-W5 froze from `m5-compare-001` — A-1.9 recorded exactly this rise, so **PP-W5's 1.25 is not inherited; it is re-derived**. *Null simulation on that population* (pool each pair's runs across arms, redraw both cells at their realized sizes, point = median of paired per-pair mean ratios): median **1.002**, **p95 = 1.321**. The margin freezes at **1.35**, the next 0.05 step above that p95 — PP-W5's construction (its 1.25 sat at its 1.247 p95), one step conservative because this population is the noisier one. *Measured false-fail at the frozen rule*: point-only **4.0 %**, **conjunction (point > 1.35 AND bound > 1.0) 2.3 %**; at a 1.25 margin on this population the conjunction is also 2.3 % while point-only rises to 9.3 % — the bound leg is what calibrates, exactly as at A-1.4. *Power, so a pass cannot be overread*: measured detection **0.24 / 0.52 / 0.75** at true 1.25× / 1.4× / 1.6× regressions (same simulation, treatment draws scaled), below PP-W5's frozen 0.33 / 0.62 / 0.87 because the realized variance is roughly double the population that produced those. **A leg-B pass means "no large tax detected", never "proven equal"** — the UNDERPOWERED branch exists so a point-only exceedance is not adjudicated as a clean miss. **What the archived data does NOT support, stated rather than smoothed:** no epoch in the tree has arms differing by an effect-checking change, so leg B's variance is borrowed from a toolchain contrast (`v0.10.0` vs v0.11 main) and *assumed* to transfer; the four N1 fixtures carry zero contracts and no higher-order code, so they measure the loop's cost of a stricter compiler, not rows' cost on rows-using code; and 4 clusters × 5 runs is the same small design PP-W5 ran — the derivation cannot repair that, only disclose it. Derivation script and full output archived in the registering PR's description; the numbers above are reproducible from `bench/phase0-agent-native/epochs/{m5-compare-001,w5-parity-002}` with `token-usage.py` and seed 4537 |

Sub-integer disclosure: the iterations-to-green primary measure was moved to
tokens-to-green because the dry-run showed it floor-bound — the loop plan's
D4.5 rule ("the dry-run may move a threshold, the task count, or N before
freezing — never after") applied as written.

**v0.12 substrate proof points (A-1.5).** Registered 2026-08-04; full text, triggers, and
disclosures in the A-1.5 entry below.

| Proof point | Threshold (frozen) | Basis |
|---|---|---|
| **PP-S1** — converter fidelity is movable | M-S1 ≥ 0.70 on ≥2 of 3 original projects; adjudicable early at D-S1.1a | Diagnostic input to Call S, **not decisive** — plan §6.2's table resolves the venue on supply, not fidelity |
| **PP-S2** — checker breadth converts existing defects into addressable ones | **Reported, not adjudicated** for v0.12 | Its metric M-S2 was measured before the freeze, so no bar may be frozen against it here (PP-L4 precedent) |
| **PP-S3** — **the benchmark can be supplied** (headline) | M-S3 ≥ 70 total; ≤3 per file; no project >40% | Four-valued (hit / miss / underpowered / not-adjudicated); non-hit triggers enumerated exhaustively, **any unlisted route is a MISS** |
| **PP-S4** — no converter-appeasement (**blocker**) | M-S4 = 0 via the A-1.5.7 fixture registry; indeterminate counts as **failing** | Blocks the v0.12.0 release |

### A.3 Annex revision log

<!-- annex-freeze (A-1.10): each entry starting with **A-1.N is byte-frozen against main by scripts/check-annex-freeze.py, keyed by its first line; newest first. Add a new entry for every annex change and update the Annex version pointer; never edit or remove an existing entry. -->

**A-1.11 — the 0.15 effect-rows proof point registers (2026-08-25); second half of the 0.15
amendment, and M1 is now complete.** Additive: adds the **PP-E1** row to §A.2 and alters no
frozen row or entry. **(i) This entry is the freeze event.** A-1.10 said so in advance ("this
guard-half is not the freeze event, and says so"), and roadmap §4.3 (i) names the
PP-registration entry as the freeze. Per roadmap M1, **no effect-row implementation (E2) merges
before it**: verified at registration by `grep -rn "Calor0424" src/` returning **empty** at
`f7cd1c46` — E2's `DoesNotFit` code does not exist in the tree, and the emitter spike's
prototype (#1096) is explicitly throwaway and did not merge. The other two M1 items were already
done: PR #944 dispositioned (#1090) and #881 corrected (#1092, entry A-1.9.1), so the cost leg
below reads the corrected `tokens.output`. With this entry M1 is **complete** and E2 may merge
citing it. **(ii) Fixture and defect classes are frozen in the row**, by path and blob SHA (the
five spike artifacts — A2 plus the four A3 combinators) and as ten named injectable mutations
across three classes (`L5` interface implementation, `L6` rank-1 instantiation, `L7` row
erasure/invoke-a-row-less-value), one per applicable (class, fixture) cell, 10 of 15 grid cells
applicable and the applicability frozen with the list. **Honest timing, plainly (the A-1.2
pattern): the fixtures were authored before this registration**, in PR #1096 on 2026-08-25, by
the same workstream and author; two of their executions are pre-existing evidence rather than
blind (the `L5-MID` mutant already exists as `after/A3-middleware-broadening.calr` and was
rejected with Calor0421 under the throwaway prototype; the negative control's clean compiles are
that spike's G-CODEGEN result). Both are disclosed in the row rather than left to surface at
adjudication. What is results-blind is the catalogue, the denominator, the bar, the outcome map,
the fallback and the margin — no shipping rows checker exists to have produced a number.
**Coverage is stated, not implied:** four of §4.4 gate 1's six laundering classes (assignment,
monomorphic argument, return, virtual override) have **zero** applicable cells in this fixture
set and stay with gate 1's `_IsError`/`_Compiles` pairs. **(iii) The outcome is four-valued** —
hit / miss / underpowered / not-adjudicated, the `tier1a-postmortem.md` §6.3 precedent where
"inconclusive" is named in advance to prevent post-hoc narrative fitting. The HIT rule is a
falsifiable threshold on a named instrument: **10/10 registered mutations detected at their
registered declarations, with a clean negative control**, and a cost leg that does not fail. The
decidability fallback is exhaustive and any route not listed is a MISS: a fixture that fails to
compile on the release compiler, the §4.1 demand ledger below its frozen floor of 25 (3 + 3121 =
3124 at registration), an invalid harness (the PP-W5 validity floor, missing per-arm product
provenance, or §2's censoring cap), or E2/E3 not shipping at all. **(iv) The margin is a number
with a derivation, not an adjective.** The PP-W5 method (300 null simulations × 400-resample
two-level cluster bootstrap) was re-run at registration. Reproduction check: on PP-W5's own
`m5-compare-001` population with the naive token field that row named, it returns null point p95
**1.257** against the registered **1.247** and the #839 reviewer's **1.243** — a transcription,
within Monte-Carlo noise. Derivation population for PP-E1 is `w5-parity-002`, the realized
parity epoch of the same four N1 pairs, with **corrected** tokens per A-1.9.1: within-cell CV
median **0.439**, max **0.804**, against the 0.20 / 0.73 PP-W5 froze — the rise A-1.9 recorded,
which is why **1.25 is re-derived rather than inherited**. Its null-simulation point p95 is
**1.321**, so the margin freezes one 0.05 step above it at **1.35**, conjoined with PP-W5's
bootstrap-lower-bound > 1.0 leg; measured null false-fail **2.3 %** for the conjunction versus
**4.0 %** for the point test alone. Measured power **0.24 / 0.52 / 0.75** at true 1.25× / 1.4× /
1.6× — below PP-W5's frozen 0.33 / 0.62 / 0.87 — so a pass means "no large tax detected", never
"proven equal", and a point-only exceedance reads UNDERPOWERED, not MISS. Three limits the
archived data cannot repair are recorded in the row: no archived epoch contrasts an
effect-checking change, so the variance is borrowed from a toolchain contrast and assumed to
transfer; the N1 fixtures carry no contracts and no higher-order code; and 4 clusters × 5 runs is
the same small design PP-W5 ran. **Instrument and venue:** the outcome is read from
`bench/phase0-agent-native/effect-rows-probe-ledger.json` by an exact-equality test,
`EffectRowsProbeLedgerTests.PpE1LedgerMatchesRecomputation`
(`tests/Calor.Compiler.Tests/Effects/EffectRowsProbeLedgerTests.cs`, `compiler` shard), with the
cost leg's `ppe1-analyze.py` / `epochs/e1-rows-parity-001/ppe1-analysis.json` alongside — all
registered here, **none built here**, and the epoch runner is deliberately not written by this
PR. **Adjudication happens at the 0.15.0 release commit** and is published in the release notes
whatever it says. **Id convention:** `PP-E1` opens the **E** (effects) family — no PP-E id
existed; `PP-A/G/L/S/T/W` are taken. It is deliberately *not* named after a workstream: roadmap
§4.2's `E1`–`E9` are 0.15 workstream ids and §A.1's `M-E1`–`M-E3` are envelope metrics; neither
shares the `PP-` namespace, and this row is the effects family's first proof point, not the
proof point of workstream E1. **Guard:** `scripts/check-annex-freeze.py --base-ref origin/main`
passes on this change with +1 row, +1 entry and the pointer moved to A-1.11; no `src/` or
`tests/` file changes in the registering PR.

**A-1.10 — mechanical freeze guard (2026-08-25); first half of the 0.15 amendment.** Additive;
registers no proof point and alters no frozen row. Roadmap `roadmap-v0.13-v0.15.md` §4.3 (i)
records that this annex had no tamper guard — `experiment-registry-tamper-check.yml` covered only
`docs/experiments/registry.json` — so every "frozen" claim above rested on discipline alone. From
this revision that workflow runs `scripts/check-annex-freeze.py` on every PR to `main` (and on
every push to `main`, `HEAD^` vs `HEAD`) and fails unless: **(1)** every §A.2 table row whose
first cell names an ASCII PP id (`PP-L1` … `PP-S4`, thirteen today) is byte-identical to `main`,
and no §A.2 row's first cell merely LOOKS like a PP id (non-ASCII letters or digits — a Cyrillic
`Р`, a fullwidth `５` — or a case/spacing variant), so a frozen row cannot be shadowed; **(2)** each
§A.2 table's header and `|---|` separator lines are byte-identical to `main` (tables may be
appended); **(3)** every §A.3 entry — split at each line beginning `**A-1.`, ending at the next
entry, the next `##`/`###` heading, or end of file, keyed by its first line; fourteen on `main`
before this one — is byte-identical to `main` (trailing blank lines aside); a new TOP-LEVEL
counter may not sit below the newest already on `main` (history cannot be back-filled), while a
sibling of an existing counter (the A-1.4 tranche 2 pattern) or a sub-version whose parent
exists (the A-1.3.1 pattern) is allowed; **(4)** any byte change in the annex region (`## Annex A`
to end of file) is accompanied by a new §A.3 entry; **(5)** the Annex version pointer at the head
of Annex A names the highest top-level `A-1.N` in §A.3; **(6)** the parse fails closed — an
empty or unparseable base or head (missing `## Annex A` / `### A.2` / `### A.3` heading, missing
or duplicated pointer, duplicate PP key) rejects the PR. Rows and entries may be ADDED, never
edited or removed; a correction to an adjudicated entry is a new entry that says so (the A-1.9
withdrawal pattern), never an in-place edit. **The guard is not judged by itself:** CI runs the
copy of the script already on `main`, not the PR's, and refuses a PR that changes this file
together with the script or the workflow — a guard change lands first, alone. **Consequence for
authors:** an entry's first line is its identity — write it once; wrapped continuation lines
must not begin with `**A-1.`. **Coverage, stated so "13 frozen rows" is not read as "every
registered proof point":** PP-A1, PP-A2, PP-G1, PP-G2, PP-L6 and PP-W2 have no §A.2 row — they
are registered and adjudicated in §A.3 entry bodies (or in the plans those entries cite) and are
frozen only through rule (3); the §A.1 metric prose and the pointer paragraph are held only by
rule (4). **Not guarded, stated so the instrument is not overread:** the main document (§0–§8;
its §7 supersession rule remains a written-analysis discipline), §A.1 prose and §A.2 prose
outside table rows (editable, but only with a new entry), and the *honesty* of what a new row or
entry says — that stays with review. The guard's own discriminating pins run first on every
invocation (`--self-test`: 28 synthetic pins — mutated / removed / shadowed rows, a renamed
column, edited / removed / back-filled entries, a new row with no log entry, pointer/entry
mismatches, an empty base — plus a mandatory shape pin on this document: 13 rows, 2 table
headers, pointer = newest entry). The two `<!-- annex-freeze -->` comment lines under the §A.2
and §A.3 headings are this revision's only insertions outside this entry, plus the pointer
update every prior revision also made; diff vs `main` is insert-only except that pointer.
**Second half pending:** the 0.15 proof point — the fixture-scale probe under a NEW PP id, with
roadmap §4.3 (i)–(iv) in full (freeze event named, fixture and defect classes frozen with
honest-timing disclosure, the four-valued outcome with a decidability fallback, the PP-W5-derived
"no large loop tax" margin) — registers in a later entry (A-1.11 or later) once
`docs/design/effect-rows-in-the-type-system.md` (roadmap §4.1) is merged; per roadmap M1 no
effect-row implementation (E2) merges before it does. The roadmap's earlier "the A-1.10 bump is
the freeze event" wording is amended in the same PR to name that later entry; this guard-half
is not the freeze event, and says so.

**A-1.9.1 — #881 correction landed (2026-08-25).** Additive amendment under the §7 supersession
rule to the A-1.9 metric erratum; no threshold changes, no frozen row touched. Both runners now
take the cost-leg token figure from the shared `bench/phase0-agent-native/token-usage.py`, which
sums `modelUsage[*].outputTokens` over the whole run (main conversation, subagent turns,
compaction segments) instead of reading the envelope's final-turn `usage.output_tokens`. The
side-model topic-detector call is excluded only when it is small (key matches `haiku` AND
`outputTokens` < 100); a large `haiku` entry is a subagent and counts. `costUSD` is untouched
and still includes the excluded call. `result.json` `tokens.output` is the corrected figure; a
`tokenUsage` block records `output_tokens_naive` (the old `usage.output_tokens`),
`output_tokens_corrected`, `models_counted`, `models_excluded`, cache-token counts,
`origin_kind`, `undercount_ratio` and `undercount_flagged`, and the runner prints a
collection-time warning whenever naive and corrected disagree by more than 5 % — the guard that
would have caught all four A-1.9 cases. If the helper fails, the runner falls back to the naive
figure and marks `tokenUsage.source` `fallback-naive` — never a silent zero. The archived
`w5-parity-001`/`-002` `result.json` files are NOT rewritten: they carry the naive figure the
frozen row named, and A-1.9's corrected ratios were recomputed from the archived `agent.json`
envelopes. Any row registered after this date that names a per-run output-token metric means
`tokens.output` as derived by `token-usage.py`. Pinned reproduction:
`bench/phase0-agent-native/tests/test_token_usage.py` (synthetic fixtures shaped like the 55×
run plus the archived run itself).

**A-1.9 — PP-W5 adjudicated. PASS on `w5-parity-002` (2026-08-06); the first attempt is
recorded VOID.** Additive; registers the outcome against the row frozen at A-1.4 tranche 1,
with the A-1.5.6 additive note applied.

**`w5-parity-001` (2026-08-05) is VOID and its PASS is WITHDRAWN.** `--calor-dll` pins only the
CLI, which the agent never invokes; the compiler that builds the agent's code comes from the arm
template's `__REPO_ROOT__`, and `run-m5-epoch.sh` never passed `--arm-repo-root`. Both arms
compiled through the same Calor.Tasks. Cost $38.75 and measured nothing. The CLI-hash guard
certified a contrast that did not exist, and the per-run `calorDll` stamp — reported at the time
as provenance — was the flag echoed back.

**`w5-parity-002`:** control `v0.10.0` (`e24a6832`, Calor.Tasks `715cb5ad…`) vs treatment `main`
(`87a783dd`, `9870805b…`), contrast **verified before the run**; 4 N1 pairs, 5 runs/arm, `raw`
both arms, **interleaved**; 40/40 valid, 0 censored, 20/arm; $54.08. **Median paired ratio: point
1.0984, lower bound 0.6531 — neither gate fires, PASS.** Iterations-to-green, which the row
requires and the void record omitted: control `{1:17, 2:2, 3:1}`, treatment `{1:18, 2:1, 4:1}`.

**Metric erratum, filed as a §7 correction rather than a clarification.** The row pins output
tokens to `agent.json` `usage.output_tokens`. That field **silently under-counts** on runs that
delegate to a subagent or resume after compaction: four runs across the two epochs are affected,
the worst by **55×** (543 recorded vs 30,084 actual, `num_turns` 1). Corrected, the per-pair
ratios are 0.953 / 0.938 / 1.622 / 1.051 and the point estimate is **1.0016** — the verdict is
PASS on either basis, and robust across 200 bootstrap seeds. Future epochs should pin
`modelUsage[*].outputTokens` or disallow subagent spawning.

**Two claims from an earlier revision of this entry are WITHDRAWN**, both artifacts of that
defect. (a) *"One task cost the treatment half the tokens"* — the 0.470 does not exist; that run
was the most expensive in its cell, not the least. (b) *"A genuine contrast multiplied the
between-pair spread roughly sevenfold"* — corrected identically, the two epochs' spreads are
0.538 and 0.684, a ratio of **1.27×**; and an arm-label permutation test (20,000 draws) places
the observed spread at the **50th percentile** of the distribution under *no arm effect at all*.
The earlier entry treated `w5-parity-001` as a clean-null baseline; it was a 7th-percentile draw.
**No heterogeneity is claimed and none is supported.** N1-003's 1.622 is real and not
outlier-driven; it is one pair.

**So the PASS is narrow but unremarkable:** corrected point 1.0016, as close to parity as this
design resolves, with a bound that still admits a wide interval — no large tax detected, not
proven equal. Within-cell CV rose from 0.20 (the frozen derivation's population at
`m5-compare-001`) to 0.42 median, so realised power is **below** the frozen 0.33/0.62/0.87.
No mechanism is attributed: all four N1 fixtures contain **zero contracts**, so the D3/D12/D14
elision withdrawal cannot operate, and the `EnableTypeChecking` flip has zero supporting
instances in the archived journals.

**The contrast is verified by execution, not by flag.** `--arm-repo-root` is `sed`-substituted
into `CalorTasksAssembly`, and `obj/calor/.calor-build-state.json` — written by `Calor.Tasks`
itself — records `compilerHash` `65ee7cf0…` (control) vs `40472f37…` (treatment).

**Five apparatus defects preceded the number**, four of them silent: arms overwriting each other;
the M5 swap guard rejecting a valid parity config; the `raw` arm never invoking the agent on bash
3.2; the product never bound per arm (the void); and interleaving re-invoking `run-pair` such
that run 2 overwrote run 1. Four were caught pre-spend or at $0.00. Guards now hard-fail a parity
epoch lacking per-arm roots, and the adjudicator refuses unless the pins record two distinct roots
with distinct `Calor.Tasks` hashes and every run's provenance matches one.
Record: `bench/phase0-agent-native/epochs/w5-parity-002/VERDICT.md`.

**PP-W5 was the last unadjudicated v0.12.0 release gate. Both now stand: PP-A1 = PASS (A-1.8),
PP-W5 = PASS (A-1.9) — the latter narrowly, as above.**

**A-1.8 — PP-A1 adjudicated (2026-08-05). PP-A1 = PASS, all nine items.** Additive; registers outcomes against the
PP-A1 list frozen at `wedge-w1-prereqs.md` §3 and carried unchanged into v0.12. **All nine PASS —
item 6 only as of #876, items 4 and 7 only after fixes made during the audit, item 9 only as of
#877 and later than its registered W2/W3 window.**

**Item 6 passed only after four review rounds found four live vectors.** Three adversarial review rounds found three false-
`Proven`-elides vectors behind this one item, each time after it had been marked ✅: D4's
explicit-mode half, D4's no-mode half (both closed in #872), **D3** and **D12** (closed in #876), and **D14** (closed in #878). D3
reproduced on the shipped `calor run --verify` path, as did **D12** (Z3 counts UTF-8 bytes, .NET
counts UTF-16 code units: `"é".Length` is 1 vs 2). Two prior claims are withdrawn: that Calor's
`str` is non-nullable (`§B{bad:str}` binds a null interop return with **zero diagnostics**), and
that D3 was "argued unreachable". **Unlike D4 and D9 neither is closable by refusing an operation** —
Z3's string sort has no null and is byte-counted, so every total axiom of its string theory is
affected. **#876 closes both by DEMOTION** (option B, maintainer-chosen): a proof carried by the
string theory becomes `Assumed`, which never elides, with the assumption named — on both elision
channels, postconditions and refinement obligations. That closes the class **by construction**
rather than by enumeration, which matters given that enumeration is what failed four times. The
modeling gaps remain open; **#875** tracks the root cause of D3.

**Registered lesson, because this bar has now failed SIX times by inspection, twice inside its own fixes:** *known*-divergence-
free is a claim about how hard anyone looked. §2 of the freeze already says it is **weaker than
differentially clean**. Item 6's bar should not be re-asserted by another reading of the divergence
table; it should be replaced by #779's solver-vs-runtime differential suite.

**Item 6 was FAIL when first adjudicated, and this is registered rather than smoothed over.** Its bar
is *no known false-`Proven`-elides vector*, known set = the divergence table + T1. **Divergence D4 was
a live one**: a non-ordinal `StringComparison` mode in a contract was translated as **ordinal**, the
mode-bearing form stayed whitelisted in `ModeledForms`, and `Z3Verifier` never demoted on the warning
— so the solver returned a genuine false `Proven`, and `Proven && !IsVacuous` (D-G1.3) **elided the
runtime check**. Reproduced end-to-end on a shipped CLI path: the same program threw
`ContractViolationException` under `calor run` and printed its value under `calor run --verify`.
Closed by refusal in **#872** (mirroring D9), cache format 1.8 → **1.9** — the bump is load-bearing,
not hygiene, since warm 1.8 caches hold exactly those false verdicts. Item 6 passes **only as of
#872**. The first adjudication cited four closed rows (D6–D9) and did not audit the remaining seven;
the corrected record carries a **row-by-row** audit of all fourteen. An intermediate revision gave
D3 an *argued-unreachable* disposition; that argument was refuted by execution and is withdrawn and D2 named as the residual inside §2's recorded risk
acceptance.

**Item 9 = DELIVERED at v0.12 (#877), later than its registered window, and the lapse stays on the
record.** The frozen text says the flip *"lands in the W2/W3 window"*; that window closed without it,
and the first adjudication marked the item ✅ anyway by re-reading "lands in the W2/W3 window" as "has
a slot" — a reinterpretation in the favourable direction. The correct disposition at that moment was
LAPSED. It is ✅ now because the work was done, not because the reading changed.

**What blocked it was never scheduling.** Flipping the default produced **92 failures**, every one a
*working program the checker refused*: unknown type `char` ×37, `object` ×13, `Type` ×6, arrays ×6,
cascades from unmodeled calls, static members read as undefined variables, string `+` reported as
non-numeric, and a duplicated `Calor0250`. All of it was **already live** — `calor_check` and
`calor_refine` set the flag — so the MCP primer's own module, two shipped benchmarks and the syntax
exemplar were being rejected. **Three adversarial review rounds** on the fix found that the flip then
introduced *new* false positives on two agent-native benchmark **gold references** (one dropping from
53 proven contracts to zero with a non-zero exit), that **no test project compiled anything under
`bench/`** so the fix's own measurement had excluded that corpus, and that the round-2 guard was dead
code. Gates over the bench gold references and over `samples/` now exist; neither did before.

**Items 4 and 7 passed only after fixes made during the audit**, both on the shipped surface rather
than the one the original evidence cited: `CompileCalor` (the MSBuild task real projects build
through) trusted a `.g.cs`'s *existence* rather than its content hash, diverging from
`CompilationDriver`; and `format --write`'s policy refusal ignored `--format json`, so an agent that
asked for an envelope got a parse failure. The `Calor1346` containment also had **no test anywhere in
the repo** — item 7 had been adjudicated on code-reading alone — and is now pinned, with a control
proving the gate is an acknowledgement rather than a broken command.

**Item 1's "published" half is dispositioned, not asserted:** CI evidences the *packaged* SDK's
consumability from a local feed; **"published" clears at the v0.12.0 publish event itself**.

**PP-W5 remains NOT adjudicated** — it requires a spend-authorized parity epoch (A-1.4 tranche 1,
restated additively at A-1.5.6) and **may not be self-cleared**. Record: `docs/plans/pp-a1-adjudication.md`.

**A correction the record carries rather than hides:** #872's first fix closed only the
*explicit-non-ordinal-mode* half of D4. A second adversarial review found the vector still live on
the **no-mode** spelling — .NET resolves `StartsWith(String)`/`EndsWith(String)`/`IndexOf(String)` to
**CurrentCulture** while the solver models them ordinally — and it reproduced with the same
throw-under-`run` / print-under-`--verify` signature. Also closed in #872 (cache 1.9 → **1.10**). The
lesson registered: the first fix was scoped to *the divergence row as written* rather than to *the
property the row protects*, and only the second is item 6's bar. **Two rounds of review found two
live vectors behind one ✅**, so the honest reading of "known-divergence-free" is a statement about
how hard anyone looked — which is what §2's own risk acceptance already says.

**A-1.7 — Call S adjudicated (2026-08-05).** Additive; registers outcomes against thresholds frozen at
A-1.5 before any eligibility was evaluated. **PP-S3 = MISS** (headline): M-S3 realized **8** screened
tasks against **≥ 70**, and FluentValidation is **62.5%** of them against a **≤ 40%** per-project cap —
two of three sub-criteria fail. The softer values do not apply and were checked in order:
`underpowered` needs supply ≥ 70 (it is 8); the measurement completed and **reproduced identically
across two runs**; three projects produced evaluable candidates; and **8/8 passed the two-arm
determinism screen**. A-1.5.2's registered default therefore governs — any unlisted route is a miss.
**PP-S1 = MISS** (A-1.6(b), census `s1-census-001`: top-3 = 40.4% < 50%, 33 causes, 0 unattributed).
**PP-S2** contributes no verdict (registered reported-not-adjudicated at A-1.5.1; measured 65% this
cycle). **PP-S4 = PASS**, blocker cleared: M-S4 = 0, with both supply-raising changes adjudicated
rather than assumed — the operator widening generalized the *corruption* and touched **0 `src/`
files**, and the unbounded-cap fix is harness-side; no `FeatureSupport` promotion or ledger removal
landed, so the D-S1.5 registry is legitimately empty.
**Call S = PROCEED to v0.13 with the real-scale venue RETIRED** — plan §6.2's (PP-S1 miss, PP-S3 miss)
cell. No real-scale epoch is authorized; the differentiated claim re-scopes to
earliness/attribution/cost, which `guarantees-probe-001` (PP-G3 HIT) already evidences and which this
call leaves untouched. Record: `docs/plans/call-s-adjudication.md`.
**Disclosures added after adversarial review, because a gate record retiring a three-release venue is
the last place to relax the standard:** (a) `substrate-plan-v0.12.md` §5 still carried an earlier
wording of not-adjudicated trigger (ii) — *"fewer than 2 projects pass the frozen fidelity bar"* —
under which the trigger **would** have fired (0 of 3 reach 0.70). The annex governs, and §6.2's
combined *"miss / not adjudicated"* column makes the call **invariant** either way; the plan is
amended in the same change. (b) **PP-S4 passes VACUOUSLY** — M-S4's population is empty (no v0.12
commit touched `src/`; `FeatureSupport.cs` untouched since `4d7f5887`), and its registered instrument
was **never built**: `fixtures/d-s1.5/` and the `d-s1.5-fixtures` CI job do not exist, so the blocker
is cleared because nothing required gating, not because the gate ran. Carried to v0.13 as debt. (c) A
**third** supply-raising change is adjudicated — the arm-signature canonicalization (5 → 8 via
`ArmsDiverge` 3 → 0, i.e. M-S5's own metric); not an M-S4 event because the exclusions were an
instrument artifact (both arms failed identical test sets, compared positionally across differently
ordered runs), and M-S5's baseline was consequently set by it at the strictest possible value. (d)
PP-G3, cited as the surviving evidence, is **authorable-fixture scale**, and this program's own §0.2
records that ceiling does not survive real code. (e) PP-S1 also misses on **M-S1's own bar** (0 of 3
vs ≥ 2 of 3) — a second independent route to the same verdict.

**Workstream shape:** WS-S1 continues on its narrowed `calor import` justification only; **WS-S2 and
WS-S3 close unstarted**; **WS-S4 closes unresolved**, leaving the proof-depth headline with no live
measurement channel. Quantitative re-entry conditions for the venue are registered in the Call S
record.

**This entry alters no frozen row and makes no §7 supersession claim** — it records outcomes against
A-1.5's thresholds, which are unchanged. Insertions only, plus the Annex version pointer.


**A-1.6 — PP-W2 restated (instrument scope) + PP-S1 disposition (2026-08-05).** Additive. Two
maintainer decisions, recorded together because the second is conditioned on the first.

**(a) PP-W2 RESTATED, under the D-G3.1 restate-or-demote precedent.** The real-scale bundle epoch's
"Calor arm" contains no Calor: both arms ship plain `.cs` (the calor arm is round-tripped C#), the
runner never invokes the Calor compiler, and the dotnet shim is a pass-through. `Calor0410` therefore
**cannot fire in the agent loop**; it is established out-of-band by the addressability probe at
generation time. Verified independently — 0 `.calr` across all 8 bundles, no differing `.csproj`, no
injection path. Evidence and options: `docs/plans/substrate-arm-validity-finding.md`.

Consequently **an epoch over these arms cannot adjudicate the verification-depth thesis**, and PP-W2
is restated to what the instrument measures:

> **PP-W2 (restated):** over the real-scale bundle arms, the measurable quantities are the
> **conversion penalty** (idiomatic C# vs machine-converted C#, both carrying the same defect) and the
> **arm-symmetric ceiling-recurrence leg**. The `Calor0410` differential is retained as a
> **compiler-level** result — evidence about what Calor's checkers catch, established out-of-band —
> and is **not** evidence about agent behaviour. The verification-depth claim is **not testable by
> this instrument as built**.

This **narrows** PP-W2's reach; it does not revive it. PP-W2 remains **NOT ADJUDICATED** (A-1.4
tranche 2), and nothing here re-opens it. Three records described an arm the implementation does not
carry and are corrected in the same change: the **registered** D-W5.2(a) arm definition
(`wedge-plan-v0.11.md`), the `w4-dryrun-001` VERDICT arm description (load-bearing for M-S3's power
derivation), and the generated bundle README (`TaskGenReportWriter.cs`), which asserted the agent is
"confronted by the diagnostic" — now corrected at the generator and pinned by tests.

**Option 1 (rebuild the arm as real Calor) was NOT taken.** Recorded so a later reader does not infer
it was impossible: `Calor0410` is an *enforcement* diagnostic requiring no contracts, so that leg does
**not** depend on the authored-contract overlay (WS-S4) and is cheaper than it. It remains available
to a successor plan.

**(b) PP-S1 disposition: CONTINUE WS-S1.** D-S1.1a's early-abort trigger fires as written — the loss
ledger's entire achievable recovery is **7 files against a need of 65** (`epochs/s1-ledger-001`). It is
**not** honoured, and the reason is registered: the ledger covers **4.7%** of the non-native gap;
**95.3%** of it is files that never converted or compiled (`Reverted` 87, `CompileError` 37,
`EmitSyntaxError` 17). PP-S1 claims *fidelity is movable*; what the ledger shows is that **the ledger**
cannot move it — a fact about the work-list's source, not about fidelity. Resolving PP-S1 = miss on it
would repeat the error the §4 go/no-go nearly made on a pre-widening ceiling of 9 that was an artifact
of our own mutation operator.

**PP-S1 remains open, not adjudicated**, and WS-S1 proceeds against the **failure statuses** rather
than the ledger. Its justification is narrowed in step with (a): `NativeFraction` is the quality metric
of the **shipped** `calor import` path and stands on its own; the "supply the epoch so PP-W2 can be
measured" framing does **not**. The census of the 141 failures is the next gate, and its
pre-committed rule is stated **exhaustively**: **top-3 causes ≥ 50% → work-list, continue; otherwise →
PP-S1 = miss** — which covers the top-3 < 50% ∧ top-10 ≥ 50% case an earlier wording left unrouted.
Any route not listed is a **miss**, the same default PP-S3 carries.

**This entry alters no frozen row and makes no §7 supersession claim.** PP-W2's *registered outcome*
(not adjudicated) is unchanged; only the description of what the instrument can measure is narrowed.


**A-1.5 — v0.12 substrate registration (2026-08-04).** Additive. Registers the v0.12 metrics,
proof points, and pinned measurement configuration, per substrate plan D-S5.2. **Frozen at the end
of S1, before any eligibility VERDICT is evaluated** — see the scope limit immediately below, which
is narrower than an earlier draft of this entry claimed.

**Scope of the ordering claim, stated precisely.** The S1 pre-pass tool evaluates no eligibility:
`SupplyEnumeration` references neither `EligibilityPredicate` nor `VerificationAddressability`. But
the s1-supply-002 **record** cites a differential-addressability measurement over 35 native
candidates (26/35 = 74.3%), and addressability is the **terminal clause of the eligibility
predicate** (`EligibilityPredicate.cs:142`). So an eligibility-*adjacent* quantity WAS known before
this freeze. That does not touch M-S1 (fidelity) or M-S3 (derived from power, not supply), but it
directly touches **M-S2**, which is defined as that quantity — see the M-S2 row.

**Inputs used at freeze time:** per-project `NativeFraction` (0.469 / 0.400 / 0.532); the `3/3/0`
baseline (A-1.4 tranche 2); the `w4-dryrun-001` variance; the S1 enumeration-only pre-pass
(`epochs/s1-supply-001`, `epochs/s1-supply-002`); **and the 35-candidate addressability probe
reported in the s1-supply-002 record** — listed here because omitting it while asserting no
eligibility-adjacent number was known would be the misrepresentation, not the omission.

**Adjudication date:** at D-S5.1's completion, whenever that falls. Registered because plan §6.1's
carve-out ("a re-box may move schedule; it never moves the adjudication date or the bar") is
inoperative without it.

#### A-1.5.1 Metrics

| ID | Definition | Frozen bar |
|---|---|---|
| **M-S1** | Per-project `ConversionCoverage.NativeFraction` at the pinned `ExcludePatterns` | **≥ 0.70 on ≥2 of 3 original projects** |
| **M-S2** | Candidates verification-addressable via the #856 differential, from the frozen operator set | **REPORTED, NOT ADJUDICATED** — see below |
| **M-S3** | **Expressible-stratum** tasks passing the full D-W4.1 predicate **and** the D-S5.1 determinism screen, at the pinned config | **≥ 70 total; ≤ 3 per file; no project &gt; 40% of the total** |
| **M-S4** | Converter/harness changes that raise M-S3 while making converted code less faithful — incl. `FeatureSupport` promotions and ledger removals without a green registered fixture, `§E`-inference behaviour, and mutation-operator changes | **0**, CI-enforced via A-1.5.7 |
| **M-S5** | Per-project `ArmsDiverge` count ÷ candidates reaching clause (b), on the mutation corpus | **Non-increasing vs the baseline**, tolerance +5pp; baselined at the first post-freeze funnel run and published **before any WS-S1 emitter merge** |

**M-S2 is registered reported-not-adjudicated, and why.** An earlier draft froze it at ≥ 60% — a bar
set 27 minutes after the quantity it measures was published at 74%, with no derivation, from a source
the plan did not name (§4 says M-S2 freezes "from the WS-S0.5 funnel table", which does not exist
because the freeze precedes the probe by design). Freezing a threshold below a value already
observed is precisely what gates §6 (":81") forbids. Rather than launder that, M-S2 takes the
**PP-L4 precedent**: measured and reported every run, **not** used to adjudicate any proof point.
PP-S2 is correspondingly demoted to a reported diagnostic for v0.12; a bar for it may be frozen in a
later tranche from data gathered after this freeze.

**M-S3's derivation, at the bound the gates require.** The dry-run's C# arm escaped on 2 of 6 tasks.
Gates §6 requires the power calculation to use the **confidence bound, not the point estimate**. The
Wilson 95% interval on 2/6 is **[0.097, 0.700]**; the adverse bound is 0.097. Reaching the ≥ 5
discordant pairs a one-sided sign test needs (0.5⁵ = 0.031) with ~80% probability at p = 0.097
requires **n ≈ 70**. (At the *point* estimate 0.33 the same calculation gives n ≈ 20 — an earlier
draft used it and registered 40, which was both the wrong estimator and an unstated margin.)

**Three limits registered with it, none of which N repairs:**
1. This is a **sign-test floor, not a §6.1 cluster-bootstrap power calculation.** Cluster-bootstrap
   power at N = 70 is not computed here; it will be lower.
2. The 0.33/0.097 discordance comes from six **logic**-stratum tasks. M-S3 counts **expressible**
   tasks, 78% of which use a corruption class (`DefaultWhenTainted`) whose agent-fix difficulty is,
   per the s1-supply-002 record, "an open question this widening silently changes the answer to".
   The constant is transported across that boundary knowingly.
3. "Deterministic catch" assumes Calor's build block converts to a catch. **It does not follow:**
   `Calor0410` is cleared by adding `fs` to `§E`, which leaves the defect shipping with a green
   build. A-1.2's frozen Basis already records that determinism covers the *surfacing* channel, not
   agent fix-behaviour.

**And the disclosure that must travel with N:** the **registered** 20–40% effect needs "on the order
of hundreds of clustered tasks" (`w4-dryrun-001` finding 4). N = 70 does **not** power it. If the
realized effect is the registered one, **PP-S3 resolves `underpowered`, not `hit`**.

#### A-1.5.2 Proof points

| ID | Claim | Outcome |
|---|---|---|
| **PP-S1** | Converter fidelity is movable | Hit / miss; adjudicable early at D-S1.1a. Diagnostic input to Call S, **not decisive** |
| **PP-S2** | Checker breadth converts existing defects into addressable ones | **Reported, not adjudicated** for v0.12 (see M-S2) |
| **PP-S3** | **The benchmark can be supplied** (headline) | **hit / miss / underpowered / not-adjudicated** |
| **PP-S4** | No converter-appeasement (**blocker**) | Pass / fail; indeterminate counts as **failing** |

**PP-S3's non-hit triggers, enumerated exhaustively. Any route not on this list is adjudicated
MISS**, with the novel route disclosed *and the miss registered anyway*:
- **underpowered** — supply ≥ 70 but below the N that **`w4-dryrun-001`'s already-frozen variance**
  requires for 80% power on the **registered 20–40% effect** under §6.1. Both inputs exist today, so
  this is decidable at Call S. (An earlier draft keyed it to "the variance dry-run" and "the realized
  effect" — neither of which exists at Call S, making the trigger circular and therefore an unbounded
  escape from *miss*.)
- **not-adjudicated**, only: (i) the supply measurement cannot complete, or candidate enumeration is
  not reproducible across two runs at the pinned configuration; (ii) **fewer than 2 projects produce
  any evaluable candidates**; (iii) eligible tasks exist but fail the determinism screen, so no valid
  epoch is constructible.
  *(Trigger (ii) is a measurability condition. An earlier draft keyed it to the fidelity bar, which
  would have made PP-S1-miss ⇒ PP-S3-not-adjudicated — contradicting plan §6.2's decision table,
  whose whole point is that **supply, not fidelity, decides the venue**, and converting the program's
  most likely bad outcome from miss into a soft landing.)*

#### A-1.5.3 Pinned measurement configuration

Any measurement at a different configuration is **reported, not adjudicated**.

- **Corpus:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`. Synthetic subjects
  excluded (authored to contain expressible candidates).
- **`TaskGenOptions`:** committed in full at
  `bench/phase0-agent-native/epochs/a15-freeze/task-gen-options.json`, SHA-256 (first 16 hex)
  **referenced from that file's sibling `SHA256SUMS`**. Adjudication values:
  `Strata = Expressible`, `MaxCandidatesPerProject = 0` (**unbounded**),
  `TargetEligiblePerProject = 0`, `RequireIdenticalSignature = true`, `BarIsProvisional = false`.
  **`MaxCandidatesPerProject = 10` is scoped to diagnostic probe passes ONLY.** An earlier draft
  pinned 10 for all runs, which capped evaluation at 3 × 10 = 30 candidates and made the M-S3 bar
  arithmetically unreachable at its own frozen configuration — the freeze failure this apparatus
  exists to prevent, arriving from the opposite direction.
  **Inert under `Strata = Expressible`, registered as inert:** `Sources` (the expressible branch reads
  `Strata` only), `RevertScanCommits` (revert is Logic-gated), and `NativeFractionBar` (the fidelity
  gate is report-only — `options.Fidelity` is read once after generation and
  `ExclusionReason.FidelityGateBelowBar` is never assigned).
- **`ExcludePatterns`:** three **per-project** hashes over the resolved, sorted pattern *values*,
  SHA-256 first-16-hex, recorded in the same `SHA256SUMS`. **Advisory until D-S1.4's CI assertion
  lands**, at which point it becomes enforced. Hashing the shared default list — as an earlier draft
  did — is invariant under a project *adding an override*, which is the exact gaming route
  (plan §1 item 1) the pin exists to close.
- **Frozen mutation-operator set** (PP-S2's anti-tautology guard, frozen **before** any checker
  extension is written): `EffectViolation`, `NullDeref`, `IndexOutOfBounds`, `DivByZero`;
  `NegateCondition` disabled. `EffectViolation`'s corruption forms frozen as `AddOne` (int/long),
  `FlipBool` (bool), `DefaultWhenTainted` (other corruptible returns), excluding
  `void`/`ref`/pointer/`async`. **Recorded weakness:** `IndexOutOfBounds` and `DivByZero` produced
  **zero** candidates in both runs, so for half the frozen set the guard "must catch something the
  frozen set already produced" is vacuous.

#### A-1.5.4 The plan §4 go/no-go: **DOES NOT FIRE**

Measured ceiling **519 sites / 203 native / ≈150 addressable** (203 is an **upper bound** — build and
`RecoverBuildAsync` are skipped, so files that convert but do not compile still count native).
Required-N is 70 (adverse-bound sign-test floor) to "hundreds" (registered effect). Against 500 the
ceiling is 2.5–3.3×, inside the 10× trigger. **PP-S3 does not resolve miss at A-1.5; the venue is
not retired.**

**The sensitivity this decision turns on, registered rather than omitted.** The comparison is against
a *candidate* ceiling, as plan §4 mandates — but attrition from candidates to eligible is
**unmeasured**, and the sole datapoint is **3 addressable → 0 eligible**. With ceiling 150 and bar 70,
M-S3 needs a clause-(a)/(b) pass rate above **~47%**. The kickoff's own working hypothesis is a ~25%
clause-(b) rate, and D-8 records that today's rate is a **biased-optimistic** estimator for the
population WS-S1 creates. **At 25% attrition the eligible ceiling is ~37, and against "hundreds" the
trigger would fire.** This decision is therefore contingent on attrition, and PP-S3's miss branch
remains live.

**Recorded because it is the substantive finding:** the pre-widening ceiling was **9**, and against
that number this trigger *would* have fired. The 9 was an artifact of the mutation operator's
corruption mechanism, not a property of the corpus. **Adjudicating one milestone earlier would have
retired the venue on an artifact.** The ceiling is still partly ours: 247 of 519 sites lost to
converter fidelity, and 284 further sites (+54%) never visited.

#### A-1.5.5 D-5 (region-granularity clause (a)): **REJECTED for v0.12**

The pre-committed rule (with-loss/native ≥ 0.5) returned **opposite verdicts on the identical corpus
under two operator sets** — pre-widening pooled 0.778 ACCEPT carried by `NullDeref`; post-widening
pooled **0.340** REJECT carried by `EffectViolation` (**0.315**). A statistic that inverts with the
operator set is not measuring the construct, so the question is decided explicitly: **REJECT.**
Site-level clause (a) is not built in v0.12.

**Three disclosures, because the route to this conclusion was not clean:**
1. Run 1 returned ACCEPT by the letter of the rule. The **SPLIT verdict was then added to the tool**
   (`SupplyEnumeration.D5Verdict`) *after* that result. That is a decision procedure amended after it
   returned a disliked answer, and it is disclosed rather than presented as having been there.
2. "Pooling was never the pre-committed part" is itself post-hoc: the kickoff wrote a single ratio
   with no per-operator split, and the tool's own doc comment called pooling deliberate.
3. The load-bearing reason — that the ACCEPT side is supply eligibility would delete — rests on
   `NullDeref`'s **doc comment**, not on a measurement. The 35-candidate probe covered
   `EffectViolation` forms only; **`NullDeref`'s 3 native candidates were never probed.**

The conclusion is conservative against the program's own interest (it declines to build a feature
that would raise supply), and run 2 says REJECT by the letter regardless. But the procedure is on the
record.

#### A-1.5.6 PP-W5 additive note (plan D-S5.5), registered results-blind

The A-1.4 tranche-1 PP-W5 row freezes `treatment = main at epoch time`, so a "v0.11 + v0.12"
treatment is consistent with **that clause**. Two further sentences in the row's **Threshold
(frozen)** column — not, as an earlier draft said, its Basis column — are affected:
1. *"by plan §3 the parity epoch runs at W5, so the treatment carries ALL v0.11 plan work (W1–W4),
   and this row adjudicates the v0.11-toolchain-vs-v0.10.0 RELEASE question"* — recorded **stale**;
   the adjudicated question is now v0.11+v0.12 vs v0.10.0.
2. The on-fail isolation recipe (`v0.10.0 + WS-W2-only`) was built for two-source attribution. A
   two-release treatment is three-source. **This is a §7 supersession, filed as such rather than
   called a clarification** — a documented protocol defect (a one-release recipe cannot attribute a
   two-release delta), corrected results-blind and before any PP-W5 epoch has run, in the
   strengthening direction. The ladder becomes two-step: `v0.10.0 + WS-W2-only`, then
   `v0.10.0 + all-v0.11`.
3. The **1.25 margin and its null calibration are NOT re-derived.** The false-fail calibration
   (~1.7%) is unchanged; what changes is that the treatment carries more change, so a true fail is
   more likely and a pass correspondingly stronger.

#### A-1.5.7 D-S1.5 fixture registry (PP-S4's instrument)

Registered because a blocker with no measurement path is unfalsifiable — the finding A-1.3 closed by
name for M-G4.

- **Location:** `bench/phase0-agent-native/fixtures/d-s1.5/` — one directory per registered feature.
- **Schema:** each entry contains the C# input, the expected converted Calor, a value-asserting test,
  and `fixture.json` naming the `FeatureSupport` key and the ledger loss kind it certifies absent.
- **CI entry point:** a `d-s1.5-fixtures` job asserting, for every `SupportLevel` promotion toward
  `Full` and every loss-kind removal in the diff, that a registered fixture exists and is green.
  **Indeterminate (fixture will not build) counts as failing**, conservatively.
- **Initial contents: empty.** The registry is frozen by location, schema, and entry point, not by
  content; entries accrue as WS-S1 lands promotions.

**Additivity.** This entry is a **pure insertion** into §A.3 plus the Annex version pointer update at
the head of Annex A. It alters no frozen row. Item 2 of A-1.5.6 is filed as an explicit §7
supersession and is the only such claim.

**A-1.4 tranche 2 — PP-W2 NOT ADJUDICATED (2026-08-04).** Additive; registers the
outcome of the real-scale measurement half, NOT a threshold. Per the two-tranche
D-W5.1 structure, tranche 2 was to freeze the PP-W2 threshold after the D-W4.4
dry-run. The dry-run (`w4-dryrun-001`, $74, 36/36 valid) and the follow-on
task-supply investigation instead triggered the wedge plan's **pre-committed
§6.2 not-adjudicated branch**: real-scale PP-W2 cannot be measured at v0.11
maturity because every thesis-testing task source is supply-starved on the
pinned corpus — logic mutations carry no verification signal (mechanical arm),
revert-bugfix yields 0 native∩separable, and the expressible-defect stratum
(mechanism proven, 100% Calor0410-addressable on real corpus code) yields 0
eligible (native surface not value-asserted / arm-divergent; **3 candidates, 3/3
addressable, 0/3 eligible — measured at the task generator's default caps
(`MaxCandidatesPerProject = 8`, `TargetEligiblePerProject = 3`) over a merged
cross-stratum candidate list taken in lexicographic order, with the run's full
switch set not recorded**). **No threshold is frozen and no epoch was run.**
**Trigger disclosure:** §6.2's parenthetical enumerates two not-adjudicated
triggers, and they are in **different states** — the D-W4.4 **ceiling** trigger
demonstrably did not fire (the check ran and cleared the other way), while the
**fidelity** trigger is **UNDETERMINED, not "did not fire"**: the D-W4.3 gate was
never applied (`w4-dryrun-001/VERDICT.md` finding 3 records this explicitly) and
no per-project `NativeFraction` was recorded in the epoch. A first version of this
entry asserted that neither trigger fired; that assertion was unmeasured and **is
withdrawn**. The **operative** trigger is a **third, not-pre-enumerated route:
task-supply starvation**, legitimate under §6.1's general principle and routing
identically, recorded as novel rather than folded into an existing trigger. Should
a future fidelity run show sub-2-project pass, the pre-enumerated trigger
**co-fires** and the routing is unchanged. The one adjudicable real-scale signal DID resolve and is
positive: the D-W4.4 **ceiling-recurrence check cleared** — the C# arm escaped
genuine bugs on 16.7% of runs (3/18 C#-arm runs, across 2 of 6 tasks), so the
v0.10 ceiling does NOT persist at real scale (evidence that the venue has
headroom; the blocker is substrate). **This is a dry-run signal at n = 6 tasks /
18 C#-arm runs and was not powered for it — an existence result, not a rate
estimate; no escape-rate figure is registered.** Full evidence + the three v0.12
levers (converter fidelity, checker breadth, corpus shape) + the
authored-contract-overlay track: `docs/plans/wedge-real-scale-closeout.md`.
**This alters no frozen row and makes no §7 supersession claim** — the §4 2a rows
and A-1.0…A-1.4-tranche-1 rows stand. The edit is insert-only **except for the
annex version pointer** at the head of Annex A, which is updated from "tranche 2
pending the D-W4.4 dry-run" to record tranche 2 as registered; every prior annex
revision (A-1.2/#808, A-1.3/#825, A-1.4-t1/#839) updated that same line, and
leaving it stale would have the pointer contradict the log it points at. Diff vs
`main`: **45 insertions, 1 deletion**, the deletion being that pointer.
Continuing: **PP-W5 and PP-A1 (release gates, both
carried forward to v0.12 — the v0.11.0 release was folded forward, no tag) and
PP-A2 (a Call W input, resolved at its pre-committed "demand unproven" value)**
are adjudicated separately and are unaffected by this entry. PP-W2's absence from
adjudication is itself the registered tranche-2 result, feeding Call W's
not-adjudicated branch.

**A-1.4 exclusion-closure note (2026-08-02).** Additive record, no threshold or
frozen-row change. The **PP-W1** row's frozen scope guards exclude "the
delegate-invocation and override/dispatch effect-laundering holes … pinned by
`DelegateInvocation_*` enforcement tests." WS-W2 (wedge plan §2; PR #842) has
now **closed both holes** — D-W2.1 makes delegate invocation an error
(`Calor0418`) and D-W2.2 enforces override/interface effect variance
(`Calor0420`/`Calor0421`). This note satisfies the WS-W2 exit criterion that the
A-1.2/A-1.3 scope-guard exclusion list "be updated (additively, as a note —
frozen rows themselves untouched)." **It does NOT alter PP-W1's adjudication:**
PP-W1 already ran and was adjudicated at `guarantees-probe-001` with those
exclusions in force, and closing the holes afterward cannot retroactively change
that epoch's scope. The closure's forward effect is only that a *future* gate
need not carry these two exclusions. No supersession claim (§7); the PP-W1 row
is unchanged.

**A-1.4 tranche 1 (2026-08-01).** Additive: registers the **PP-W5** frozen row
(A.2) — the wedge plan v0.11's strictness-parity release blocker — per the
plan's two-tranche A-1.4 structure (D-W5.1: tranche 1 freezes PP-W5 from
existing N1 epoch variance BEFORE the WS-W2 strictness batch merges; tranche 2
— the real-scale PP-W2 threshold, fidelity bar, eligibility predicate, and
annotation protocol — freezes after the D-W4.4 dry-run, before the real-scale
epoch). Derivation data, simulation method (300 null simulations × 400-resample
two-level cluster bootstrap over the archived `m5-compare-001` N1 result files),
and the power table are recorded in the row's Basis column; the margin (1.25)
deliberately coincides with the main document's frozen Phase-1 neutral
iterations envelope (125 %). No existing metric, threshold, or frozen row is
altered. Registered results-blind: zero WS-W2 batch code exists at freeze time. **Review round recorded (the #825 convention):** this registration was adversarially reviewed pre-merge (PR #839) — the reviewer independently re-derived the calibration (null p95 1.243 vs 1.247 registered, power within MC noise) and its M1–M3/m1–m3 amendments (CV-max correction, attribution restatement, validity floor, table join, artifact naming, source narrowing) were applied in-document, results-blind, before merge. Disclosed narrowing: plan D-W5.1 named `guarantees-probe-001` as a derivation source, but that epoch contains no N1 cells (W5-only pairs) — the derivation uses `m5-compare-001` alone, the only N1 population.

**A-1.3.2 (2026-07-30).** Results-blind amendment from the #827 epoch-driver
review, applied before any epoch run; instruments only, thresholds and epoch
shape unchanged. (1) **Seeded-declaration registration:** each fixture's
`defect.json` gains a `declarationId` field (W5A→`f003`, W5B→`f003`,
W5C→`f005`, derived mechanically — Calor0410 declarationId from compiling
the starter under the pinned config for the effect classes, §S-carrier scan
for W5-B; agent-visible fixture content unchanged). The adjudicator resolves
the M-G3 declaration from this field — a §S-only scan blinded the
build-proof/build-block channels for 6 of 9 defects (review C1). (2)
**M-G3 aggregation tie rule:** the 5-way channel vote can tie where the
binary catch vote cannot (2-2-1); a tie resolves to the LATEST (least
favorable) tied channel — plurality with a conservative tie-break, so an
ambiguous vote never flatters the treatment arm (review M1). (3)
**`build-block` code set** = exactly {`Calor0410`} under the pinned config:
journal diagnostics carry no severity and warning-severity Calor04xx exist
(review m1). (4) **Smoke-tamper enforcement at adjudication:** a
`smokeTampered` run is INVALID for every instrument (missed channel, cannot
satisfy the leg-b predicate, out of the M-G4 denominator) — the A-1.2
semantics carried by A-1.3, now enforced mechanically rather than by
post-hoc narrative (review C2). (5) **M-G4 archived-source resolution by
declaration, not filename:** the final source is whichever archived file
contains `§F{<declarationId>:...}`; weakened-by-rule applies only when NO
archived file contains the declaration — a renamed/moved file with the
declaration intact is not a weakening (review M3, faithful to the annex's
function-node-ID matching). (6) **PP-G4 leg (a) decided by the bound**: the
leg fails iff the one-sided 95% CI lower bound exceeds 1.0, exactly as
worded; the bootstrap p-value is reported but does not decide (review m2).

**A-1.3.1 (2026-07-30).** Results-blind amendment from the #826
instrumentation review, applied before any epoch run: (1) **M-G4 is a
two-leg comparison** — §Q and §S conjoined and compared SEPARATELY, weakened
iff the §S conjunction was relaxed OR the §Q conjunction was strengthened
(review C2: the frozen single-conjunction rule scored the canonical
prover-appeasement move — add a §Q until the refutation disappears — as a
strengthening; reproduced end-to-end on the W5-B defective shape before
amending); by-rule weakened now also covers a changed signature and an
unparseable final source. (2) **PP-G3 leg-b adjudicates on the emitted
`intactOrStrengthened` field**, not on `¬weakened` (review M3: a gutted
incomparable contract is not-weakened yet must earn no credit). (3) The
verify-gate §0.2 check is **bidirectional and effect-checked** (reviews
C1/C3/M1): expected-on-with-gate-unset AND expected-off-with-gate-set are
both INVALID, and an expected-on arm must pass a refuted-canary compile
proving the solver is actually reachable — env-var intent alone is not
validity. (4) Declared-done archival is recursive and fail-loud; an
incomplete archive invalidates the run (review M2). (5) Out-of-signed-32-bit
integer literals are refused, not wrapped (review C4 follow-through;
verification cache format 1.6 → 1.7). Thresholds, margins, denominators,
and the epoch shape are UNCHANGED — this amendment tightens instruments
only.

**A-1.3 (2026-07-29).** Additive: registers **M-G1–M-G4** (A.1) and the
**PP-G3/PP-G4** frozen thresholds (A.2) for the v0.10 Guarantees probe epoch
(guarantees plan D-G5.1/D-G5.2; plan merged `db8c1e4b`, so §-references
resolve). Epoch shape frozen: the A-1.2 D5.1 fixture set unchanged (no
fixture re-registration needed; the A.1 probe-integrity semantics —
`smokeTampered`, invalid-slot rules — carry over), **5 runs/arm** (odd;
supersedes A-1.2's exactly-3 for this epoch's M-W1 continuity copy only),
per-arm-native configurations declared in M-G3, control arm = tag
`guarantees-baseline-v0.9` (`ce7708af`) with the v0.9-review-C2 attribution
commitment (isolation build if non-plan `src/` changes accrete on main by
epoch time). Two disclosed methodology decisions made at freeze, both
results-blind: PP-G4's iterations leg widens from the plan's
"contract-carrying tasks" wording to all 9 pairs (3-cluster bootstrap is
degenerate — PP-W1 precedent; trio medians additionally reported), and the
weakening margin freezes at ≤ 3/15 excess runs with the small-sample
rationale stated in the row. **Fixture-metadata note (#825 review m4):** the W5-B `pair.json`/`defect.json`
files register "catch channel = Debug runtime guard, static-verify excluded
until #807" — #807 is fixed; that channel claim now binds the v0.9 CONTROL
arm only, and the v0.10 arm's build-proof channel is governed by this entry
(fixture files deliberately unchanged). **Review round recorded:** this
registration was adversarially reviewed pre-merge (#825); C1–C3/M1–M4/m1–m4
amendments were applied in-document, results-blind, before the epoch — the
review's operationalization standard (every registered quantity names its
per-run artifact, and unbuilt instrumentation is frozen as a pre-epoch
checklist) is now part of the entry. Spend is NOT authorized by this
registration — that is a separate gate per
`phase-2-spend-authorisation.md`.

**A-1.2 (2026-07-27).** Additive: registers **M-W1** (A.1) and the
**PP-W1** frozen threshold row in A.2 (WS5/M4b, kickoff
`docs/plans/loop-m4b-ws5.md`). Honest timing: the registration froze
**before the probe epoch runs**, but fixture authoring was concurrent —
the D5.1 pair set was being built on a sibling branch while this row was
written, and one class definition (W5-C) was re-priced from an
authoring-time discovery (#809: cross-module emission gap) BEFORE this
row's class definitions froze; the class definitions are therefore inlined
in the A.2 row itself rather than referenced from the mutable kickoff
table. What remains results-blind, and is the property this registration
protects: the threshold, the adjudication rule, the scope guards, and the
decidability fallback were all fixed with zero live agent runs observed.
Also records the scope guards (excluded enforcement holes, the
#807-until-fixed exclusion of the static-verify channel, W5-B
postcondition-only per #755) and the feasibility-by-determinism argument
standing in for a D4.5 dry-run (supersession entry in
`loop-plan-v0.9.md` §10). No existing metric or threshold is altered;
pre-existing blanket prose ("frozen from dry-002") is qualified rather
than falsified.

**A-1.1 (2026-07-25).** Additive clarification, no threshold changes: M-L2's
`mcp-file` sourcing (per-attempt `mcp-writes.jsonl` stream, M3 PR 4) and its
heal accounting — `applied/attempts` is first-apply-after-autoheal validity;
`appliedUnhealed` is the strict form; both reported in cross-mechanism
comparisons (review of #799).

**A-1.0 (2026-07-24).** Initial freeze. Definitions from loop plan §4;
thresholds per the `loop-feasibility-dry-002` verdict
(`bench/phase0-agent-native/epochs/loop-feasibility-dry-002/VERDICT.md`);
PP-L5 tokens threshold approved by the maintainer 2026-07-24 (recorded in
the PR #795 conversation; at bus factor 1 this approval is self-asserted —
disclosed per the main document's §7 convention).
