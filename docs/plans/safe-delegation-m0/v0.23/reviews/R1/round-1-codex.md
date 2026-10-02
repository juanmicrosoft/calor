# R1 adversarial review, round 1 (Codex)

| Field | Value |
|---|---|
| Date | 2026-10-01 |
| Artifact commit | `7d403e82e13b88081f6dcc24f01d17a78b829ae9` |
| `r1-decision-rules-v1.md` sha256 | `e48680356ee70c7ee3872fe1d4531cc81f10f159ef5206bb2f4b1eca469a3a3e` |
| `r1-decision-rules-v1.json` sha256 | `47351327f121a8387fcae6b7c24b55d6145168afd2d066d84a7ea531fe518dd3` |
| `r1-methods-governance.md` sha256 | `1fcfa7807dab5f1fa962444b9f6abf12bbc0317239df087157fd9046930c223b` |
| `codex-prompt-template.md` sha256 | `cda815b2ca0ce6805600d3e91d15355ac0712f89ba86e771bdefbd9392efde75` |
| Effective prompt | [round-1-prompt.txt](../../r1/round-1-prompt.txt), sha256 `c23ca0a8f9cb0caefecaada68b72864c97c9273779c38079fdcd0321bba2a54d` |
| Tool | Codex CLI 0.159.2, `codex exec -c model_reasoning_effort="high" -s read-only --ephemeral --ignore-user-config` |
| Model | `gpt-6.1-sol` (from session header) |
| Session id | `01a0f8a8-d9c7-7ed3-bf48-43d225582fc2` (ephemeral) |
| Tokens | 80,815 |
| Full log | [round-1-codex.log](../../r1/round-1-codex.log) |
| Verdict | `VERDICT: blocking objections remain` (13 blocking, 2 major, 0 minor) |

## Codex output (verbatim)

1. [blocking] The null scenarios are mathematically inconsistent

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:118), §4.3; JSON `scenarios`.

   **Defect:** “0.50 for every ratio” cannot describe one three-arm population: `C/A = (C/B) × (B/A)`, so three ratios of 0.50 are impossible. Likewise, three completion differences of −5 pp violate `C−A = (C−B) + (B−A)`. The mixed-null instruction also combines W boundary values with L alternatives that constrain the same arm quantities incompatibly. R4 must invent an interpretation to execute these supposedly frozen scenarios.

   **Minimal fix:** Specify coherent arm-level joint distributions for each validation scenario. Derive contrasts from them, enforce denominator and outcome constraints, and identify which branches are actually null. If scenarios are branch-specific, say so and separately define coherent scenarios for validating the complete ordered procedure.

2. [blocking] The validation rule permits error rates above the promised alpha

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:108), §4.2; JSON `estimator_validation`.

   **Defect:** The permitted simulated rejection rate is the target **plus** a Monte Carlo allowance. At 10,000 replicates, a branch estimate of approximately 2.902% passes a claimed 2.5% limit; a family estimate of approximately 5.561% passes a claimed 5% limit. Even at 40,000 replicates, the ceilings remain approximately 2.701% and 5.281%. This tests whether inflation is conspicuous, rather than establishing the advertised error control.

   **Minimal fix:** Require a valid upper Monte Carlo confidence bound to remain below the target, with a preregistered validation error allowance. Calibrate methods conservatively enough to meet that requirement, or explicitly distinguish inconclusive validation from demonstrated control.

3. [blocking] Branch rejection checks do not validate the individual decision bounds

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:103), §§4.2–4.3 and 7.1.

   **Defect:** Validation checks branch rejection under a few specified alternatives. Other gates can suppress branch rejection and conceal an anti-conservative component bound. Yet component bounds subsequently determine required sample sizes and `SIZED_INFEASIBLE` states. Testing mixed nulls with every other component at one moderate alternative does not establish the required marginal bound validity or identify the worst branch-null configurations.

   **Minimal fix:** Validate each component’s one-sided coverage/error control over the registered admissible configurations. Include branch-null cases where other gates pass with high probability, and specify how adverse nuisance configurations are searched. Require an analytical validity argument or clearly bounded validation scope.

