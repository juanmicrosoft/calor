# R1 adversarial review, round 2 (Codex)

| Field | Value |
|---|---|
| Date | 2026-10-01 |
| Artifact commit | `9e7ee8159681164488808fd4efe1ec68d57685e0` |
| `r1-decision-rules-v1.md` sha256 | `9327fc4bec35e6b4a69280d26d423483c488be13fbba10cd4f661c7e3f0c2099` |
| `r1-decision-rules-v1.json` sha256 | `d9b11814737e133d500ad22375a694e447e455fd1e56b780a4617052e639bda1` |
| `r1-safety-nmin.py` sha256 | `f9c86990b7ceafca4dde46cd84c65260fdd0f4c6120d33f6ef4f379cce53eba1` |
| `r1-methods-governance.md` sha256 | `5977aeb48913b34af53c8f6c0c9dda48afccf8fc7eb9d04c0ce5c4c22b4f9fbc` |
| `codex-prompt-template.md` sha256 | `aa61e2e8945fc3128df031a2cea3892e6d80d60a2563f8f98b5db9ac78cec663` |
| Effective prompt | [round-2-prompt.txt](round-2-prompt.txt), sha256 `1468d52d0bd548993e6f876c61d3c32e0b9e4d117bc91223122f175af02e0a77` |
| Tool | Codex CLI 0.159.2, `codex exec -c model_reasoning_effort="high" -s read-only --ignore-user-config` (persisted session) |
| Model | `gpt-6.1-sol` (from session header) |
| Session id | `01a0f8ba-8e80-7393-a425-7a1950d0d0f6` (rollout `~/.codex/sessions/2026/10/01/rollout-2026-10-01T14-29-30-01a0f8ba-8e80-7393-a425-7a1950d0d0f6.jsonl`) |
| Tokens | 84,580 |
| Full log | [round-2-codex.log](round-2-codex.log) |
| Verdict | `VERDICT: blocking objections remain` (3 blocking, 2 major, 0 minor) |

## Codex output (verbatim)

1. [major] The claimed Neyman–Pearson ceiling is false for paired outcomes

   **Location:** `docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md`, §7.4; `r1-safety-nmin.py`, `mp_power()` and its module documentation.

   **Defect:** Neyman–Pearson applies to specified **joint distributions**, not merely two subject-arm marginal probabilities. Changing the subject’s marginal while holding the comparator’s marginal fixed does not establish that the likelihood ratio depends only on the subject’s escape count. Pairing can provide additional information.

   A counterexample uses paired escape indicators `(subject, comparator)` with probabilities `P(1,1)=0.01`, `P(0,1)=0.01`, `P(1,0)=0`, and `P(0,0)=0.98`. This has the registered −1 pp difference and can coexist with the registered completion probabilities.

   For 250 independent pairs, reject when there are no subject-only escapes and at least one comparator-only escape. Over the composite null, write the discordant probabilities as `u` and `v`, with `u−v >= 0.01`. The maximum rejection probability is

   `max_v [(0.99−v)^250 − (0.99−2v)^250] = 0.020304`.

   Its alternative power is `1−0.99^250 = 0.918941`. The calculator’s supposed ceiling at `c=0.01` is only `0.577608`. Thus the claimed ceiling, and the assertion that 383 clusters are necessary at this nuisance point, are false.

   Counting clusters also does not make their repeated-slot means binary. The original §6.1 explicitly uses within-task means. Finally, a finite grid does not certify a minimum over continuous nuisance values.

   **Minimal fix:** Replace this argument with a bound on the complete cluster observation, allowing paired outcomes and arbitrary dependence within each cluster. There is a possible repair: contaminate the entire alternative cluster distribution with probability `ε=0.02/1.01` using a distribution whose escape difference is +1. The resulting mean difference is +0.01, and power is bounded by `0.025/(1−ε)^n`. This gives a conservative necessary count of 174 under the independent-cluster model.

   Consequently, I am **not** claiming that the current cutoff of 172 is numerically anti-conservative. A valid replacement argument can support it. The submitted proof and calculator description nevertheless need correction.

