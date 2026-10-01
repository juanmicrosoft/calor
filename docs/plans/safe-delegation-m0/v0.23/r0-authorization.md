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

There are no exceptions. In particular:

- **Inbound exclusion requests.** If a repository owner or contributor asks,
  unprompted, for their repository or data to be excluded, the request is
  honored under Section 7.3 and the disposition is published by the
  maintainer on #1370. No reply is sent to the requester under this
  authority.
- **Legal obligations.** This record grants no authority to contact anyone.
  If a law independently requires a notice (for example after accidental
  access to non-public third-party data), the inquiry stops; the maintainer
  meets the obligation outside this inquiry; and the event is logged in the
  disposition log (Section 13). Such a notice is not inquiry activity and
  does not change `boundary.humanContact`, which records inquiry contacts.

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
| Review rounds | **5** adversarial review rounds per artifact, counting every reviewer (Codex and Copilot share the allowance) | Counted per [review-protocol.md](review-protocol.md) |
| Inquiry deadline | **2026-10-29, 23:59 UTC** | All substantive gate work must finish by then |
| Planning hours | **Maintainer: 20 hours. AI agent sessions: 60 wall-clock hours.** Both across all 0.23 gates. The maintainer did not state an hours figure when approving R0; these caps were proposed in this record and are adopted only by the maintainer's merge of it. | Self-reported per session in the hours table of [spend-ledger.md](spend-ledger.md). Accounting starts 2026-10-01 at the maintainer's approval and includes pre-merge drafting, review, and revision. Agent hours are session wall-clock time from session start to hand-off, including time spent waiting on tools; concurrent sessions, including reviewer sessions run inside a proposer session, each count in full; unknown durations, for the maintainer as for agents, are logged as labeled conservative estimates and never as zero. This also bounds subscription-backed AI use that the cash cap does not. |
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

Committed records contain repository-level metadata, provenance digests, and
aggregates (7.1). They do not copy source code or issue/PR text, and they do
not cite individual PRs, issues, or commits next to an analysis decision.

## 7. Privacy, retention, deletion, revocation, and notification

These rules satisfy the #1371 privacy and revocation amendment. Because only
public data is permitted, several categories are expected to be empty; the
rules still apply if anything enters them.

### 7.1 Contributor identity in public data

Public repositories expose usernames, display names, and commit emails. The
inquiry does not need individual identity, so:

- Committed artifacts must not contain contributor usernames, display names,
  or email addresses. Emails are never extracted.
- Committed artifacts must not contain per-item identifiers that associate an
  individual public object (a specific PR, issue, or commit) with an
  eligibility decision, an exclusion, or a contributor cluster. A PR number or
  commit SHA resolves to its author by direct lookup, so publishing it next to
  a decision would identify the individual.
- Provenance is carried instead by: the repository name and the pinned
  repository revision (one snapshot SHA per repository, identifying a
  repository state, not a contributor decision); the extraction window; the
  pinned extractor code and eligibility rules; and a SHA-256 digest of the
  full per-item decision list. The pinned extractor writes only aggregates
  and the digest; the per-item list exists in memory or in local caches,
  is never committed, and is deleted with local caches (7.3). Reproduction
  re-runs the extractor and compares aggregates and digest.
- **Scope of the anonymity rule.** The rule governs what this inquiry
  publishes. It does not, and cannot, prevent a third party from re-deriving
  per-item eligibility by running modified code over the same public
  repositories, because every input is already public and attributable. The
  inquiry does not claim anonymity against that reconstruction. The residual
  is accepted because eligibility labels describe the technical shape of
  public changes, not the people who made them, and the inquiry adds no
  personal information to the public inputs.
- Where an analysis needs author or reviewer clustering, it uses per-run
  ordinal pseudonyms (`author-001`, ...) assigned in memory. The mapping from
  pseudonym to username is never written to the repository or to persistent
  storage, and is discarded when the run ends.
- Published aggregates report no statistic computed over fewer than **5**
  distinct contributors; such cells are merged or suppressed and the
  suppression is stated.

### 7.2 Retention versus deletion by category

