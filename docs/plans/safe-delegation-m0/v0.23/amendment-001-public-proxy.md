# Safe delegation M0 v0.23 — Amendment 001: public-proxy domain

**Recorded:** 2026-10-01. **Version:** 001. **Epic:** #1370. **Affects:**
#1372-#1377. **Authority:** @juanmicrosoft under
[r0-authorization.md](r0-authorization.md); effective upon maintainer merge.
**Machine-readable state:** [gate-state.json](gate-state.json).

This amendment replaces gate definitions that cannot be satisfied under R0
(zero permitted human contacts, public data only). It does not overwrite the
values of the original gates. It narrows what any 0.23 result can mean.

## 1. Why the original gates cannot be met

| Original gate | Requirement as filed | Status under R0 |
|---|---|---|
| R1 (#1372) | Consenting independent **human** methods reviewer | R0 permits no human contact. The authorized search is empty. |
| R2A (#1373) | Authorized **organization** conditionally supporting planning and data access | The maintainer has no organization available, and R0 permits no outreach. |
| R2B (#1375) | **Private** adopter task supply and **human** reviewer hours | No private task source and no human reviewer capacity exist. |
| R3, R4, R5 (#1374, #1376, #1377) | Acceptance or countersignature by the independent human reviewer and the adopter | Depend on the gates above. |

## 2. Original gate values (recorded, not overwritten)

These values are recorded under the `history` key of
[gate-state.json](gate-state.json). They stay on record permanently. The
amended gates below are new definitions, not corrections of these values.

| Original gate | Value | Scope of the claim |
|---|---|---|
| R1-original | `UNAVAILABLE` | No consenting human reviewer was obtained because the authorized search was empty. Not a claim that none exists. |
| R2A-original | `UNAVAILABLE` | No organization was available to the maintainer and no outreach was authorized. Not a claim that no consenting organization exists. |
| R3-original | `NOT_REACHED` | Blocked by R1-original and R2A-original. |
| R2B-original | `NOT_REACHED` | Blocked by R1-original, R2A-original, and R3-original; no private source exists. |
| R4-original | `NOT_REACHED` | Blocked by every original prerequisite. |

The original-definition R5 (an independently countersigned classification of
the organizational M0 question) cannot be produced in 0.23. The formal M0
status in [../decision.md](../decision.md) therefore remains
**UNADJUDICATED** for the original domain regardless of any amended result.
R5 (#1377) records this in its closeout.

## 3. Replacement definitions

| Gate | Amended definition |
|---|---|
| R1 | Methods governance by cross-family AI adversarial review under [review-protocol.md](review-protocol.md), replacing the independent human reviewer |
| R2A′ | Public-proxy authority: public .NET open-source repositories as the task source; their licenses and the hosting service's published terms replace a signed organizational agreement |
| R3 | Paper three-arm boundary produced and frozen with AI adversarial review |
| R2B′ | Task supply measured from public repository history; reviewer capacity measured as AI-review compute, rounds, and spend rather than human hours |
| R4 | Joint sizing over the amended inputs, with AI adversarial review |
| R5 | Scientific classification and maintainer action, both by @juanmicrosoft, with AI adversarial review of the record |

## 4. Amended dependency graph

```text
R0 -> R1, R2A'
R1 + R2A' -> R3
R1 + R2A' + R3 -> R2B'
R1 + R2A' + R3 + R2B' -> R4
R4 -> R5 -> Epic close
```

The #1370 propagation and lifecycle rules apply unchanged: only `MET` unlocks
a dependent gate; a dependent of a non-`MET` prerequisite closes
`NOT_REACHED`; expiry or revocation of a prerequisite makes dependents
`INVALIDATED`. R5 still runs after every earlier gate is dispositioned, even if
R4 is `NOT_REACHED`, to record the closeout.

## 5. Amended closure criteria

Every amended gate shares these conditions:

- **Revalidation.** R0 is `MET`, not expired, and within the cash cap at
  start, before each data access, and before closure.
- **Review.** The gate artifact completes the protocol with no unresolved
  blocking objection within 5 rounds. If blocking objections remain after
  round 5, the gate cannot be `MET`.
- **Merge.** The gate value takes effect only when @juanmicrosoft merges the
  record and its [gate-state.json](gate-state.json) update.
- **Labels.** No record describes AI review as independent or human review.
- **Expiry.** `EXPIRED` if R0 expires or is revoked before the gate is
  dispositioned (`INVALIDATED` if work had started).

### R1: AI methods governance

- `MET`: A versioned, frozen rules record clears the protocol and is merged
  **before** any R2B′ supply inspection. It covers the gate table (cost,
  completion, safety, trust, usable adoption, handoff), alpha and error
  control, positive-branch logic, missingness and invalid-epoch rules,
  amendment control, the four classifications, and the public-record format.
  It also covers the objective source-selection and eligibility rules used by
  R2A′, R3, and R2B′.
- `UNAVAILABLE`: Blocking objections remain after 5 rounds, or no
  cross-family reviewer can be run before the deadline.
- `NOT_REACHED`: R0 is not `MET`.

### R2A′: public-proxy authority

- `MET`: A versioned record clears the protocol. It defines the public .NET
  source pool by objective criteria fixed **without inspecting task counts**
  (for example language, license, public visibility, and activity window).
  It records each source's license (SPDX identifier and revision) and the
  hosting terms that permit the analysis, the public-only boundary, and
  exclusion handling per R0 Section 7. It asserts no organizational
  commitment, adopter interest, or participant consent.
- `UNAVAILABLE`: No public source with permitting license and terms can be
  identified, or blocking objections remain after 5 rounds.
- `NOT_AUTHORIZED`: The maintainer declines the public-proxy source.
- `NOT_REACHED`: R0 is not `MET`.

### R3: paper boundary

- `MET`: A versioned three-arm paper boundary (ordinary C#, strong protected
  C#, protected Calor) and its parity standard clear the protocol and are
  merged. Acceptance by an independent human or an adopter is not claimed.
  The record creates no starters, workflows, oracle, acceptance service, or
  task pool.
- `UNAVAILABLE`: No non-empty support intersection or acceptable comparator
  can be defined, or blocking objections remain after 5 rounds.
- `NOT_REACHED`: R1 or R2A′ is not `MET`.

### R2B′: public supply and AI review capacity

- `MET`: A provenance-bearing inventory from public history clears the
  protocol. It applies the frozen R1 and R3 rules to the R2A′ pool and gives
  the extraction window, source revisions or hashes, eligibility decisions,
  exclusions, clustering, duplication, and missingness. It reports historical
  supply and any forward projection separately; a forward projection is
  labeled a projection from public activity, not a workload commitment. It
  gives AI-review capacity in rounds, tokens, and USD under the cap. Its
  aggregates follow R0 Section 7. It makes no task pool, reservation, or
  roster.
- `UNAVAILABLE`: Public records are insufficient for the frozen rules,
  capacity cannot be estimated, or blocking objections remain after 5 rounds.
- `NOT_REACHED`: R1, R2A′, or R3 is not `MET`.

### R4: joint sizing

- `MET`: Pinned, reproducible estimators with seeds, environment, and Monte
  Carlo error checks size every registered gate over the R2B′ inputs. They
  use least-favorable approved assumptions. They support all four
  classifications. The maintainer records the prospective study envelope as
  approved, rejected, or unavailable, separately from the R0 cap. The record
  clears the protocol.
- `UNAVAILABLE`: Required inputs are missing, or blocking objections remain
  after 5 rounds.
- `NOT_REACHED`: R1, R2A′, R3, or R2B′ is not `MET`.

### R5: classification and maintainer action

- `MET`: @juanmicrosoft records one scientific classification
  (`FEASIBLE AS PROPOSED`, `REQUIRES SEPARATE APPROVAL`, `NOT FEASIBLE`, or
  `INSUFFICIENT INFORMATION`) labeled **"AI-adjudicated, public-proxy
  domain"**. The maintainer action (`STOP`, `DEFER`, or `ALLOW A SEPARATE
  AUTHORIZATION REQUEST`) is recorded in a separate field. The classification
  record clears the protocol. R4 must be `MET`.
- `UNAVAILABLE` or `EXPIRED`: Publish the dated reason and preserve
  `UNADJUDICATED`.
- In every case the closeout states that the original-domain M0 status
  remains `UNADJUDICATED`.

## 6. Required labeling consequence

Any classification produced under this amendment:

- must carry the label **"AI-adjudicated, public-proxy domain"** wherever it
  is stated (enforced for [gate-state.json](gate-state.json) and for
  `Scientific classification:` declarations in Markdown under `v0.23/` by
  `tests/Calor.Compiler.Tests/Plans/SafeDelegationV023BoundaryTests.cs`);
- addresses only the feasibility of a comparison study on public .NET work;
- says nothing about organizational adoption, adopter demand, private
  workloads, or human reviewer capacity;
- must not be described as independent, independently reviewed,
  human-reviewed, countersigned, or adopter-accepted;
- does not satisfy #1259, #1254, or #1284-#1309, and activates nothing.

## 7. Bias controls specific to the proxy

The maintainer, who authors Calor, chooses the proxy and adjudicates the
result. To limit selection that favors Calor:

- The R2A′ pool criteria and the R1/R3 eligibility rules are frozen and
  merged before any R2B′ count is inspected. Changing them afterwards needs a
  new amendment version and a disposition of every affected count.
- Repositories are not added or dropped after counting except under R0
  Section 7 (exclusion request, license change, non-public data).
- Domain cells that Calor does not support are excluded symmetrically from
  all three arms, and the exclusion count is reported, so that unsupported
  shapes cannot be dropped only from the Calor arm (R3).
- Every review round instructs the reviewer to look specifically for bias in
  favor of Calor (protocol).

## 8. Amendment control

Later amendments are numbered (`amendment-002-...`) and versioned. Each names
the definitions it replaces, the evidence it affects, and how that evidence is
dispositioned. No amendment may change a value recorded in the `history` key
of [gate-state.json](gate-state.json).