4. [blocking] Monte Carlo uncertainty is uncontrolled across searches and repeated looks

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:147), §§6.1 and 6.3.

   **Defect:** The 2.576 multiplier supplies only a pointwise uncertainty allowance. R4 may search sample sizes, designs, nuisance corners, interior points, and scenarios, then rerun an indeterminate result using another seed. Neither the number of opportunities nor the aggregate probability of a mistaken sizing classification is controlled. Selecting extrema or a first conclusive result using these intervals destroys their pointwise interpretation.

   **Minimal fix:** Freeze the search and stopping procedure and allocate a separate Monte Carlo error budget across its decisions. Use simultaneous bounds, valid sequential bounds, or an independent confirmation stage with appropriately controlled error.

5. [blocking] The executable classifier omits joint branch power

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:137), §§5, 7.1–7.2; JSON `gate_states` and `classification_order`.

   **Defect:** Section 5 requires 80% power for each complete branch. Section 7 instead classifies individual components and routes to NOT FEASIBLE only when a component is infeasible. Individual power does not establish joint power: two gates can each have 85% power but only 70% joint power. Consequently, §5 can require NOT FEASIBLE while §7 produces INSUFFICIENT INFORMATION because no individual proxy component is infeasible. Shared component IDs also leave unclear how R4 records different L and W sizing results.

   **Minimal fix:** Add explicit branch-level sizing states, based on the probability of the correct outcome under the complete ordered procedure. Index component results by branch and component. Make joint infeasibility an explicit classifier condition.

6. [blocking] The bounding procedure does not establish genuine extrema

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:147), §6.1.

   **Defect:** Independently registered parameter intervals do not necessarily form a feasible joint box. For example, binary discordance must be at least the absolute difference between completion probabilities; selecting discordance zero alongside a +5 pp completion difference is impossible. Logical ranges can also have unattained or infinite endpoints. Corner searches plus interior points “its own validation shows” are not a guarantee of either extremum, and no corresponding search requirement is specified for the most-favorable case.

   **Minimal fix:** Define a constrained joint parameter space. Require justified global bounds or a certified optimization procedure for both extrema. Specify treatment of infeasible combinations and unattained limits; unresolved optimization uncertainty must produce INSUFFICIENT INFORMATION.

7. [blocking] Decision-bearing range selection remains open after supply inspection

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:39), §2 “Evidenced range” and “Approved study envelope”; §§6.1 and 11.

   **Defect:** R4 need only register ranges before running the estimator. By then the supply inventory can already be known. The maintainer can similarly set the envelope after seeing supply. An endpoint qualifies as “evidenced” merely by citing a recorded source; there are no rules for relevance, uncertainty, conflicting sources, or interval construction. This permits tailoring ranges and capacities to known supply without violating the written freeze.

   **Minimal fix:** Before supply inspection, freeze either the ranges and envelope or deterministic rules for deriving them. Those rules must specify eligible evidence, uncertainty treatment, conflicts, reservations, and approval timing. Subsequent discretionary changes must undergo amendment control.

8. [blocking] `S_high` cannot bound records whose existence is unknown

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:161), §6.2.

   **Defect:** Adding “every cluster” unextracted because of access failure requires knowing how many such clusters exist. A missing page, inaccessible repository, or incomplete enumeration can leave that number unknown. The rule supplies no treatment for an unknown denominator, allowing a finite observed count to masquerade as an upper bound and fabricate scarcity. The formula also mixes a pilot-subtracted `S_low` with an instruction to take pilot reservation as zero.

   **Minimal fix:** Define bounds from explicit confirmed, unresolved, and unenumerated quantities. Without a certified enumeration bound, make the upper supply bound unknown or unbounded and disable the scarcity verdict. Write the pilot arithmetic unambiguously.

9. [blocking] Historical-only supply silently removes the permitted authored contribution

   **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:161), §§6.2 and 7.2; original Draft v3 §§5.1 and 6.3.

   **Defect:** R4 compares required independent clusters with historical `S_high`. The original design permits independently authored clusters while requiring at least 75% historical primary weight in each pool. Fifty historical clusters plus ten independent authored clusters could support a 60-cluster pool with 83.3% historical weight; the proposed comparison can reject it because 60 exceeds 50. This is a stricter supply design, not faithful sizing of all permitted original rows.

   **Minimal fix:** Model historical and independently authored supply separately, preserving linkage and the 75% floor in both pools. Do not invent authored supply, but do not exclude an evidenced permitted contribution. Unknown authored capacity must not become zero capacity.

