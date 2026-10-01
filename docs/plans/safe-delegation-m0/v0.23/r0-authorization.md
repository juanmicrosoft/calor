# Safe delegation M0 v0.23 — R0 bounded inquiry authorization

**Recorded:** 2026-10-01. **Issue:** #1371. **Epic:** #1370.
**Gate value:** `MET`, effective upon maintainer merge of the pull request that
introduces this record. Before that merge this file is a proposal and grants no
authority. **Machine-readable state:** [gate-state.json](gate-state.json).

This record authorizes a bounded, public-data-only planning inquiry into the M0
prerequisites. It is not a study authorization, a participant protocol, an
implementation authorization, or a software release. The downstream gate
definitions it governs are changed by
[amendment 001](amendment-001-public-proxy.md); this record sets the authority,
caps, and privacy rules that every 0.23 gate, original or amended, must obey.

## 1. Decision

The maintainer, [@juanmicrosoft](https://github.com/juanmicrosoft), approved R0
on 2026-10-01 with the terms below. The approval was given to the implementing
agent in the session that produced this record and becomes the repository
record of that decision when the maintainer merges it.

| Field | Value |
|---|---|
| Decision authority | @juanmicrosoft |
| Budget authority | @juanmicrosoft |
| Gate value | `MET` on merge |
| Planning window start | 2026-10-01 |
| Inquiry deadline | 2026-10-29, 23:59 UTC |
| Governing amendment | [amendment-001-public-proxy.md](amendment-001-public-proxy.md) |
| Review procedure | [review-protocol.md](review-protocol.md) |
| Spend record | [spend-ledger.md](spend-ledger.md) |

## 2. Roles

Only the maintainer and AI agents take part. No other person has a role.

| Role | Holder | Authority |
|---|---|---|
| Decision and budget authority; merger of gate records; R5 classifier and action owner | @juanmicrosoft | Sole authority to set, amend, revoke, or expire any gate value |
| Proposer / implementer | Claude Code (Anthropic) | Drafts records, code, and dispositions; no gate authority |
| Required cross-family adversarial reviewer | OpenAI Codex CLI (codex-cli 0.159.2), run per [review-protocol.md](review-protocol.md) | Raises objections; no gate authority; cannot be overruled into silence (see protocol) |
| Supplementary adversarial reviewer | GitHub Copilot pull-request review | Raises objections on PRs; no gate authority |

AI reviewers are quality controls. They are not independent human reviewers,
methods signatories, adopters, or participants, and no 0.23 record may describe
them as such.

## 3. Permitted contacts and communications

**Permitted contacts: none.** No human reviewer, adopter, organization,
participant, repository owner, or contributor is contacted by anyone acting
under this authority. There is no human outreach.

Two narrow non-solicitation exceptions exist, both executed only by the
maintainer:

1. **Inbound exclusion requests.** If a repository owner or contributor asks,
   unprompted, for their repository or data to be excluded, the maintainer may
   reply once to confirm the disposition (Section 7). The reply may not solicit
   participation, review, or opinion.
2. **Legally required notices.** If Section 7 discovers that non-public
   third-party data was accessed and a notice is legally required, the
   maintainer may send that notice.

Each such message is logged in the disposition log (Section 12). Neither
exception authorizes any other contact.

## 4. Permitted work

Within the window and caps, AI agents may, under maintainer direction:

- write and revise the 0.23 gate records under `docs/plans/safe-delegation-m0/v0.23/`;
- read public data within Section 6;
- write and run pinned, offline analysis code over public-data extracts and
  synthetic inputs (for example R4 estimators), where that code performs no
  task execution, participant activity, or paid collection;
- run adversarial review rounds under [review-protocol.md](review-protocol.md).

Everything else is outside this authority, including everything in Section 9.

## 5. Caps and deadline

| Cap | Value | Measurement |
|---|---|---|
| Cash | **USD 200.00 hard ceiling** across all 0.23 gates. The maintainer stated "approximately USD 200"; this record fixes it as a ceiling. | Marginal billed AI API/tool spend, recorded per invocation in [spend-ledger.md](spend-ledger.md). Existing subscriptions (Claude Code, the ChatGPT plan used by Codex CLI, GitHub Copilot) are excluded from the cash figure; their token use is still logged. |
| Review rounds | **5** adversarial review rounds per artifact | Counted per [review-protocol.md](review-protocol.md) |
| Inquiry deadline | **2026-10-29, 23:59 UTC** | All substantive gate work must finish by then |
| Person-hours | **No person-hour cap was set by the maintainer.** Maintainer hours are not metered, as in 0.20. AI agent time is bounded only by the cash cap, the round cap, and the deadline. | Recorded as an open item; a person-hour cap requires an R0 amendment |
| Compensation | **USD 0** to any person | Section 8 |

Exceeding any cap, or extending the deadline, requires a versioned R0
amendment merged by the maintainer **before** the excess work or spend occurs.
Spend that would cross the ceiling is not incurred; the affected gate records
the shortfall instead.

## 6. Data access

**Permitted:** public data only:

- public open-source repositories, including source, history, and metadata;
- their public issue and pull-request history;
- their licenses and the published terms of the hosting service.

**Prohibited:** private, restricted, internal, or personal data; any repository
or record that is not publicly readable, even if a maintainer credential
(for example an authenticated `gh` token) can read it; contributor email
addresses; any data obtained by contacting a person.

**Restricted storage: none is approved and none is needed**, because no
restricted data may be accessed. If any gate ever needs non-public data, a
versioned R0 amendment that names the data, the storage location, access
control, and retention rules must be merged by the maintainer **before** that
access. Without such an amendment, any attempt to access non-public data is a
boundary violation handled by Section 7.

Committed records contain metadata, identifiers of public objects (repository,
issue or PR number, commit SHA), and aggregates. They do not copy source code
or issue/PR text beyond what is needed to cite a public object.

## 7. Privacy, retention, deletion, revocation, and notification

These rules satisfy the #1371 privacy and revocation amendment. Because only
public data is permitted, several categories are expected to be empty; the
rules still apply if anything enters them.

### 7.1 Contributor identity in public data

Public repositories expose usernames, display names, and commit emails. The
inquiry does not need individual identity, so:

- Committed artifacts must not contain contributor usernames, display names,
  or email addresses. Emails are never extracted.
- Per-item provenance uses public object identifiers (repository, PR or issue
  number, commit SHA). These identifiers are public pointers and can be
  resolved to an author by anyone; this residual linkability is accepted as
  the cost of provenance and is not further reduced.
- Where an analysis needs author or reviewer clustering, it uses per-run
  ordinal pseudonyms (`author-001`, ...) assigned in memory. The mapping from
  pseudonym to username is never written to the repository or to persistent
  storage, and is discarded when the run ends. Reproduction re-derives it by
  re-running the pinned extractor over the same public revisions.
- Published aggregates report no statistic computed over fewer than **5**
  distinct contributors; such cells are merged or suppressed and the
  suppression is stated.

### 7.2 Retention versus deletion by category

| Category | Expected content | Retained | Deletion trigger |
|---|---|---|---|
| Contact data | None (no contacts). Inbound exclusion requests only. | Request date, repository, disposition; no personal details beyond the requester's public handle in the public issue/PR where they asked | Not deleted (governance record) unless the requester asks; then reduced to date and repository |
| Restricted metadata | None permitted | Nothing | Immediate on discovery (7.3) |
| Local caches (clones, API responses, scratch extracts) | Public data | Only while a gate is active | Any trigger in 7.3 |
| Committed per-item derived records | Public object identifiers, eligibility decisions | As part of the public record | Repository exclusion request, license change that forbids the use, or discovery of personal/non-public data in the record |
| Committed derived aggregates | Counts and estimates | Permanently, as the public record | Only if computed from data later found to be non-public or withdrawn; then marked invalid and recomputed or removed |
| Logs | Review transcripts, spend ledger, disposition log | Permanently, as governance records | Not deleted; corrected by appended entries |
| Consent records | None (no participants). Repository licenses are recorded in their place. | License identifier and revision per source | Not applicable |
| Pseudonym maps | In memory only | Never persisted | End of each run |

### 7.3 Triggers and deadlines

| Trigger | Action | Deadline |
|---|---|---|
| Inquiry deadline passes or R0 becomes `EXPIRED` | Stop substantive work; delete local caches | Stop immediately; delete within 7 days |
| R0 becomes `REVOKED` | Same as expiry, plus Section 10 propagation | Stop immediately; delete within 7 days |
| Milestone close (#1370 closed) | Delete local caches; committed public record stays | Within 7 days |
| Repository owner or contributor asks for exclusion | Exclude the repository from all further analysis; remove its per-item records by PR; mark affected aggregates invalid and recompute or remove | Stop use immediately; PR within 7 days; reply (Section 3) within 7 days |
| A source's license or terms stop permitting the use | Same as exclusion | Same as exclusion |
| Non-public, restricted, or personal data is found in any cache or record | Stop the run; delete local copies; remove from the working tree by PR | Stop immediately; delete within 24 hours; PR within 24 hours |
| Refusal (`NOT_AUTHORIZED`) of any gate | No access occurred under that gate; delete any preparatory caches | Within 7 days |

### 7.4 Retention exceptions and isolation

- **Git history.** Material merged into this public repository remains in git
  history after a removal PR. History is not rewritten by default. Retained
  material is isolated by (a) a disposition-log entry naming the commit SHA,
  path, and reason, and (b) a rule that no later analysis may read it as
  input. Within 7 days the maintainer decides whether a history purge is
  required and records that decision.
- **Governance records.** Gate records, review transcripts, the spend ledger,
  and the disposition log are retained as the audit trail. Invalidated or
  expired evidence inside them may be retained only for audit and may not be
  reused (#1370 lifecycle amendment).
- No legal hold or other policy retention obligation is known on 2026-10-01.
  If one arises, it is recorded in the disposition log with the same
  isolation rule.

### 7.5 Notification

| Event | Who is notified | By whom | Deadline |
|---|---|---|---|
| Any change to access scope, confidentiality, or a source's permission (exclusion, license change, non-public discovery) | The maintainer | The agent or person who detects it, by stopping and recording the event in the active PR or gate record | Immediately, in the same session |
| The same events | The public record: #1370 and the affected gate issue | The maintainer | Within 3 calendar days |
| R0 `EXPIRED` or `REVOKED`; any gate `INVALIDATED` | The public record: #1370 and every affected gate issue | The maintainer | Within 3 calendar days |
| Inbound exclusion request | The requester | The maintainer (Section 3, exception 1) | Within 7 days |
| Non-public third-party data accessed and notice legally required | The data owner | The maintainer (Section 3, exception 2) | As the law requires |

### 7.6 Evidence of disposition

Every deletion, revocation, isolation, or notification is evidenced by an
appended entry in Section 12 giving: date, trigger, material, action taken,
the command or PR/commit SHA that performed it, and a verification (for local
deletion, the command and its output showing the path no longer exists; for
notices, a link to the public comment or a dated note that a private notice
was sent).

## 8. Confidentiality, compensation, and conflicts

**Confidentiality.** Nothing in the inquiry is confidential. All records are
public. If confidential material is encountered, it is treated as a
non-public-data discovery under 7.3.

**Compensation.** None. No person is paid or offered anything. AI providers are
paid only through existing subscriptions or within the Section 5 cash cap.

**Conflicts.**

| Party | Interest | Mitigation |
|---|---|---|
| @juanmicrosoft | Authors Calor and has an interest in a favorable outcome. Also holds decision, budget, and R5 classification authority, so these roles are not separated. | Cross-family adversarial review of every gate artifact; unresolved blocking objections prevent `MET` (protocol); mandatory "AI-adjudicated, public-proxy domain" label; no claim of independence |
| Claude Code (proposer) | Its model family produced much of Calor's code and documentation, so it shares authorship bias with the subject under study. | It may not review its own artifacts as the required reviewer; a different model family must |
| OpenAI (Codex) and GitHub (Copilot) | No stake in Calor's outcome. Their models share training-data overlap with the proposer and may share blind spots. The model behind Copilot review is not pinned and may not be a different family. | Codex is the required cross-family reviewer; Copilot is supplementary only; the shared-blind-spot risk is disclosed in every R5 output |

## 9. Non-authorization boundary

This authority does **not** permit, and no 0.23 gate may record as permitted,
started, or performed:

- participant enrollment or recruitment;
- task execution (running study tasks with any agent or person);
- paid experiment collection;
- acceptance-service implementation, starter sets, workflows, or a held-out
  oracle;
- reuse of any 0.23 approval, funds, tasks, or evidence by #1254 or #1259;
- reopening, satisfying, or changing the state of #1284-#1309;
- any human contact beyond Section 3;
- any non-public data access beyond Section 6.

Even a later `FEASIBLE AS PROPOSED` does not activate implementation, spending,
or recruitment.

**Machine check.** [gate-state.json](gate-state.json) records these
prohibitions as boundary flags that must all be `false`, together with every
gate value. `tests/Calor.Compiler.Tests/Plans/SafeDelegationV023BoundaryTests.cs`
fails if:

- any boundary flag is not `false`;
- any gate value is outside the #1370 process-state vocabulary;
- any gate is `MET` while a prerequisite is not `MET`;
- the original-gate history values (R1, R2A, R3, R2B, R4 as filed) are
  removed or changed;
- an R5 scientific classification lacks the label
  "AI-adjudicated, public-proxy domain", or a Markdown record under
  `v0.23/` declares such a classification without that label;
- a Markdown record under `v0.23/` declares participant enrollment, task
  execution, paid collection, or acceptance-service implementation as
  authorized, started, or performed;
- a review file is misnamed or exceeds round 5.

## 10. Expiry, revocation, and revalidation

Each gate revalidates this authority, the deadline, and the cash cap before it
starts, before each data access, and immediately before closure (#1370
lifecycle amendment).

- **Expiry.** If 2026-10-29 23:59 UTC passes before every gate R1-R4 is
  dispositioned, R0 becomes `EXPIRED`. In-progress gates become `INVALIDATED`;
  later gates become `NOT_REACHED`. R5 still runs, but only to record the
  closeout and preserve `UNADJUDICATED` with a dated reason; it may not access
  new evidence.
- **Revocation.** If the maintainer withdraws this authority, the current R0
  value changes from `MET` to `REVOKED` with a timestamp in
  [gate-state.json](gate-state.json) and Section 11. Section 7 rules execute.
  Every dependent gate, including any already closed as `MET`, becomes
  `INVALIDATED`; later gates become `NOT_REACHED`. A historical `MET` entry
  stays in the audit log below but is not the current value and unlocks
  nothing.
- **Renewal.** Extending or restoring authority requires a new versioned R0
  amendment; it does not silently revalidate invalidated outputs.

## 11. Interpretation limits

- Failure to obtain a party or source is reported only as "none obtained
  within the authorized search and deadline". With zero permitted contacts,
  the authorized search for human parties is empty, so a negative result says
  nothing about whether such parties exist.
- Any 0.23 result is a public-proxy, AI-adjudicated result (amendment 001). It
  does not change the formal M0 status recorded in
  [../decision.md](../decision.md) for the original organizational domain,
  which stays **UNADJUDICATED**.

## 12. Audit log

### Gate value history

| Date (UTC) | Value | By | Evidence |
|---|---|---|---|
| 2026-10-01 | `MET` (effective on merge) | @juanmicrosoft | This record and its merge commit |

### Retention and disposition log

| Date | Trigger | Material | Action | Command / PR / SHA | Verification |
|---|---|---|---|---|---|
| — | — | — | No entries | — | — |
