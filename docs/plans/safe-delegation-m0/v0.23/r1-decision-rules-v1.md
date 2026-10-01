# M0 decision rules, version 1 (R1, #1372)

**Rules version:** `r1-decision-rules/v1`. **Recorded:** 2026-10-01.
**Issue:** #1372, parent epic #1370. **Governance:**
[r1-methods-governance.md](r1-methods-governance.md).
**Machine-readable mirror:** [r1-decision-rules-v1.json](r1-decision-rules-v1.json).
**Frozen calculator:** [r1-safety-nmin.py](r1-safety-nmin.py) (standard-library
Python). This Markdown file is normative (section 10).

**Status label for any output produced under these rules:** *AI-adjudicated,
public-proxy domain.* These rules are reviewed by cross-family AI adversarial
review, not by an independent human methods reviewer. Any classification they
produce addresses public .NET open-source work only. It is not evidence about
organizational adoption, an adopter's demand, or production use.

This file freezes the decision rules **before** any decision-bearing inspection
of task supply (section 9). It does not authorize task execution, participant
enrollment, paid collection, an acceptance service, or any of #1284-#1309. It
changes no threshold of the original M0 claim.

## 1. Source claim being operationalized

The claim is the original M0 adoption claim from the
[#1278 authority section](https://github.com/juanmicrosoft/calor/issues/1278#authority-and-stop-semantics)
and the archived
[Draft v3](../../roadmap-v0.20-reference-draft-v3.md), sections 5.1, 6.1-6.3,
and 8, as carried forward by [decision.md](../decision.md). Arms:

| Arm | Definition |
|---|---|
| A | Ordinary C# |
| B | Strong protected C# |
| C | Protected Calor |

The claim is not re-derived, narrowed, or made easier here. Where the
public-proxy domain cannot measure a component of the claim, these rules mark
that component **NOT MEASURABLE IN DOMAIN** and record it as missing
evidence, which can never count as passed (section 7.3 states the resulting
outcomes: UNADJUDICATED under amendment 001 as written, or at most
INSUFFICIENT INFORMATION or NOT FEASIBLE). No component is dropped.

## 2. Definitions

| Term | Definition (frozen) |
|---|---|
| Public-proxy domain | Task supply drawn from the history of public .NET open-source repositories, per the maintainer decision of 2026-10-01 recorded in `amendment-001-public-proxy.md` (R0). No adopter organization exists. |
| Sampling frame | The candidate frame of public .NET repositories, window, draw, and eligibility rules frozen in section 6.1-6.3 (domain cells added by R3) **before** R2B′ inspects any task data. Every supply figure and every NOT FEASIBLE statement is scoped to that frame. |
| Source cluster | One underlying change request: the issue and every linked pull request/commit implementing the same change. Variants, reopened duplicates, backports, and repeated runs of the same change are one cluster. Under Draft v3 section 5.1, a cluster is the independent unit: repetitions and variants do not increase the independent-task count. |
| Historical cluster | A source cluster replayed from the frame's recorded history. |
| Authored cluster | An independently authored request (not a variant of a historical cluster). Draft v3 caps authored clusters at 25% of primary task weight in each pool. |
| Repository cluster | All source clusters from one upstream repository or fork family. |
| Assigned slot | One cluster, one arm, one preregistered repetition, with one fixed total budget (Draft v3 section 5.2). |
| Accepted | The arm's normal review workflow approves the artifact within the slot budget. |
| Correctly accepted | Accepted **and** passes the independent held-out behavioral requirements and protected-boundary checks. |
| Serious escaped defect | An accepted artifact violates a preclassified critical requirement in the independent evaluation; at most one event per slot. A serious escape therefore implies accepted and not correctly accepted. |
| Decision-bearing inspection | Any query, download, count, sample, or reading of public repository issue/PR/commit content for the purpose of eligibility, counting, rate estimation, or sizing. Reading this repository's own documents and issues is not decision-bearing. |

## 3. Frozen gate table (the claim)

All bounds are one-sided at 97.5% (alpha = 0.025 per positive branch; section 4).
"Domain status" states whether the public-proxy domain can measure the
component **as originally defined**. The subject arm is C in branch L and B in
branch W (section 4.1).

| ID | Gate component | Metric and unit | Threshold and direction | Original measurement | Domain status |
|---|---|---|---|---|---|
| G0 | Workload horizon | Assigned relevant change requests the adopter expects within 12 months, count | >= 50 (business case fails before pilot otherwise) | Adopter confirmation (Draft v3 section 6.1) | **NOT MEASURABLE IN DOMAIN**: no adopter; public history is not an adopter's forward workload |
| G1.1 | Cost, subject vs A | Ratio of total cost per correctly accepted change, `(setup/50 + mean_t cost_t) / mean_t success_t`, in the primary accounting currency | Upper bound <= 0.50 | Adopter setup amortized over 50 assigned requests; run, AI, human review, repair, escalation, and replacement costs at approved role rates; least-favorable approved setup/rate combination | **NOT MEASURABLE IN DOMAIN**: no adopter setup, no independent human review/repair time, no approved role rates |
| G1.2 | Cost, C vs B (L only) | Same | Upper bound <= 0.50 | Same | **NOT MEASURABLE IN DOMAIN** |
| G2.1 | Completion level, subject | Task-weighted correctly accepted fraction, proportion | Lower bound >= 0.80 | Normal arm review by trained non-maintainer reviewers; independent non-implementing evaluator's held-out suite | **PROXY ONLY**: acceptance by AI reviewers; oracle derived from public repository tests |
| G2.2 | Completion difference, subject - A | Difference in correctly accepted fraction, percentage points | Lower bound >= -5 pp | Paired by cluster | **PROXY ONLY** |
| G2.3 | Completion difference, C - B (L only) | Same | Lower bound >= -5 pp | Paired by cluster | **PROXY ONLY** |
| G3.1 | Safety, subject - A, all-assigned | Difference in task-weighted serious-escape rate over all assigned slots, pp | Upper bound <= +1 pp | Adopter-preclassified business-critical requirements; independent evaluation | **PROXY ONLY**: criticality rubric frozen by R3 for public code, not adopter-classified |
| G3.2 | Safety, C - B, all-assigned (L only) | Same | Upper bound <= +1 pp | Same | **PROXY ONLY** |
| G3.3 | Safety, subject - A, accepted | `mean_t escape_t / mean_t accepted_t` difference, pp | Upper bound <= +1 pp; undefined if an arm accepts nothing, and an undefined comparison fails | Same | **PROXY ONLY** |
| G3.4 | Safety, C - B, accepted (L only) | Same | Same | Same | **PROXY ONLY** |
| G4 | Trust boundary | Confirmed false-established properties plus accepted unauthorized boundary bypasses, in the frozen mechanism suite and confirmatory runs, count | Must equal 0 (zero tolerance, not statistically sized) | Frozen mechanism suite, bypass probes, isolated acceptance boundary | **MEASURABLE IN PRINCIPLE**; isolated acceptance boundary not established ([mechanism-and-resources.md](../mechanism-and-resources.md)) |
| G5 | Usable adoption | Binary: an independent adopter completes install, integration, one change, with all effort and lost guarantees disclosed | Must be true | Independent adopter organization | **NOT MEASURABLE IN DOMAIN**: an AI agent or the maintainer is not an independent adopter |
| G6.1 | Handoff: export equivalence | Binary: a runnable C# export passes the behavioral comparison against the approved requirements; lost guarantees and remaining runtime checks documented | Must be true | Behavioral comparison on the exported C# | **MEASURABLE IN PRINCIPLE** (mechanical) |
| G6.2 | Handoff: independent receipt | Binary: a non-maintainer recipient accepts the handoff within the registered scope | Must be true | Independent adopter | **NOT MEASURABLE IN DOMAIN** |

Branch L requires every row for subject C, including G1.2, G2.3, G3.2, G3.4.
Branch W requires G0, G1.1, G2.1, G2.2, G3.1, G3.3, G4, G5, G6 for subject B,
and additionally that branch L does not pass (Draft v3 section 8).

The safety margin stays at **+1 percentage point**. Larger margins
{2, 5, 10} pp may be shown only as labeled tradeoff illustrations. They cannot
change a classification. Affordability is not a risk rationale.

## 4. Error control, multiplicity, and simulation

### 4.1 Future-study error control (what R4 must represent)

1. Two positive branches: **L** (LANGUAGE EARNS CONTINUATION) and **W**
   (WORKFLOW VALUE, LANGUAGE NOT JUSTIFIED). Each receives alpha = 0.025.
2. Within a branch, every statistical component (G1-G3) must pass with a valid
   one-sided 97.5% bound. The branch is an intersection-union test, so no
   further within-branch multiplicity correction is applied.
3. The union bound limits any false positive branch claim to 0.05 overall.
   This is not simultaneous coverage of every quantity.
4. Precedence of published study outcomes: INVALID, then L, then W, then
   TARGET NOT MET. Precedence is not error control.
5. Descriptive 95% intervals may be published but cannot adjudicate a gate.
6. Secondary metrics (review minutes, iterations, tokens, prevented
   violations) cannot create a positive route.

### 4.2 Role of simulation under v1

Under v1, simulation results are **descriptive**. They cannot produce
`SIZED_FEASIBLE` or `SIZED_INFEASIBLE` for any component (section 7), for two
reasons: FEASIBLE AS PROPOSED and REQUIRES SEPARATE APPROVAL are unreachable in
the public-proxy domain (section 7.3), and NOT FEASIBLE under v1 uses only the
method-independent analytic bound in section 7.4, which needs no simulation,
nuisance ranges, or estimator validation. R4 may still build and publish the
simulation required by #1376; when it does, it must meet the requirements below
so a later rules version can rely on it.

| Requirement | Frozen content |
|---|---|
| Arm-level parameterization | Every scenario is specified by arm-level quantities (per-arm acceptance, correct-acceptance, and serious-escape probabilities; per-arm cost distributions), from which all contrasts are derived. Contrasts are never specified independently. Infeasible combinations (for example escape > accepted - correct) are rejected, not repaired. |
| Design structure | Paired by source cluster; repository-cluster effect; reviewer effect; repeated slots within cluster; accepted and all-assigned denominators; replacement executions charged to the original slot (at most two per paired slot, infrastructure-only, symmetric across arms) |
| Validity over dependence | Component bounds must be valid under arbitrary within-cluster dependence, because the cluster is the independent unit |
| Undefined results | Zero correct acceptances gives infinite cost, and the cost component fails in that draw. Zero acceptances makes accepted-denominator safety undefined, and that comparison fails. Undefined draws are failures, never dropped. |
| Sparse events | No method that yields a zero-width safety interval when no events occur |
| Component validity | For every component, one-sided coverage at every registered boundary configuration: the Monte Carlo upper 99.5% bound on the rejection rate must be <= 0.025 (not "0.025 plus Monte Carlo error") |
| Branch and family validity | Same rule for each branch (<= 0.025) and for any false positive branch (<= 0.05) at the global null, every mixed null, and branch nulls in which every other component passes with probability >= 0.99 |
| Search and Monte Carlo error budget | R4 registers, before running, the finite list of K evaluations (scenarios x sample sizes x nuisance points) and uses Bonferroni-adjusted Monte Carlo bounds with total Monte Carlo error 0.01 across the list. No rerun with a new seed to resolve an indeterminate result. |
| Power | Reported per branch as the probability of the correct branch outcome under the complete ordered procedure, never as a product or minimum of component powers |
| Reproducibility | Pinned code commit, runtime version, lockfile hash, seeds, input and output hashes; a second run reproduces outputs within a declared tolerance |

### 4.3 Registered design alternatives

These are **design alternatives**, not predictions. No evidence supports any
of them ([mechanism-and-resources.md](../mechanism-and-resources.md)). Each is
stated at arm level. `a` is the per-slot acceptance probability, `k` the
correct-acceptance probability, `e` the all-assigned serious-escape
probability, and `m` the mean cost per assigned slot (with `m_A = 1`).

| Scenario | Arm A | Arm B | Arm C | Derived contrasts |
|---|---|---|---|---|
| L global null (all L components at boundary) | k=0.85, e=x | k=0.85, e=x, m=1 | k=0.80, e=x+0.01, cost per correct = 0.50 of A and of B | C/A = C/B = 0.50; C-A = C-B = -5 pp; C level 0.80; safety +1 pp |
| W global null | k=0.85, e=x | k=0.80, e=x+0.01, cost per correct = 0.50 of A | equal to B | B/A = 0.50; B-A = -5 pp; B level 0.80; safety +1 pp; C/B = 1 (L false) |
| L design, least favorable | k=0.90, e=y | k=0.90, e=y | k=0.90, e=y, cost per correct = 0.40 of A and of B | C/A = C/B = 0.40; differences 0; safety 0 |
| L design, most favorable | k=0.90, e=y+0.01 | k=0.90, e=y+0.01 | k=0.95, e=y, cost per correct = 0.35 of A and of B | C/A = C/B = 0.35; +5 pp; safety -1 pp |
| W design, least favorable | k=0.90, e=y | k=0.90, e=y, cost per correct = 0.40 of A | equal to B | B/A = 0.40; C/B = 1 |
| W design, most favorable | k=0.90, e=y+0.01 | k=0.95, e=y, cost per correct = 0.35 of A | equal to B | B/A = 0.35; +5 pp; safety -1 pp; C/B = 1 |

`x` and `y` are nuisance base rates, constrained by `e <= a - k` per arm.
Acceptance is registered as `a = 1` for every arm in these scenarios, so the
all-assigned and accepted safety denominators coincide. A separate
accepted-denominator boundary scenario uses `a = 0.95` in every arm with
`e_subject = e_comparator + 0.0095`, which puts the accepted-denominator
difference at exactly +1 pp. Cost distributions, dependence structures, and
the values of `x` and `y` are registered by R4 before running; because
simulation is descriptive under v1 (section 4.2), these choices cannot affect a
classification.

## 5. Positive-branch logic for the M0 classification

A three-arm study must be able to resolve **both** branches. A design powered
only for branch L is insufficient (Draft v3 section 6.3). Therefore:

- **NOT FEASIBLE** follows when a necessary condition of **either** branch is
  shown unattainable by the method-independent bound in section 7.4.
- **FEASIBLE AS PROPOSED** would require branch-level power >= 0.80 for both
  branches under the least-favorable registered alternatives. Under v1 it is
  unreachable in the public-proxy domain (section 7.3).

## 6. Source selection, eligibility, supply bounds, and missingness

### 6.1 Source selection (frozen by R1, per amendment 001)

**Candidate frame.** A repository is in the frame if, at the R2B′ extraction
time `T`, it:

1. is a public GitHub repository that is not a fork, archived, a mirror, or a
   template;
2. has GitHub-reported primary language `C#`;
3. carries a license whose SPDX identifier is in the set R2A′ (#1373) permits;
4. has at least one pull request merged in the window `[T - 365 days, T)`;
   `T` is no later than 2026-10-29;
5. is not on the prior-familiarity list (section 6.3).

**Enumeration.** R2B′ enumerates the frame with the GitHub search API, using
date-sliced queries so that no query exceeds the API's result cap. The
extraction specification (queries, slices, timestamps) is committed and hashed
before it runs. If the enumeration cannot be completed or its completeness
cannot be checked against the API's reported totals, the frame size is
uncertified and `H_enum_max` is unbounded (section 6.4).

**Inventory draw.** R2B′ orders the enumerated frame by
`SHA-256(salt || "/" || owner/name)` with `salt` = the lowercase hex merge
commit SHA of the PR that freezes these rules, and inspects the first 30
repositories in that order (all of them if fewer). Stars, activity, size, and
familiarity with the code play no role. The draw estimates eligibility rates
descriptively. It does **not** define supply: the supply bound in section 6.4
covers the whole frame, with uninspected items counted as unresolved.

### 6.2 Eligibility rules R2B′ applies

A source cluster is confirmed eligible only if every criterion is confirmed:

| ID | Criterion | Mechanical test |
|---|---|---|
| E1 | Merged change in the window | A pull request with `merged_at` in `[T - 365 days, T)`; linked issues (closing keywords) and reverts/backports that cite it join its cluster |
| E2 | Production C# change | Changes at least one `.cs` file outside test paths, with a non-whitespace diff; test paths match the case-insensitive regex `(^|/)(test|tests|[^/]*\.tests?)(/|$)` |
| E3 | Oracle ascertainable | **Confirmed true** if the change modifies at least one `.cs` file in a test path. E3 is **never confirmed false** under v1: absence of changed or existing test code does not show that no oracle could be authored (requirements, documentation, or an independently written oracle may exist), so every other case is **unresolved** and stays in `H_high`. This test does not require the historical change to have modified tests; an independently authored oracle may still be needed, which is R3's and the future study's concern |
| E4 | Human-authored | Author account type is `User` (not `Bot`); evaluated from account type, never from names |
| E5 | Not generated or formatting-only | At least one changed non-test `.cs` file is not under `obj/`, `bin/`, or a path containing `Generated`, and has a change outside whitespace |
| E6 | Inside the R3 domain cells | The R3 (#1374) domain-cell rule, which R3 must freeze before R2B′ inspects data |

R3 may adopt a stricter oracle rule only as a disclosed domain restriction
under E6, never by redefining E3. A criterion that is confirmed false is a
rule-based exclusion with a recorded reason. A criterion that cannot be evaluated (missing data, API error,
ambiguous linkage, undecidable domain cell) leaves the cluster **unresolved**;
it is never counted as ineligible or as eligible.

### 6.3 Prior familiarity

The prior-familiarity list is fixed at the R1 merge commit as: the
`bench/corpus/` submodules (MediatR, Serilog, FluentValidation); every GitHub
repository whose URL appears in this repository's `bench/`, `benchmarks/`,
`tests/`, `docs/`, or `.gitmodules` at that commit; and every repository named in the
pre-extraction familiarity declarations below.

**Declarations.** Before R2B′ runs its extraction, the R2B′ record must contain
two dated declarations listing every public repository known to have been
studied for Calor development, including exposure that left no committed URL
(for example, a repository read during an agent or chat session): one by the
maintainer and one by the proposer agent of record for R2B′. The proposer's
declaration is limited by the agent's lack of cross-session memory; it must
state which sources were searched (this repository's history, the session
logs available to it, and the maintainer's answers) and that it may be
incomplete. The declaration cutoff is the extraction start time. A repository
first recognized as familiar after the cutoff is moved to the familiar stratum
in a disclosed correction; its counts remain in `H_high`. For the record, the
R1 proposer session that drafted these rules studied no public repository
other than this one. R2B′ publishes
the list. Listed repositories are excluded from the frame, the inventory draw,
and any future task pool. Their item counts are reported as a separate
stratum and **are added to `H_high`** (section 6.4), because removing them
must not manufacture scarcity. The proposer (Claude) and the reviewer (Codex)
have unmeasurable pretraining exposure to popular public .NET repositories;
this cannot be excluded and is disclosed in every R5 output.

### 6.4 Supply bounds (from R2B′, over the whole frozen frame)

R2B′ reports, for the whole frozen frame:

| Quantity | Definition |
|---|---|
| `H_conf` | Historical clusters confirmed eligible under the frozen R3 rules, with complete provenance |
| `H_unres` | Enumerated items whose eligibility is missing or indeterminate (including extraction failures for enumerated items) |
| `H_enum_max` | A certified upper bound on the number of items in the frame that could form clusters, taken from a complete enumeration (for example a platform-reported total count for the window) that the frozen extraction specification names in advance. If no certified enumeration exists for any repository in the frame, `H_enum_max` is **unbounded**. |
| `H_fam` | Merged pull requests in the window in prior-familiarity repositories (section 6.3); unbounded if not certified |
| `H_high` | `min(H_conf + H_unres + (H_enum_max - enumerated items), H_enum_max) + H_fam`, that is, every item that is not confirmed ineligible for a recorded, rule-based reason is counted as potentially eligible; unbounded if `H_enum_max` or `H_fam` is unbounded |
| `F_high` | `floor(H_high / 0.75)`: the largest final pool permitted when the pilot reservation is zero and authored clusters fill the maximum 25% weight. Authored supply is neither invented beyond that cap nor set to zero. |

Clusters excluded for a recorded rule-based cause are excluded from all bounds.
Missing eligibility is never counted as ineligible (that would fabricate
scarcity). Historical and forward supply are reported separately; forward
supply cannot substitute for G0.

### 6.5 Missingness, invalid runs, and invalid epochs

| Situation | Frozen treatment |
|---|---|
| Gate NOT MEASURABLE IN DOMAIN | Recorded as missing for that gate; never treated as passed, failed, zero, or "not applicable" |
| Unbounded or uncertified `H_enum_max` | The supply route to NOT FEASIBLE is disabled |
| R2B extraction run or R4 run with a pin/hash mismatch, a frozen-rule violation, an input registered after outputs were seen, or an unrecorded configuration change | **Invalid run.** Retained in the manifest with reason; not used. One rerun from the identical pre-declared specification is permitted. The first valid run binds; choosing among valid runs is prohibited. |
| Upstream gate EXPIRED, REVOKED, or INVALIDATED (#1370 lifecycle rules) | Every dependent output is invalid from that time; the classification is UNADJUDICATED |
| Future study (modeled by R4): failed, crashed, refused, exhausted, or unreviewed slot | Failed slot carrying its full cost; never removed from any denominator |
| Future study: held-out correctness or severity evaluation missing for an **accepted** artifact | Acceptance and cost records are kept. For the bound on each component, the missing outcome is imputed adversarially to the claim: in the subject arm, not correct and serious escape; in the comparator arm, correct and no escape. If such imputations exceed 5% of accepted slots in any arm, the epoch is INVALID. |
| Future study: protocol or trust defect | Epoch INVALID; affected evidence identified; no favorable old runs retained (Draft v3 section 6.4) |

## 7. Classification rules

### 7.1 Component states

R4 reports exactly one state per **(branch, component)** pair:

| State | Applies to | Meaning |
|---|---|---|
| `SIZED_FEASIBLE` | G1-G3 | Not available under v1 (section 4.2) |
| `SIZED_INFEASIBLE` | G3.1 (both branches) | Section 7.4: the admissibility certificate is recorded and `F_high < N_MIN` |
| `NOT_MEASURABLE_IN_DOMAIN` | any | Section 3 domain status |
| `PROXY_ONLY` | G2, G3 | Measurable only by proxy, and not `SIZED_INFEASIBLE` |
| `ROUTE_BLOCKED` | G4, G6.1 | Section 7.6 assignment step 1 |
| `ROUTE_UNRESOLVED` | G4, G6.1 | Section 7.6 assignment steps 2 and 5 |
| `ROUTE_MISSING` | G4, G6.1 | Section 7.6 assignment step 3 |
| `ROUTE_ESTABLISHED` | G4, G6.1 | Section 7.6 assignment step 4 |

### 7.2 Ordered classification procedure

Apply in order; the first matching rule decides.

1. **UNADJUDICATED** (process status, not a classification) if: R1 is not
   `MET`; any upstream gate (R0, R2A′, R3, R2B′, R4) is not `MET` or has been
   `INVALIDATED`; or the R5 countersignature (section 7.5) is absent. R5
   publishes the dated reason. R5 may state which rule the evidence *would*
   match, explicitly labeled "not a formal classification".
2. **NOT FEASIBLE** if G3.1 is `SIZED_INFEASIBLE` in branch L or branch W.
   Infeasibility of one necessary component establishes infeasibility of the
   conjunctive branch, and a three-arm study must resolve both branches
   (section 5).
3. **FEASIBLE AS PROPOSED** if every (branch, component) is `SIZED_FEASIBLE`
   or `ROUTE_ESTABLISHED`, G0, G5, and G6.2 are evidenced true, all named
   roles in Draft v3 section 7.1 exist, and an approved envelope covers the
   least-favorable requirement. Unreachable under v1 in this domain.
4. **REQUIRES SEPARATE APPROVAL** if rule 3 would hold under a **specified**
   change in resources, risk, or scope, where every change names its
   quantity, owner, and cost; every input to the changed sizing is evidenced;
   no gate is dropped; and any risk change carries the separate business-risk
   rationale, adopter acceptance, independent methods review, and owner
   approval required by #1278. Unreachable under v1 in this domain.
5. **INSUFFICIENT INFORMATION** otherwise. R5 must name the smallest
   information request that could change the classification, its owner, and a
   deadline, and must not convert missing information into zero demand,
   success, or universal infeasibility.

The rules are mutually exclusive by construction: rule 2 requires a
`SIZED_INFEASIBLE` state, which rules 3 and 4 exclude.

### 7.3 Pre-registered reachability in the public-proxy domain

**Definitions.** A component is **missing evidence** when the frozen
procedure ran and recorded it as `NOT_MEASURABLE_IN_DOMAIN`, `PROXY_ONLY`,
`ROUTE_MISSING`, or `ROUTE_UNRESOLVED`. R4 has **failed to complete the
assessment** when any frozen procedure did not run or produced an invalid run
(section 6.5) for a reason other than the domain itself.

**Under amendment 001 as written.** Amendment 001 section 5 makes R4
`UNAVAILABLE` when "required inputs are missing", and makes R5's
classification require R4 `MET`. G0, G1.1, G1.2, G5, and G6.2 are
`NOT_MEASURABLE_IN_DOMAIN` by construction, so their inputs are always missing
in this domain. Under amendment 001 as written, R4 is therefore expected to
close `UNAVAILABLE`, rule 1 of section 7.2 applies, and the expected v1 result
is process status **UNADJUDICATED**, with no formal classification. These
rules do not override that criterion.

**Only if a governing amendment changes R4's closure criterion.** If the
maintainer adopts a versioned R0 amendment that lets R4 be `MET` after a
complete assessment in which specified components are recorded as missing
evidence (as defined above), and R4 has not failed to complete the
assessment, then rules 2-5 apply and the reachable results are:

- **NOT FEASIBLE (AI-adjudicated, public-proxy domain; frame-scoped)** through
  rule 2;
- **INSUFFICIENT INFORMATION (AI-adjudicated, public-proxy domain)** through
  rule 5.

Rules 3 and 4 cannot be satisfied in this domain under v1 in either case,
because G0, G1.1, G1.2, G5, and G6.2 are `NOT_MEASURABLE_IN_DOMAIN`. Reaching
a positive classification requires a later rules version that adds an evidence
source able to measure them as originally defined.

**Expectation recorded before inspection.** Without such an amendment:
UNADJUDICATED. With it: most likely INSUFFICIENT INFORMATION, because the
supply bound covers the whole frame (section 6.4) and needs the R3 certificate
(section 7.4); NOT FEASIBLE is reachable only if the frame is small and fully
enumerated. These expectations are recorded so they cannot be presented later
as findings. Whether to adopt the R4 amendment is a maintainer decision; it is
not made here.

### 7.4 Method-independent necessary cluster count (supply route)

**Setting.** Source clusters are independent and identically distributed draws
of a complete cluster observation `X` (all arms, all slots, all outcomes, with
arbitrary within-cluster and cross-arm dependence). The safety component G3.1
of either branch concerns `D = mean over the cluster's slots of (subject
escape) - mean of (comparator escape)`, with `-1 <= D <= 1`, and the null
`E[D] >= +0.01`. The test is level 0.025 (branch alpha) and must be valid over
every admissible joint distribution, because the cluster is the independent
unit (Draft v3 section 5.1; section 4.2).

**Bound.** Let `P1` be any cluster distribution with `E[D] = -0.01` (the
registered most-favorable alternative, section 4.3). Let `Q` be a cluster
distribution with `D = +1` almost surely (every subject slot is a serious
escape and no comparator slot is; this is coherent because a serious escape
only requires an accepted, incorrect artifact). Define
`P0 = (1 - eps) P1 + eps Q` with `eps = 0.02 / 1.01`, so
`E_P0[D] = (1 - eps)(-0.01) + eps = +0.01`: `P0` is on the null boundary and
the branch claim is false under it. For n clusters, the product measure
satisfies `P0^n >= (1 - eps)^n P1^n` on every event, so for any test `phi`,
`E_P1[phi] <= E_P0[phi] / (1 - eps)^n <= 0.025 / (1 - eps)^n`. Power 0.80 at
`P1` therefore requires `(1 - eps)^n <= 0.025 / 0.80`, that is

`N_MIN = ceil( ln(0.025 / 0.80) / ln(1 - 0.02/1.01) ) = 174`.

The bound holds uniformly over every `P1` with mean difference -1 pp, every
base rate, every within-cluster dependence, every repetition count, and every
ascertainment method. It needs no grid, no simulation, and no nuisance range.
The frozen calculator [r1-safety-nmin.py](r1-safety-nmin.py) evaluates this
closed form. (A round-2 reviewer proposed this argument after showing that the
earlier marginal Neyman-Pearson argument was invalid for paired outcomes.)

**Admissibility certificate.** The bound uses the null point `P0`, which
mixes in `Q`. It applies only if `Q` is admissible under the frozen design:
every eligible cluster type can, under the design's model, have a serious
escape in every subject slot and none in any comparator slot. A design that
uses known structural constraints in inference (for example a stratum whose
escape probability is fixed at zero or at a known value, or known stratum
proportions used to bound escape rates) can make `Q` inadmissible, and then a
valid test may need fewer clusters. R3 must therefore record, before R2B′
inspects data, either (a) a certificate that the confirmatory analysis uses no
such constraint (the model for each cluster's escape outcomes is
unrestricted, apart from the coherence `e <= a - k` that `Q` satisfies), or
(b) the constraints it does use. Under (b), or if no certificate is recorded,
the supply route is **disabled** and G3.1 cannot be `SIZED_INFEASIBLE`.

**Rule:** G3.1 is `SIZED_INFEASIBLE` (in both branches) if and only if the
R3 certificate (a) is recorded, `F_high` is finite, and `F_high < N_MIN`, that
is, `H_high <= 130` (`floor(130 / 0.75) = 173`; `floor(131 / 0.75) = 174`).

**Scope.** The bound does not depend on how acceptance or escapes are
ascertained, on reviewer type, or on cost. It holds for the original endpoint.
What it scopes is the **supply**: it shows that a study drawing its final pool
from the frozen frame cannot reach 80% power for the +1 pp safety gate under
the registered most-favorable alternative, for any valid test. It does not show
that an adopter-domain study could not. Pilot reservations are taken as zero
and authored clusters at their 25% cap, both favorable to feasibility. A larger
true safety advantage than -1 pp would lower `N_MIN`; -1 pp is the registered
most-favorable alternative, fixed before inspection, and cannot be changed
after inspection (section 10).

### 7.5 Countersignature of a classification

A classification is formal only when R5 records: the rules version and file
hashes; the R2B/R4 output hashes; a Codex adversarial round under
[r1-methods-governance.md](r1-methods-governance.md) stating "no blocking
objections" against the R5 artifact SHA; and the maintainer's merge. The
maintainer action (STOP, DEFER, ALLOW A SEPARATE AUTHORIZATION REQUEST) is a
separate field.

### 7.6 Non-statistical route checklists and state assignment

| Gate | Checklist items |
|---|---|
| G4 | (a) frozen mechanism suite with negative controls covering the supported construct/property matrix; (b) isolated acceptance boundary with access separation (not the legacy `run-pair.sh` seam); (c) bypass probes for every protected requirement; (d) **recorded results** of (a)-(c) showing zero confirmed false-established properties and zero accepted unauthorized bypasses, and no open unexcluded false-established finding in the supported matrix (#1311) |
| G6.1 | (a) export pipeline producing runnable C# for every supported construct; (b) behavioral comparison against the approved requirements with **recorded passing results** for every supported construct; (c) documented lost static guarantees and remaining runtime checks |

An item is **absent** when no artifact for it exists. It is **unresolved**
when an artifact exists but any of these holds: it does not cover the whole
supported matrix; any result is invalid, indeterminate, unmeasurable, or not
yet validity-checked; a finding's confirmation is pending; or any reviewer
(Codex, Copilot, or the maintainer) has recorded an unresolved objection. The
status depends on the evidence, not on whether a reviewer has noticed it.
Assign exactly one state by the first matching step:

1. `ROUTE_BLOCKED` if any **confirmed, validity-checked** result violates the
   gate metric (a confirmed false-established property, an accepted
   unauthorized bypass, or a failed behavioral comparison from a
   validity-checked instrument), whatever the state of other items. A recorded
   violation that is invalid, indeterminate, disputed, not validity-checked, or
   pending confirmation does not match this step; it makes its item
   unresolved (step 2).
2. `ROUTE_UNRESOLVED` if any item is unresolved.
3. `ROUTE_MISSING` if any item is absent.
4. `ROUTE_ESTABLISHED` if every item is present, complete, validity-checked,
   undisputed, and passing against the gate metric.
5. Otherwise `ROUTE_UNRESOLVED`. This step makes the assignment exhaustive.

### 7.7 Interpretation limits

- NOT FEASIBLE means: a three-arm study drawing its final pool from the frozen
  public-proxy frame cannot reach 80% power for the +1 pp safety gate under the
  most-favorable registered alternative, for any valid test. It does not
  establish infeasibility for an adopter-domain study, for a different frame,
  universal impossibility, or absence of value.
- INSUFFICIENT INFORMATION is a stopped inquiry, not evidence of no demand or
  infeasibility.
- Whatever the v1 result, the original-domain M0 status remains
  **UNADJUDICATED** (amendment 001, section 2); R5 states this in its closeout.
- No classification activates implementation, recruitment, spending, #1254,
  #1259, or #1284-#1309.

## 8. Public record and evidence manifest

### 8.1 Redacted public record

All decision inputs in the public-proxy domain are public repository history,
so there is no restricted evidence tier under v1. "Redacted" here means only
what [r0-authorization.md](r0-authorization.md) section 7 already requires,
which these rules adopt without change:

- no contributor usernames, display names, or emails in any committed
  artifact; emails are never extracted;
- no per-item identifier (PR number, issue number, commit SHA) next to an
  eligibility decision, exclusion, or cluster assignment; provenance is the
  repository name, one pinned repository revision, the window, the pinned
  extractor and rules version, and a SHA-256 digest of the full per-item
  decision list, which is never committed and is deleted with local caches;
- author clustering, if needed, uses in-memory ordinal pseudonyms only;
- no published statistic over fewer than 5 distinct contributors;
- no copied upstream source beyond what its license permits.

If any non-public evidence becomes necessary, work stops; R0 permits no
restricted access.

### 8.2 Evidence manifest format

Each R2B′/R4/R5 output ships an `evidence-manifest.jsonl` with one JSON object
per **extraction or computation run** (never per item):

| Field | Type | Meaning |
|---|---|---|
| `id` | string | Stable evidence ID, `ev-<gate>-<nnn>` |
| `gate` | string | Producing gate (`R2B'`, `R4`, `R5`) and consuming rule component (for example `G3.1`) |
| `rules_version` | string | `r1-decision-rules/v1` |
| `repositories` | array | Repository names with one pinned revision each, or `frame` for frame-level enumeration |
| `window` | string | ISO 8601 interval |
| `extraction_spec_sha256` | string | Hash of the frozen query/filter specification |
| `tool` | string | Extractor/computation code commit and runtime version |
| `extracted_at` | string | ISO 8601 UTC timestamp |
| `per_item_list_sha256` | string | Digest of the uncommitted per-item decision list (R0 section 7.1) |
| `aggregate_path` | string | Path of the committed aggregate in this repository |
| `aggregate_sha256` | string | Hash of that aggregate |
| `access_class` | string | `public` (only value permitted) |
| `licenses` | array | SPDX identifier per repository |
| `status` | string | `valid`, `invalid`, or `superseded` |
| `status_reason` | string | Required unless `valid` |
| `inspected_before_freeze` | boolean | Must be `false` for every decision-bearing run |

## 9. Freeze record

- **Decision-bearing inspection before freeze:** none. In drafting these
  rules, the proposer read only this repository's documents and the issues
  #1278, #1283, and #1370-#1377. No public .NET repository issue, PR, or commit
  data was queried, counted, or sampled. The adversarial reviewer is
  instructed not to inspect such data (governance file, section 5).
- **Everything that decides a v1 classification is fixed here:** the
  thresholds, `N_MIN` and its calculator, the supply-bound formulas, the
  reachability statement, and the ordered procedure. The candidate frame, draw,
  eligibility rules, and prior-familiarity rule are frozen in sections
  6.1-6.3. What remains for R3 is the domain-cell rule (E6), the criticality
  rubric, and the admissibility certificate, which R3 must freeze before R2B′
  inspects data; nothing in v1 lets a range, envelope, frame, or alternative be
  chosen after inspection.
- **Freeze event:** v1 is frozen at the merge commit of the PR that adds this
  file, provided the final adversarial round countersigns the same file
  content (`sha256` recorded in
  [r1/countersignature.md](r1/countersignature.md)). R2B
  inventory inspection and R4 sizing may not start before that merge. If the
  merged content differs from the countersigned content, v1 is not frozen and
  R1 is not `MET`.

## 10. Amendment control

1. Any change after freeze creates a new file `r1-decision-rules-v<N>.md`
   (and JSON mirror and calculator, if changed); v1 is never edited in place
   except for typo fixes that change no rule, threshold, scenario, state, or
   procedure, listed in the next version's changelog.
2. Every new version records: the diff against the prior version; the
   rationale; what decision-bearing evidence (if any) had been inspected when
   the amendment was proposed; and a disposition of each affected evidence item
   (`unaffected`, `must be re-derived`, or `invalidated`).
3. Every new version passes the same adversarial protocol (at most 5 rounds)
   and a maintainer merge.
4. **Never permitted:** changing the thresholds 0.50, 0.80, -5 pp, +1 pp, or
   alpha 0.025 per branch; adding a positive route; dropping a gate or
   component; reclassifying a NOT MEASURABLE component as measurable without a
   new evidence source; changing a registered design alternative after
   decision-bearing inspection. A different claim requires the separate
   business-risk process in #1278 and is a new claim, not an amendment.
5. **After decision-bearing inspection has begun**, an amendment is permitted
   only to correct a contradiction, an undefined case, or an error. R5 must
   then compute the classification under both the prior and new versions. If
   they differ, the classification is INSUFFICIENT INFORMATION, and both
   results and the amendment are disclosed.
6. If this Markdown and the JSON mirror or calculator disagree, the Markdown
   governs and the discrepancy is an amendment-control event.

## 11. Obligations passed to downstream gates

| Gate | Obligation from these rules |
|---|---|
| R3 (#1374) | Freeze the domain-cell rule (E6), the criticality rubric, the proxy acceptance/oracle definitions, and the section 7.4 admissibility certificate or its constraints, all without inspecting task data |
| R2B′ (#1375) | Apply sections 6.1-6.3; produce `H_conf`, `H_unres`, `H_enum_max`, `H_fam`, `H_high`, `F_high`, the prior-familiarity list, repository-cluster counts, exclusions with reasons, and the evidence manifest under section 8 |
| R4 (#1376) | Report one section 7.1 state per (branch, component); run the frozen calculator; any simulation follows section 4.2 and is descriptive under v1 |
| R5 (#1377) | Apply section 7.2 in order; apply the status label; keep classification and maintainer action separate |