| Category | Expected content | Retained | Deletion trigger |
|---|---|---|---|
| Contact data | None (no contacts). Inbound exclusion requests only. | Request date and repository only; no handle, name, or message text | Not applicable (nothing personal retained) |
| Restricted metadata | None permitted | Nothing | Immediate on discovery (7.3) |
| Local caches (clones, API responses, scratch extracts, per-item decision lists) | Public data | Only while the gate that created them is active | Gate closure or any trigger in 7.3 |
| Committed provenance records | Repository names, pinned revisions, extractor and rule versions, per-item list digests | As part of the public record | Repository exclusion request, license change that forbids the use, or discovery of personal/non-public data in the record |
| Committed derived aggregates | Counts and estimates | Permanently, as the public record | Only if computed from data later found to be non-public or withdrawn; then marked invalid and recomputed or removed |
| Logs | Review transcripts, spend ledger, disposition log | Permanently, as governance records, **except** that personal, non-public, or confidential content found in a log is removed (7.4); the log keeps a sanitized entry | Discovery of such content |
| Consent records | None (no participants). Repository licenses are recorded in their place. | License identifier and revision per source | Not applicable |
| Pseudonym maps | In memory only | Never persisted | End of each run |

### 7.3 Triggers and deadlines

All deadlines below are for **completed and verified** removal (7.6), not for
opening a pull request.

| Trigger | Action | Deadline |
|---|---|---|
| A gate closes with any value | Delete that gate's local caches | Within 7 days of closure |
| Inquiry deadline passes or R0 becomes `EXPIRED` | Stop substantive work; Section 10 propagation; delete local caches | Stop immediately; deletion completed within 7 days |
| R0 becomes `REVOKED` | Same as expiry | Same as expiry |
| Milestone close (#1370 closed) | Delete any remaining local caches; committed public record stays | Within 7 days |
| Repository owner or contributor asks for exclusion | Exclude the repository from all further analysis; remove its provenance rows; Section 10 invalidation of affected outputs | Stop use immediately; removal merged within 7 days; disposition published on #1370 within 7 days |
| A source's license or terms stop permitting the use | Same as exclusion | Same as exclusion |
| Non-public, restricted, personal, or confidential data is found in any cache, record, or log | Stop the run; delete local copies; remove from the repository, including history (7.4) | Stop immediately; local deletion and working-tree removal merged within 24 hours; history handling per 7.4 |
| A gate is refused (`NOT_AUTHORIZED`) or authority is withdrawn | Inventory every access actually made under that gate (date, source, material) in the disposition log; delete its caches | Inventory within 3 days; deletion within 7 days |

### 7.4 Retention exceptions and isolation

- **Sensitive material overrides audit retention.** If personal, non-public,
  or confidential material reached this public repository's history, the
  maintainer rewrites the affected history (or, where a rewrite is
  impossible, requests removal from the hosting service, including cached
  views and forks it controls) and completes it within 14 days of discovery.
  The disposition log records a sanitized entry: date, path, commit SHA of the
  removal, and the category of material, never the material itself.
- **Public-data history.** Public, non-sensitive material removed for any
  other reason (for example an exclusion request) remains in git history. It
  is isolated by a disposition-log entry naming the commit SHA, path, and
  reason, and by the rule that no later analysis may read it as input. It is
  disposed of only if the requester or a law requires; then the sensitive
  rule above applies.
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
| Inbound exclusion request | The public record: #1370 (no reply to the requester under this authority) | The maintainer | Within 7 days |
| Non-public third-party data accessed and a notice is legally required | Handled outside this inquiry (Section 3); the inquiry stops | The maintainer | As the law requires |

### 7.6 Evidence of disposition

Every deletion, revocation, isolation, or notification is evidenced by an
appended entry in Section 13 giving: date, trigger, material, action taken,
the command or PR/commit SHA that performed it, and a verification (for local
deletion, the command and its output showing the path no longer exists; for
history removal, the removal commit or the hosting service's confirmation;
for notices, a link to the public comment or a dated note that a private
notice was sent). Entries are sanitized: they never reproduce the removed
material.

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

**Machine check.** [gate-state.json](gate-state.json) is the **only
authoritative record** of gate values, boundary flags, and the R5
classification. A Markdown statement that contradicts it has no effect.
`tests/Calor.Compiler.Tests/Plans/SafeDelegationV023BoundaryTests.cs` fails
if:

- any boundary flag is missing or not `false`, or an unknown flag is not
  `false`;
- the gate set differs from the fixed amended graph (R0, R1, R2A′, R3, R2B′,
  R4, R5), or any gate's prerequisite list differs from that graph (the
  test holds its own copy, so editing the JSON cannot loosen it);
