# Roadmap - v0.20 "Earn the Language"

**Created:** 2026-09-08

**Status:** Draft v3, focused revision after two critique rounds. Decision-first proposal;
no prototype, experiment, recruitment commitment, or spending authorized.

**Release sequence:** v0.18 finishes its current work; v0.19 addresses the
[language audit epic #1182](https://github.com/juanmicrosoft/calor/issues/1182)
and [website audit epic #1202](https://github.com/juanmicrosoft/calor/issues/1202).
This roadmap evaluates the investment proposed for v0.20; a decision-only
stop does not trigger a package release or consume that version number.

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
M0 first sizes the original gates, then maps conditional alternatives
without approving them. Its four outputs are defined in section 6.3.
No output itself authorizes implementation or changes the safety claim.

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

M0 sketches B using property-based testing, runtime guards, the shared
acceptance service, and Roslyn/static restrictions on I/O, host mutation,
and nondeterminism. A banned-API list alone is not an effect system:
the design must address wrappers, transitive calls, and unanalyzed code.
M1 requires a C#-experienced non-maintainer to approve B's design and tool
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
M0 reports available inventory or labels scenario counts as unverified;
M1 confirms actual eligible requests before authorizing implementation.
Record source IDs/hashes, exclusions, usable historical count, independently
authored clusters, source linkage, and pilot/final reservations. Apply the
75% historical-weight floor to each pool. Missing supply cannot be filled
with correlated variants. The 50-future-request adoption minimum is neither
a historical measurement nor a ceiling; section 6.3 accounts for actual supply.
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
spend. The safety margin remains **+1 percentage point** in this revision.
M0 may map alternatives, but cannot approve one. A changed margin requires
a separate, written business-risk rationale, adopter acceptance, independent
review, and decision-owner approval before any comparative pilot. Publish
the original and replacement claims; affordability alone is not a rationale.
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
estimate with repeated-task precision. The maintainer proposes the cost
dictionary, setup bands, and role-rate scenarios; the adopter's engineering
lead and independent methods reviewer approve them in M1 before the pilot.
Band widths must cover documented estimation uncertainty; tighter ranges
need written evidence, not a convenient universal percentage. Record names,
estimates, uncertainty sources, and classifications, including training
versus research-only work. Report the measured setup, bands, and break-even
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
**Error control is across the two positive decision branches.** Allocate
alpha = 0.025 to LANGUAGE EARNS CONTINUATION and alpha = 0.025 to WORKFLOW
VALUE. Within each branch, every statistical component must pass at that
branch's level: use valid **one-sided 97.5% bounds**, including pessimistic
registered cost inputs. The branch is an intersection-union test; if any
required component is false, passing the entire conjunction has probability
at most 0.025. No further within-branch multiplicity penalty is needed.

The union bound therefore limits any false positive branch claim to 5%,
without assuming independent components or branches. Section 8's precedence
selects the reported outcome; precedence alone is not error control. These
are decision bounds, **not simultaneous confidence coverage** of every
quantity. Descriptive 95% intervals may also be shown but cannot adjudicate
the gates. M4 validates the estimators under the actual clustered design.
See [intersection-union theorem 17.3](https://bookdown.org/jkang37/stat205b-notes/lecture13.html#thm:thm17003).

Review time, agent iterations, context read, and prevented violations are
secondary explanations. Neither a fivefold point-estimate defect reduction
nor a better token score can rescue failure of the primary economic route.

### 6.3 Size first; calibrate only if a credible route exists

#### M0 desk screen: conditional outputs, no implementation authority

These are single-rate, zero-event illustrations, not Calor results or the
paired risk-difference design. For n independent binary observations the
upper bound is `1 - alpha^(1/n)`:

| Independent observations n | Descriptive 95% bound | Branch-level 97.5% illustration |
|---|---|---|
| 20 | 13.91% | 16.84% |
| 30 | 9.50% | 11.57% |
| 50 | 5.82% | 7.11% |
| 100 | 2.95% | 3.62% |
| 299 | <= 1.00% | 1.23% |
| 368 | 0.81% | <= 1.00% |

The 1% crossovers are 299 and 368 observations respectively. For scale,
an unpaired approximation with equal 3% event rates, zero true difference,
1-point margin, alpha 0.025 and 80% power gives about 4,570 per arm:
`n = (1.960 + 0.842)^2 * 2 * 0.03 * 0.97 / 0.01^2`.
Pairing, accepted-only denominators, and clustering require the actual
methods, not substitution of these figures as final sample sizes.

Capacity and task supply are separate constraints. At 20 minutes per slot,
40 available review hours buy 40 three-arm triplets with one repetition;
all 80 hours buy 80, before pilot reservations and other duties. If actual
eligible historical supply H is 50, the 75% historical-weight floor allows
at most `floor(H / 0.75) = 66` equal-weight clusters across both pools,
before further source-linkage restrictions. This is an upper bound, not a
promise that independent authored clusters exist. Final capacity must also
subtract pilot history and review use. **H has not been measured here**;
the minimum 50 future requests does not imply H = 50.

Before calculating, register scenario ranges, methods, and the following
output rules. Size the original 1-point margin first. Map margins
{1, 2, 5, 10} points against review capacities {40, 80, 150, 300} triplets
and inventoried or explicitly hypothetical task supplies. Larger margins
are tradeoff illustrations, not acceptable risk by default. Record the
adopter/owner's independently justified risk limit separately; absent
approval, 1 point remains the only adoption claim.

For each row, show every gate's required sample/cost range, both positive
decision scenarios, source-task limits, and assumptions about pairing,
event rates, completion, cost variation, setup, and reviewer dependence.
Compute available final n from **both** review capacity and independent
task supply after pilot reservations. M0 uses analytic bounds/approximations
with limitations disclosed; an indeterminate calculation is not a pass.

| M0 output | Rule and next action |
|---|---|
| FEASIBLE AS PROPOSED | Conservative registered scenarios show a credible joint route under the original gates, candidate resources, and evidenced task supply. Refer to M1 authorization; this is not final power confirmation. |
| REQUIRES SEPARATE APPROVAL | A credible conditional row requires specified resource, risk, or scope changes. Refer only that proposal to M1 review; no implementation until business approval and updated sizing establish the approved route. |
| NOT FEASIBLE | Even the most favorable registered, defensible scenario needs more than available capacity/supply for every acceptable row. Publish the scoped stop; no automatic exploration or funding. |
| INSUFFICIENT INFORMATION | Missing inventory/estimates or unresolved method uncertainty prevents the above dispositions. Name the smallest information request, owner, and deadline; M1 may approve only that bounded inquiry. Deadline expiry stops the study with this label retained, not converted into evidence of no demand or no value. |

Apply rules in order; favorable-only scenarios cannot yield FEASIBLE AS
PROPOSED. The independent methods reviewer countersigns the memo before
M1 may authorize follow-on work. Unknown supply stays unknown, not zero
or an invented large pool. A stop is scoped to the registered scenario
envelope, not a proof that no future experiment could work.

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
with no paid agent collection, new acceptance service, or adopter
commitments. Its authorization also names the independent methods reviewer
and budgets review time explicitly against the non-maintainer allowance.
It may precede v0.19; roadmap approval alone does not authorize execution.

M0 reports recruitment status since Call W as **not attempted**, **attempted
without commitment**, **participant secured**, or **unknown**, with dated
aggregate evidence. Do not equate unattempted/unknown recruitment with a
failed search. Either can leave demand unproven without implying rejection.

| Approval responsibility | Required owner and decision |
|---|---|
| Resources and investment | Repository maintainer: authorize M0/inquiries, decide resource proposals and final action; no self-approval of independent methodological review. |
| Methods and M0 disposition | Independent methods reviewer: approve scenario rules before calculation, countersign M0, approve estimators and final interpretation. |
| Business risk and cost assumptions | Adopter engineering lead plus independent methods reviewer: approve safety acceptability, cost classification, setup/rate uncertainty; maintainer signs any replacement claim. |
| Protected-C# credibility | Qualified non-maintainer C# reviewer: sign B's design before C integration; may also fill another compatible role. |
| Historical supply and consent | Adopter engineering lead: confirm inventory, workload horizon, research agreement and exit promises in M1. |
| Correctness follow-through | Compiler maintainer: record ownership/disposition of the false-established search even if comparison work stops. |

Enter actual assignee names and dated decisions in the authorization record;
these roles are not claims that anyone has agreed. Missing required approval
blocks that action. M1 review-only entry under a conditional M0 output may
address requirements or bounded information requests, not start M2-M4.

If the original comparison is not feasible, record that before considering
an **exploratory option**. A separate proposal must name one uncertainty,
the smallest artifact needed, a fixed dataset, allowed claims, a time/spend
ceiling, and a stop decision. Prefer existing tools and an in-house example.
A friction study can estimate human minutes and repair costs; a mechanism
probe can find false guarantees. Neither can pass section 6, downgrade its
safety gate to descriptive reporting, cross Call 3, or authorize full
section 4 implementation. A promising exploration may justify proposing a
new study, not automatically continuing language investment.
Maintainer-only timings must be labeled **author time**, not adopter review
savings. Any external-review-time claim needs a non-maintainer participant
and must still disclose the exploratory sample and its limits.

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

M1 may review FEASIBLE AS PROPOSED or REQUIRES SEPARATE APPROVAL outputs;
INSUFFICIENT INFORMATION permits only an explicitly bounded inquiry. M1
**implementation authorization** additionally requires an approved, sized
route, all named approvals, and v0.19's published release/epic disposition.
Only then does the delivery clock start. M0 authorization fixes the M1
decision/inquiry deadline; expiry cannot trigger automatic extension.
M1 publishes actual dates and critical-path estimates within the ceiling.
Overlapping milestones do not create extra capacity.

| Milestone | Dependency | Deliverable and stop condition |
|---|---|---|
| M0: desk feasibility | Limited authorization; named methods reviewer | Original-gate sizing, conditional frontier, B sketch, actual/scenario task supply, and countersigned four-way output. No implementation authority. |
| M1: review then authorize | M0 route or bounded inquiry; v0.19 disposition required for implementation | Resolve stated conditions, independently approve any business-risk change, and confirm governance, B design, inventory, cost bands, people, dates, and resources. Only completed authorization opens M2. |
| M2: build the smallest credible comparison | M1 implementation authorization complete | Working A/B/C and shared protected service; separately budgeted matrix-wide mechanism suite. Missing credible comparator or trust boundary stops collection. |
| M3: establish independent task evidence | M1 inventory; M2 runnable | Shared oracle, deterministic references, disjoint pilot/final pools, reviewer assignment, boundary cases. Insufficient independent task supply stops. |
| M4: calibrate once and freeze | M2-M3 entry criteria pass | Pilot report, executable joint-power/cost analysis, fixed final counts, exact estimators, pins, and funded collection dates. Infeasible means stop, not a bar change. |
| M5: collect without tuning | M4 frozen and feasible | Complete paired runs, timed reviews, immutable submissions, costs and hidden outcomes. Budget stop or soundness defect cannot pass. |
| M6: publish and decide | M5 complete or any stop | Report, required independent interpretation, obligations and correctness-work disposition, and investment decision. A stop does not require later implementation or a package release. |

v0.19 retains ownership of both epics; this roadmap neither reopens their
tasks nor silently transfers them to v0.20. Its entry disposition must list
every finding's resolution or explicit deferral. A deferred audit item
touching the experiment's guarantees must be resolved or explicitly
excluded with an enforced restriction.
Website defects do not become new proof work; their remediation must keep
eventual claims and instructions aligned with the actual experiment.
On any stop, the compiler maintainer records a disposition for section 4.4's
broader false-established search: covered by existing #1182 work, proposed
as a separately scoped correctness follow-up with a named owner, or explicitly
deferred with rationale and risk. Known audit fixes remain v0.19 obligations.
Do not silently enlarge that epic, drop the search, or automatically fund
an experimental acceptance service.

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
Preserve INSUFFICIENT INFORMATION when an M0 inquiry expires unresolved;
it is a stopped investment, not evidence of infeasibility or rejected demand.
REQUIRES SEPARATE APPROVAL is pending, never a positive experimental result.

Exploratory evidence is **EXPLORATORY ONLY**, outside this outcome table's
positive decisions. It cannot produce LANGUAGE EARNS CONTINUATION or
WORKFLOW VALUE by omitting a safety gate. Record a stopped full comparison
separately from any later authorized exploration.

One domain and one model do not support a general language superiority claim.
Wider claims require separately funded replication. A later model or adopter
may justify a new experiment, but not a rewrite of this outcome.

**Release rule:** a memo or stop decision alone does not produce a v0.20
NuGet package, release tag, or version bump. Close the investigation with
its decision record; the next version number remains for actual software.
If separately authorized implementation ships, its release plan must name
the artifacts, supported guarantees, and normal release criteria. Neither
a green experiment nor a stop memo substitutes for those criteria.

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

## 10. Definition of done for this investment decision

- [ ] Current v0.18/v0.19 status is recorded; final dispositions gate implementation, not an early M0 stop.
- [ ] M0 sizes the original claim and conditional options, records supply assumptions, and receives independent sign-off.
- [ ] If pursued, M1 approves governance, the business case, B design, cost bands, people, and a reconciled finite budget.
- [ ] If pursued, credible A/B/C paths and the full supported-matrix boundary checks pass.
- [ ] If pursued, independent tasks and reviewers exist, with source-cluster pilot/final separation.
- [ ] A powered, affordable, frozen design exists, or a documented pre-collection stop replaces downstream work.
- [ ] Every executed outcome and cost is retained; no favorable-only reporting.
- [ ] The report states the supported claim, uncertainty, and exclusions.
- [ ] The decision owner records and applies section 8's investment action.
- [ ] Any stop preserves support obligations and a named disposition for correctness work; no memo-only release is cut.

The investigation may finish with a documented stop. Actual software
delivery has a separate definition of done; no unfinished experiment is
a demonstrated advantage.

## 11. Review disposition

Draft v3 responds to the second 2026-09-08 critique, against `85ae1c54`.
The first-round dispositions remain in that commit. This revision retains
the original safety margin, strong C# control, total-cost primary, protected
requirements, and refusal to fund mechanisms automatically. No independent
human approval or experimental result is claimed.

| Round-2 item | Draft v3 disposition |
|---|---|
| S1 / R1: conditional M0 | Accepted four outputs and a requirement/resource frontier. Mapping risk does not approve it; M1 has a review-only path before implementation authorization. |
| S2 / R3: task-supply ceiling | Accepted inventory and explicit supply accounting. H = 50 is conditional, not implied by the minimum future workload; linked variants are not new independent tasks. |
| S3 / R2: multiplicity | Accepted intersection-union branches, each at alpha 0.025 with one-sided 97.5% component bounds. Dropped simultaneous-coverage requirements and the six-way illustration; precedence is not error control. |
| S4: mechanical screen and recruitment | Registered output rules, independent M0 sign-off, bounded inquiries, and explicit attempted/not-attempted/unknown recruitment states. Favorable-only sizing cannot authorize the study. |
| S5 / R4: cost-band ownership | Named proposer and approvers; uncertainty must be evidence-based, not a mandatory arbitrary percentage. |
| S6-S7 / R5: correctness and release | Named disposition for the broader correctness search on every stop; existing audit obligations remain v0.19's. No package/tag/version bump for a memo alone. |
| S8-S10 / R6: smaller clarifications | Author time distinguished from external review, static restricted-subset enforcement explicit in B, and confidence-bound meaning retained. |

Review should now check the conditional authorization paths, branch error
control, and whether supplied inventory/cost assumptions can support M0.
It must not treat this document as approval to execute any stage.
