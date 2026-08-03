# WS-W4 D-W4.4 Dry-Run — Spend Authorisation

**Status: ACTIVE — runner merged (PR #853 → `5691ce54`, adversarially reviewed
MERGE-CLEAN: oracle provably hidden from the agent, verdicts un-gameable).
§2 calibration to be filled by the 3 priced trials before the full 30-run
dry-run.** This is the separate, numbered spend authorisation the
wedge plan requires for the D-W4.4 feasibility dry-run — explicitly NOT covered
by the W5 epoch authorisation (wedge plan §2 D-W4.4, review m6). It follows the
`phase-2-spend-authorisation.md` discipline (calibrate-before-committing,
envelope, abort triggers, recorded authorisation).

## §1 — What this authorises

Real Anthropic API spend, through the operator's Claude Code CLI, to run the
**D-W4.4 feasibility dry-run** of the WS-W4 real-scale benchmark:

- **Substrate:** injected-mutation task bundles from the Slice-C generator
  (`gen-tasks --source injected`), the only viable substrate on the pinned
  corpus (real-defect revert tasks yield 0 — see the real-corpus measurement).
- **Design:** **≥5 eligible tasks across ≥2 projects** (Serilog +
  FluentValidation; MediatR yields 0 injected tasks), **2 arms** — `csharp`
  (idiomatic C# + compiler + visible tests) vs `calor` (round-tripped
  Calor-mechanical: enforcement + `calor import` annotations, **no authored
  §Q/§S** per the D-W4.5 provisional protocol (i), because the authored-contract
  overlay mechanism does not exist) — **3 runs/arm** (odd, no tie rule). Floor =
  5 × 2 × 3 = **30 runs**.
- **Purpose (feasibility, NOT adjudication):** measure realized variance,
  cost/run, and the achievable MDE; and run the **D-W4.4 ceiling-recurrence
  check** — the C#-arm escaped-bug incidence. Pre-committed branch (wedge §2
  D-W4.4): incidence ≈ 0 → PP-W2 is **not frozen and not adjudicated**, and the
  finding is published as **"the ceiling persists at real scale."**
- **Runner:** the bundle dry-run runner (`bench/phase0-agent-native/
  run-bundle-epoch.sh --real`), null-agent-verified and adversarially reviewed
  (PR #853) — oracle physically hidden from the agent (two-copy strip), cost
  summed from `total_cost_usd`.

The spend is irreversible. Anthropic charges per call regardless of whether the
dry-run produces interpretable results.

**What this does NOT authorise:** the real-scale epoch (PP-W2) or the parity
epoch (PP-W5) — those carry their own W5 authorisations. This is the dry-run
only.

## §2 — Per-run calibration (run BEFORE committing to the full 30-run dry-run)

**PENDING** — filled after PR #853 merges. Procedure: run **3 real-money trials**
spanning the difficulty range (one cheap bundle, one medium, one expensive —
by visible-suite size / project) at `--runs 1`, both arms, and record observed
per-run cost from summed `total_cost_usd`, tokens, and iterations.

| Trial (bundle · arm) | $ cost | output tokens | iterations |
|---|---:|---:|---:|
| FluentValidation · csharp | TBD | TBD | TBD |
| Serilog · csharp | TBD | TBD | TBD |
| Serilog · calor | TBD | TBD | TBD |
| **Median per-run** | **TBD** | — | — |
| **Projected 30-run total** | **TBD** | — | — |

If the projected total exceeds the §3 hard ceiling, reduce the run grid
(disclosed) or stop and re-plan — never silently overspend.

## §3 — Budget envelope

- **Per-run estimate (pre-calibration):** $0.88 (authorable-fixture floor,
  `guarantees-probe-001`) is a lower bound; real-scale runs carry OSS-repo
  context + full-suite builds per iteration, so the working estimate is the
  real-scale range **$4–18/run** (kickoff §5 illustrative multipliers).
- **Projected 30-run dry-run:** ~$130–540.
- **Soft target:** $600. **Hard ceiling:** **$1,500** (the frozen per-epoch
  ceiling; the dry-run must stay under it — wedge §7 risk 2). Calibration (§2)
  replaces the estimate with a measured projection before the full run.

## §4 — Abort triggers

Stop the dry-run and re-plan (do not push through) if:
1. Calibration (§2) projects the 30-run total **over the $1,500 hard ceiling**.
2. Invalid-run rate (rate-limit/crash, per §0.2 detection) exceeds ~⅓ after
   retries — the measurement would be too degraded to price feasibility.
3. The runner emits **incoherent verdicts** (e.g. a null-agent-style
   inconsistency, an oracle-leak symptom, or caught/escaped that don't match
   the held-out state) — a correctness failure, not a spend question.
4. Realized spend reaches the **soft target ($600)** with fewer than the floor
   runs complete — re-price before continuing toward the hard ceiling.

## §5 — Authorisation record (bus factor 1, §7 convention)

The wedge plan carves the dry-run spend out from the epoch approval and requires
it be authorised separately. The operator surfaced the decision explicitly with
the cost estimate and the weaker-substrate context (injected-mutations-only,
~50% converter fidelity, real-defect yield 0); the maintainer chose **"Run the
dry-run now"** (2026-08-03), an informed authorisation of dry-run spend under the
$1,500 ceiling. This is recorded here as the authorisation basis. At bus factor
1 this is self-asserted, disclosed per the main document's §7 convention.

**Model:** the session model at run time (record the exact id in the epoch
`pins.json`). **Calibration commit:** recorded in §2 when filled.

## §6 — Post-dry-run

The dry-run's realized variance, cost, and ceiling-recurrence result feed the
**A-1.4 tranche-2 freeze** (fidelity bar, PP-W2 threshold + restated wording,
annotation protocol, ceiling-recurrence floor, N, underpowered criterion) —
results-blind before any epoch task selection, per the split clock. The dry-run
may move the threshold / task count / corpus before that freeze — never after.
