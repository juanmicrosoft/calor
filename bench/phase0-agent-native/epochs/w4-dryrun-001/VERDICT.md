# w4-dryrun-001 — WS-W4 D-W4.4 Feasibility Dry-Run

**Date:** 2026-08-03 · **Model (arms):** `sonnet` · **Runner:** `run-bundle-epoch.sh`
@ `d5910343` (post set-e fix) · **Spend:** $69.99 (dry-run) + ~$4.37 (calibration)
= **~$74**, under the $600 soft target and the $1,500 ceiling.
**Runs:** 36/36 valid (6 injected-mutation bundles × 2 arms × 3 runs), 0 invalid.

This is a **feasibility probe**, not an adjudication (wedge plan §2 D-W4.4). Its
job: price the epoch, measure variance, and run the ceiling-recurrence check.

## Arms (D-W4.5 provisional protocol i — mechanical-only)
`csharp` = idiomatic C# + compiler + visible tests. `calor` = machine-converted
Calor working copy + WS-W2 effect enforcement + `calor import` annotations, **no
authored §Q/§S** (the authored-contract overlay does not exist). Both arms get
the identical scrubbed failing-behaviour report and the identical visible/held-out
split; the held-out oracle is physically hidden from the agent (PR #853).

## Results

| Task (injected logic mutation) | C# caught | Calor caught | |
|---|:--:|:--:|---|
| FluentValidation cand1 (off-by-one) | 2/3 | 3/3 | Calor better |
| FluentValidation cand2 (off-by-one) | 1/3 | 2/3 | Calor better |
| FluentValidation cand3 (boundary) | 3/3 | 3/3 | tie |
| Serilog cand10 (arithmetic) | 3/3 | 3/3 | tie |
| Serilog cand11 (off-by-one) | 3/3 | 0/3 | C# much better |
| Serilog cand8 (arithmetic) | 3/3 | 1/3 | C# better |

**Task-level: 2 Calor-better, 2 C#-better, 2 tie.** Run-level: C# 15/18 caught,
Calor 12/18. All escapes are GENUINE (finalBuildOk=true, visible suite green,
held-out fails — the agent shipped the defect at declared-done, not a build
artifact). Calor arm cost ~2× the C# arm (converted §-syntax, the pre-registered
presentation asymmetry / bias against Calor).

## Findings

1. **Ceiling-recurrence (D-W4.4): the ceiling does NOT persist at real scale.**
   C#-arm escaped **3/18 runs across 2/6 tasks (16.7%)** — decisively above the
   pre-registered "≈0 → ceiling persists" branch. Unlike the v0.10 authored
   fixtures (C# 9/9), at real scale the C# arm DOES ship bugs. Real-scale has
   measurable escaped-bug headroom → the venue is testable.

2. **The mechanical-only arm has no verification signal for LOGIC bugs.** Every
   injected mutation is arithmetic/off-by-one/boundary — a defect class neither
   the type system, nor effect enforcement, nor a mechanical (non-authored)
   contract can catch. On this task class the Calor arm's only differentiator is
   the conversion penalty. So this configuration measures the conversion penalty
   + enforcement value, **not** the verification-depth thesis.

3. **The "tie" point estimate is confounded by per-project fidelity.** Both
   Calor-wins are FluentValidation; both Calor-losses are Serilog (which converts
   worse). The D-W4.3 fidelity gate + converter-attribution rule — built exactly
   to strip this — were NOT applied in the dry-run. The point estimate is not
   trustworthy without them.

4. **Power: the logic-bug outcome-headline is undetectable at authorized spend.**
   Detecting the registered 20–40% relative reduction against a ~16.7% base
   escape rate needs on the order of hundreds of clustered tasks; $100s buys
   ~10–15 tasks.

## Disposition (post-adversarial synthesis; maintainer-approved 2026-08-03)

Run a **re-scoped near-term epoch**, do NOT adjudicate the logic-bug outcome
headline with it, build the contract channel in parallel:
- **Add an expressible-defect stratum** — effect-discipline violations,
  null-derefs, index-OOB, div-by-zero — defect classes the mechanical arm's
  EXISTING machinery (WS-W2 enforcement, NullDereferenceChecker, bug-pattern
  checkers) can catch, giving it a real non-authored signal (potentially a large,
  detectable effect: a deterministic Calor build-block vs no C#-arm signal).
- **Keep the logic-bug stratum** as the honest conversion-penalty / day-one
  product (PP-A2) measurement, reported with CIs.
- **Apply the fidelity gate + converter-attribution** (de-confound per-project).
- **Restate PP-W2** (D-G3.1 restate-or-demote): adjudicate ceiling-recurrence,
  product-truth, and the fidelity gate at power; honestly demote the logic-bug
  outcome-advantage rather than pretend the mechanical arm can carry it.
- **De-serialize**: start the authored-contract overlay + converter-fidelity work
  concurrently for a proper v0.12 PP-W2 epoch.

Raw per-run `result.json`s, `epoch-summary.json`, and `driver.log` preserved in
this directory. Workspaces were ephemeral (oracle isolation) and are not retained.

---

> **SUPERSEDED IN PART (2026-08-04) — see
> [`docs/plans/wedge-real-scale-closeout.md`](../../../../docs/plans/wedge-real-scale-closeout.md).**
> The disposition above was maintainer-approved on 2026-08-03 and its
> "add an expressible-defect stratum" item was carried out (PR #856). The stratum
> then yielded **0 eligible tasks**, which removed the precondition for the other
> two items: **"run a re-scoped near-term epoch" and "restate PP-W2" no longer
> apply** — with no eligible task there is no epoch, and with no epoch there is no
> adjudication to restate from. PP-W2 is registered **not adjudicated**
> (gates Annex A-1.4 tranche 2). This block is a forward pointer only; nothing
> above it has been edited.
