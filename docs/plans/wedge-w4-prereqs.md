# WS-W4 Kickoff — Real-Scale Benchmark Prerequisites & Results-Blind Decisions

**Status: KICKOFF (planning). Records decisions made results-blind at W4
kickoff; freezes nothing.** Per the split clock (wedge plan §2 D-W4.5, review
n3), the D-W4.5 annotation protocol and the D-W4.1 eligibility predicate are
*decided here, results-blind*, where they govern **dry-run** task selection;
they are **frozen in Annex A-1.4 tranche 2** only after the D-W4.4 dry-run and
before **epoch** task selection. This document is the W4 analogue of
`wedge-w1-prereqs.md`. It does not touch any frozen row.

Cross-references verified against `wedge-plan-v0.11.md` §2 (D-W4.1–D-W4.5),
§3 (PP-W2/PP-W5), §6 (Call W), `agent-native-gates.md` Annex A (tranche 1
frozen), `real-scale-benchmark-design.md`, and the round-trip harness
(`RoundTripPipeline.cs`, `RoundTripReport.cs`, `ProjectConfigs.cs`).

---

## §1 — Governing principle: predicate trust = "no silent substitution survives native conversion"

The D-W4.1 eligibility predicate is trustworthy **iff no silent semantic
substitution can survive *native* conversion into the Calor arm.** The crux
(brief §F gap): predicate clause (b) — "the removed held-out test fails
identically on both arms' baselines" — only witnesses divergence the *removed
test covers*. A silent substitution **outside** that test's coverage passes the
predicate while the two arms actually diverge, corrupting the measurement in an
undetectable direction.

**The decisive simplification for W4's engineering scope:** the benchmark does
**not** require faithfully migrating each unsupported C# feature. It requires
only that the converter **never silently substitutes** — every unfaithful
conversion must be **LOUD**: a build break, a `Calor####` diagnostic, or
escalation to `§CSHARP`/interop preservation (which the coverage model already
counts as a loss, `RoundTripReport.cs:89-110`). A LOUD failure makes the region
**non-native** (`ConvertedNative` requires `LossCount == 0` **and** not-reverted,
`RoundTripReport.cs:81`), so predicate clause (a) excludes the task
mechanically. This is `#774`'s own requirement #6 ("escalate unsupported
expressions and patterns to containing-member interop in lossless mode")
generalized across all six migration-fidelity items.

**Consequence:** W4's conversion-honesty prerequisite is a bounded
*SILENT→LOUD* pass, not a feature-migration project. `#774` (the pervasive
expression/operator/pattern silent class) is the keystone and is already in
flight; the structural items get a smaller loud-refusal treatment (§2).

---

## §2 — Migration-fidelity go/no-go triage (six epic items)

Decision rule: a **SILENT** divergence (converted baseline still compiles and
may pass the visible suite while behaving differently) is a **predicate-trust
blocker** and must be made LOUD before the eligibility predicate is trusted. A
**LOUD** divergence (build break / diagnostic / interop fallback) is already
handled by the fidelity gate's baseline-green check + predicate clause (a) and
needs only containment/registry-honesty, not a predicate-blocking fix.

| # | Class | Decision | Scope of the W4 fix |
|---|-------|----------|---------------------|
| **#774** P1 | **SILENT** (keystone) | **FIX-NOW** (in flight) | Full de-silencing: exhaustive operator/pattern `SyntaxKind` switches, dedicated char literal, distinct compound-assignments incl. `>>>=`, typed/type-only pattern nodes, no wildcard/add/negate fallbacks, interop escalation for the rest. |
| **#772** P0 | **SILENT** (first-`#if`-branch swap) | **FIX-NOW, minimal (SILENT→LOUD)** | Make the converter **refuse** (whole-member/whole-file interop, or a diagnostic) on any `#if` whose condition it cannot evaluate, instead of silently keeping the first branch. Full conditional-compilation preservation is **deferred** (not needed — refusal → exclusion). Fix the registry false-green claim. |
| **#769** P0 | **SILENT** (same-name type merge) | **FIX-NOW, minimal (SILENT→LOUD)** | Make recovery **refuse to merge** two types that share a bare name across different namespaces (escalate to interop or emit a diagnostic). Full namespace-topology preservation **deferred**. Flattening that does *not* risk identity merge may remain, but must not be claimed as faithful in the registry. |
| **#775** P0 | **MIXED, leans LOUD** | **CONTAINMENT** | The positional-record ctor mismatch is already a build break (excluded). Owed: registry honesty — downgrade the "records fully supported" claim; escalate records whose behaviour is exercised through dropped `Equals`/`with`/`Deconstruct` to whole-member interop so those are non-native, not silently degraded. |
| **#777** P0 | **MIXED, leans LOUD** | **CONTAINMENT** | Unresolved captured locals already build-break (excluded). Owed: whole-member interop for local functions with type params or captures (the residual silent case = a hoisted non-capturing generic local fn that compiles but loses generic identity); registry honesty. |
| **#751** | **LOUD** (`Calor0258`) | **NO PREDICATE FIX** | Bare-block flattening now fails loud at `calor -i` (post-#731). Excluded by predicate clause (a). Low priority; author notes the case is uncommon. |

