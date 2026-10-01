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
| R2A′ | Public-proxy authority: public .NET open-source repositories as the task source; their licenses and the hosting service's published terms replace a signed organizational agreement. Authority only; no repository selection |
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
R4 is `NOT_REACHED`, as the R0 Section 10 administrative closeout.

## 5. Amended closure criteria

Every amended gate shares these conditions:

- **Revalidation.** The full procedure of R0 Section 10 runs at start,
  before each data access, and before closure: R0 and **every gate in the
  transitive prerequisite closure** (value, expiry, withdrawal status, and
  relied-on output version), and the
  spend and hours caps. The R5 administrative closeout (R0 Section 10) is
  the only exception.
- **Review.** The gate artifact completes the protocol with no unresolved
  blocking objection within 5 rounds. If blocking objections remain after
  round 5, the gate cannot be `MET`.
- **Merge.** The gate value takes effect only when @juanmicrosoft merges the
  record and its [gate-state.json](gate-state.json) update.
- **Labels.** No record describes AI review as independent or human review.
- **Expiry and withdrawal.** R0 Section 10 governs: when R0 expires or is
  revoked, or a prerequisite's authority or input is withdrawn, every
  affected gate that started or finished (including one already `MET`)
  becomes `INVALIDATED` and every unstarted descendant becomes
  `NOT_REACHED`.

### R1: AI methods governance

- `MET`: A versioned, frozen rules record clears the protocol and is merged
  **before** any R2B′ supply inspection. It covers the gate table (cost,
  completion, safety, trust, usable adoption, handoff), alpha and error
  control, positive-branch logic, missingness and invalid-epoch rules,
  amendment control, the four classifications, and the public-record format.
  It also freezes the objective source-selection rules (the candidate frame
  of public .NET repositories and how repositories are drawn from it) and the
  eligibility rules that R2B′ later applies. It states how repositories
  already used in Calor development (for example the `bench/corpus/`
  submodules MediatR, Serilog, and FluentValidation, and any repository the
  maintainer or proposer has studied for Calor) are excluded or reported
  separately, and it records that prior familiarity.
- `UNAVAILABLE`: Blocking objections remain after 5 rounds, or no
  cross-family reviewer can be run before the deadline.
- `NOT_REACHED`: R0 is not `MET`.

### R2A′: public-proxy authority

- `MET`: A versioned record clears the protocol. It establishes **authority
  only**: which license classes (SPDX identifiers) and which published
  hosting-service terms permit the planned analysis of public repositories,
  the public-only boundary, and exclusion handling per R0 Section 7. It does
  **not** select, enumerate, or inspect repositories; pool selection happens
  only in R2B′ under the frozen R1 selection rules, and each selected
  source's license is recorded there with its revision. It asserts no
  organizational commitment, adopter interest, or participant consent.
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
  protocol. It draws repositories from the frozen R1 candidate frame within
  the R2A′ license and terms authority, records each selected source's
  license and pinned revision, and applies the frozen R1 and R3 eligibility
  rules. It gives the extraction window, revisions, aggregate eligibility
  decisions, exclusions, clustering, duplication, and missingness, with
  per-item provenance carried by digest as R0 Section 7.1 requires. It reports historical
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

- Selection and eligibility rules are frozen in R1 (and R3 for the domain)
  and merged before any repository is drawn or counted. R2A′ establishes
  license and terms authority only and selects nothing. Changing the rules
  afterwards needs a new amendment version and a disposition of every
  affected count.
- Prior familiarity is declared: repositories already used in Calor
  development, or studied by the maintainer or proposer for Calor, are
  excluded or reported as a separate stratum under the R1 rule.
- Repositories are not added or dropped after counting except under R0
  Section 7 (exclusion request, license change, non-public data).
- Domain cells that Calor does not support are excluded symmetrically from
  all three arms (R3). R2B′ reports the excluded share of the eligible frame
  (items and repositories), so a domain narrowed to Calor-supported shapes is
  visible. Any R5 statement is limited to the included cells.
- Every review round instructs the reviewer to look specifically for bias in
  favor of Calor (protocol).

## 8. Amendment control

Later amendments are numbered (`amendment-002-...`) and versioned. Each names
the definitions it replaces, the evidence it affects, and how that evidence is
dispositioned. No amendment may change a value recorded in the `history` key
of [gate-state.json](gate-state.json).