2. [blocking] Provenance verification is optional at countersignature

   **Location:** `docs/plans/safe-delegation-m0/v0.23/r1-methods-governance.md`, §§5.1 and 7.

   **Defect:** Persisting sessions makes verification possible, but the rules never require verification. Section 7 still permits `MET` from a recorded verdict, five file hashes, and a maintainer merge. A proposer-produced log with invented session metadata can satisfy those recorded conditions.

   Section 5.1 acknowledges that fabrication could be detected by comparing records. Disclosure of that possibility does not perform the comparison required by round-1 objection 13.

   **Minimal fix:** Make the maintainer’s recorded verification a condition of countersignature. It must compare the committed prompt, transcript, configuration, and verdict with the actual session record and confirm their artifact binding. Missing records or mismatches must prevent `MET`. Preserve the disclosed residual trust in the maintainer and capture operator.

3. [blocking] The merge-time hash check does not bind authoritative dependencies

   **Location:** `docs/plans/safe-delegation-m0/v0.23/r1-methods-governance.md`, §§3, 5.1, and 7; `r1-decision-rules-v1.md`, §§1, 7.5, and 9.

   **Defect:** The artifact SHA identifies repository contents at review time, but the merge condition checks only five normative files. Referenced originals, R0 authorization and amendments, lifecycle rules, and supplied repository instructions can change while those five hashes remain identical.

   Likewise, the hashed prompt template is not the complete round prompt. The latter is recorded, but its correspondence with the countersigning session and merged interpretation inputs is not a required acceptance check. This leaves part of round-1 objection 13 unresolved: a valid review can be attached to changed dependencies.

   **Minimal fix:** Require a dependency manifest binding every authoritative local reference and every available instruction supplied to the reviewer. Bind the complete round prompt to the verified session. At merge, verify dependency content as well as the five normative hashes; relevant changes require another review or explicit invalidation.

4. [blocking] Route states overlap, and the checklists do not require successful results

   **Location:** `docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md`, §§7.1 and 7.6.

   **Defect:** R4 must assign exactly one state, but:
   
   - An absent checklist item can also constitute incomplete evidence, satisfying both `ROUTE_MISSING` and `ROUTE_UNRESOLVED`.
   - A known blocking defect can coexist with absent items, satisfying `ROUTE_BLOCKED` and `ROUTE_MISSING`.
   - Every checklist item can exist while its results demonstrate a failure.

   The last case is substantive. G4 requires a frozen suite and bypass probes, but its checklist does not require recorded zero false-established properties and zero accepted unauthorized bypasses. G6.1 requires a behavioral comparison “with recorded results,” without explicitly requiring a passing comparison. These are evidence-existence checklists, not sufficient conditions for the gate metrics in §3.

   **Minimal fix:** Freeze an exclusive assignment procedure with precedence for known violations and disputed evidence. Require affirmative, passing results against the actual gate metric before `ROUTE_ESTABLISHED`. Define what distinguishes wholly missing evidence from incomplete or disputed evidence.

5. [major] Acceptance probabilities remain unspecified in the registered scenarios

   **Location:** `docs/plans/safe-delegation-m0/v0.23/r1-decision-rules-v1.md`, §§4.2–4.3.

   **Defect:** The table defines `a` but supplies no acceptance probabilities. It therefore does not establish its claimed “all components at boundary” configurations for accepted-denominator safety.

   For example, with `x=0` and acceptance probabilities of 0.90, an all-assigned subject escape rate of 0.01 produces an accepted-denominator difference of 1.111 pp, rather than 1 pp. Choosing all acceptance probabilities as 1 would resolve this example, but that choice is not frozen. Cost distributions and the joint outcome/dependence specifications also remain open.

   **Minimal fix:** Register acceptance probabilities and coherent joint outcome specifications, deriving both safety denominators explicitly. Provide the distributions or a bounded, explicitly descriptive scenario specification. This is major rather than blocking because simulation no longer decides a v1 classification.

All round-1 objections were accepted; there are no rejected dispositions to adjudicate. Verification of every claimed fix follows:

| Round-1 objection | Round-2 assessment |
|---|---|
| 1 | **MAINTAINED, downgraded to major.** The contradictory contrasts are repaired, but acceptance probabilities and complete scenario specifications remain missing; objection 5 above. |
| 2 | **WITHDRAWN.** The Monte Carlo allowance no longer permits rejection rates above the advertised alpha. |
| 3 | **WITHDRAWN as a v1 blocker.** Component checks and strong-other-component branch nulls are required; simulation cannot decide classifications. This does not establish validity outside the registered configurations. |
| 4 | **WITHDRAWN.** Finite evaluation registration, a total Monte Carlo error budget, and removal of reseeding address the decision-bearing search defect. |
| 5 | **WITHDRAWN.** States are branch-indexed, joint branch power is explicitly defined, and individual component power cannot establish positive feasibility. |
| 6 | **MAINTAINED, downgraded to major.** The former optimization route is removed, but the replacement grid is not a continuous extremum certificate and its claimed paired-test ceiling is false. A different uniform bound can repair the conservative cutoff; objection 1 above. |
| 7 | **WITHDRAWN.** Decision-bearing constants and alternatives are frozen, and R3 must freeze its frame before inventory inspection. |
| 8 | **WITHDRAWN.** Complete frame enumeration is required for a certified finite bound; uncertified supply disables the scarcity route. Pilot reservation is explicitly zero in the upper bound. |
| 9 | **WITHDRAWN.** `floor(H_high/0.75)` credits the maximum permitted authored contribution without requiring evidence that it exists. This is appropriate for an upper bound. |
| 10 | **MAINTAINED, downgraded to major.** The frame-scoped interpretation is repaired, but the submitted mathematical transfer argument is invalid. The conservative cutoff can be supported by a different argument; objection 1 above. |
| 11 | **WITHDRAWN.** G0 restores the forward workload threshold and explicitly prevents substitution of historical public supply. |
| 12 | **WITHDRAWN.** Known acceptance and costs are retained, missing outcomes receive component-specific adverse imputation, and the invalidity threshold is frozen. |
| 13 | **MAINTAINED, blocking.** Attempts, retries, logs, and residual trust are now documented. Mandatory independent checking and merge-time dependency binding remain absent; objections 2–3 above. |
| 14 | **WITHDRAWN.** The resource-envelope rejection route is removed, and the remaining scarcity verdict is explicitly frame-scoped. |
| 15 | **MAINTAINED, now blocking.** The added states and checklists leave overlapping assignments and permit evidence-existence certification without passing results; objection 4 above. |

VERDICT: blocking objections remain

## Dispositions (Claude, proposer)

| # | Severity | Disposition | Change |
|---:|---|---|---|
| 1 | major | Accepted and fixed | Rules section 7.4 replaced with the reviewer's contamination argument: `P0 = (1 - eps) P1 + eps Q`, `Q` with `D = +1` a.s., `eps = 0.02/1.01`; power at any `P1` with mean difference -1 pp is at most `0.025 / (1 - eps)^n`. This covers complete cluster observations, paired outcomes, within-cluster means, and every base rate, with no grid. `N_MIN` changes from 172 to **174** (cutoff `H_high <= 130`). `r1-safety-nmin.py` now evaluates the closed form. |
| 2 | blocking | Accepted and fixed | Governance section 7 item 2: maintainer provenance verification against the persisted session store is a condition of `MET`; the countersigning round must be persisted; missing records or mismatches prevent `MET`. Residual trust in the maintainer and capture operator is disclosed. |
| 3 | blocking | Accepted and fixed | Governance section 5.1 "Binding file": from round 3, `round-<N>-bindings.sha256` binds the normative set, the complete effective prompt, `AGENTS.md`, `CLAUDE.md`, the v0.20 originals, and dated snapshots of issues #1278, #1370, #1372 (committed under `reviews/r1/snapshots/`). Section 7 item 3: `shasum -a 256 -c` must pass on the merge commit. R0 records, not present at review, are bound at merge by recording their commit and treating any divergence from the restated R0 inputs as an amendment-control event. |
| 4 | blocking | Accepted and fixed | Rules section 7.6: checklists now require recorded passing results against the gate metric (G4: zero confirmed false-established properties and zero accepted bypasses; G6.1: passing behavioral comparison for every supported construct). Definitions of absent versus incomplete/disputed items, and an exclusive first-match assignment: BLOCKED, then UNRESOLVED, then MISSING, then ESTABLISHED. |
| 5 | major | Accepted and fixed | Rules section 4.3 registers `a = 1` for all arms (denominators coincide) plus a separate accepted-denominator boundary scenario with `a = 0.95` and `e_subject = e_comparator + 0.0095`. Cost distributions, dependence, and base rates are registered by R4 before running and remain descriptive under v1. |