**Frozen list for W4 Slice A (the conversion-honesty prerequisite):** `#774`
(full), `#772` (loud-refusal), `#769` (loud-refusal, no-merge), `#775`/`#777`
(containment + registry honesty). `#751` requires nothing. **Definition of done
for Slice A:** a corpus-wide sweep proves that on the pinned corpus, no file
converts *natively* while carrying a silent substitution — operationalized as: a
registry/loss-ledger audit test asserting every unsupported construct routes to
loss-counted interop or a diagnostic, plus a differential check that native
regions round-trip with runtime equivalence.

---

## §3 — Results-blind decisions (recorded at kickoff; frozen A-1.4 tranche 2 after the dry-run)

### D-W4.5 — Annotation protocol: **provisionally (iii) both-as-arms, dry-run-gated, with (i) as the pre-committed fallback**

The Calor arm carries **no `§Q`/`§S`** off the converter; every prior
`build-proof` result ran on authored contracts. What the arm carries at real
scale is this results-blind decision (wedge §2 D-W4.5).

**Decision (blind to any real-scale outcome):** target **(iii) two labelled
arms** —
- **Arm i (mechanical-only):** converted code + WS-W2 effect enforcement +
  `calor import`-derived annotations only. This *is* the product's day-one
  onboarding output; blind by construction; serves PP-A2 (go-to-market truth)
  and the enforcement/runtime-contract value claim.
- **Arm ii (authored-under-blindness):** contracts authored on the converted
  corpus by a registered rule, **frozen before mutation selection** (so no
  contract can be aimed at a known defect). Exercises the `build-proof` channel
  that carries PP-W2's headline claim.

Rationale for targeting (iii): it is the only option that measures **both** the
day-one product (un-confounded, arm i) and proof-depth (arm ii); with the
verify gate's outcome-vs-channel ceiling finding (plan §1), starving the proof
channel entirely (option i alone) would leave PP-W2 unable to test its own
headline. Choosing among options results-blind is legitimate under the plan;
the choice does not depend on any epoch result.

**Two dry-run gates that can collapse (iii)→(i) before the tranche-2 freeze
(split clock permits this):**
1. **Overlay feasibility.** Arms ii/iii require a *deterministic
   contract-overlay mechanism* that re-applies the frozen authored contract set
   onto every fresh per-task conversion mechanically and identically (review
   n1) — **this mechanism does not exist today.** The dry-run prices its build.
   If it cannot be made deterministic within budget, collapse to (i).
2. **Three-arm cost.** A 3-arm epoch multiplies run count 1.5×. The dry-run
   measures the real per-run cost; if a 3-arm epoch at the decidable N breaches
   the **$1,500/epoch ceiling**, collapse to (i) (or drop arm i from the epoch
   and keep ii, decided by the dry-run's cost table).

**PP-W2 wording, drafted now for both outcomes (D-G3.1 restate-or-demote):**
- Under **(iii)**: "On converted OSS C# of this shape, does the *authored-contract*
  Calor arm (arm ii) show an escaped-bug advantage at the frozen margin, with
  arm i reported alongside as the day-one-product baseline?"
- Under **(i)-fallback**: "…does the *mechanical-only* Calor arm (enforcement +
  import-derived annotations, no authored proofs) show an escaped-bug
  advantage…?" — the claim is explicitly about enforcement/runtime-contract
  value, **not proof depth**, and Call W reads it as such.

