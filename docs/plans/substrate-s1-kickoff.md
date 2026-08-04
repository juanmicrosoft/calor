# v0.12 S1 Kickoff — WS-S0.5 Funnel Probe

**Status:** Kickoff. Decisions below are **priced and recorded before the probe runs** — that is the point of the document.
**Parent:** [`substrate-plan-v0.12.md`](substrate-plan-v0.12.md) (Draft v2.1). Governing gates: [`agent-native-gates.md`](agent-native-gates.md), Annex A at A-1.4 tranche 2.
**Created:** 2026-08-04
**Milestone:** S1 — the plan's §3 step 1. Ends with the A-1.5 registration frozen.

---

## 0. Why S1 exists at all

v0.11's measurement half died of task-supply starvation, and the close-out named three candidate causes. **None of them has been measured against the others.** The maintainer's decision at the v0.12 review round was to stop guessing: WS-S0.5 raises *n* from 3 to ~30 and reports where in the funnel candidates actually die, and WS-S1's L-sized fidelity box is scoped from that table rather than from a prior.

The funnel the harness actually implements:

```
candidates  →  clause (a) pass  →  clause (b) pass  →  addressable  →  [eligible: SEALED]
               (native region)     (observable +        (Calor check
                                    arms agree)          introduced)
```

At n = 3 the entire observed mortality was in clause (b). At n = 3 that is also consistent with a 25% true pass rate (0/3 happens ~42% of the time). S1 is the cheapest possible resolution of that ambiguity.

---

## 1. Priced decisions

### D-1 — Fidelity gate is DISABLED for the probe (`--native-bar 0`). Probe-only.

**This is the decision that makes the probe possible at all, and it must not be misread.**

`FidelityGate.Evaluate` excludes any project below the bar, and an excluded project **"contributes no tasks"** (`FidelityGate.cs:70–79`). The corpus is at 0.469 / 0.400 / 0.532 against a provisional bar of 0.70 — so **all three projects are excluded at the default bar**, and a probe run there would return an all-zero funnel that re-measures the fidelity gate instead of measuring the funnel.

- **Decision:** run the probe with `--native-bar 0`.
- **What this is:** a diagnostic setting that lets clause (a) and clause (b) rates be observed at all.
- **What this is NOT:** a relaxation of the adjudication bar. M-S1 stays provisional 0.70 and freezes at A-1.5 (§4). No task generated under `--native-bar 0` is eligible for an epoch, and the probe generates no bundles — it produces a table.
- **Why it is recorded here:** relaxing a bar mid-investigation is exactly what the close-out refused to do with `RequireIdenticalSignature`. The distinction is that this bar is being disabled for a *measurement that is not an adjudication*, and it is registered before the numbers exist rather than after.

### D-2 — `--stratum expressible`, not `both`

The candidate cap applies to the **merged** cross-stratum list in lexicographic order (`TaskGenerator.cs`), so at `--stratum both` logic candidates consume slots and the expressible *n* stays small — the confound the close-out's re-review surfaced. The thesis-testing stratum is the expressible one (§0.1: v0.12 supplies enforcement-testing tasks), and the logic stratum is retired as a thesis channel (plan §9).

- **Decision:** `--stratum expressible --source injected`.
- **Consequence accepted:** the probe says nothing about logic-stratum supply. That is intended.

### D-3 — Candidate cap 10/project, no early stop

- **Decision:** `--max-candidates 10 --target 0`. Three projects → **n ≈ 30**, the plan's stated target.
- `--target 0` disables the early stop, which would otherwise truncate enumeration at 3 eligible and bias the funnel.
- **Cost:** each candidate costs a conversion + build + held-out run + visible-suite check + attribution build/run on a real library. Budget the probe as an **unattended multi-hour run**, not a minutes-long one. If the realized per-candidate cost makes n = 30 impractical, the cap is reduced and **the reduction is reported with the funnel** — a silently truncated denominator is the failure mode this whole exercise exists to avoid.

### D-4 — `NegateCondition` stays disabled (plan-level disposition)

Enumerated but filtered from the default set (`TaskGenerator.cs:54–56`) because it inverts a whole branch (all-or-nothing coverage) rather than making a point mutation. The code carried a rationale; the plan owed a disposition.

- **Decision:** stays **disabled** for the probe and for v0.12 supply. Rationale accepted as written: a whole-branch inversion is a different defect class, and its coverage profile would make clause-(b) outcomes incomparable with the point-mutation operators.
- Revisit only if D-S0.5.2's operator breadth work leaves supply short.

### D-5 — Region-granularity clause (a): **DECISION DEFERRED to a written accept/reject before the probe's second run, not taken implicitly here**

D-S0.5.4 requires an explicit accept/reject "recorded before the numbers are seen." Recording it honestly means recording that it is **not yet decided**, rather than letting the first probe run silently constitute the decision.

- **Probe run 1 uses file-granularity clause (a) — today's behavior, unchanged.** It therefore measures the funnel *as it currently exists*, which is the baseline the plan's zero is stated against.
- Whether to add site-level clause (a) is decided **after** run 1's clause-(a) mortality is known, and the decision is written down with its rationale before any run 2. If clause (a) turns out to kill almost nothing, the change is unnecessary and the decision costs nothing; if it kills a lot, the change is worth pricing properly.
- **This is a deviation from D-S0.5.4's "decided before numbers" wording**, taken deliberately and recorded: the alternative is a decision made with no information at all, which is not obviously better than one made with clause-(a) mortality in hand and stated in advance as the trigger.

### D-6 — MediatR submodule must be initialized

`git submodule status` shows `bench/corpus/MediatR` uninitialized locally. It is one of the three pinned subjects; running the probe on two would silently change the denominator.

- **Decision:** initialize before the run; if it cannot be initialized, the probe reports **two** projects and says so.

---

## 2. The pinned configuration (probe run 1)

Recorded here so the run is reproducible and so successor measurements compare like with like — the omission the close-out's re-review flagged.

```
calor-roundtrip gen-tasks MediatR Serilog FluentValidation \
  --stratum expressible \
  --source injected \
  --max-candidates 10 \
  --target 0 \
  --native-bar 0 \
  --projects-dir bench/corpus \
  --output <epoch-dir>
```

Corpus pins: MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.

## 3. What S1 emits

1. **The funnel table** — per project, per stage, through `addressable`. Openly reported.
2. **The sealed eligible count** — written to the epoch directory, not read until A-1.5 is frozen (plan D-S0.5.1's split-reporting rule, erratum v2.1).
3. **D-S0.5.3's baseline zero** — the full `ExclusionAccounting` dispositions as a versioned artifact, replacing the uncommitted investigation log the close-out had to disclose.
4. **Box confirmation or re-scope** for WS-S1 / WS-S2 / WS-S3, from the table.
5. **The A-1.5 registration**, frozen at the end of S1.

## 4. Exit criteria

- Funnel table committed with the pinned configuration and any realized cap reduction disclosed.
- Baseline zero committed as a versioned artifact.
- D-5 (region-granularity) decided in writing.
- WS-S1/S2/S3 boxes confirmed or re-scoped.
- A-1.5 frozen — after which the sealed eligible count may be read.

## 5. Deviations recorded

| Deviation | From | Why |
|---|---|---|
| Region-granularity decision deferred to post-run-1 | D-S0.5.4 ("recorded before the numbers are seen") | A decision with zero information is not better than one with clause-(a) mortality in hand; the trigger is stated in advance (D-5) |
| Fidelity gate disabled for the probe | The provisional 0.70 bar | At the real bar the funnel is all zeros and unmeasurable; the bar itself is unchanged for adjudication (D-1) |