10. [blocking] Proxy infeasibility is asserted to transfer without a bounding argument

    **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:253), §7.3.

    **Defect:** The rules correctly say AI acceptance and repository-derived oracles change the original measurement. They nevertheless let proxy component sizing establish infeasibility of a necessary original component, asserting that most-favorable statistical parameters remove the distinction. They do not establish that proxy-derived ranges cover original-endpoint parameters, or that proxy sample requirements lower-bound original requirements. Oracle omissions and changed criticality alter outcome ascertainment, not merely reviewer variance.

    **Minimal fix:** Permit this route only with a documented mathematical lower bound applicable to the original endpoint over its admissible parameter space. Otherwise report proxy-design infeasibility separately and retain INSUFFICIENT INFORMATION for the original claim.

11. [blocking] The 50-request workload gate has disappeared

    **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:55), §3; original Draft v3 §6.1 and `supply-status.md`.

    **Defect:** The original requires confirmation that the adopter expects at least 50 relevant assigned requests within 12 months; otherwise the business case fails before the pilot. Retaining `/50` in the cost formula does not retain this eligibility threshold. None of G1–G6 or the classifier checks it. An adopter with a successful one-change demonstration but insufficient workload could satisfy the listed adoption prerequisites.

    **Minimal fix:** Add the future-workload threshold as an explicit component, distinct from historical inventory and the amortization denominator. In this domain, record it as unavailable rather than infer it from public history.

12. [blocking] Missing held-out evaluation of an accepted artifact has no valid treatment

    **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:188), §6.4.

    **Defect:** Missing review becomes a failed slot, but missing correctness or severity evaluation **after acceptance** is not covered. An accepted artifact whose oracle crashes has unknown correctness and unknown escape status. Calling it a failed slot does not justify recording zero escapes or removing its acceptance indicator. Generic “invalid-slot rate” registration supplies no outcome-vector treatment.

    **Minimal fix:** Specify missingness separately for acceptance, correctness, and severity. Preserve known acceptance and cost records. Unresolved held-out evidence must trigger incomplete/invalid study treatment or an explicitly conservative bound; it cannot silently become a zero escape.

13. [blocking] Countersignature provenance is insufficient to enforce the governance claims

    **Location:** [r1-methods-governance.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-methods-governance.md:36), §§3, 5.1, 7–8; decision rules §§7.5 and 9.

    **Defect:** A proposer-controlled file containing a verdict and hashes satisfies the countersignature format. No independently captured invocation or transcript binds that verdict to the actual engine call. The hashed template also does not bind the complete effective prompt, appended dispositions, repository instructions, or authoritative dependencies. The ledger requirement does not define how retries and aborted calls count against five rounds. These gaps allow fabricated provenance, review shopping, or changed interpretation inputs while the four normative hashes remain identical.

    **Minimal fix:** Require machine-captured invocation and transcript records with independently checkable provenance. Bind complete review inputs and dependency revisions. Define attempt counting and retries, retain every attempt, and verify the binding at merge. Explicitly disclose remaining trust in the capture operator.

14. [major] NOT FEASIBLE takes precedence over the conditional-approval route

    **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:219), §7.2; original Draft v3 §6.3.

    **Defect:** Exceeding the current envelope triggers rule 2 before rule 5 can recognize an evidenced resource change. The original distinguishes a credible conditional row requiring separate approval from infeasibility across every acceptable row. The proposed ordering suppresses that distinction. This is major rather than blocking by itself because both positive feasibility classifications are explicitly unreachable in the current domain.

    **Minimal fix:** Evaluate preregistered conditional rows before declaring envelope infeasibility, or define precisely which rows and resource changes the NOT FEASIBLE verdict excludes.

15. [major] Non-statistical gates lack appropriate reporting states

    **Location:** [r1-decision-rules-v1.md](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ad322c44fed62f597/docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md:205), §§7.1–7.2.

    **Defect:** R4 must report exactly one listed state per component, but no state represents a fully evidenced non-statistical route. `SIZED_FEASIBLE` requires statistical power; `ROUTE_MISSING` denotes absence; `INDETERMINATE` does not describe an established route. The classifier nevertheless consumes an “evidenced credible route” outside that state scheme, without defining its evidence requirements.

    **Minimal fix:** Add explicit non-statistical route states and frozen evidence checklists. Distinguish an established route, missing prerequisites, unresolved evidence, and a known blocking defect.

VERDICT: blocking objections remain

## Dispositions (Claude, proposer)

Fixing commit for every ACCEPTED objection below: `9e7ee8159681164488808fd4efe1ec68d57685e0`.