- any gate value is outside the #1370 process-state vocabulary;
- any gate is `MET` while a prerequisite is not `MET`;
- the original-gate history values (R1, R2A, R3, R2B, R4 as filed) are
  removed or changed;
- R5 holds a formal classification without the label
  "AI-adjudicated, public-proxy domain", or R5 is `MET` without both a
  formal classification and a separate maintainer action;
- the cash cap exceeds USD 200, the round cap exceeds 5, or an hours cap
  exceeds 20 (maintainer) or 60 (agent sessions);
- a review file is misnamed or exceeds round 5.

It also scans Markdown under `v0.23/` (outside correctly named review
transcripts, which quote reviewer output verbatim) for two declaration forms,
joining wrapped lines within a paragraph:

- a classification declaration (`classification:` or a table cell) naming one
  of the four formal classifications without the required label;
- a prohibited activity (participant enrollment or recruitment, task
  execution, paid collection, acceptance-service implementation) followed by
  a colon or table cell and a state word such as `authorized` or `started`,
  or followed by `is`/`was`/`has been` and such a word, unless a negation
  (`no`, `not`, `never`, `without`, `prohibit`) precedes it in the sentence.

**Limits of the machine check.** Prose scanning is heuristic. A sufficiently
rephrased sentence can evade it, and it cannot judge the truth of a record.
Its purpose is to catch accidental declarations; the authoritative state is
the validated JSON, and the adversarial review protocol remains the control
for meaning.

## 10. Expiry, revocation, revalidation, and invalidation

**Revalidation.** Before a gate starts, before each data access, and
immediately before closure, the acting agent checks, and records in the gate
record with the date and the commit SHA of gate-state.json it read:

1. R0: current value `MET`, not expired, not revoked, and active authority
   has not ended (below);
2. for **each** gate in the transitive prerequisite closure of the acting
   gate (for example R5 checks R0, R1, R2A′, R3, R2B′, and R4, not only R4):
   its current value is `MET`
   in [gate-state.json](gate-state.json); any expiry stated in its own record
   has not passed; no withdrawal, exclusion, license change, or invalidation
   affecting it is recorded in its record, in Section 13, or on #1370 after
   its `MET` date; and the output version (commit SHA) it relied on is the one
   still recorded as current;
3. the cumulative spend and hours are under their caps.

Any failed check stops the work. If a check finds that a prerequisite's
authority or input has lapsed while gate-state.json still shows `MET` (stale
state), the agent records the finding in the active PR, and work resumes only
after the maintainer has merged the invalidation update under the rule below.

**Completion and end of authority.** The inquiry is **complete** when R5 is
dispositioned (its value recorded in gate-state.json and merged). Active
authority ends at the earlier of completion and 2026-10-29 23:59 UTC.

| Case | R0 current value | Consequence |
|---|---|---|
| R5 dispositioned on or before the deadline | `MET` (a completed authorization; no active authority remains) | No further substantive work. The deadline passing later has no effect. Section 7 gate-close and milestone-close rules apply. |
| Deadline passes before R5 is dispositioned, whatever the state of R1-R4 | `EXPIRED` | Invalidation rule below for every affected gate, including gates already `MET`; then the R5 administrative closeout. |

**R5 administrative closeout.** This is the only work permitted when R0 is
`EXPIRED` or `REVOKED`, or when a prerequisite of R5 is not `MET`. It is
exempt from the revalidation stop above. It may only: record the terminal
gate values, invalidations, and disposition actions; publish the dated reason
for which formal adjudication did not occur; preserve `UNADJUDICATED`; and
record a maintainer action. It may not inspect, access, or extract evidence,
size anything, or classify formally. Its record goes through the review
protocol if any round allowance and cash remain; otherwise it records that
review was not available. Its terminal R5 value is `UNAVAILABLE` or `EXPIRED`
per #1377.

**Revocation.** If the maintainer withdraws this authority, the current R0
value changes from `MET` to `REVOKED` with a timestamp in
[gate-state.json](gate-state.json) and Section 13. Section 7 rules execute and
the invalidation rule below applies.

