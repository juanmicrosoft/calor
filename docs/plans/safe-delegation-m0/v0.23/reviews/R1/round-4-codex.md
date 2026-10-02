# R1 adversarial review, round 4 (Codex)

| Field | Value |
|---|---|
| Gate | R1 (#1372) |
| Date (UTC) | 2026-10-01 |
| Artifact commit | `f3e9ce25b36dc4946644e30488fe7ecf8cf4d80a` |
| Bindings | [round-4-bindings.sha256](../../r1/round-4-bindings.sha256) (22 files; the reviewer reports all 22 pass) |
| Effective prompt | [round-4-prompt.txt](../../r1/round-4-prompt.txt) |
| Tool | Codex CLI 0.159.2, `codex exec -c model_reasoning_effort="high" -s read-only --ignore-user-config` (persisted session) |
| Model | `gpt-6.1-sol` (from session header) |
| Session id | `01a0f8d8-3468-7ea2-bcff-4459d1fcc7a2` |
| Tokens | 112,044 |
| Full log | [round-4-codex.log](../../r1/round-4-codex.log) |
| Verdict | `VERDICT: blocking objections remain` (5 BLOCKING, 0 MAJOR, 0 MINOR) |

## Codex output (verbatim)

Reviewed commit `f3e9ce25b36dc4946644e30488fe7ecf8cf4d80a`. All 22 bindings pass. The frozen calculator returns `N_MIN=174` and `H_high <= 130`. No network or public task-supply data was inspected.

Paths below are relative to `docs/plans/safe-delegation-m0/v0.23/`, unless stated otherwise.

| Round-3 objection | Assessment |
|---|---|
| 1 | **WITHDRAWN.** §7.4 now requires the pre-inspection admissibility certificate and disables the bound without it. |
| 2 | **WITHDRAWN.** The complete R0 records are present and bound. Their contents introduce separate compatibility problems below. |
| 3 | **MAINTAINED in part.** Assignment is now exhaustive, and invalid or indeterminate evidence is explicitly unresolved. However, step 1 can still classify an invalid failed comparison as `ROUTE_BLOCKED` before that treatment applies; objection 3 below. |

No earlier objection was rejected. The earlier-round dispositions remain **WITHDRAWN**, except round-1 objection 15 and round-2 objection 4, whose remaining evidence-state problem is covered by objection 3 below. The residual dependency and admissibility objections carried into round 3 are now **WITHDRAWN** for the reasons above.

The proposer-found fixes are assessed as follows:

| Item | Verification |
|---|---|
| P1 | **Partly fixed.** Selection and mechanical eligibility rules are present. The oracle-existence test and familiarity declaration remain defective; objections 4 and 5 below. |
| P2 | **Fixed as described.** The manifest uses run-level rows, provenance digests, and the contributor aggregation floor. |
| P3 | **Partly fixed.** The expectation and original-domain `UNADJUDICATED` statement are present. Formal reachability conflicts with R4’s authoritative closure criteria; objection 2 below. |
| P4 | **Partly fixed.** Protocol accounting and deviations are disclosed, and the verbatim instruction is present. The new state-update condition conflicts with the binding condition; objection 1 below. |

1. [blocking] Binding the live gate-state file conflicts with the required state transitions

   **Location:** `r1-methods-governance.md`, opening authority paragraph, §5.1 “Binding file,” and §7 items 3 and 6; `r1/round-4-bindings.sha256`.

   **Defect:** The complete `gate-state.json` is bound with R1 equal to `null`. Item 3 requires every bound file to remain unchanged after review. Item 6 requires a PR or follow-up PR to change R1 to `MET`. The opening paragraph additionally says that changes to the R0 records after the countersigning round prevent R1 from being `MET`; §5.1 includes `gate-state.json` among those records.

   A same-PR update fails the merge-time hash check. A follow-up update conflicts with the prohibition on post-review changes. Subsequent legitimate downstream state updates have the same problem. The rules do not explicitly distinguish an authorized state transition from a change to reviewed authority.

   **Minimal fix:** Bind an immutable reviewed state snapshot and define a narrow, checked exception for authorized live-state transitions. Preserve binding of authority, graph, limits, definitions, and boundary flags. Specify how R1’s `null → MET` transition and subsequent gate updates are verified without invalidating the reviewed rules.

2. [blocking] The claimed formal outcomes conflict with R4’s authoritative closure criteria

   **Location:** `r1-decision-rules-v1.md`, §§3, 4.2, 7.2–7.3 and 11; `amendment-001-public-proxy.md`, §5 “R4: joint sizing” and “R5: classification and maintainer action.”

   **Defect:** Amendment 001 requires R4 to size every registered gate, support all four classifications, and return `UNAVAILABLE` when required inputs are missing. R5 requires R4 to be `MET`.

   These rules permanently mark several original components unmeasurable, prohibit simulation from producing any sized state, and pass R4 only a state-reporting obligation plus the safety calculator. Those obligations do not establish that R4 can satisfy its governing closure criteria.

   Consider the explicitly expected case: supply does not establish scarcity, while adopter workload, economic inputs, adoption, and independent receipt remain unavailable. Under R4’s missing-input closure rule, R4 is `UNAVAILABLE`; §7.2 step 1 then requires `UNADJUDICATED`. The claimed formal `INSUFFICIENT INFORMATION` route cannot simply bypass that prerequisite. The asserted reachability and downstream gate contract therefore disagree.

   **Minimal fix:** Reconcile the authoritative definitions before freeze. Either preserve R4’s current criteria and disclose the resulting formal unreachability, or obtain a versioned governing amendment that explicitly permits R4 to be `MET` after a complete assessment containing specified unmeasurable components. Distinguish missing evidence from failure to complete the assessment.

3. [blocking] Invalid failed comparisons can still be laundered into confirmed route failure

   **Location:** `r1-decision-rules-v1.md`, §7.6, lines 406–421, particularly assignment step 1.

   **Defect:** The new unresolved definition includes invalid and validity-unchecked results. But step 1 assigns `ROUTE_BLOCKED` for any recorded failed behavioral comparison, “whatever the state of other items,” before step 2 checks unresolved evidence.

   Suppose G6.1 has a recorded comparison failure from an instrument subsequently found invalid. The artifact explicitly qualifies as unresolved, yet the first matching step can assign `ROUTE_BLOCKED`. The successful-result branch requires validity checking; the failure branch does not. This creates asymmetric treatment of equally invalid evidence.

   **Minimal fix:** Require validity-checked, confirmed violations in step 1. Invalid, indeterminate, disputed, or pending-confirmation failures must reach `ROUTE_UNRESOLVED`. Genuine confirmed violations may retain precedence over unrelated missing items.

4. [blocking] E3 substitutes test-file modification for oracle availability

   **Location:** `r1-decision-rules-v1.md`, §6.2 criterion E3 and §6.4 supply formula; JSON `source_selection.eligibility.E3`; original `docs/plans/roadmap-v0.20-reference-draft-v3.md`, §5.1.

   **Defect:** E3 labels “changes at least one file in a test path” as the mechanical test for “oracle material exists.” These are different properties.

   A production change may have usable existing tests, documented behavioral requirements, or other material for an independently authored oracle without modifying a test-path file. Conversely, modifying a test-directory README establishes no behavioral oracle. The original requires an independently evaluated, arm-shared instrument; it does not require every historical implementation to modify test files.

   E3’s confirmed-false treatment removes otherwise usable requests from `H_high`. This affects the only scientific negative route. In a hypothetical frame with 120 qualifying requests and 20 otherwise usable changes covered by unchanged tests, E3 can reduce the reported historical bound below the scarcity cutoff despite at least 140 usable historical requests. This is an eligibility restriction presented as an instrument-existence check.

   **Minimal fix:** Replace E3 with an explicit evidence-availability rule that admits existing oracle material and treats unresolved ascertainability as unresolved supply. Do not infer absence of an oracle from absence of a test-file change. If a changed-test-only study is deliberately intended, disclose and justify that additional domain restriction rather than claiming it operationalizes the original requirement.

5. [blocking] Known proposer familiarity is not required to be declared

   **Location:** `r1-decision-rules-v1.md`, §6.3; JSON `source_selection.prior_familiarity`; `amendment-001-public-proxy.md`, §5 R1 criteria and §7 bias controls.

   **Defect:** The rule includes corpus repositories, repositories whose URLs appear in selected repository directories, and repositories the maintainer declares. It does not require a declaration from the proposer, nor require the maintainer to enumerate all repositories studied outside those directories.

   Amendment 001 explicitly covers repositories studied by either the maintainer **or proposer** for Calor. A repository studied in an agent session without a committed URL can remain in the inventory draw and future task pool under the current rule. Disclosure of unmeasurable pretraining exposure does not address known project-specific exposure.

   **Minimal fix:** Require pre-inspection declarations from both the maintainer and proposer covering known Calor-related repository study, including session-only exposure. Record the declaration cutoff and completeness limitations. Apply the separate-stratum treatment and conservative supply credit to those repositories.

VERDICT: blocking objections remain

## Dispositions (Claude, proposer)

Fixing commit for every ACCEPTED objection below:
`2222a2a0b7e438de1ee2c96840bd1fd6b84283aa`.

| # | Severity | Disposition | Change |
|---:|---|---|---|
| 1 | BLOCKING | ACCEPTED | `gate-state.json` is no longer hash-bound. An immutable copy, `r1/snapshots/gate-state-at-review.json`, is bound instead. Governance section 5.1 "Live gate state" permits only gate `value`/`effective`/`record` changes, R5 classification/label/action changes, and appended `history` entries, each made by a merged record under the R0 transition rules; any other difference is a change to reviewed authority. The R1 `null -> MET` transition is a permitted change only when section 7 items 1-5 hold (section 7 item 6). |
| 2 | BLOCKING | ACCEPTED | Rules section 7.3 rewritten. Under amendment 001 as written, R4 is expected to close `UNAVAILABLE` because the inputs for G0, G1, G5, and G6.2 are always missing in this domain, so the expected v1 result is **UNADJUDICATED** with no formal classification. NOT FEASIBLE and INSUFFICIENT INFORMATION are reachable only if the maintainer adopts a versioned R0 amendment that lets R4 be `MET` after a complete assessment with recorded missing components. "Missing evidence" and "failure to complete the assessment" are defined. Section 1 now points to section 7.3 instead of asserting INSUFFICIENT INFORMATION. Adopting that R0 amendment is listed as a maintainer decision; these rules do not make it. |
| 3 | BLOCKING | ACCEPTED | Rules section 7.6 step 1 now requires a confirmed, validity-checked violation; an invalid, indeterminate, disputed, unchecked, or pending violation makes its item unresolved. |
| 4 | BLOCKING | ACCEPTED | E3 is now "oracle ascertainable": confirmed true if the change modifies a `.cs` file in a test path; never confirmed false under v1 (absence of test code does not show that no oracle could be authored); unresolved otherwise. (Commit `2222a2a0` first allowed "confirmed false" when a repository had no test code at all; the round-5 artifact removes that case.) Unresolved items stay in `H_high`. R3 may adopt a stricter oracle rule only as a disclosed E6 domain restriction. |
| 5 | BLOCKING | ACCEPTED | Rules section 6.3 requires dated pre-extraction familiarity declarations by both the maintainer and the R2B′ proposer agent, covering session-only exposure, with the cutoff, the sources searched, and the completeness limitation stated. Late-recognized familiar repositories move to the familiar stratum by disclosed correction and stay in `H_high`. The R1 proposer session declares that it studied no public repository other than this one. |