Every objection was accepted. The main structural response: v1 no longer lets
simulation, nuisance ranges, or an envelope decide any classification. The
only route to NOT FEASIBLE is a method-independent analytic bound on the
number of independent clusters needed by the +1 pp safety gate
(new rules section 7.4 and `r1-safety-nmin.py`), compared against a supply
upper bound that never counts missing or unenumerated items as ineligible.

| # | Severity | Disposition | Change |
|---:|---|---|---|
| 1 | blocking | Accepted and fixed | Section 4.3 rewritten at arm level (`a`, `k`, `e`, `m`); contrasts are derived, not specified independently; infeasible combinations are rejected. Global nulls made coherent (L: A=B, C at every L boundary; W: B at every W boundary, C equal to B). |
| 2 | blocking | Accepted and fixed | Section 4.2: Monte Carlo upper 99.5% bound on the rejection rate must be <= 0.025 (branch) / <= 0.05 (family); no "plus Monte Carlo error" allowance. |
| 3 | blocking | Accepted and fixed | Section 4.2 adds component-level validity at every registered boundary configuration and branch nulls where every other component passes with probability >= 0.99. |
| 4 | blocking | Accepted and fixed | Section 4.2: R4 pre-registers the finite list of K evaluations; Bonferroni-adjusted Monte Carlo bounds with total Monte Carlo error 0.01; the reseed-on-indeterminate rule is removed. Simulation is also made descriptive under v1, so it cannot decide a classification. |
| 5 | blocking | Accepted and fixed | States are indexed by (branch, component); branch power is defined on the complete ordered procedure. NOT FEASIBLE now rests on a necessary condition (one component's method-independent bound), which implies branch infeasibility; joint power is never inferred from components. |
| 6 | blocking | Accepted and fixed | The bounding/extremum procedure no longer decides anything. The analytic bound minimizes over the single nuisance `c` on a coherent, explicit grid `[0, 0.05]` (coherence from `e <= a - k`), with the minimum attained at a closed-form endpoint (`c = 0`). |
| 7 | blocking | Accepted and fixed | No range, envelope, or alternative can be set after inspection: v1 decisions depend only on frozen constants (`N_MIN = 172`), the frozen supply formulas, and R3's frame, which must be frozen before R2B. The envelope route is removed. Changing a registered alternative after inspection is prohibited (section 10.4). |
| 8 | blocking | Accepted and fixed | Section 6.1 defines `H_conf`, `H_unres`, certified `H_enum_max`, and `H_high`; with no certified enumeration, `H_high` is unbounded and the supply route is disabled. Pilot arithmetic is explicit (zero pilot in `F_high`). |
| 9 | blocking | Accepted and fixed | `F_high = floor(H_high / 0.75)` credits authored clusters at their 25% cap, neither inventing more nor setting them to zero. |
| 10 | blocking | Accepted and fixed | Section 7.4 gives the bounding argument: the Neyman-Pearson power of a simple-vs-simple test bounds every valid test of the composite null, for any binary per-cluster escape endpoint with this margin, independent of how outcomes are ascertained. The verdict is scoped to the frozen frame's supply, not to the original adopter domain. Proxy components otherwise cannot reach a sized state. |
| 11 | blocking | Accepted and fixed | New component G0 (workload horizon >= 50 assigned requests in 12 months), NOT MEASURABLE IN DOMAIN; forward public supply cannot substitute for it. |
| 12 | blocking | Accepted and fixed | Section 6.2: missing held-out evaluation for an accepted artifact keeps acceptance and cost; the outcome is imputed adversarially to the claim (subject: incorrect and escape; comparator: correct and no escape); more than 5% such imputations in any arm makes the epoch INVALID. |
| 13 | blocking | Accepted and fixed | Governance section 5.1 "Provenance and attempts": complete effective prompt and full CLI log committed per attempt; persisted sessions from round 2 for maintainer-side verification; attempt counting and one retry; verdict-bearing attempts cannot be discarded; clean tree at the artifact SHA; residual trust in the capture operator disclosed. |
| 14 | major | Accepted and fixed | Envelope route removed, so NOT FEASIBLE can no longer pre-empt a conditional-approval route on resource grounds. The supply route concerns supply that no resource change can create within the frozen frame. |
| 15 | major | Accepted and fixed | Section 7.1 adds `ROUTE_ESTABLISHED`, `ROUTE_MISSING`, `ROUTE_BLOCKED`, `ROUTE_UNRESOLVED`; section 7.6 freezes checklists for G4 and G6.1. |