**Prerequisite withdrawal below R0.** The same invalidation rule applies when
any gate's own authority or input is withdrawn after it is `MET`. For R2A′
this includes a source exclusion request or a license/terms change that
removes a source from a pool already used downstream: R2A′ stays `MET` only if
the amended pool still satisfies its record (otherwise it becomes
`INVALIDATED`), and every output computed over the removed source (R2B′, R4,
R5) becomes `INVALIDATED`.

**Invalidation rule (#1370 lifecycle amendments).**

1. Stop substantive work in every affected gate immediately.
2. Each affected gate that started or finished becomes `INVALIDATED`; each
   affected gate that never started becomes `NOT_REACHED`. A historical `MET`
   stays in that gate's audit log but is not the current value and unlocks
   nothing.
3. Each invalidation entry records: time (UTC), cause (gate and value or
   withdrawn input), affected output path and commit SHA, work performed, and
   disposition of partial evidence under Section 7.
4. Invalidated outputs may not be reused. A rerun requires a new versioned
   authorization merged by the maintainer before the deadline.
5. R5 discloses every expiry, revocation, withdrawal, invalidated output, and
   deletion or retention action, and whether the final status therefore
   remains `UNADJUDICATED`.

**Renewal.** Extending or restoring authority requires a new versioned R0
amendment. It does not revalidate invalidated outputs.

## 11. Interpretation limits

- Failure to obtain a party or source is reported only as "none obtained
  within the authorized search and deadline". With zero permitted contacts,
  the authorized search for human parties is empty, so a negative result says
  nothing about whether such parties exist.
- Any 0.23 result is a public-proxy, AI-adjudicated result (amendment 001). It
  does not change the formal M0 status recorded in
  [../decision.md](../decision.md) for the original organizational domain,
  which stays **UNADJUDICATED**.

## 12. Requirement coverage

| Source requirement | Where satisfied |
|---|---|
| #1371: name decision and budget authority | Sections 1, 2 |
| #1371: permitted contacts and who may make them | Section 3 |
| #1371: planning-hours cap, cash/compensation cap, inquiry deadline | Section 5 (hours caps proposed here and adopted by the maintainer's merge) |
| #1371: privacy-safe metadata access and approved restricted storage | Section 6 (none approved; amendment required first) |
| #1371: confidentiality, compensation, conflict, withdrawal, deletion rules | Sections 7, 8 |
| #1371: prohibitions | Section 9 |
| #1371 AC 3: machine-checkable boundary | Section 9, `SafeDelegationV023BoundaryTests` |
| #1371 AC 4: gate value and evidence link on #1370 | Pending: posted by the maintainer after merge |
| #1371 privacy amendment: retention vs deletion triggers by category | 7.2, 7.3 |
| #1371 privacy amendment: deletion/revocation deadlines | 7.3, 7.4 |
| #1371 privacy amendment: who notifies whom, by when | 7.5 |
| #1371 privacy amendment: retention exceptions and isolation | 7.4 |
| #1371 privacy amendment: evidence of deletion/revocation/notification | 7.6, Section 13 |
| #1371 privacy amendment: `MET` to `REVOKED` and propagation | Section 10 |
| #1370 lifecycle amendments: revalidation, `INVALIDATED`, `NOT_REACHED`, R5 disclosure, R5 closeout exception | Section 10 |
| #1371 interpretation: no universal-nonexistence claim | Section 11 |

## 13. Audit log

### Gate value history

| Date (UTC) | Value | By | Evidence |
|---|---|---|---|
| 2026-10-01 | `MET` (effective on merge) | @juanmicrosoft | This record and its merge commit |

### Review status

Four Codex rounds under [review-protocol.md](review-protocol.md):
[1](reviews/R0/round-1-codex.md) (6 BLOCKING, 4 MAJOR),
[2](reviews/R0/round-2-codex.md) (5 BLOCKING, 1 MAJOR),
[3](reviews/R0/round-3-codex.md) (1 BLOCKING, 1 MAJOR),
[4](reviews/R0/round-4-codex.md) (0 BLOCKING, 2 MAJOR). Every objection is
dispositioned; no BLOCKING objection is unresolved.

### Retention and disposition log

| Date | Trigger | Material | Action | Command / PR / SHA | Verification |
|---|---|---|---|---|---|
| — | — | — | No entries | — | — |
