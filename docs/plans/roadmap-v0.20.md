# 0.20 planning tranche: M0 only

**Updated:** 2026-09-09, after the critique of epic #1278.
**Status:** planning revision; no study execution, recruitment commitment,
prototype, paid collection, or resource increase authorized.

The owner requested backlog creation and then application of the epic critique
on 2026-09-09. That resumes **planning edits only**, not the paused experiment.
No independent human methodological sign-off or committed research adopter is
evidenced in the reviewed records. Maintainer triage is assigned to
[@juanmicrosoft](https://github.com/juanmicrosoft); an assignment is not consent
or independent approval.

## What is active

Only **#1279-#1283 (M0)** remain active in [epic #1278](https://github.com/juanmicrosoft/calor/issues/1278).
**#1284-#1309 are parked, closed as not planned pending M0**, not implemented.
The native sub-issue relationship is the task-status source of truth; this
document does not maintain a second checkbox list.

The full [Draft v3 reference design](roadmap-v0.20-reference-draft-v3.md) is
preserved verbatim, not approved for execution. Its conditional M1-M6 design
is not an active delivery schedule. The [review disposition](epic-1278-review-disposition.md)
states what was accepted, qualified, or not claimed.

The proposed adoption claim and its change authority live in the epic's
[Authority and stop semantics](https://github.com/juanmicrosoft/calor/issues/1278#authority-and-stop-semantics).
The original safety margin has not changed. There is no intention or permission
here to widen it merely to obtain an affordable result.

## Current evidence, not an assumed success

- v0.18 and v0.19.0 have shipped. The [v0.19 audit disposition](v0.19-audit-disposition.md)
  lists all 31 child resolutions and retained limitations. Closed issues are
  not, by themselves, the discriminating proof artifacts a future study needs.
- Call W recorded **DEMAND UNPROVEN**, no secured adopter, and Call 3 closed
  on 2026-08-04. The reviewed record does not establish the recruitment activity
  **since** Call W. That interval remains **unknown** until evidence is supplied,
  not an invented failed search across nine releases.
- The prior PP-W-rows practice result was zero escapes on both arms in 28 valid
  runs, followed by a fixture redesign. The redesign is not a positive outcome.
  #1254 records its actual prerequisites and retired spending assumptions.
- There is no evidenced affordable, powered route for the original three-arm
  claim, no accepted new budget, no agreed external methods reviewer, and no
  supported prior that Calor can halve protected C#'s total cost.

This is enough to **park downstream work**. It is not a countersigned M0 result,
a proof that every possible paired design is infeasible, or evidence of no value.

## Preliminary arithmetic: one-page triage, not an authorized study

These checks reproduce the formulas in the critique and reference design.
The inputs are illustrations, not observed adopter supply, event rates, review
time, or cost variation. No agent trial or source-code soundness sweep was run
to produce this note.

| Illustration | Result at branch level (one-sided 97.5%) |
|---|---|
| Zero-event single-rate bound `1 - 0.025**(1/n)` at n=40 / n=80 | 8.81% / 4.51% |
| Independent zero-event observations for margins 1 / 2 / 5 / 10 points | 368 / 183 / 72 / 36 |
| Unpaired equal 3% rates, zero difference, 1-point margin, 80% power | about 4,568 per arm |
| Paired completion illustration, 5-point margin, discordance 2 / 5 / 10% | about 63 / 157 / 314 independent pairs |
| Cost illustration, n=40, paired log-cost SD 0.5 / 0.8 | true ratio about 0.401 / 0.351 to pass an upper bound of 0.50 with 80% power |

Reproduce with Python's standard `math` module:

```python
import math
z = 1.959963984540054 + 0.8416212335729143
for margin in (0.01, 0.02, 0.05, 0.10):
    print(math.ceil(math.log(0.025) / math.log(1 - margin)))
for n in (40, 80):
    print(1 - 0.025 ** (1 / n))
for discordance in (0.02, 0.05, 0.10):
    print(math.ceil(z * z * discordance / 0.05**2))
for sd in (0.5, 0.8):
    print(0.5 * math.exp(-z * sd / math.sqrt(40)))
print(z * z * 2 * 0.03 * 0.97 / 0.01**2)
```

At an assumed 20 review minutes per slot, 40/80 **available timed-review hours**
buy 40/80 three-arm triplets before pilot reservations. The proposed 80-hour
total also has other duties. Longer reviews reduce capacity further. H=50
historical requests would permit at most 66 equal-weight clusters across both
pools under the 75% rule, but **H is unmeasured**.

Within the displayed zero-event illustration, even n=80 cannot meet the original
1-point gate. The 368 figure is not being promoted into a universal lower bound
for every paired/clustered risk-difference estimator. Likewise, review time,
discordance, and log-cost variation have not been measured. Formal joint sizing
must include accepted denominators, clustering, both positive branches, and
the **least favorable allowed setup/rate inputs**, not a central cost band.

**Triage disposition:** no justified route to implementation. Formal M0
classification remains unadjudicated pending the missing information and
methods approval. Preserve INSUFFICIENT INFORMATION if an authorized inquiry
expires unresolved. Do not automatically substitute a 5-10-point risk margin.

## Minimum M0 work and tangible outputs

The former two-person-day envelope was not a demonstrated estimate for the
31-ticket specification. Do not promise a full source audit, recruitment,
statistical-method development, and countersignature in that time.

A **half-day desk triage is a proposed cap, not approved spend or a commitment**.
#1280 must first obtain an actual owner-approved cap, named methods reviewer
and review allowance, and a decision/inquiry deadline. Work that cannot fit
returns missing information or a scoped stop; it does not grow silently.

| Issue | Bounded deliverable | Concrete artifact |
|---|---|---|
| #1279 | Reconcile predecessor facts and locate supporting evidence | `docs/plans/v0.19-audit-disposition.md`; proposed `docs/plans/safe-delegation-m0/predecessor-status.md` |
| #1280 | Record authorization or refusal, external-review vacancy, cap, and deadline | proposed `docs/plans/safe-delegation-m0/authorization.md` |
| #1281 | Explain plausible cost mechanisms for BOTH C/B and B/A; inspect reuse entry points and reconcile a bounded resource worksheet | proposed `docs/plans/safe-delegation-m0/mechanism-and-resources.md` |
| #1282 | Distinguish measured inventory, unknown supply, and documented recruitment activity | proposed `docs/plans/safe-delegation-m0/supply-status.md` |
| #1283 | Produce the one-page four-way disposition with assumptions, least-favorable costs, missing information, and sign-offs or their absence | proposed `docs/plans/safe-delegation-m0/decision.md` |

The proposed files do not exist merely because they are named here. Do not
invent an adopter, signatory, rate band, model availability window, or deadline.
An owner can decline to fund further inquiry; record that administrative stop
without fabricating a scientific NOT FEASIBLE verdict.

The cost-mechanism paragraph must confront countervailing costs: Calor's
translation, onboarding, and model unfamiliarity versus any saved review or
repair; and B's setup/enforcement costs versus saved review in A. If A already
achieves 90% correct acceptance, improving that denominator to 100% alone gives
only a 1/0.9 factor. That 90% is a scenario, not a measured baseline. No
mechanism or magnitude is presumed established.

For a cheap reuse screen, start at `harness-capture.py`'s `heldout-final` parser
and the held-out invocation/capture path in `run-pair.sh`, under
`bench/phase0-agent-native/`. These are **candidate legacy entry points**, not
an identified isolated evaluator approved for this study. Inspect exact paths
and boundary behavior; stop with a gap rather than auditing all harness code
or building a replacement service under a desk estimate.

## Correctness work has a separate home

[Issue #1311](https://github.com/juanmicrosoft/calor/issues/1311) scopes an
adopter-independent false-established search on released v0.19.0. It names
the code-derived construct matrix, an independent property oracle, negative
controls, existing tests, and `bench/correctness/false-established/v019/`
as a proposed evidence destination. It has no M1 prerequisite.

A counterexample is valuable evidence; zero findings within a bounded search
are not whole-compiler soundness or an agent-productivity result.

## Sequence and identifiers

**0.20 and 0.21 are planning/release-target containers, not execution dates or
investment approvals.** The next actual software version remains governed by
normal shipping criteria; a stopped investigation consumes no version.

The independent proposed study identity is **`sd-001`**, not a software version.
Only if separately authorized would new experiment artifacts use
`bench/safe-delegation/sd-001/`. No registered epoch has been created or renamed.
The old `v0.20` namespace remains only in the archived proposal.

New funding/activation of the PP-W-rows proposal #1254 is held until #1283
records the M0 disposition and the maintainer explicitly states whether a
separate narrower proposal is justified. #1259 still requires its own bounded
spend/null-result approval. A milestone number cannot overrule the investment
decision or authorize a run. Its frozen scientific protocol is unchanged.

## If downstream design is ever reopened

Require fresh explicit scoping and authorization, not automatic reopening
when M0 closes. In the parked graph, adopter approvals depend on an adopter;
the executable analysis/estimators and undefined-ratio rules precede the pilot;
only preregistered nuisance estimates and the prescribed sample-size calculation
may update afterward. Stop rules do not depend on building the runner.
Decision/report/exit tasks require the M0 disposition, not an unrun M5.

Any return to collection must record actual dates and the pinned model's
availability/deprecation window. Existing independence, safety, power,
authority, cost, privacy, and stop requirements are not waived.