### D-W4.1 — Eligibility predicate (mechanical mapping, brief §F)

A task is **eligible** iff:
- **(a) Mutated region converts natively:** its `FileConversionResult` is
  `ConvertedNative` — `Status == Replaced` **AND** `LossCount == 0` **AND** not
  reverted by build recovery (`RoundTripReport.cs:81`,
  `RoundTripPipeline.cs:339-399`). Interop/emitter-fallback regions carry
  `LossCount > 0` (`ApplyLossLedger`, `RoundTripReport.cs:89-110`) → ineligible.
  This is the mechanical guard against "the defect left in raw C# inside the
  'Calor' arm."
- **(b) Mutation survives conversion:** the removed held-out test **fails
  identically** on both arms' post-conversion baselines (a targeted
  `RunTestsAsync` with a `TestFilter`, as in `BisectRegressionsAsync`).
- **(governing, §1):** Slice A must hold — no silent substitution survives
  native conversion — so that (a)+(b) are not fooled by untested silent drift.

Ineligible tasks are **excluded, counted, and disclosed** (no silent corpus
shrinkage). **Construction order is mutate-then-convert** (review C2): the
mutation is applied in C# and the converter carries it into the Calor arm
mechanically. **Presentation asymmetry is recorded** (review): the Calor arm
works on machine-converted `§`-syntax vs the C# arm's idiomatic original — a
bias *against* Calor, so a PP-W2 win is conservative and a loss is confounded
with conversion idiom.

### D-W4.3 — Conversion-fidelity gate (structure now; threshold value at tranche 2)

