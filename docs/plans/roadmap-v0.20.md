# Roadmap - v0.20 "Earn the Language"

**Created:** 2026-09-08

**Status:** Draft v2, revised for a second review round. Decision-first proposal;
no prototype, experiment, recruitment commitment, or spending authorized.

**Release sequence:** v0.18 finishes its current work; v0.19 addresses the
[language audit epic #1182](https://github.com/juanmicrosoft/calor/issues/1182)
and [website audit epic #1202](https://github.com/juanmicrosoft/calor/issues/1202).
v0.20 tests whether safe delegation earns Calor an adoption advantage.

**Decision owner:** repository maintainer. An independent reviewer must sign
off on the comparison and final interpretation before any advantage claim.

## 1. The decision, in plain language

> Can a team let an agent change important business rules in Calor at half
> the total cost per correctly accepted change, without materially worse
> safety or completion, compared with well-equipped C#?

Here "half" means an upper confidence bound at or below 0.50, not just a
favorable point estimate. The true improvement needed for an affordable
study to pass depends on variance; no evidence currently establishes it.

The comparison must include C# with protected requirements and enforcement,
not just ordinary C#. Otherwise we could prove that checking rules helps
without proving that a new language helps.

v0.20 ends with one of three investment decisions: **continue Calor in this
narrow domain, move the useful workflow into C# tooling, or stop expanding
this bet**. Inconclusive evidence is a reason to stop the investment under
this budget, not evidence that the scientific claim is false.

"Once and for all" means making this investment decision against a frozen,
affordable experiment. It cannot mean proving that one language beats every
other language on every application or future model.

### This is not a new thesis

[Agent-native strategy](agent-native-strategy.md), section 0, already proposes
safer agent changes and reduced review burden. Its section 2 already says
the product is the workflow. Repeating that thesis is not progress.

This roadmap adds a strong C# workflow control, protected acceptance rules,
end-to-end adoption economics, and a finite decision deadline. It does not
relabel old mechanism results as user benefit.

The [v0.18 roadmap](roadmap-v0.18.md) measures a narrower effect-row claim.
Record its actual outcome as input; do not presume a win or use it as a
substitute for this experiment.

### First decision: can we afford to answer this?

**M0 is a desk feasibility screen before implementation or external
commitments.** The initial arithmetic in section 6.3 does not justify
scheduling the full comparison under the proposed resource envelope.
M0 must either produce a credible route to the unchanged adoption gates,
or stop that comparison before M1-M4 consume resources.

An independently approved exploratory study is another option, not a
fallback pass. It may measure friction or disprove a mechanism on an
in-house example, but cannot declare safe-delegation superiority, justify
production adoption, or automatically fund a larger acceptance service.
Section 7 specifies the separate authorization that option needs.

## 2. Questions that must receive answers

| Question | Evidence required | Consequence |
|---|---|---|
| What painful work are we improving? | A non-maintainer adopter's recurring rule changes, current review process, and expected change volume | No adopter means demand unproven; stop before building the workflow |
| Does enforcement improve agent work? | Protected C# versus ordinary C#, with the same requirements | Separates workflow value from language value |
| Does Calor add enough beyond that workflow? | Calor versus both C# arms on total cost, safety, and completion | Only this comparison can earn continued language investment |
| Can we trust the acceptance boundary? | Production-path counterexamples, bypass checks, and independent output evaluation | A false guarantee blocks collection or invalidates the affected epoch |
| Are we moving work onto humans? | Specification, integration, review, repair, and training time, including failed tasks | All enter cost; no free annotation or review labor |
| Is the result useful outside a fixture? | Real task provenance, unseen tasks, and a non-maintainer handoff | Synthetic mechanism wins alone cannot pass |
| What happens if the result is weak? | Precommitted outcome table and deadline | No automatic next release of feature expansion |

## 3. Scope: one small part of an existing C# application

**Proposed first domain: refund eligibility and amount decisions.** Confirm
an adopter with this need in M1. If none exists, one alternative among
entitlement, pricing, or routing decisions may be selected on documented
user need **before any comparative pilot**. No cycling through domains
until Calor wins.

The host remains C#. Calor handles a bounded decision module: explicit
inputs, a decision output, and no execution of payments or database writes.
Use integer minor units for money, with explicit ranges and overflow rules.
Time, tenant identity, and policy data arrive as explicit validated inputs.

Example protected requirements:

- A proposed refund never exceeds the remaining refundable amount.
- A request cannot produce a decision for a different tenant.
- Evaluating the policy cannot perform external I/O or mutate host state.

The tenant rule covers the modeled input/output boundary, not all database
access in the application. The host must authenticate the tenant and supply
the correct records. Payment execution, transaction isolation, concurrent
refunds, and correctness of external services are outside the initial proof
boundary. Include these assumptions in every evidence report.

Start with immutable values, bounded integer calculations, branches, and
statically resolved calls whose summaries can be checked. M1 publishes the
exact supported construct/property matrix against the v0.19 compiler.
Unsupported constructs are refused by the protected workflow, not assumed
safe. Do not require a heap model or general-purpose C# parity to enter.

**Not in scope:** a new syntax redesign, broad migration, a new IDE,
whole-application proofs, distributed-system correctness, or making the
benchmark's composite advantage score larger.

### Research participation is not production adoption

Use isolated copies, historical tasks, and non-production integration for
this experiment. A signed research agreement does not authorize a team to
put Calor in a live refund path.

[Call W's disposition](call-w-adjudication.md) records DEMAND UNPROVEN and
keeps Call 3 closed. The [loop plan](loop-plan-v0.9.md), sections 6.2-6.3,
records the external-adoption decision and its continuing obligations.
M1 needs an explicit maintainer governance disposition before recruiting a
committed adopter: how this research relates to Call 3, which prior
conditions apply, and what remains closed. Do not silently supersede that
decision by naming recruitment a milestone.

The research agreement must state the possible negative outcomes, duration
of support, maintenance/compatibility limits, data permissions, withdrawal
procedure, and funded exit assistance. Production adoption, if later sought,
requires separate Call 3 approval. No experiment participant is required
to depend on the continued development of Calor.

The exit deliverable is a runnable C# handoff with behavioral comparison
against the independently approved requirements. Document lost static
guarantees, remaining runtime checks, and dependencies. Record its actual
test status in M1 and demonstrate it before the adopter handoff; do not
promise "lossless" export merely because emission succeeds.

## 4. Product prototype: protected change, not editable promises

### 4.1 Separate authority from implementation

The agent may edit the implementation and its own supplementary tests.
It may propose requirement changes, but cannot approve them.

A separate trusted acceptance service owns the requirement bundle,
dependency/effect summaries, compiler and solver pins, build configuration,
allowed file surface, and final acceptance decision. Agent workspace hooks
alone are not the protection: the service rebuilds from approved inputs in
an isolated environment and checks the actual submitted artifact.

Protect generated C#, project/build files, referenced packages, analyzers,
proof caches, runner configuration, requirement files, and acceptance
scripts against agent-controlled substitution. Trusted checks come from
the protected baseline, not executable scripts in the proposed patch.
No privileged service credentials or hidden evaluation assets enter the
agent's environment.

The service binds evidence to source, requirements, dependencies, toolchain,
and configuration by content hash. A change to any relevant input makes
previous evidence stale. Record the exact artifact accepted for deployment;
do not approve one tree and deploy another.

### 4.2 Small requirements, independently approved

Requirements express stable business limits, not a second implementation.
The adopter approves them before agents implement the changes. All arms
receive the same natural-language specification and semantic constraints.
Translation into arm-specific forms is reviewed and charged to that arm.

Check that preconditions are satisfiable and that valid business inputs
remain admitted. Always returning "deny", narrowing the input domain, or
throwing on every request is not success. Independent behavioral checks
include positive business cases and behavior not fully described by the
protected contracts.

The intended task can legitimately change a business requirement. In that
case the task starts with the same independently approved replacement
bundle for every arm. Unauthorized weakening during implementation fails.

### 4.3 Honest outcomes and useful repair feedback

Present **established**, **violated**, and **not established**, mapping the
existing detailed proof statuses without throwing their distinctions away.
Timeout, unsupported analysis, missing dependencies, and stale evidence
remain distinct machine-readable reasons under "not established".

An established result names the exact properties, modeled domain,
assumptions, and trusted dependencies. It is never "the application is safe".
A violation supplies a concrete counterexample when available. If a fact
cannot be established, the strict workflow does not automatically accept.
Human exception handling is recorded as escalation, with its full cost;
it cannot count as machine-established evidence.

Compose dependency summaries only where the compiler checks the relevant
call, argument, state, and effect relationships. Unanalyzed code is not a
free pure summary. Cross-module edits invalidate dependent evidence.

### 4.4 Prototype acceptance criteria

The prototype must reject unauthorized rule/configuration changes, raw-C#
escape attempts, changed dependencies, and stale proof reuse. Deliberately
bad implementations must fail; legitimate changes must pass. Exercise the
same production compilation and acceptance paths used in the experiment.

Every #1182 finding whose construct or property appears in M1's supported
matrix must have a discriminating regression that fails on its pre-fix
baseline and passes on the pinned compiler. Include false postcondition
proofs, simplification, assignment effects, expression grouping, match-arm
execution, and numeric semantics (#1183-#1185 and #1187-#1189). Include
inherited contracts (#1186) if interfaces/inheritance are admitted; otherwise
enforce their exclusion. Apply the same rule to all other epic findings.
Issue closure is not a substitute for these artifacts.
Keep runtime guards in this prototype; retaining guards does not repair an
unsound proof, so false-established results still block the experiment.

Budget a separate search for false-established results over the complete
supported matrix, including feature interactions, mutation, and dependency
changes. The oracle is a violated **claimed property** on a modeled input
while that property remains established. Changed behavior alone is not a
false proof: different behaviors may satisfy the same contract. Retain
behavior-preserving and contract-preserving mutations as negative controls.
Zero findings establish only that these probes found none, not soundness
of the entire compiler.

## 5. Experiment: three credible workflows

| Arm | Language and tooling | Protection |
|---|---|---|
| A: ordinary C# | Current C#, Roslyn/NRT/analyzers, normal tests, and agent-generated tests or other practical evidence | Common protected requirements and runner; normal review, no new domain-specific proof gate |
| B: protected C# | A plus a credible implementation of the protected-rule workflow using analyzers, verification tools, restricted patterns, and/or runtime enforcement | Same independently approved rules and authority separation as Calor |
| C: protected Calor | Calor in the same C# host, using the supported contracts/effects and protected acceptance prototype | Same independently approved rules and authority separation as B |

All arms may use practical existing tools, including formal tools in C#.
Do not require B to analyze unrestricted C#: it may use the same restrictions
as C. Do not ban a good C# solution because it makes Calor less distinctive.

M0 sketches B using practical existing components, including property-based
testing, runtime guards, and the shared acceptance service. M1 requires a
C#-experienced non-maintainer to approve B's design, diagnostics, and tool
choices **before C's integration starts**, not merely endorse the result.
Publish the review's concrete acceptance criteria and disclose conflicts.

Budget B and C integration separately from the shared service, with equal
integration allowances, actual effort logs, and Calor's sunk investment
reported separately. Equal new effort does not erase existing maturity
differences: this measures deployable workflows, not equal research effort.
If credible B cannot be delivered within its allowance, the comparison is
**not ready**; that is not evidence Calor won. M0 states the proposed true
cost advantage and why it is plausible; do not invent that expectation from
a desired sample size or imply that previous effect-row results support it.

Expose equivalent requirement content and tool guidance. Equal information
does not require identical syntax or identical diagnostics. Those differences
are part of the workflows being measured. Do not claim pure syntax causation.

### 5.1 Tasks and independent correctness

Use real changes or independently authored requests grounded in the
adopter's recorded work. Freeze the sampling frame and inclusion criteria
before selecting confirmatory tasks. Include ordinary changes, boundary
cases, cross-function changes, valid rule updates, and tasks outside the
protected subset at registered proportions representative of that work.
Report eligible versus excluded work across the entire sampling frame.

Freeze the primary task mix in M1: **at least 75% of primary task weight
on distinct replayed historical requests; at most 25% on authored variants**.
Count each source request as one independent task cluster; related variants
and repeated agent runs do not increase the independent-task count. Keep
source clusters disjoint between pilot and final pools. Report historical
and authored outcomes separately as well as under the frozen task weights.

Use the adopter's preceding 12-month history as the initial sampling frame.
Do not assume the next two weeks can supply a year's changes. M0 estimates
required task supply; M1 inventories eligible historical requests and their
artifacts. If that frame cannot supply the powered design, stop rather than
fill it with synthetic variants. The 50-future-request adoption horizon
is a separate business assumption, not the size of the evaluation pool.
Historical implementations and incident reports inform the independent
oracle but are not automatically correct reference answers.

Keep mutation-injected failures and deliberate bypass probes as a separate
mechanism suite. Do not pool them with natural tasks to inflate real-world
defect rates. Do not make tasks harder merely because strong C# succeeds.
Screen whether the primary economic and joint safety/completion gates can
distinguish the workflows; do not impose a C# failure-rate quota. Equal
100% completion does not prevent a meaningful cost difference.

Author idiomatic starters from the shared behavioral specification.
Independently compare input/output behavior before admitting a task.
Mechanical C# -> Calor conversion is optional, not the correctness oracle;
exclude conversion defects from measurement only before freezing the suite.

A non-implementing evaluator owns one arm-shared black-box held-out suite
and severity rubric, including hidden regression and property cases.
Reference solutions must satisfy it. The evaluator and acceptance service
are separate: accepted artifacts can fail held-out evaluation.
No implementation agent or acceptance reviewer receives held-out feedback
until the entire confirmatory epoch is sealed.
Before freeze, archive five consecutive successful runs of each reference
solution against its held-out suite, including the pins and determinism
evidence. This checks the instrument, not the completeness of its oracle.

### 5.2 Assignment, reviewers, and execution

Use one pinned primary model/agent configuration in all arms. Freeze model
ID, settings, tool versions, hardware, dependency versions, prices, and
time/token/iteration budgets. Randomize arm execution order and pair runs
by task. A second model is optional descriptive replication only; it cannot
rescue a primary failure. A model change mid-epoch invalidates comparison.
Explicitly pin solver version, per-query and total solver time limits,
CPU/memory limits, and cache policy. Solver time is ordinary workflow cost,
including in B if B uses a solver; a timeout is not a free infrastructure retry.

Model familiarity with C# is a registered limitation of generalization and
a real part of today's adoption cost, not a reason to excuse a Calor loss.
Report syntax/compile-error repair separately from logic/contract repair
using a frozen classification, retaining mixed/unknown categories. Do not
assert familiarity is the dominant cause without evidence.

Each arm/task/repetition starts with a fresh agent conversation and isolated
working tree. No shared agent memory, earlier solutions, repair transcripts,
or cross-arm artifacts are available. Freeze permitted persistent resources
(for example package caches and approved documentation) and an identical
cold/warm-cache policy; charge setup and cache preparation consistently.
Use fresh protected proof caches per slot except for reuse within that slot.

Recruit at least three qualified non-maintainer reviewers for timed review.
Train them on each workflow; charge training time. Counterbalance assignment
and ensure a reviewer does not see the same underlying task in multiple arms.
Freeze a feasible assignment schedule, including repeated-run handling and
pilot/final separation, within the total reviewer-hours ceiling.
The M1 role/assignment table must cover specification approval, B-design
review, three timed reviewers, independent oracle ownership, and final
interpretation. People may cover compatible roles, but an implementer cannot
own the hidden oracle or independently judge their own artifact. Do not
assume five to seven distinct people are already available.
Language cannot be blinded, so disclose that limitation. Hide arm labels
from the independent behavioral/severity adjudication wherever possible.

Reviewers use the evidence available in that arm and may accept, reject, or
request repair under the same time cap. Log active review, diagnosis, repair,
and escalation time. No automatic Calor acceptance while forcing a full
line-by-line review in C#: each workflow gets its natural evidence-based
review. A and B may also earn quick acceptance.

Evaluate the final immutable artifact after the ordinary review/repair
workflow ends. Do not let hidden tests guide further repair. Preserve
pre-review outcomes as secondary evidence to distinguish agent performance
from human rescue.

An **assigned slot** is one task, arm, and preregistered repetition. It has
one fixed total budget, including ordinary tool calls, review, and repair.
Agents may recover from compiler errors, solver failures, or unsupported
constructs within that budget without changing the protected requirements.
A terminal refusal/crash, exhausted budget, or unresolved escalation is a
failed slot with its full cost. There is only one final accepted artifact
per slot; intermediate candidates are not accepted for deployment.

Only independently detected shared infrastructure failures may replace an
execution, with at most two replacement executions per paired slot under
frozen rules applied symmetrically to all arms. Retain every execution and
charge its cost to the original slot. Replacements never add denominator
entries, and a genuine accepted-artifact failure cannot be relabeled as an
infrastructure error. An exhausted slot remains a failure. After freeze,
no exclusions based on which arm lost.

## 6. Primary claim and decision rules

These are **proposed adoption thresholds**, not observed results or approved
spend. M0 first sizes these thresholds as written. M1 may approve them or
propose a separately reviewed change on business grounds before any
comparative pilot; it cannot silently trade safety for affordability.
Once pilot outcomes are visible, do not relax them to obtain power or a win.
M4 freezes the complete statistical implementation before final collection.
v0.20 uses one primary economic route, not a choice between several favorable
metrics after seeing results.

### 6.1 Outcomes and denominators

An **accepted change** is approved by the normal arm workflow within budget.
A **correctly accepted change** also passes all independent held-out
behavioral requirements and the protected-boundary checks.

The primary outcome is **total cost per correctly accepted change**:

`(adopter setup cost / 50 + mean_t(cost_t)) / mean_t(success_t)`.

Here `t` is an independent source-request task cluster. `cost_t` is its mean
operating cost across assigned repetitions/variants; `success_t` is the
corresponding mean of correctly accepted indicators. Freeze within-task
weights before collection, then give each task equal primary weight.
Slot-level outcomes remain the raw records, not independent observations.
Replacement executions add to their original slot's cost, never to the
task count or its number of assigned repetitions.

Operating cost includes all run, review, repair, and replacement-execution
costs, including failed slots. Zero correct acceptances means infinite cost,
not a missing observation. Report task-weighted human minutes per correctly
accepted change as a secondary outcome alongside total economic cost.

The primary adoption horizon is **50 assigned change requests**, not 50
successful changes or the experiment's sample count. M1 must confirm that the
adopter expects at least 50 relevant requests within 12 months; otherwise
the proposed business case fails before the pilot. Allocate specification,
integration, onboarding, and training costs over that horizon. Also publish
first-change cost, unamortized totals, cost at 10 and 100 changes, and the
observed break-even volume. Shared specification effort is charged equally;
arm-specific translation and maintenance are charged separately. The M1
cost dictionary fixes the following classification before any pilot:

| Cost | Allocation |
|---|---|
| Initial adopter integration, baseline requirements and translations, onboarding, reviewer training | Setup, amortized over 50 assigned requests |
| New/change-specific requirements and translations, per-change review/repair/escalation, execution and recurring workflow maintenance | Operating cost of the relevant task |
| Research-only oracle authoring, comparator vetting, statistical work, study-specific reviewer orientation, prototype engineering | Study/R&D expenditure, reported separately and charged against the resource ceiling |

No item may be charged twice or moved between setup and operating cost after
outcomes are seen. Separate adopter training needed in normal use from
research-only orientation; otherwise the study can hide adoption work.

Record actual cash expenditure, API-list-price equivalent usage, subscription
allocation, compute consumption, and human time separately. Choose the
primary buyer's accounting basis in M1; do not call subscription usage a
per-run bill or mix list-price usage with cash without labeling it.
Use preapproved role-specific human rates, identical for the same role in
all arms. Publish results over a preregistered plausible rate range.

Setup observed once per arm is a case-specific cost, not a population
estimate with repeated-task precision. M1 freezes setup uncertainty bands
and rate scenarios from documented estimates before observing comparative
pilot outcomes. Report the measured setup, the bands, and break-even
sensitivity. An economic pass must hold at the least favorable allowed
setup/rate combination for each required comparison. Statistical intervals
cover task/reviewer sampling conditional on those inputs; they do not imply
precision about setup at other organizations. Out-of-band setup invalidates
the cost assumptions and blocks a pass, rather than triggering a favorable
band change.

Prototype/tool R&D is reported separately from adopter setup and operating
cost. Report both the adoption result and the total project investment;
do not hide a large tooling build behind cheap subsequent executions.

**Serious escaped defect:** an accepted artifact violates a preclassified
business-critical requirement in the independent evaluation. Count at most
one serious-defect event per assigned slot, regardless of failing test count.
Let `escape_t` and `accepted_t` be within-task means of their slot indicators.
Report task-weighted rates `mean_t(escape_t)` over all assignments and
`mean_t(escape_t) / mean_t(accepted_t)` conditional on acceptance, alongside
unweighted raw counts for auditability.
The first detects delivered harm; the second prevents refusal rates from
making conditional reliability look better. Neither is a production incident
rate or proof of absence of bugs.
If an arm accepts nothing, its conditional defect rate is undefined, not
zero; comparisons requiring that rate cannot pass. Freeze conservative
handling of other undefined ratios in M4 rather than dropping those draws.

### 6.2 Pass requires all gates, against both A and B

| Gate | Required outcome for Calor |
|---|---|
| Economic advantage (primary) | Upper confidence bound on cost-per-correct-acceptance ratio <= 0.50 versus each C# arm |
| Useful completion | Lower confidence bound on Calor correctly accepted fraction >= 80%, and on its difference versus each C# arm >= -5 percentage points |
| Safety non-inferiority | Upper confidence bound on Calor minus each C# arm serious-defect rate <= +1 percentage point, for both all-assigned and accepted denominators |
| Trust boundary | No confirmed false-established property or accepted unauthorized boundary bypass in the frozen mechanism suite or confirmatory runs |
| Usable adoption | Independent adopter completes install, integration, a change, and an export-to-C# handoff; all effort and loss of guarantees disclosed |

The +1 percentage point safety allowance is a proposed non-inferiority
margin, **not "no extra risk"**. The adopter must explicitly accept it before
pilot collection; unacceptable risk means no experiment under this proposal.
Report absolute rates and intervals alongside relative differences.
Bounds are one-sided with simultaneous 95% coverage across the registered
gate quantities for C versus A, C versus B, and the B-versus-A workflow
decision. M4 freezes the joint error-control method; unadjusted individual
95% intervals do not substitute for the complete decision procedure.

Review time, agent iterations, context read, and prevented violations are
secondary explanations. Neither a fivefold point-estimate defect reduction
nor a better token score can rescue failure of the primary economic route.

### 6.3 Size first; calibrate only if a credible route exists

#### M0 desk screen: before recruitment or implementation

The following is planning arithmetic, not results from Calor runs. For
independent binary observations with zero events, the exact one-sided
95% upper bound on a **single rate** is `1 - 0.05^(1/n)`:

| Independent observations n | Single-rate upper bound | Six-way Bonferroni illustration |
|---|---|---|
| 20 | 13.91% | 21.29% |
| 30 | 9.50% | 14.75% |
| 50 | 5.82% | 9.13% |
| 100 | 2.95% | 4.67% |
| 299 | <= 1.00% | > 1.00% |

The six-way illustration uses `1 - (0.05/6)^(1/n)`; it reaches 1% at
477 observations. Neither column is the paired risk-difference interval
or the final simultaneous procedure. They show why zero defects in a small
sample is not strong safety evidence. Accepted-only denominators and
task/reviewer dependence add further constraints.

For another illustration, an unpaired normal approximation at equal 3%
defect rates, true difference zero, 1-point margin, one-sided alpha 0.05,
and 80% power gives about 3,600 observations **per arm**:
`n = (1.645 + 0.842)^2 * 2 * 0.03 * 0.97 / 0.01^2`.
Pairing may reduce this if cross-arm outcomes are correlated; neither that
correlation nor these assumed rates have been measured for this workload.

If 40 of the proposed 80 non-maintainer hours remain after other duties,
20 minutes of review per slot buys 120 slots, or just 40 three-arm task
triplets with one repetition, **before** reserving a disjoint pilot.
Even spending all 80 hours on timed review buys only 80 triplets under
that assumption. These are capacity scenarios, not measured review times.

**Current disposition: the full comparison is not justified for scheduling
under the proposed ceiling.** M0 must assess all section 6.2 gates at
candidate independent task counts 20, 30, 50, and 100, plus any larger
fundable design. Include paired event-rate/correlation scenarios, cost
variation, setup bands, completion uncertainty, and task/reviewer supply.
Do not infer final power from this single-rate table.

M0 delivers a short sizing memo and resource worksheet, not a new compiler
or full adjudicator. Explicitly identify which assumptions lack evidence.
If conservative, plausible scenarios do not show a credible joint route
within an approved resource ceiling, stop before M1. A tiny pilot may not
be funded merely in the hope that implausibly favorable rates will appear.
An increased resource proposal needs separate approval before commitment.

#### M4 calibration and final registration

Use one disjoint calibration pilot, never reused as confirmatory evidence.
Estimate task-level variation, failure/event rates, review-time variation,
and setup costs. More agent repetitions of a few fixtures do not create
more independent business tasks.

M0's analytic screen is necessary but not sufficient: M4 updates sizing
from pilot uncertainty without changing the business thresholds or choosing
easier task subsets. A failed M4 is still possible, but it should resolve
an empirical uncertainty, not discover an obvious capacity mismatch.

M4 must commit an executable analysis and sample-size simulation, with
coverage/type-I-error checks for the proposed estimators, sparse events,
zero-event cells, reviewer effects, and paired/clustered task structure.
Use task-level pairing and account for reviewer clustering. Do not use a
naive bootstrap that gives a zero-width safety interval when no bugs occur.
Freeze exact estimators, interval methods, resampling seeds, and handling of
undefined ratios before collection. A method that cannot handle these cases
cannot adjudicate the gate.

Preregister and simulate the complete ordered decision procedure in section
8. Require at least 80% probability of the correct positive decision in
**each** of two stated scenarios: a language advantage (C passes versus both
controls), and a workflow-only advantage (B passes versus A and C does not
earn continuation). Assumed effects must be stronger than gate boundaries,
with conservative pilot uncertainty. At a true cost ratio of exactly 0.50,
demanding an upper bound <= 0.50 is not an 80%-power design. Publish assumed
effects, event rates, and sample counts for both scenarios.

Check that the probability of any false positive investment claim is <= 5%
across global-null and relevant mixed-null scenarios. Each positive decision
requires its full conjunction, with the precedence in section 8; secondary
metrics cannot create additional win routes. A design powered only for a
Calor win is insufficient. If either branch is infeasible, v0.20 stops this
three-arm experiment; a smaller C#-only study requires separate authorization.

The 1-point safety margin can require hundreds or thousands of independent
observations. If realistic event rates, task supply, reviewer capacity, and
budget cannot establish it, record **NOT FEASIBLE** and stop. Do not widen
the margin, inject more bugs, or quietly substitute zero observed failures.

### 6.4 No optional stopping or convenient restarts

Fix sample counts and collection dates at M4. No early stopping for a win,
no outcome-based sample extension, and no harvesting the best task subset.
Stop immediately for a trust-boundary defect or at the spending ceiling.
Publish incomplete counts and costs; stopped collection cannot pass.

For a protocol defect, publish the defect and identify affected evidence.
Do not patch the compiler and keep favorable old runs. A replacement epoch
requires fresh preregistration, unseen tasks where leakage occurred, and
separate authorization; it is not automatic v0.20 scope.

## 7. Delivery plan and finite resource envelope

### 7.1 Decision stage first

M0 is proposed as a maximum **two engineering person-day desk exercise**,
with no paid agent collection, new acceptance service, or external
commitments. It may be done before v0.19 finishes because its first job is
to reject an unaffordable design. Approval of this roadmap alone does not
authorize even that exercise; the owner records its limited authorization.

Order: desk sizing of the original gates -> protected-C# design sketch ->
resource/task-supply screen -> decision whether to pursue research
participation. M1's independent B-design sign-off and research agreement
follow only a credible M0 outcome. Do not recruit against an eight-week
experiment that has not passed its desk screen.

If the original comparison is not feasible, record that before considering
an **exploratory option**. A separate proposal must name one uncertainty,
the smallest artifact needed, a fixed dataset, allowed claims, a time/spend
ceiling, and a stop decision. Prefer existing tools and an in-house example.
A friction study can estimate human minutes and repair costs; a mechanism
probe can find false guarantees. Neither can pass section 6, downgrade its
safety gate to descriptive reporting, cross Call 3, or authorize full
section 4 implementation. A promising exploration may justify proposing a
new study, not automatically continuing language investment.

### 7.2 Resource reconciliation is an entry gate

The original **candidate ceiling** was eight delivery weeks, 30 engineering
person-days, $1,000 pilot API/compute plus $5,000 confirmatory API/compute,
and 80 total non-maintainer hours. It remains a proposal, **not a supported
estimate or booked schedule**. No automatic budget increase is implied.

For visibility, the first critique supplied the following decomposition:

| Full-comparison work item | Critique's planning range, engineering days |
|---|---|
| Domain, governance, readiness, and pricing | 3 |
| Protected-C# integration | 5 |
| Calor integration | 5 |
| Shared protected acceptance service | 8-12 |
| Mechanism suite and false-established search | 3-5 |
| Task/oracle construction and review protocol | 5 |
| Pilot, analysis, joint-power calibration | 5 |
| Collection operations | 4 |
| Report, independent interpretation, and handoff | 3 |
| **Total, before M0 desk work** | **41-47** |

These are review estimates, not measured effort or a revised authorization.
They expose missing allocations; they do not establish that a particular
implementation must take 41 days. M0 replaces them with a bottom-up worksheet
using inspected reuse opportunities. Shared-service and mechanism work
must remain separate line items, not disappear inside the equal B/C
integration allowances.

For every item record engineering days, non-maintainer hours, compute,
owner, dependency, reuse assumption, and contingency. Allocate the 80 hours
across specification, B-design review, oracle/severity work, training,
timed review, and final interpretation. Record any overlap between role
budgets; the same person's hour is one resource, not two available hours.
Include exit support obligations and pilot/final task counts. If the totals
cannot fit, reduce nonessential scope without dropping a gate or request
explicitly different resources before implementation. Otherwise stop.

Price all human effort, including maintainer time, in the report. Do not
borrow unused v0.18 budget or treat subscription list-rate equivalents as
cash authorization. M1 records the actual authorized ceiling and accounting
basis; M4 cannot increase it after examining pilot outcomes.

### 7.3 Gated milestones, not a prebooked eight-week study

M1 may start only after M0 has a credible disposition and v0.19 has a
published release/epic disposition. The delivery clock starts on **M1
approval**, after people, scope, and resources are committed, not on this
document's date. M0 authorization must also set a fixed deadline for an M1
decision so recruitment cannot remain pending indefinitely. M1 publishes
actual dates and critical-path estimates within the authorized ceiling.
No overlapping milestone is assumed to create extra engineering capacity.

| Milestone | Dependency | Deliverable and stop condition |
|---|---|---|
| M0: desk feasibility | Limited owner authorization | Sizing of unchanged gates, B sketch, resources, task supply, and pursue/stop disposition. No credible route means no M1-M4 implementation commitment. |
| M1: authorize the chosen comparison | M0 credible; v0.19 disposition | Governance, research agreement, B-design sign-off, historical inventory, supported matrix, cost dictionary/bands, people, dates, thresholds, and resources. Missing demand or capacity stops. |
| M2: build the smallest credible comparison | M1 approved | Working A/B/C and shared protected service; separately budgeted matrix-wide mechanism suite. Missing credible comparator or trust boundary stops collection. |
| M3: establish independent task evidence | M1 inventory; M2 runnable | Shared oracle, deterministic references, disjoint pilot/final pools, reviewer assignment, boundary cases. Insufficient independent task supply stops. |
| M4: calibrate once and freeze | M2-M3 entry criteria pass | Pilot report, executable joint-power/cost analysis, fixed final counts, exact estimators, pins, and funded collection dates. Infeasible means stop, not a bar change. |
| M5: collect without tuning | M4 frozen and feasible | Complete paired runs, timed reviews, immutable submissions, costs and hidden outcomes. Budget stop or soundness defect cannot pass. |
| M6: publish and decide | M5 complete or any stop | Report, independent interpretation where a comparative claim is made, handoff obligations, and investment decision. A pre-collection stop does not require building later milestones. |

v0.19 retains ownership of both epics; this roadmap neither reopens their
tasks nor silently transfers them to v0.20. Its entry disposition must list
every finding's resolution or explicit deferral. A deferred audit item
touching the experiment's guarantees must be resolved or explicitly
excluded with an enforced restriction.
Website defects do not become new proof work; their remediation must keep
eventual claims and instructions aligned with the actual experiment.

### 7.4 Reuse plan to cost, not assumed compatibility

| Existing asset | Proposed reuse or required replacement |
|---|---|
| `bench/phase0-agent-native/harness-capture.py` | Inspect reuse of transcript/build-state capture and pin admission; add explicit A/B/C identities and task/slot records without changing old epochs. |
| `bench/phase0-agent-native/run-pair.sh` | Candidate execution/archiving scaffold only. Its existing two-arm configuration and invalid-run retries must not silently define this study; implement the three-arm, slot-budget, replacement, and isolated-evaluator rules above. |
| Existing epoch `pins.json` and `loop-telemetry-schema.md` | Reuse provenance conventions; version additions for human review, accounting basis, authority hashes, and task clusters. |
| `bench/phase0-agent-native/ppw-analyze.py` | Keep its frozen PP-W-rows adjudication unchanged. Build a separately registered analyzer for this cost/safety decision; reuse helpers only when their assumptions match. |
| Existing held-out runner | Evaluate whether it can keep oracle execution and feedback outside the agent/reviewer boundary. In-workspace silent tests alone do not demonstrate isolation. |

Adopt existing determinism evidence and wasted-iteration concepts as
secondary instrumentation, and configuration canaries as pre-collection
checks. Do not import historical hardness quotas, outcome thresholds, or
retry semantics when they answer a different question. M2's PR must name
actual integration points and discriminating regressions; new flags,
commands, or schemas are not assumed to exist.

## 8. Published outcomes and mandatory actions

| Outcome | Evidence | Action |
|---|---|---|
| LANGUAGE EARNS CONTINUATION | Every section 6 gate passes versus A and B; adopter handoff succeeds | Continue only in the measured domain. Publish exact scope, costs, margins, model, and remaining trust assumptions. |
| WORKFLOW VALUE, LANGUAGE NOT JUSTIFIED | B passes the same economic/completion/safety adoption gates versus A (with B substituted for C), but Calor does not pass all gates versus both C# arms; B also passes boundary and adopter gates | Prioritize C# workflow tooling; keep Calor in maintenance unless a separately approved new hypothesis appears. |
| TARGET NOT MET | Valid complete data fail one or more continuation gates and the C# workflow outcome above does not apply | Stop broad safe-delegation feature expansion on this bet. Report the pinned model and its Calor exposure, and whether intervals rule out the target or leave it uncertain; failure to pass is not equivalence. |
| NOT FEASIBLE / DEMAND UNPROVEN / INVALID | No funded powered design, no adopter, incomplete collection, or a protocol/trust defect | Publish the reason and spent resources. No advantage claim and no automatic extension. |

Decision precedence: INVALID first, then LANGUAGE EARNS CONTINUATION,
then WORKFLOW VALUE, then TARGET NOT MET. Pre-collection stops use the
specific NOT FEASIBLE or DEMAND UNPROVEN label.

Exploratory evidence is **EXPLORATORY ONLY**, outside this outcome table's
positive decisions. It cannot produce LANGUAGE EARNS CONTINUATION or
WORKFLOW VALUE by omitting a safety gate. Record a stopped full comparison
separately from any later authorized exploration.

One domain and one model do not support a general language superiority claim.
Wider claims require separately funded replication. A later model or adopter
may justify a new experiment, but not a rewrite of this outcome.

## 9. Evidence package and relationship to existing gates

Proposed implementation artifacts, **not files created by this roadmap**:

- A protected registration under `bench/safe-delegation/v0.20/` naming task
  hashes, pins, requirement versions, authority rules, approved resources,
  estimators, sample sizes, and all acceptance thresholds.
- The pilot, analysis script, frozen reference/oracle provenance, and raw
  per-slot outcomes and per-execution timing/cost records with a versioned
  data dictionary.
- A final report linking immutable source commits and raw results, the
  independent reviewer disposition, all exceptions, and the investment action.

Keep final hidden tasks/oracles in separate access-controlled storage until
collection ends; public pre-registration stores hashes and selection rules,
not answers visible to the agent. Publish reproducible synthetic equivalents
or appropriately licensed task material when real adopter data cannot be
released. Disclose resulting reproducibility limits; never publish private
code, credentials, or personal reviewer data without authorization.

[Existing gates](agent-native-gates.md) remain frozen historical protocols.
This roadmap does not edit their thresholds or reinterpret their results.
M4 registers a **new experiment identity**, with an explicit crosswalk of
changed metrics, denominators, controls, and resource rules. Existing guards
and append-only registry requirements must still be honored.

## 10. Definition of done for v0.20

- [ ] v0.18's actual result and v0.19's two epic dispositions are recorded.
- [ ] M0 sizes the original claim before implementation and records pursue/stop.
- [ ] If pursued, M1 approves governance, the business case, B design, cost bands, people, and a reconciled finite budget.
- [ ] If pursued, credible A/B/C paths and the full supported-matrix boundary checks pass.
- [ ] If pursued, independent tasks and reviewers exist, with source-cluster pilot/final separation.
- [ ] A powered, affordable, frozen design exists, or a documented pre-collection stop replaces downstream work.
- [ ] Every executed outcome and cost is retained; no favorable-only reporting.
- [ ] The report states the supported claim, uncertainty, and exclusions.
- [ ] The decision owner records and applies section 8's investment action.

A release may finish with a documented stop rather than a positive result.
It must not call an unfinished experiment a demonstrated advantage.

## 11. Review disposition and next-round questions

Draft v2 responds to the 2026-09-08 critique of `b63b50f6`, titled
*Critical Review - Roadmap v0.20 "Earn the Language"*. That critique is a
local review artifact, not a prerequisite for understanding this revision.
No independent human review or experimental result is claimed.

| Critique item | Disposition in this revision |
|---|---|
| C1: late, likely unaffordable safety sizing | Accepted the sequencing defect. Added M0 arithmetic and a pre-implementation stop. Single-rate and unpaired calculations are illustrations, not exact paired-design impossibility proofs. |
| C2: adoption governance and obligations | Accepted Call 3/exit requirements; separated research from production adoption and allowed compatible roles to share people. Historical lack of an adopter is not evidence about current recruitment without an update. |
| C3: unallocated service and undercosted schedule | Accepted. Exposed the review's 41-47-day estimate, required a bottom-up worksheet, and removed prebooked week numbers. |
| M1: setup measured once | Accepted. Setup/rate bands fixed before pilot; economic gates must pass under pessimistic registered inputs. Sampling intervals do not establish cross-organization setup precision. |
| M2 / R3: replace dollars with human minutes | Partly accepted. Human minutes, actual spend, list-price usage, and role rates are separate; total adoption cost remains primary with sensitivity analysis. |
| M3: historical supply versus future change horizon | Accepted. Inventory the previous 12 months; at least 75% historical requests, cluster related variants, and do not treat old implementations as unquestionable oracles. |
| M4: weak protected-C# control | Accepted design-first independent sign-off and an explicit hypothesized effect; equal integration budgets do not imply equal maturity. |
| M5: model familiarity | Accepted as a measured limitation and practical adoption cost, not established as the dominant causal factor or grounds for excusing a loss. |
| M6: entry coverage and false-established search | Accepted with a corrected oracle: violated claimed property plus established verdict, not merely changed behavior. |
| M7 / R6: import hardness gates | Rejected C# failure quotas for a cost-primary study. Adopted determinism/configuration evidence and applicable diagnostic instrumentation. |
| M8-M9: units and cost classification | Accepted task-cluster formulas and a pre-pilot allocation dictionary, including research-only versus adopter training. |
| m1-m3: headline, reuse, solver pins | Clarified confidence-bound meaning; named reuse candidates and required replacements; made solver limits/cache policy explicit. |
| R1: automatically fund the trust-boundary half | Rejected automatic funding. Only a separately scoped exploratory proposal can authorize a smaller artifact. |
| R2: demote safety to reporting | Rejected for the adoption claim. A descriptive safety result belongs to a separately labeled exploration, not a weakened pass. |
| R4-R5: cheapest decisions first; re-cost | Accepted through M0, governance-aware M1, and the reconciled resource gate. |

The next review should answer these questions before another implementation
plan is commissioned:

1. Does M0 have a credible, inexpensive way to rule out the full comparison,
   or does it still hide an empirical study inside "desk sizing"?
2. Is any plausible route to the unchanged joint gates compatible with task
   supply and human capacity, without inventing advantageous base rates?
3. Does the proposed C# control reflect what an informed adopter would use?
4. Can any cost allocation, setup band, variant weighting, or positive
   decision branch manufacture a win?
5. Are research consent, negative-outcome support, and Call 3 boundaries
   explicit enough to avoid accidental production commitments?
6. Does the exploratory option remain a bounded question, rather than a
   back door to another release of mechanisms without value evidence?

This round is for review of Draft v2. It is not approval to run M0, recruit,
implement the service, or collect pilot/final data.
