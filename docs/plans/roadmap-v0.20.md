# Roadmap - v0.20 "Earn the Language"

**Created:** 2026-09-08

**Status:** Proposed decision roadmap; no experiment run or spending authorized.

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

For the v0.19 fixes, carry forward discriminating regressions for false
postcondition proofs, contract simplification, assignment effects, and
numeric semantics (#1183-#1185 and #1189), plus any other findings touching
the chosen subset. Issue closure is not a substitute for these artifacts.
Keep runtime guards in this prototype; retaining guards does not repair an
unsound proof, so false-established results still block the experiment.

## 5. Experiment: three credible workflows

| Arm | Language and tooling | Protection |
|---|---|---|
| A: ordinary C# | Current C#, Roslyn/NRT/analyzers, normal tests, and agent-generated tests or other practical evidence | Common protected requirements and runner; normal review, no new domain-specific proof gate |
| B: protected C# | A plus a credible implementation of the protected-rule workflow using analyzers, verification tools, restricted patterns, and/or runtime enforcement | Same independently approved rules and authority separation as Calor |
| C: protected Calor | Calor in the same C# host, using the supported contracts/effects and protected acceptance prototype | Same independently approved rules and authority separation as B |

All arms may use practical existing tools, including formal tools in C#.
Do not require B to analyze unrestricted C#: it may use the same restrictions
as C. Do not ban a good C# solution because it makes Calor less distinctive.

A C#-experienced non-maintainer reviews B for credibility, diagnostics, and
missing readily available tools. Give B and C equal prototype engineering
allowances, log actual effort, and record Calor's existing sunk investment
separately. If a credible B cannot be delivered within the allowance, the
comparison is **not ready**; that is not evidence Calor won.

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

Keep mutation-injected failures and deliberate bypass probes as a separate
mechanism suite. Do not pool them with natural tasks to inflate real-world
defect rates. Do not make tasks harder merely because strong C# succeeds.

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

### 5.2 Assignment, reviewers, and execution

Use one pinned primary model/agent configuration in all arms. Freeze model
ID, settings, tool versions, hardware, dependency versions, prices, and
time/token/iteration budgets. Randomize arm execution order and pair runs
by task. A second model is optional descriptive replication only; it cannot
rescue a primary failure. A model change mid-epoch invalidates comparison.

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
spend. Approve or amend them on business grounds in M1 before any comparative
pilot. Once pilot outcomes are visible, do not relax them to obtain power
or a win. M4 freezes the complete statistical implementation before final
collection. v0.20 uses one primary economic route, not a choice between
several favorable metrics after seeing results.

### 6.1 Outcomes and denominators

An **accepted change** is approved by the normal arm workflow within budget.
A **correctly accepted change** also passes all independent held-out
behavioral requirements and the protected-boundary checks.

The primary outcome is **total cost per correctly accepted change**:

`(adopter setup cost / 50 + mean operating cost per assigned slot) /
fraction of assigned slots correctly accepted`.

Use equal task weights; average repeated runs within task before aggregation.
Operating cost includes all run, review, repair, and replacement-execution
costs, including failed slots. Zero correct acceptances means infinite cost,
not a missing observation. Record API/compute dollars and human hours
separately, and convert hours at the same preapproved rate across arms.

The primary adoption horizon is **50 assigned change requests**, not 50
successful changes or the experiment's sample count. M1 must confirm that the
adopter expects at least 50 relevant requests within 12 months; otherwise
the proposed business case fails before the pilot. Allocate specification,
integration, onboarding, and training costs over that horizon. Also publish
first-change cost, unamortized totals, cost at 10 and 100 changes, and the
observed break-even volume. Shared specification effort is charged equally;
arm-specific translation and maintenance are charged separately.

Prototype/tool R&D is reported separately from adopter setup and operating
cost. Report both the adoption result and the total project investment;
do not hide a large tooling build behind cheap subsequent executions.

**Serious escaped defect:** an accepted artifact violates a preclassified
business-critical requirement in the independent evaluation. Count at most
one serious-defect event per assigned slot, regardless of failing test count.
Report both events per all assigned slots and events per accepted slot.
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

### 6.3 Make the statistics feasible before spending on final runs

Use one disjoint calibration pilot, never reused as confirmatory evidence.
Estimate task-level variation, failure/event rates, review-time variation,
and setup costs. More agent repetitions of a few fixtures do not create
more independent business tasks.

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

The following **proposed ceiling** is for approval in M1, not permission to
spend: eight calendar weeks, 30 engineering person-days, $1,000 pilot
API/compute plus $5,000 confirmatory API/compute, and 80 total non-maintainer
hours for specification, comparator review, task adjudication, and timed
review. Price all human effort, including maintainer time, in the report.
Do not borrow unused v0.18 budget or start paid runs on roadmap approval alone.

| Milestone | Target window | Deliverable and stop condition |
|---|---|---|
| M1: choose and price the decision | Week 1 | Adopter/domain, demand evidence, v0.19 readiness matrix, approved thresholds/rates/horizon, people and spending authorization. Missing demand, readiness, or resources stops the experiment. |
| M2: build the smallest credible comparison | Weeks 2-3 | Working A/B/C paths and protected service; equal B/C prototype allowances capped at five engineering days each. Missing credible comparator or trust boundary stops collection. |
| M3: establish independent task evidence | Week 4 | Sampling frame, shared behavioral oracle, disjoint pilot/final pools, reviewer protocol, positive/negative boundary cases. No useful independent task supply stops the experiment. |
| M4: calibrate once and freeze | Week 5 | Pilot report, joint-power/cost simulation, final task count, exact analysis, all pins and funded collection schedule. Not affordable or statistically decidable means NOT FEASIBLE. |
| M5: collect without tuning | Weeks 6-7 | Complete paired runs, timed reviews, immutable submissions, costs and hidden outcomes. Budget stop or soundness defect cannot pass. |
| M6: publish and decide | Week 8 | Reproducible report, independent interpretation, adopter handoff, and explicit investment decision. No automatic extension. |

Read existing harnesses, telemetry, gates, and SDK paths first; reuse what
meets the protocol. New commands, configuration flags, or schemas mentioned
in implementation proposals are not assumed to exist. M2's implementation
PR must name actual integration points and their regression coverage.

M1 can begin after v0.19's release disposition. v0.19 retains ownership of
both epics; this roadmap neither reopens their tasks nor silently transfers
them to v0.20. Any deferred audit item touching the experiment's guarantees
must be resolved or explicitly excluded with an enforced restriction.
Website defects do not become new proof work; their remediation must keep
eventual claims and instructions aligned with the actual experiment.

## 8. Published outcomes and mandatory actions

| Outcome | Evidence | Action |
|---|---|---|
| LANGUAGE EARNS CONTINUATION | Every section 6 gate passes versus A and B; adopter handoff succeeds | Continue only in the measured domain. Publish exact scope, costs, margins, model, and remaining trust assumptions. |
| WORKFLOW VALUE, LANGUAGE NOT JUSTIFIED | B passes the same economic/completion/safety adoption gates versus A (with B substituted for C), but Calor does not pass all gates versus both C# arms; B also passes boundary and adopter gates | Prioritize C# workflow tooling; keep Calor in maintenance unless a separately approved new hypothesis appears. |
| TARGET NOT MET | Valid complete data fail one or more continuation gates and the C# workflow outcome above does not apply | Stop broad safe-delegation feature expansion. Report whether intervals rule out the target or leave it uncertain; failure to pass is not equivalence. |
| NOT FEASIBLE / DEMAND UNPROVEN / INVALID | No funded powered design, no adopter, incomplete collection, or a protocol/trust defect | Publish the reason and spent resources. No advantage claim and no automatic extension. |

Decision precedence: INVALID first, then LANGUAGE EARNS CONTINUATION,
then WORKFLOW VALUE, then TARGET NOT MET. Pre-collection stops use the
specific NOT FEASIBLE or DEMAND UNPROVEN label.

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
- [ ] M1 approves the real business case, thresholds, people, and finite budget.
- [ ] Credible A/B/C paths and the protected boundary pass entry requirements.
- [ ] Independent tasks and reviewers exist, with pilot/final separation.
- [ ] A powered, affordable, frozen design exists, or NOT FEASIBLE is published.
- [ ] All assigned outcomes and costs are retained; no favorable-only reporting.
- [ ] The report states the supported claim, uncertainty, and exclusions.
- [ ] The decision owner records and applies section 8's investment action.

A release may finish with a documented stop rather than a positive result.
It must not call an unfinished experiment a demonstrated advantage.