Per project, per task: the converted arm's baseline suite must be green **and**
its coverage ≥ the A-1.4-registered bar **before** any task from that project
counts. The bar attaches to `ConversionCoverage.NativeFraction`
(`RoundTripReport.cs:299`), with **reverted files kept in the denominator as
failures** (the #837 anti-masking property, `RoundTripReport.cs:258`). The
**converter-attribution rule**: a defect expressible *only* through
converter-introduced divergence is excluded, isolated via `BisectRegressionsAsync`
(`RoundTripPipeline.cs:585`). **Threshold value is NOT frozen here** — provisional
`NativeFraction ≥ 0.70` to steer the dry-run; the dry-run moves it; frozen
tranche 2. **If fewer than 2 projects pass the bar, PP-W2 is not adjudicated**
(reported only, §6.1).

### D-W4.4 — Ceiling-recurrence floor (structure now; value at tranche 2)

The dry-run must show C#-arm escaped-bug incidence **above a floor** on the
dry-run tasks; a relative-reduction threshold is blind to both-arms-at-zero,
which is how the authored suite died (strategy §9). **Pre-committed branch:**
incidence ≈ 0 → PP-W2 is **not frozen and not adjudicated**, and the finding is
published as **"the ceiling persists at real scale"** — fed to Call W's
not-adjudicated branch (§6.2), not a quiet failure. Provisional floor to steer
the dry-run: **≥ 1 C#-arm escaped bug per ~5 eligible tasks**; frozen tranche 2.

---

## §4 — Corpus pinning (D-W4.2)

Vendor the three OSS projects (**MediatR, Serilog, FluentValidation**) at
**pinned SHAs** plus in-repo **Synthetic**; the external, un-pinned
`~/sources/repos/experimental/github-top10` dependency ends
(`ProjectConfigs.cs:53-81`) — an un-pinned corpus is an unreproducible epoch.
`ProjectConfigs.Get` points `OriginalProjectPath` at the vendored copy.
Per-corpus **dependency effect manifests** come via **D-W3.1 `calor import`**
(the two program halves meet here). **Baseline suite-green per project is not
statically determinable** — it is measured early in W4; ≥2 must pass the
fidelity bar or PP-W2 is not adjudicated. **Corpus bias stated:** 3 OSS
projects, shape-C#-selected → conclusions scope to *"on converted OSS C#
codebases of this shape,"* nothing broader.

---

## §5 — Dry-run spend (D-W4.4) — separate numbered authorization required

The dry-run's agent spend is **authorized separately, with numbers**, via a new
`phase-2-spend-authorisation.md` entry **before the dry-run starts** — it is
**not** covered by the W5 epoch authorization (review m6). The user's standing
"I approve the epoch spend too" pre-approves the spend under the **$1,500/epoch
ceiling**; the numbered doc + §2 real-scale calibration is the owed *artifact*,
not a fresh approval gate.

Cost model (brief §E; trustworthy unit = **$0.88/run** from `guarantees-probe-001`
summed `total_cost_usd` — **not** the m5 hand-priced $0.176 artifact):

| Scenario | per-run | runs | dry-run total |
|---|---:|---:|---:|
| Authorable-fixture floor | $0.88 | 30 | ~$26 |
| Real-scale conservative (5× repo/iteration) | $4.40 | 30 | ~$132 |
| Real-scale heavy (20× full-suite churn) | $17.60 | 30 | ~$528 |
| 3-arm (iii), heavy | $17.60 | 45 | ~$792 |

All scenarios sit under the $1,500 ceiling; the dry-run's §2 calibration
(≥3 real-money trials on real-scale tasks) replaces these estimates with
measured cost, variance, and achievable MDE **before** PP-W2's threshold
freezes. The dry-run may move the threshold, task count N, or corpus before the
freeze — never after.

---

## §6 — W4 slice plan

- **Slice A — conversion honesty (predicate trust).** `#774` (full, in flight)
  + `#772`/`#769` loud-refusal + `#775`/`#777` containment & registry honesty.
  DoD: no file converts natively while carrying a silent substitution
  (audit test + differential runtime-equivalence on native regions).
- **Slice B — corpus vendoring (D-W4.2).** Vendor + pin the 3 OSS projects;
  measure baseline suite-green per project; generate dependency manifests via
  `calor import`.
- **Slice C — task generation (D-W4.1).** Mutate-then-convert harness; held-out
  test extraction; eligibility-predicate implementation over the harness's
  `ConvertedNative` / targeted-test-run primitives; exclusion accounting.
- **Slice D — fidelity gate (D-W4.3).** Wire the per-project `NativeFraction`
  bar (the Slice-5b threshold substrate) + converter-attribution rule via
  bisect.
- **Slice E — dry-run.** New numbered spend authorization + §2 calibration →
  **D-W4.4 dry-run** (≥3 runs/arm × ≥5 tasks × ≥2 projects) → **A-1.4 tranche 2
  freeze** (protocol, predicate, fidelity bar, attribution rule,
  ceiling-recurrence floor, N, PP-W2 threshold + restated wording, quantified
  underpowered criterion, M-A1/M-R*/M-T1 defs).

Slices A–D parallelize where they don't share files; Slice E is strictly last
(the dry-run cannot run before the substrate exists) and gates the tranche-2
freeze, which gates the W5 epochs.

---

## §7 — Additive note owed on the frozen exclusion rows (WS-W2)

WS-W2 (D-W2.1 delegate-invocation = error `Calor0418`; D-W2.2 override/interface
effect variance `Calor0420`/`Calor0421`) **closed both holes** cited as
exclusions by the frozen **PP-W1 row** (`agent-native-gates.md:294`) and the
A-1.2 log (`:409-410`). The wedge plan (§2 WS-W2 exit) requires this be recorded
**additively, as a note — the frozen rows themselves are untouched.** This note
lands with the A-1.4 tranche-2 registration PR (a natural batching point), not
as an edit to any frozen row.

---

## §8 — Ordering invariants (must hold through W4→W5)

1. **Split clock:** protocol (D-W4.5) and predicate (D-W4.1) are *decided
   results-blind at kickoff* (govern dry-run selection), *frozen tranche 2*
   before **epoch** selection. "Frozen before task selection" = *epoch* tasks.
2. **Dry-run authorization precedes the dry-run** (numbered, with §2
   calibration).
3. **PP-W2 never freezes until the dry-run confirms decidability**; the
   ceiling-recurrence floor must clear first.
4. **Restate-or-demote:** PP-W2's wording restates to match whichever protocol
   the dry-run leaves standing.
5. **Additive-only:** A-1.4 tranche 2 registers a successor measurement on a
   different suite; the frozen §4 2a rows and A-1.0…A-1.3 rows are untouched;
   **no §7 supersession claim.**
