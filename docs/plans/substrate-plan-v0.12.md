# The Substrate — v0.12 Execution Plan

**Status:** Draft v2 — adversarial review round 1 applied (verdicts on v1: evidence 62%, strategy 52%, measurement discipline 45%; dispositions in §10). Standing review targets: the M-S3 threshold's power derivation (§4), the A-1.5 registration content (§2 D-S5.2), and whether WS-S0.5's probe is cheap enough to precede WS-S1 (§2).
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-08-04
**Parent:** [`agent-native-strategy.md`](agent-native-strategy.md) and [`agent-native-gates.md`](agent-native-gates.md). **Annex A is at A-1.4 tranche 1 + the exclusion-closure note at the commit this plan audits (`85347e49`); tranche 2 lands with PR #855** — see the merge-order dependency in §3.
**Predecessor:** [`wedge-plan-v0.11.md`](wedge-plan-v0.11.md), whose **Call W** resolved **PROCEED (substrate)** on 2026-08-04 — PP-W2 **not adjudicated**, PP-A2 = **"demand unproven"**. The two records this plan is built on — `wedge-real-scale-closeout.md` (the finding and the three levers) and `call-w-adjudication.md` (the shape decision) — **are not yet on `main`; they land with PR #855**, and this plan must not merge before them (§3).
**Release carry-forward:** there is **no v0.11.0 tag** — the maintainer folded v0.11 forward. v0.12.0 therefore ships the v0.11 range as well, and **PP-W5** (strictness parity, frozen at A-1.4 tranche 1 / #839) and **PP-A1** (CI adoption gates, frozen list at [`wedge-w1-prereqs.md`](wedge-w1-prereqs.md) §3, carried **unchanged**) carry forward as v0.12.0 release gates.
**Target release:** v0.12.0.

---

## 0. Objective

> **Make the real-scale measurement possible: supply the benchmark with tasks it can actually adjudicate, by first measuring which stage of the supply funnel is actually starved — and prove the supply mechanically, before spending on another epoch.**

v0.11 answered its question and the answer was structural. The real-scale benchmark is *built* and *proven* — oracle-hidden bundle runner, three task strata, a working `Calor0410` addressability differential — and it produced **zero** eligible thesis-testing tasks on a pinned three-project corpus. Not a null result: a supply failure, with three candidate causes and, as of Draft v2, **no measurement of which one binds**.

The discipline that makes this plan cheap *in agent spend* is that its headline proof point needs none. Eligible-task supply is a mechanical property of the task generator over the corpus: it is computed by running the harness, not by running agents. That inverts v0.11's cost structure, where the supply failure was discovered *after* the dry-run spend. **It does not make the plan cheap in the resource that actually binds** — maintainer calendar time at bus factor 1 (§7 risk 7). WS-S1 alone targets a 32–75% relative lift in native fraction. Dollar-cheapness must not be used to launder calendar-expensiveness, and §2's sizes, not §0's framing, are the honest cost signal.

### 0.1 What v0.12 buys, stated precisely (restate-or-demote record)

Every supply lever this plan builds — effect-discipline violations (`Calor0410`), null-deref, index-OOB, div-by-zero — is **enforcement and shape-keyed bug-pattern checking**. None of them is a Z3 proof. So a fully successful v0.12, followed by a powered epoch and a PP-W2 hit, would establish that **effect enforcement and mechanical checkers catch injected defects that C# ships**. That is a real and worthwhile result. It is **not** the strategy's headline claim that machine-checked *proofs* beat very good agent-generated tests.

Under the restate-or-demote precedent this program already uses (D-G3.1), that is recorded here rather than left implicit: **v0.12 supplies enforcement-testing tasks, and proof depth remains untested.** The only channel connected to the proof-depth claim is the authored-contract overlay (WS-S4), which stays off the critical path and out of Call S (§6.2) by explicit decision — the cost being that the headline claim waits. Any v0.12 communication that implies otherwise is an overclaim.

### 0.2 The one banked positive, with its caution intact

The v0.10 authorable-fixture ceiling does not survive real code: the C# arm shipped genuine, held-out-failing bugs where it shipped none on authored fixtures. **Caution, carried verbatim from the close-out rather than shed in transmission: this is a dry-run signal at n = 6 tasks / 18 C#-arm runs, and the dry-run was not powered for it. It is an existence result, not a rate estimate.** There is headroom to measure; how much is unknown.

Non-goals: running the real-scale epoch (the *next* spend gate, explicitly outside this plan's box — §6.2); new adoption depth (PP-A2 routed the adoption half to maintenance-mode); Phase 3 items; frames/`old()`; async/concurrency verification; the Call 3 crossing (reserved, §6.3); 1.0.

---

## 1. Audit delta: the substrate at Call W

**Audit rule (carried from v0.9 §1), stated honestly for this plan:** every anchor below is grep-verified on 2026-08-04. Anchors are verified against **`85347e49`** (main at Call W) *except* where explicitly marked as living on an **unmerged branch**, which are verified against that branch's recorded SHA. v1 of this plan violated this rule by citing unmerged work as if it were on main; the marks below are the correction.

1. **Converter fidelity is measured, and its magnitude is known.** Slice B (`5de804cc`, corroborated by the committed table at `bench/corpus/README.md:122–124`): **`NativeFraction` MediatR 0.469, Serilog 0.400, FluentValidation 0.532** — all below the provisional 0.70 steer (`tools/Calor.RoundTrip.Harness/TaskGen/FidelityGate.cs:11`, bar configurable and explicitly provisional). So **47–60% of the convertible surface is excluded**, not "roughly half" — and the denominator is the *convertible* surface: `ConversionCoverage.Compute` counts only files whose `Status != Excluded`, so pattern-excluded files are outside the fraction entirely (`RoundTripReport.cs:294–311`). **That denominator is unpinned and is the cheapest gaming route in the plan** — one added glob in a project's `ExcludePatterns` raises `NativeFraction` with zero conversion work (D-S1.4 fix).
2. **The loss ledger tells us where fidelity goes — but reading it is not free.** `ConversionLossKind` (`src/Calor.Compiler/Migration/ConversionContext.cs:125–147`) partitions losses into `InteropPreserved`, `FallbackTodo`, `Dropped`, `PreprocessorStripped`, `EmitterFallback`. Two corrections to v1: `ConversionLoss.Line`/`.File` are **optional** (`int?`/`string?`, with an "unknown location" fallback at `:162–168`), so per-loss location is not guaranteed; and the aggregate `LossKindCounts` is **by kind only** (`RoundTripReport.cs:312`), with the kind×feature cross product present in-model (`FileLossDetail`, `:126–132`) but not surfaced by the report writer, which orders loss kinds *alphabetically* (`ReportGenerator.cs:91–93`). D-S1.1 therefore needs report-side work; v1's "nearly free" was wrong. The companion claim survives attack: **the ledger has never been read as a ranked work-list.**
3. **The honesty work moved the registry — but not as cleanly as v1 claimed.** `FeatureSupport` is **137 entries: 105 Full / 10 Partial / 22 NotSupported** at `85347e49` (counted as `grep -c "Support = SupportLevel.<L>,"`, cross-checked against `grep -c "new FeatureInfo"` = 137), against v0.10.0's 124 = 105/8/11 at `e24a6832` — the same count method at `wedge-plan-v0.11.md`'s own recorded audit commit, so the comparison is apples-to-apples. **Three corrections to v1, which got its own honesty thesis wrong:**
   - v1 said "all 13 net-new entries are `Partial` or `NotSupported`." **False — 2 are `Full`:** `type-pattern` (added by `a91c679c`, one of the two commits v1 named as the cause) and `char-literal` (added by `a326fd5e`).
   - v1 said "`Full` is unchanged at 105." True of the integer, **false of the set**: `record` went Full→**NotSupported** and `preprocessor-directive` went Full→**Partial**, offset by the two new `Full` entries. The flat count masks two demotions.
   - v1 attributed all 13 to W1 Slice A (`a91c679c`, `4d7f5887`). Those two commits added **5**; the other **8** came from `a326fd5e` (#836, "W1 Slice 3 — conversion honesty"), which v1 never cited.

   The narrative survives all three corrections and is still the most important thing to hold onto: the registry grew overwhelmingly toward *declared* loss, and **some of the current low native fraction is honesty, not incapability**. §5's PP-S4 exists to keep it that way. But the claim is now stated at the precision the evidence supports.
4. **Checker breadth is narrow, and narrower in some ways than v1 said.** `NullDereferenceChecker` keys on unwrap-family target spellings — `.unwrap`, `.unwrap_unchecked`, and a `Contains("unwrap")` catch-all (`Analysis/BugPatterns/Patterns/NullDereferenceChecker.cs:264–268`) — **and also on `.expect` / `.get_unchecked`**, which v1's "entirely" elided. The headline negative claim survived a falsification attempt: **plain reference-null dereference is not modeled at all** (`DiagnosticCode.NullDereference` has a single report site, `:247`, reached only when the receiver name cannot be extracted). `IndexOutOfBoundsChecker` keys on `.get`/`.at`/`[]`/`array_get`/`list_get` (`:203–208`) and explicitly cannot see native array access (`:164`, "BoundNodes don't have BoundArrayAccessExpression yet"). Two v1 errors corrected: there are **six** checkers, not five (`DivisionByZero`, `IndexOutOfBounds`, `NullDereference`, `OffByOne`, `Overflow`, `PreconditionSuggester`), and **not all are shape-keyed** — `PreconditionSuggester` keys on unconstrained-parameter dataflow. And "converted C# produces neither vocabulary natively" is over-broad: `IsArrayAccessCall` lowercases and matches any `.get`/`.at`, which the corpus already hits (`bench/corpus/FluentValidation/.../PrecisionScaleValidator.cs:68`). The honest form is "**neither vocabulary reliably**" — and the over-broad match is itself a false-positive path, which is what D-S2.3 guards.
5. **The expressible-defect machinery is proven but UNMERGED, and its measured zero is configuration-dependent.** `ExpressibleMutationOperators` + `VerificationAddressability` (plus modifications to `ExclusionAccounting`) live on branch **`w4-expressible-stratum` @ `c77da1d1` (PR #856, open)** — **not** at `85347e49`. v1 called this "banked"; it is not banked until merged (§3). Corrections: it adds **14 test methods** (`TaskGenExpressibleTests` 9, `TaskGenAddressabilityTests` 5), taking the harness project 118→132 — v1's "132 tests" misattributed the whole pre-existing suite, a ~10× inflation. **One of the 14 skips without a native Z3** (`DivByZero_GuardRemoval_IsAddressable_Calor0920`). `0 src/ changes` is correct. Measured yield: **3 native candidates, 3/3 addressable, 0/3 eligible**, every exclusion under clause (b). **Two disclosures v1 omitted:** the per-candidate dispositions are from an investigation log and are **not a committed artifact** (D-S0.5.3 fixes this); and the measurement ran at generator defaults `MaxCandidatesPerProject = 8`, `TargetEligiblePerProject = 3` (`TaskGenOptions.cs:62,65` on `c77da1d1`) — so "3 candidates" is 3 out of **at most 8 evaluated per project**, not 3 out of the corpus.
6. **The clause-(b) exclusions are two different failures and must not be collapsed.** The three candidates died on `NoObservableDefect` (no covering value-asserting test — the corpus-shape diagnosis) *and* on `ArmsDiverge` (arm-divergent failure signature — a *semantic-fidelity-within-already-native-code* problem, which raising `NativeFraction` does nothing for). Both are `IsClauseB` (`EligibilityPredicate.cs:78,117`). v1 attributed the whole zero to `NoObservableDefect`; that is the corpus-shape lever taking credit for an exclusion half of which belongs elsewhere.
7. **The expressible stratum's supply currently depends on a converter defect this plan is chartered to fix.** The `EffectViolation` operator documents its own mechanism: the `Directory.*` calls are effects, but **nested in a `using` body — the converter's `§E`-inference walker's blind spot** — they are omitted from the converted `§E`, so `Calor0410` fires. The signal exists *because the converter mis-infers effects*, not because verification caught what C# missed. **PP-S1 hitting can therefore drive PP-S3 to miss**, and leaving the blind spot in place is a supply-preserving incentive pointing the wrong way (D-S1.6, M-S4).
8. **Two release gates are unadjudicated and now belong to v0.12.** PP-W5 is frozen at A-1.4 tranche 1 (`agent-native-gates.md:297`, from `08446f6d`/#839) and has never run — no parity epoch exists under `bench/phase0-agent-native/epochs/`. Its frozen `treatment = main at epoch time` still holds by the letter, but the row's Basis sentence ("the treatment carries ALL v0.11 plan work … adjudicates the v0.11-toolchain-vs-v0.10.0 release question") is now stale, and its pre-committed on-fail isolation recipe (`v0.10.0 + WS-W2-only`) cannot attribute a tax across two releases. D-S5.5 registers the additive note. PP-A1 carries forward unchanged.

---

## 2. Workstreams

Ordering rationale, **corrected in Draft v2**. v1 asserted that fidelity caps every other lever. That is true of clause (a) native supply and **false of the funnel as a whole**: all three candidates cleared clause (a) and died on clause (b), and fidelity moves the candidate term by at most ~1.5× (0.45→0.70) while the term measured at zero is a different one. But the counter-argument is equally thin — the diagnosis rests on **n = 3**, where a true 25% clause-(b) pass rate yields 0/3 about 42% of the time. Neither ordering is entitled to an L-sized commitment on that evidence. So **WS-S0.5 measures the funnel first, cheaply, and WS-S1 is scoped from the result** — exactly the discipline D-S1.2 already applies to the ledger, applied one level up.

### WS-S0.5 — Funnel probe (size: S; precedes everything)

- **D-S0.5.1 Raise *n* and measure stage-wise pass rates.** Run the generator at a raised, pre-registered candidate cap with **no early stop** (`TargetEligiblePerProject = 0`), broadened operators (D-S0.5.2), and region-granularity clause (a) (D-S0.5.4), reporting per project and per stratum: `candidates → clause-a pass → clause-b pass (split NoObservableDefect / ArmsDiverge / other) → addressable → eligible`. Target n ≈ 30 candidates, which distinguishes a ~0% clause-(b) rate from a ~25% one.
- **D-S0.5.2 Mutation-operator breadth (harness-side, no `src/` changes).** The demonstrated cheapest supply lever in the record: broadening `EffectViolation` is what moved native supply 0→3. Extend the operator set and record an explicit decision on `NegateCondition`, currently filtered from the default set with a rationale in code (`TaskGenerator.cs:54–56`) but no plan-level disposition.
- **D-S0.5.3 Commit the baseline zero.** Emit the full `ExclusionAccounting` dispositions of the baseline run as a **versioned artifact** under `bench/phase0-agent-native/`, with the pinned `TaskGenOptions` alongside. PP-S3 is a before/after against this zero; it cannot be an investigation log.
- **D-S0.5.4 Region-granularity clause (a) — priced, with a recorded decision either way.** Eligibility gates at *file* granularity while defects are at *method* granularity, so one interop block anywhere excludes every site in the file. The ledger's per-loss location (where present — §1 item 2) supports a site-level test, and the D-W4.3 attribution guard already catches divergence originating elsewhere. This raises candidate supply at **today's** fidelity with zero emitter work. It is a granularity fix, not a bar relaxation — but it is adjacent enough to the deliberately-unrelaxed `RequireIdenticalSignature` guard that it gets an explicit accept/reject, recorded before the numbers are seen.

Exit criteria: the funnel table committed, per project, per stratum, at the pinned configuration; WS-S1/WS-S2/WS-S3 boxes confirmed or re-scoped from it (v0.9 discipline); D-S0.5.4 decision recorded.

### WS-S1 — Converter fidelity (size: L, scoped by WS-S0.5)

- **D-S1.1 Read the ledger — as a marginal-recovery ranking.** Because `ConvertedNative` requires `LossCount == 0` for the whole file, occurrence count is actively misleading: a loss kind with 500 occurrences spread across files carrying four other kinds recovers **zero** native fraction. The primary output is a **"one-loss-away" file histogram** — files whose *sole remaining* loss is kind K — and a ranking by files-made-native-if-fixed. Requires the report-side work in §1 item 2.
- **D-S1.1a Early abort (go/no-go).** If the ledger's cumulative achievable recovery cannot reach the M-S1 bar, **PP-S1 resolves miss at D-S1.1**, before D-S1.2 is funded. v1 deferred that verdict to Call S, i.e. until after the L was paid for.
- **D-S1.2 Close the top-ranked emitter gaps,** in marginal-recovery order through the pre-priced work-list. **The bar is adjudicated on what the work delivers, not the work defined as "until the bar is met."**
- **D-S1.3 `EmitterFallback` disposition.** Each occurrence is either a real feature gap (→ D-S1.2) or a ledger-threading gap (`ConversionContext.cs:139–146`, #836 M2). Both in scope; conflating them is not.
- **D-S1.4 Fidelity non-regression in CI, with a pinned denominator.** Per-project `NativeFraction` asserted against a committed floor that ratchets up only — **and the assertion covers the project's `ExcludePatterns` list by hash**, so changing the exclusion set breaks the build instead of moving the metric. Any pattern change is a recorded deviation reporting the fraction under both lists. **The ratchet floors are a regression device; a ratchet value may never be cited as the basis for setting or repricing the M-S1 bar.**
- **D-S1.5 Honesty invariant, mechanically decidable (the anti-gaming arm).** Every `SupportLevel` promotion toward `Full`, and every loss kind removed from the ledger for a feature, requires a **committed round-trip differential fixture** — registered at A-1.5 — whose value-asserting tests pass against the converted output *and* whose conversion records zero ledger entries. Checked by the fixture runner in CI, not by a human reading a diff. Indeterminate case (fixture will not build) counts as **failing**, conservatively. v1's "accompanied by a semantic test" was satisfiable by a compiles-clean assertion written in the same commit by the same author.
- **D-S1.6 `§E`-inference gaps are IN SCOPE, and the resulting supply loss is a finding.** Fixing the `using`-body blind spot (§1 item 7) may retire the `EffectViolation` operator's differential and reduce M-S3. **Pre-committed disposition, recorded now while it costs nothing: the fix ships and the supply loss is published.** A converter blind spot is not an asset. Additionally, the operator's differential carries a regression test that fails loudly if fidelity work retires it, so the interaction is observed rather than discovered.

Exit criteria: M-S1 met on ≥2 of 3 original projects **or** PP-S1 resolved miss at D-S1.1a; marginal-recovery work-list committed with per-item disposition; D-S1.4/D-S1.5 green in CI.

### WS-S2 — Breadth (size: M)

Split in Draft v2, because the two halves have different risk profiles and different owners.

- **D-S2a — operator breadth** is executed in WS-S0.5 (harness-side, no `src/` changes, no adopter-facing risk). **The operator set freezes at A-1.5**, before any checker extension is written — see PP-S2.
- **D-S2.1 Plain reference-null dereference** (`src/`). The highest-yield checker gap (§1 item 4).
- **D-S2.2 Index-OOB over converted accessor forms** (`src/`), including native array access, which the checker currently cannot see.
- **D-S2.3 Checker-honesty guard.** Every extension ships a positive **and a negative** corpus: the check must fire on the defect and must **not** fire on the clean conversion. A checker broadened into a false-positive generator would inflate eligible-task counts while destroying the differential.
- **D-S2.4 Adopter-facing containment.** New diagnostics ship **off by default or as warnings** until a false-positive rate is measured on the full vendored corpus. WS-S0 commits to keeping the shipped adoption surface working; shipping new errors that fire on adopter code, in a release that already carries two versions' change, is not that (§7 risk 6).
- **D-S2.5 Addressability re-measurement** at the pinned configuration after each extension — **reporting addressable and clause-level exclusion counts only**; eligible counts are sealed until Call S (§6.1).

Exit criteria: M-S2 met against the frozen operator set; every extension carries its negative corpus and a measured FP rate; re-measurement committed per extension.

### WS-S3 — Corpus shape (size: S–M)

- **D-S3.1 Add a value-asserted subject,** SHA-pinned as a submodule per `5de804cc`. **Pre-registered selection criteria, fixed before candidates are measured:** permissive license; builds on the pinned SDK without source retargeting; a test suite asserting on **returned values** rather than interaction/shape; **and measured `NativeFraction` ≥ the D-W4.3 bar, or an explicit ledger-priced path to it.** v1 omitted the fidelity criterion, which would have let a subject satisfy every registered criterion and still contribute zero tasks through the fidelity gate.
- **D-S3.2 Selection honesty.** Pre-register a **ranked candidate list** and commit to taking the **first candidate that passes**; any departure is a recorded deviation that must publish the eligible-task counts of *every* candidate evaluated, including post-measurement rejections. Auditable is not the same as unbiased: measuring ten and taking the best is corpus-shopping even with a complete register.
- **D-S3.3 Blind authored value-assertions — priced, with a recorded decision.** `NoObservableDefect` is a hard wall: the held-out oracle is strictly the vendored project's existing tests. WS-S4 proposes to clear the analogous wall for *contracts* via blindness-by-construction; the identical discipline applied to *assertions* — pre-registered, defect-blind value assertions added **before** mutation selection — would attack `NoObservableDefect` today without finding and vendoring a new subject. It weakens "real OSS tests" as the oracle, which is a real cost. Accept or reject explicitly; v1's silence on the asymmetry was the finding.
- **D-S3.4 Per-project supply accounting.** Supply reported per project, never pooled.
- **D-S3.5** D-S1.1's ledger read extends to the new subject.

Exit criteria: ≥1 new subject vendored, building, and over the fidelity bar; selection register committed; D-S3.3 decision recorded.

### WS-S4 — Authored-contract overlay (size: M; parallel, off the critical path)

The D-W4.5 arm-ii channel, and per §0.1 **the only channel connected to the program's proof-depth headline**.

- **D-S4.1 Overlay mechanism** — deterministic, re-appliable, and **blind by construction** (derivable without reference to the injected defect, or the arm proves nothing).
- **D-S4.2 Feasibility record** — a written finding either way; a published negative retires arm-ii honestly.

Exit criteria: mechanism shipped **or** D-S4.2 negative published. Explicitly **not** a Call-S input (§6.2), by decision recorded in §0.1 — with the acknowledged cost that the headline claim waits.

### WS-S5 — Measurement and the call (size: S; no live agent spend)

- **D-S5.1 Supply measurement** at the pinned configuration: the full funnel per project, per stratum, with exclusion accounting, **plus a determinism screen** — gates §0.2's 5-consecutive-green rule applied to each eligible candidate's held-out test on both arms. The eligibility predicate decides on **one** run per arm, so unscreened "eligible" tasks may be ineligible for an epoch. Only screened tasks count toward M-S3.
- **D-S5.2 Annex A-1.5 registration — frozen in S1, not S5.** Registers M-S1/M-S2/M-S3/M-S4/M-S5, PP-S1–PP-S4, the **exact `TaskGenOptions`** (all fields, committed as JSON, hash-referenced), each project's `ExcludePatterns` hash, the **frozen mutation-operator set**, the D-S1.5 fixture registry, and PP-S3's exhaustive non-hit trigger list. Governing rule, quoted correctly this time (`agent-native-gates.md:81`): *"reduce category count or raise the threshold **before** freezing — never after."* Draft v1 paraphrased this as "registration happens before freezing — never after," a tautology that constrained nothing.
- **D-S5.3 Call S** (§6.2).
- **D-S5.4 Release gates.** Run the carried-forward PP-W5 parity epoch (own spend authorization) and clear PP-A1, then ship v0.12.0 covering the v0.11 range.
- **D-S5.5 PP-W5 additive annex note,** registered results-blind before the epoch: (a) the adjudicated question is now "v0.11+v0.12 toolchain vs v0.10.0 release", superseding the frozen row's v0.11-only Basis sentence, which is recorded stale rather than deleted; (b) the on-fail isolation ladder becomes **two-step** (`v0.10.0 + WS-W2-only`, then `v0.10.0 + all-v0.11`) so a tax can be attributed among batch / v0.11-other / v0.12; (c) the 1.25 margin and its null calibration were derived for a one-release delta and are **not** re-derived, so a fail is more likely and a pass correspondingly stronger. The A-1.4 exclusion-closure note is the precedent for doing this additively.

### WS-S0 — Adoption: maintenance-mode posture (size: S; standing)

PP-A2's pre-committed routing. Keep the shipped v0.11 adoption surface working, correct, and documented — bug fixes, honest docs, green gates. Fund **no new adoption depth** ahead of demand. Crossing Call 3 is what takes the adoption half off maintenance-mode; any growth here is a recorded deviation.

---

## 3. Sequencing

0. **Merge-order dependency (blocking).** PR #855 (close-out + Call W record + A-1.4 tranche 2) and PR #856 (expressible stratum) land **before or with** this plan. Until they do, this plan's §0/§1/§5 sources and its "Annex A at tranche 2" reference dangle, and the pre-registration claim is unverifiable from the repo — the annex preamble's own rule, and the exact finding PR #855 was corrected for.
1. **S1 (kickoff):** WS-S0.5 funnel probe → funnel table → WS-S1/S2/S3 boxes confirmed or re-scoped. **A-1.5 registration frozen here, at the end of S1** — after the probe (which measures *pass rates* and *loss inventory*, legitimate pre-freeze inputs) and **before any deliverable computes an eligible-task count**.
2. **S2:** WS-S1 execution, with D-S1.1a's go/no-go first and D-S1.4/D-S1.5 landing early so the ratchet and the honesty invariant guard the rest.
3. **S3 (parallel):** WS-S2 (checker half) and WS-S3.
4. **S4 (parallel, off critical path):** WS-S4.
5. **S5:** D-S5.1 supply measurement → **Call S** → release gates → v0.12.0.

**Two non-negotiable ordering constraints:** (a) A-1.5 freezes before any eligible-count measurement — Draft v1 put the freeze in S5 while D-S2.4/D-S3.3 measured eligible counts in S3, which set both bars after their values were visible; (b) the mutation-operator set freezes before checker extensions are written (PP-S2).

---

## 4. Success metrics (definitions)

| Metric | Definition | Bar |
|---|---|---|
| **M-S1** Native fraction | `ConversionCoverage.NativeFraction` per project (`RoundTripReport.cs:311`), **at the A-1.5-pinned `ExcludePatterns`** | **Provisional ≥ 0.70** on ≥2 of 3 original projects; frozen at A-1.5 (end of S1) |
| **M-S2** Addressable candidates | Candidates verification-addressable via the #856 differential, **arising from the A-1.5-frozen operator set**, at the pinned configuration | Frozen at A-1.5 from the WS-S0.5 funnel table |
| **M-S3** Eligible-task supply | **Expressible-stratum** tasks (addressability clause applicable) passing the full D-W4.1 predicate **and the D-S5.1 determinism screen**, per project, at the pinned `TaskGenOptions` | **≥ N total and ≥ 2 from each of ≥ 2 projects**; N frozen at A-1.5 from the power derivation below |
| **M-S4** Honesty invariant | Converter/harness changes that raise M-S3 while making the converted code **less faithful** — including `FeatureSupport` promotions and ledger removals without a green registered fixture, **and `§E`-inference behavior**, **and mutation-operator changes** | **0**, enforced in CI |
| **M-S5** Semantic-fidelity guard | Per-project `ArmsDiverge` rate on the mutation corpus | **Non-increasing**, ratcheted, as M-S1 rises |

**Why M-S5 exists.** Raising `NativeFraction` means natively converting exactly the code the emitter previously punted to interop — the marginal, hardest cases, which are the likeliest to diverge semantically. A successful M-S1 can therefore *increase* `ArmsDiverge` exclusions and *reduce* eligible supply. Without M-S5, the lead workstream's only metric is not monotone in the headline metric.

**M-S3's bar comes from required power, not realized supply.** Draft v1 said N would be "computed at A-1.5 from the S1/S3 measurements" — but those are mechanical build/test runs that produce **no variance estimate**, and setting a supply bar from realized supply is precisely the error §4 claims to avoid. The dry-run already tells us what is *needed*: detecting the registered 20–40% relative reduction against a ~16.7% base escape rate "needs on the order of hundreds of clustered tasks; $100s buys ~10–15" (`w4-dryrun-001/VERDICT.md`, finding 4). Even under the most generous assumption available — a deterministic build-block catching 100% while C# escapes at dry-run rates — only ~33% of tasks are discordant, so ~15 tasks are needed for a sign test at p ≈ 0.03 and realistically 25–40 for a cluster bootstrap. **v1's provisional 5 was 3–8× short under the most favorable assumption, and self-contradictory besides** (it claimed to support "an epoch of the dry-run's shape (6 tasks)" while being 5). A-1.5 must therefore state N from an explicit derivation under both effect-size assumptions, and §0's go/no-go below governs whether v0.12 is worth running at all.

**Caution on M-S1's provisional value.** 0.400 / 0.469 / 0.532 → 0.70 is a **32–75%** relative increase per project (v1 said 35–75%; 0.70/0.532 = +31.6%). The 0.70 steer predates any ledger read. D-S1.1 may reprice it in either direction; repricing it *from the ledger* before the A-1.5 freeze is legitimate, repricing it after seeing whether we hit it is not.

**The condition under which v0.12 is worth paying for** (v1 never stated one): if the A-1.5 power derivation puts required-N more than an order of magnitude beyond the corpus's plausible eligible ceiling as measured by WS-S0.5, then the real-scale venue is unreachable and **PP-S3 resolves miss at A-1.5** — take that branch immediately rather than funding S2–S5 to confirm it.

---

## 5. Proof points (go/no-go claims — review these)

| ID | Claim | Test | Outcome | If it fails |
|---|---|---|---|---|
| **PP-S1** | Converter fidelity is movable | M-S1 on ≥2 of 3 original projects; **adjudicable early at D-S1.1a** | Hit / miss | Fidelity is structural, not a work-list. **Diagnostic for v0.13 scoping** — it does not by itself retire the venue (see §6.2) |
| **PP-S2** | Checker breadth converts existing defects into addressable ones | M-S2 against the **A-1.5-frozen operator set**, frozen before the extensions are written | Hit / miss | Checker breadth is not the lever; WS-S4's feasibility finding becomes the main proof-depth hope — **a v0.13 input, not a Call-S input** |
| **PP-S3** | **The benchmark can be supplied** (the headline) | M-S3, at the pinned configuration, determinism-screened | Hit / miss / **underpowered** / **not adjudicated** | Miss → the program stops paying for the real-scale venue; the claim re-scopes to earliness/attribution/cost, which *is* evidenced |
| **PP-S4** | No converter-appeasement (**blocker**) | M-S4 = 0 via D-S1.5's registered fixtures; indeterminate counts as failing | Pass / fail | Blocks the release. Fidelity bought by reclassification is worse than no fidelity |

**PP-S2's anti-tautology guard.** Expressible-stratum candidates are authored to make a named check fire (every candidate carries an `ExpectedCheck`), so shipping a checker for shape X plus an operator producing shape X yields addressable candidates *by construction*. Freezing the operator set **before** the checker extensions are written is what makes PP-S2 failable: the extension must catch something the frozen operator set already produced.

**PP-S3's four values, with triggers enumerated in advance** — v1 claimed four and listed three, and gave "not adjudicated" no triggers at all, leaving an unbounded post-hoc escape from "miss":
- **Underpowered:** supply ≥ bar but below what the variance dry-run shows is needed for 80% power → epoch not authorized, supply work continues, **no venue retirement**.
- **Not adjudicated**, exhaustively: (i) the supply measurement cannot complete, or candidate enumeration is not reproducible across two runs at the pinned configuration; (ii) fewer than 2 projects pass the frozen fidelity bar; (iii) eligible tasks exist but fail the determinism screen, so no valid epoch is constructible.
- **Any route not on that list is adjudicated MISS**, with the novel route disclosed *and the miss registered anyway*. Task-supply starvation was a novel not-adjudicated route once (A-1.4 tranche 2, PP-W2); the default now reverses so it cannot be reused as an escape.

Carried forward as **release gates, not Call-S inputs**: **PP-W5** (frozen #839, restated additively at D-S5.5) and **PP-A1** (frozen list at `wedge-w1-prereqs.md` §3, unchanged). **PP-A2** is resolved ("demand unproven") and is not re-opened by this plan; only the Call 3 crossing reopens it.

---

## 6. Decision structure

### 6.1 Per-workstream gates

Exit criteria are checked at each workstream's close. A workstream that misses is re-boxed or descoped **with the deviation recorded**. **But where a workstream's exit criterion coincides with a registered proof point, the proof point is adjudicated on the metric value at the A-1.5-registered adjudication date, regardless of box status** — a re-box may move schedule; it never moves the adjudication date or the bar. (v1's WS-S1 exit criterion was textually identical to PP-S1's test, so "re-box" would have routed around the proof point before it could be adjudicated.)

**Blinding.** Eligible-task counts computed before Call S are written to a sealed artifact under the epoch directory and are not read until adjudication; pre-Call-S deliverables report addressable and clause-level exclusion counts only. At bus factor 1 this is a discipline, not an enforcement — it is recorded so a lapse is visible as a deviation.

### 6.2 Call S — the v0.12 → v0.13 gate

Adjudicated on **PP-S3**, with PP-S1/PP-S2 as diagnostic inputs and PP-S4 as a blocker. **WS-S4 is not an input** (§0.1).

| | **PP-S3 hit** | **PP-S3 underpowered** | **PP-S3 miss / not adjudicated** |
|---|---|---|---|
| **PP-S1 hit** | Authorize a **variance dry-run**, whose output powers the epoch | Supply work continues; venue retained | Venue retired (miss) or route published (n/a) |
| **PP-S1 miss** | **Authorize anyway** — supply arrived without fidelity; record that the fidelity diagnosis was wrong | Supply work continues on the non-fidelity levers | Venue retired; v0.13 leads with product |

v1 contradicted itself here: §5 said a PP-S1 miss "retires the venue as unreachable" while §6.2 made PP-S1 a mere diagnostic — and the joint state (PP-S1 miss, PP-S3 hit) is entirely reachable. The table resolves it: **supply, not fidelity, decides the venue.**

**Supply is a necessary condition for the epoch, never its power calculation.** A PP-S3 hit authorizes a variance dry-run; the dry-run powers the epoch. Gates §6 requires the upper confidence bound of the estimated variance, which a task count cannot supply.

In every branch the program continues; Call S decides *shape*. This is the Call 2 / Call G / Call W pattern.

### 6.3 Call 3 — reserved

Unchanged: the named-external-adopter crossing is a one-way door reserved for the maintainer. PP-A2's "demand unproven" parks it, it does not close it.

---

## 7. Risks

1. **Fidelity work is unbounded.** *Mitigation:* WS-S0.5 precedes scoping; D-S1.1a aborts early; PP-S1's miss is a pre-committed diagnostic.
2. **Checker broadening inflates supply with false positives.** *Mitigation:* D-S2.3's mandatory negative corpus; the differential structurally requires absence on the clean conversion.
3. **Corpus-shopping.** *Mitigation:* pre-registered ranked candidate list, first-passing wins, full evaluation register (D-S3.1/3.2).
4. **Fidelity bought by relabelling.** Highest severity — it corrupts the evidence base rather than wasting effort. *Mitigation:* PP-S4 as a blocker with D-S1.5's mechanically-decidable fixture arm, plus M-S4's widened population and the pinned `ExcludePatterns` denominator.
5. **The overlay quietly becomes the critical path.** *Mitigation:* excluded from Call S; D-S4.2 permits a published negative.
6. **v0.12's own work fails v0.12's release gate, or taxes adopters.** WS-S1 is aggressive emitter change and WS-S2 adds diagnostics — both land in PP-W5's treatment arm, whose frozen 1.25 margin was calibrated for one release of change, and both can fire on adopter code while the adoption half is unfunded. *Mitigation:* D-S5.5's two-step isolation ladder; D-S2.4's default-off/warning containment with a measured FP rate.
7. **Bus factor 1 / calendar cost.** The plan is cheap in agent spend and expensive in the resource that binds; v1 dropped both the milestone boxes and the bus-factor risk row that every predecessor plan carried. *Mitigation:* §2 sizes are the honest signal; WS-S0.5 exists precisely so the L is not spent on the wrong lever; re-box openly rather than compress silently.
8. **WS-S1 and WS-S2 can destroy each other's yield** (§1 item 7). *Mitigation:* D-S1.6's pre-committed disposition and its regression test, so the interaction is observed rather than discovered.
9. **v0.12.0 carries two releases' worth of change.** *Mitigation:* release notes and CHANGELOG cover the full v0.11+v0.12 range; D-S5.5 states PP-W5's arm.

---

## 8. Relationship to the parent strategy

v0.12 is **not** on the strategy's Phase-2a/2b progression — it is prerequisite engineering underneath it, entered by Call W's not-adjudicated branch. Per §0.1, what v0.12 buys is the ability to test an **enforcement**-level claim; the strategy's proof-depth headline stays untested and waits on WS-S4. The adoption half stays at maintenance-mode posture, so the 2a items shed by v0.11 — persisted semantic index, `calor query` — remain deferred, now for the third consecutive version.

---

## 9. Deferred item register

| Item | Disposition |
|---|---|
| Persisted semantic index + `calor query` (strategy 2a never-shed core) | Deferred again. **Flagged: third consecutive deferral — re-examine explicitly at v0.13 planning, do not roll forward silently** |
| Real-scale epoch re-run | Out of box by design; separate spend gate after Call S, gated on a variance dry-run |
| Logic-mutation stratum as a thesis channel | Retired as a mechanical-arm channel; retained only as a conversion-penalty measurement |
| Proof-depth measurement (arm-ii) | Rides on WS-S4; explicitly not adjudicated by v0.12 (§0.1) |
| Frames / `old()`; `Justified` tier; #807-adjacent verifier limits | Unchanged from the v0.10/v0.11 registers |

---

## 10. Revision log

- **Draft v1 (2026-08-04):** initial plan, written against the Call W record and the close-out finding.
- **Draft v2 (2026-08-04):** adversarial review round 1 applied — three independent reviewers (evidence 62%, strategy 52%, measurement discipline 45%). Maintainer decisions taken during the round: **insert the WS-S0.5 funnel probe and scope WS-S1 from it** (rather than committing an L to fidelity on n = 3), and **restate the objective honestly as enforcement-testing** while keeping WS-S4 off the critical path (§0.1). Principal dispositions:
  - **Evidence.** Corrected §1 item 3 — v1's "all 13 net-new entries are Partial/NotSupported" was **false** (2 are `Full`, one added by a commit v1 cited as the cause), "Full unchanged at 105" masked two demotions, and 8 of the 13 came from an uncited commit. Corrected "132 tests" → **14** (a ~10× inflation inherited from #856's PR body); "five checkers" → six, not all shape-keyed; 35–75% → **32–75%**; "roughly half excluded" → 47–60% of the *convertible* surface; the `NoObservableDefect`/`ArmsDiverge` collapse; the ledger's optional locations and kind-only aggregation (so D-S1.1 is not "nearly free"). Audit rule restated to mark unmerged anchors, and the merge-order dependency promoted into §3.
  - **Measurement.** Moved the A-1.5 freeze from S5 to **end of S1**, before any eligible-count measurement (v1 set both bars after their values were visible). Pinned `TaskGenOptions`, `ExcludePatterns`, and the operator set. Gave PP-S2 a frozen-operator-set bar (v1 had hit/miss against a metric §4 declined to threshold). Enumerated PP-S3's non-hit triggers with **miss as the default** for unlisted routes, and added the fourth value (underpowered) v1 claimed but omitted. Rebuilt PP-S4's instrument around registered round-trip fixtures. Added M-S5 (`ArmsDiverge` ratchet) and the determinism screen. Corrected the misquoted freeze rule. Added the §6.2 joint decision table and the §6.1 re-box carve-out.
  - **Strategy.** Added WS-S0.5 (funnel probe, operator breadth, committed baseline zero, region-granularity decision); D-S1.1's marginal-recovery ranking and D-S1.1a early abort; D-S1.6 (`§E`-inference in scope, supply loss published); D-S3.3 (blind authored assertions, priced); D-S2.4 (adopter containment); risks 6–8; and §4's explicit "when is v0.12 worth paying for" condition. Restored the bus-factor risk and the honest framing of what "cheap" means.
  - **Not adopted:** promoting WS-S4 to a Call-S input (maintainer decision — restate rather than expand scope); re-ordering to corpus-first outright (WS-S0.5 measures the question instead of guessing it in either direction).
