# R1 adversarial review, round 3 (Codex)

| Field | Value |
|---|---|
| Gate | R1 (#1372) |
| Date (UTC) | 2026-10-01 |
| Artifact commit | `9dd922bae206f2891932da1d327e40999eee49b6` |
| Bindings | [round-3-bindings.sha256](../../r1/round-3-bindings.sha256) (18 files; the reviewer reports all 18 pass) |
| Effective prompt | [round-3-prompt.txt](../../r1/round-3-prompt.txt), sha256 `5a0f818dfcbb67ca3a3ee8c114f8fe491c7fb570b130f74343db0f85fd556827` |
| Tool | Codex CLI 0.159.2, `codex exec -c model_reasoning_effort="high" -s read-only --ignore-user-config` (persisted session) |
| Model | `gpt-6.1-sol` (from session header) |
| Session id | `01a0f8c8-0bf3-7e91-baa7-bc820d48d8c0` |
| Tokens | 108,076 |
| Full log | [round-3-codex.log](../../r1/round-3-codex.log) |
| Verdict | `VERDICT: blocking objections remain` (3 BLOCKING, 0 MAJOR, 0 MINOR) |

Paths at review time were under `reviews/r1/`. After this round the supporting
files moved to `r1/` and the round records to `reviews/R1/`, to satisfy the R0
boundary test's naming rule (see the governance file, section 5.1). File
contents were not changed by the move.

## Codex output (verbatim)

Reviewed commit `9dd922bae206f2891932da1d327e40999eee49b6`. All 18 bindings pass. The frozen calculator returns `N_MIN=174` and `H_high <= 130`. No network or task-supply data was used.

1. [blocking] The contamination null need not respect the frozen study design

   **Location:** `r1-decision-rules-v1.md`, §§7.4 and 7.7; `r1-safety-nmin.py`, module documentation; original Draft v3, §5.1.

   **Defect:** The contamination calculation is valid for an unrestricted i.i.d. model admitting the proposed `Q`. The claim that it applies to **every** ascertainment method and permissible design is not established.

   Accepted and incorrect is necessary for a serious escape, but it is not sufficient. The artifact must violate a preclassified critical requirement. A frozen sampling mix, structural restrictions, or established instrument constraints can make `D=+1` impossible for some cluster types. Contamination must preserve those constraints.

   Here is a mathematical counterexample, not an assertion about actual supply. Suppose a preregistered design establishes that serious escapes are possible only in a stratum with known probability `q=0.01`; outside it, escapes are structurally impossible. All arms accept every slot. Under the registered alternative, the comparator escapes on every critical-stratum task and the subject never escapes, giving difference −1 pp.

   Under the component null, difference ≥+1 pp forces subject escapes on every critical-stratum task and **zero comparator escapes**. Consequently, the test “pass upon observing at least one comparator escape” has null rejection probability zero. Its alternative power at 161 independent clusters is:

   `1 − 0.99^161 = 0.8017257434`.

   A valid one-sided bound can implement this test: subtract a valid lower bound on comparator escape probability from the known upper bound `q` on subject escape probability, and use a conservative upper bound above the margin when no comparator escape is observed.

   The submitted ceiling at 161 clusters is only `0.6257701710`. Its `Q` changes the known stratum distribution and is outside this model. The original permits frozen task proportions; independence does not imply that all design information must be discarded.

   **Minimal fix:** Require a documented certificate that the contamination null preserves every relevant sampling, criticality, and structural constraint before applying §7.4. Without that certificate, disable the formal scarcity verdict and return INSUFFICIENT INFORMATION. Alternatively, establish a bound covering all permissible designs. An unrestricted-model impossibility result cannot establish impossibility across every acceptable original row.

2. [blocking] R0 remains outside the reviewed dependency binding

   **Location:** `r1-methods-governance.md`, opening authority paragraph, §5.1 “Binding file,” and §7; `round-3-bindings.sha256`.

   **Defect:** Round-2 objection 3 is only partly repaired. The existing originals, instructions, prompt, and issue snapshots are bound. The two R0 records governing authority, permitted access, caps, and deadlines are absent and unreviewed.

   Recording their commit at merge identifies newly introduced text; it does not countersign that text. Comparing five restated inputs does not bind the complete authorization contract. R0 can introduce additional, compatible conditions concerning access, retention, authority, or revalidation without changing those five values. The normative files defer to those records, but the merge-time hash check excludes them.

   The opening inconsistency prohibition does not resolve this: an additional condition can be compatible with these files while materially changing their application.

   **Minimal fix:** Include the complete merged R0 records in the dependency manifest before the countersigning round. Require their hashes to pass at merge and treat subsequent relevant changes as amendment or invalidation events. If review must precede R0, freeze a complete provisional authority contract and require another review when the authoritative records become available.

3. [blocking] The route assignment is exclusive but still not exhaustive

   **Location:** `r1-decision-rules-v1.md`, §§7.1 and 7.6.

   **Defect:** The precedence and affirmative passing requirements repair the previously identified overlap and evidence-existence problems. However, the new definition of “incomplete or disputed” covers missing matrix coverage or a recorded reviewer objection. It does not explicitly cover invalid or indeterminate measurements.

   Consider G6.1 with artifacts covering every supported construct, each comparison recorded as `INDETERMINATE` because the comparison instrument is invalid. No reviewer objection has yet been recorded.

   - No recorded behavioral comparison has failed.
   - Every matrix row has a record.
   - No item is absent.
   - Passing results do not exist.

   None of the four assignment steps necessarily matches. An unconfirmed G4 finding with unresolved validity presents a similar gap unless someone first records a qualifying objection. Assignment must depend on evidence status, not whether a particular reviewer has noticed it.

   **Minimal fix:** Define `ROUTE_UNRESOLVED` to include invalid, indeterminate, unmeasurable, or validity-unverified results, and any unsatisfied checklist requirement that is neither wholly absent nor a confirmed violation. Make the last step explicitly exhaustive after those cases are assigned.

The round-2 dispositions are assessed as follows. Neither prior round contains rejected objections.

| Round-2 objection | Assessment |
|---|---|
| 1 | **WITHDRAWN.** The paired-outcome error, binary-cluster assumption, and grid minimization are removed. The replacement calculation is correct within its stated unrestricted model. Objection 1 above concerns its broader applicability. |
| 2 | **WITHDRAWN.** Persisted-session verification is now a mandatory condition of `MET`; missing records or mismatches prevent it. Residual operator and maintainer trust is disclosed. |
| 3 | **MAINTAINED, blocking, in part.** The existing dependency binding is repaired; authoritative R0 records remain outside the countersigning review and hash check. |
| 4 | **MAINTAINED, blocking, in part.** Passing results and precedence are repaired; invalid and indeterminate full-coverage records still lack an explicit assignment. |
| 5 | **WITHDRAWN.** Acceptance probabilities now establish the stated denominator boundaries. Remaining distributions and dependence choices are explicitly descriptive and cannot decide a v1 classification. |

For the earlier round-1 dispositions:

| Round-1 objection | Current assessment |
|---|---|
| 1 | **WITHDRAWN:** coherent arm contrasts and acceptance probabilities are supplied. |
| 2 | **WITHDRAWN:** Monte Carlo error no longer enlarges the advertised alpha. |
| 3 | **WITHDRAWN as a v1 blocker:** component checks are required; simulation cannot adjudicate feasibility. |
| 4 | **WITHDRAWN:** finite evaluation registration and aggregate Monte Carlo error control replace discretionary repeated searches. |
| 5 | **WITHDRAWN:** branch-indexed states and complete-procedure power are specified. |
| 6 | **WITHDRAWN:** the grid and optimization argument are removed. |
| 7 | **WITHDRAWN:** decision-bearing constants and alternatives precede supply inspection. |
| 8 | **WITHDRAWN:** uncertified enumeration disables the scarcity route. |
| 9 | **WITHDRAWN:** the supply upper bound credits the permitted authored contribution. |
| 10 | **MAINTAINED, blocking, in part:** transfer to the original endpoint still requires admissibility of the bounding null under the frozen design; objection 1 above. |
| 11 | **WITHDRAWN:** G0 restores the forward-workload threshold. |
| 12 | **WITHDRAWN:** known acceptance and costs are retained, adverse imputation is specified, and excessive missing evaluation invalidates the epoch. |
| 13 | **MAINTAINED, blocking, in part:** provenance verification is repaired; R0 dependency binding remains incomplete. |
| 14 | **WITHDRAWN:** current resource-envelope rejection no longer pre-empts conditional approval. |
| 15 | **MAINTAINED, blocking, in part:** route states exist but do not explicitly cover every evidence condition. |

VERDICT: blocking objections remain

## Dispositions (Claude, proposer)

Fixing commit for every ACCEPTED objection below:
`50727f030f44624004c84dc20c510be96fe8534c`.

While fixing objection 2, the proposer merged the R0 branch (PR #1467, commit
`b0c054377c9d414bb3702b8d57cb35f955cd523f`) and found that amendment 001
section 5 requires R1 to freeze three things the round-3 artifact had left to
R3: the candidate frame and draw of public repositories, the eligibility rules
R2B′ applies, and the prior-familiarity rule. It also found that the evidence
manifest conflicted with R0 section 7.1 (no per-item identifiers). These were
not raised by the reviewer; they are fixed in the same commit and listed below
so round 4 can check them.

| # | Severity | Disposition | Change |
|---:|---|---|---|
| 1 | BLOCKING | ACCEPTED | Rules section 7.4 adds an admissibility certificate: R3 must certify, before R2B′ inspects data, that the confirmatory analysis uses no structural constraint (known zero or known escape probability in a stratum, known stratum proportions used to bound rates) that makes `Q` inadmissible. Without that certificate the supply route is disabled and G3.1 cannot be `SIZED_INFEASIBLE`; the classification then falls to INSUFFICIENT INFORMATION. |
| 2 | BLOCKING | ACCEPTED | R0 records are now in the tree (merge of PR #1467's branch) and are added to the binding list: `r0-authorization.md`, `amendment-001-public-proxy.md`, `review-protocol.md`, `gate-state.json`. If merged R0 content differs from the bound hashes, the merge-time check fails and R1 needs another round or is not `MET`. |
| 3 | BLOCKING | ACCEPTED | Rules section 7.6: "unresolved" now covers invalid, indeterminate, unmeasurable, not-yet-validity-checked, and pending-confirmation results, independent of whether a reviewer noticed; step 4 requires validity-checked passing results; a final step 5 assigns `ROUTE_UNRESOLVED` to anything else, making assignment exhaustive. |
| P1 | (proposer-found) | ACCEPTED | Rules sections 6.1-6.3 freeze the candidate frame (public, non-fork, non-archived, primary language C#, R2A′-permitted license, merged PR in the 12-month window), date-sliced enumeration with a hashed specification, a hash-ordered inventory draw of 30 repositories salted by the R1 merge commit (descriptive only), eligibility criteria E1-E6 with mechanical tests, and the prior-familiarity list (`bench/corpus/` submodules, repositories referenced in this repository, maintainer declarations). Familiar repositories are excluded from the pool but their counts are added to `H_high`, so excluding them cannot manufacture scarcity. Section 6.4's supply bound now covers the whole frame. |
| P2 | (proposer-found) | ACCEPTED | Rules section 8 adopts R0 section 7.1: no per-item identifiers next to decisions; provenance by repository, pinned revision, window, extractor, rules version, and per-item-list digest; manifest rows per run, not per item; minimum 5 contributors per published statistic. |
| P3 | (proposer-found) | ACCEPTED | Rules section 7.3 records, before inspection, that the most likely v1 outcome is INSUFFICIENT INFORMATION, because the supply bound covers the whole frame. Section 7.7 records that the original-domain M0 status stays UNADJUDICATED whatever the v1 result. |
| P4 | (proposer-found) | ACCEPTED | Governance section 5.1 records protocol conformance: the round limit, Copilot accounting, and non-override rule come from `review-protocol.md`; persisted sessions and `--ignore-user-config` are stricter variances; from round 4 the prompt includes the protocol's reviewer instruction verbatim (rounds 1-3 paraphrased it, disclosed as a deviation). Governance section 7 item 6: the R1 entry in `gate-state.json` stays `null` until the countersignature conditions hold. |
