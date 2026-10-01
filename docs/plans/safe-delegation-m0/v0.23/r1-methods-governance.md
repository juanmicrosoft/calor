# R1 methods governance (#1372)

**Recorded:** 2026-10-01. **Issue:** #1372, parent epic #1370.
**Authority:** the maintainer decisions of 2026-10-01 recorded in
`r0-authorization.md` and `amendment-001-public-proxy.md` (R0, #1371). This
file defers to those records for caps, deadlines, and permitted access. If
they are absent, unmerged, or inconsistent with this file, R1 is not `MET`.

**Rules governed:** [r1-decision-rules-v1.md](r1-decision-rules-v1.md).

## 1. What replaces the independent human reviewer

#1372 originally requires a consenting independent **human** methods reviewer.
Only the maintainer and AI agents are available. On 2026-10-01 the maintainer
decided that R1 methods governance is performed by cross-family AI adversarial
review instead. Consequences, stated plainly:

- R1 cannot reach the `MET` value as #1372 was originally written. Any `MET`
  recorded here is **"MET (AI-adjudicated)"** under the maintainer's
  substitution, and every downstream output carries the label
  *AI-adjudicated, public-proxy domain*.
- This is **not independent human review**. It does not satisfy any
  requirement elsewhere (#1254, #1259, #1284-#1309, Draft v3 section 7.1) that
  names an independent human reviewer, adopter lead, or non-maintainer
  participant.

## 2. Roles

| Role | Holder | Function |
|---|---|---|
| Proposer | Claude (Anthropic), via Claude Code | Drafts rules; dispositions objections |
| Adversarial methods reviewer | OpenAI Codex CLI (`codex exec`), read-only sandbox | Hostile methods review; countersigns by stating "no blocking objections" against a specific artifact SHA |
| Third check | GitHub Copilot pull-request review | Advisory; its comments are dispositioned in the PR but it cannot countersign |
| Decision and merge authority | Maintainer @juanmicrosoft | Decides rejected blocking objections (section 6); merges |

## 3. Reviewer identity (configuration, not a person)

The "identity" of the reviewer is its configuration. Each round file records
the actual values; the template values are:

| Field | Value |
|---|---|
| Tool | OpenAI Codex CLI, `codex exec` |
| Tool version | Recorded per round (`codex --version`; 0.159.2 at drafting) |
| Model | Recorded per round from the CLI session header; the CLI default under `--ignore-user-config` (`gpt-6.1-sol` at drafting), unchanged across rounds where possible |
| Reasoning effort | `high` (`-c model_reasoning_effort="high"`) for review rounds |
| Sandbox | `--sandbox read-only` |
| Other flags | `--ephemeral` (no session persistence), `--ignore-user-config` (no user plugins or notifier); Codex `memories` feature disabled, so no cross-session memory |
| Working directory | This repository's worktree checked out at the round's artifact SHA |
| Prompt | [reviews/r1/codex-prompt-template.md](reviews/r1/codex-prompt-template.md); its `sha256` is recorded in each round file. Round-specific additions (prior dispositions) are appended verbatim and recorded |
| Network | Not relied on; the reviewer is told to read local files only |

A change of model or tool version between rounds is recorded and does not by
itself invalidate earlier rounds; the countersigning round is the one whose
configuration is cited.

## 4. Competence, independence, and conflicts

**Competence.** Neither AI reviewer has verified credentials in clinical-trial
or econometric methods. Their competence is unmeasured beyond a single
planted-defect probe (section 5.3). The rules compensate by making R4's
estimator falsifiable (type I, Monte Carlo, and reproducibility checks in
rules section 4.2) rather than trusting reviewer judgment about it.

**Independence.**

| Risk | Assessment | Mitigation |
|---|---|---|
| Shared training data | Claude and Codex models are trained on overlapping public text, including the same statistics literature; they may share blind spots and stock answers | Cross-vendor review reduces, but does not remove, correlated error. Recorded as a limitation |
| Framing by proposer | Claude writes the review prompt and the dispositions the reviewer sees | Fixed, hashed prompt template; reviewer reads the artifacts and the original v0.20 records directly instead of a Claude summary; all dispositions are published verbatim |
| Same-vendor overlap in the third check | Copilot may run OpenAI models, so it is not independent of Codex | Copilot is advisory only |
| Sycophancy / leniency | An AI reviewer may stop objecting under repeated rounds or authoritative-sounding rebuttals | Rejected blocking objections cannot be closed by Claude alone (section 6); the round cap is 5; the competence probe tests whether the reviewer flags planted defects |
| Self-review | Claude cannot review its own rules as an independent party | Claude is proposer only and never countersigns |

**Conflicts of interest.**

- The maintainer created Calor and benefits from a favorable result. The
  maintainer also authored the substitution of AI review for human review and
  controls merge. This is a material conflict. It is disclosed, and the rules
  limit it: the maintainer may not overrule a blocking objection into a
  countersignature (section 6), and the thresholds cannot be amended
  (rules section 10).
- Claude (Anthropic) and Codex (OpenAI) have no financial stake in Calor known
  to this record. Their vendors' commercial interest in AI-assisted coding is a
  diffuse conflict and is disclosed.

## 5. Adversarial review protocol

### 5.1 Rounds

1. Record the artifact commit SHA and the `sha256` of each reviewed file.
2. Run Codex with the template prompt plus, from round 2, the prior round's
   objections and their dispositions.
3. Codex emits numbered objections, each with severity `blocking`, `major`,
   or `minor`, and ends with either `VERDICT: no blocking objections` or
   `VERDICT: blocking objections remain`.
4. Claude dispositions every objection: **accepted and fixed** (with the
   change) or **rejected** (with an explicit reason).
5. Stop when a round has zero blocking objections, or after round 5.
6. Each Codex call is appended to [spend-ledger.md](spend-ledger.md).

Round files: `reviews/r1/round-<N>-codex.md` (raw Codex output plus the
disposition table).

### 5.2 Scope of review

The reviewer looks for rules that bias toward Calor, undefined or post-hoc
adjustable thresholds, missing error control, missingness laundering
(treating missing or invalid as success or as zero demand), ambiguous
classifications, untestable criteria, and divergence from the original v0.20
claim. The reviewer must not query public repository task data
(rules section 9).

### 5.3 Competence probe

Before round 1, Codex reviews a short planted-defect rule set
([reviews/r1/competence-probe.md](reviews/r1/competence-probe.md)) containing
known defects. The result is recorded in that file. A reviewer that misses
most planted defects is still used (no alternative exists), but the
limitation is recorded and weakens the countersignature's evidentiary value.

### 5.4 Budget

At most 5 rounds per artifact, within the overall approximately USD 200 API
cap and the 2026-10-29 window set by R0. Exceeding either ends R1 as
`EXPIRED`.

## 6. Recusal and rejected objections

- Claude **cannot be final arbiter** of its own rejection of a **blocking**
  objection. A rejected blocking objection is re-presented to Codex in the
  next round with the rejection reason. If Codex withdraws or downgrades it,
  the rejection stands.
- If Codex maintains a blocking objection after rejection, or round 5 ends
  with it unresolved, it goes to the maintainer, whose explicit decision is
  recorded in `reviews/r1/countersignature.md`:
  - **accept**: the objection must be fixed, which requires another round
    (if rounds remain); or
  - **overrule**: recorded with reasons; R1 is then **not `MET`**, because the
    countersignature requires zero blocking objections. The maintainer cannot
    convert an overrule into a countersignature.
- Rejected `major` and `minor` objections are dispositioned by Claude with
  reasons and published; they do not block.

## 7. Countersignature format

R1 is `MET (AI-adjudicated)` only when all of the following are recorded:

1. A Codex round output containing `VERDICT: no blocking objections`, against
   artifact commit `<SHA>`, with the `sha256` of the normative set:
   `r1-decision-rules-v1.md`, `r1-decision-rules-v1.json`, this file, and
   `reviews/r1/codex-prompt-template.md`.
2. The maintainer's merge of a PR whose merged content for those files has the
   same `sha256` values.
3. No unresolved maintainer overrule under section 6.

Terminal values otherwise: `UNAVAILABLE` (5 rounds without zero blocking
objections, or Codex unavailable), `EXPIRED` (window or cap exhausted),
`REVOKED` / `INVALIDATED` per the #1370 lifecycle amendments, or
`NOT_REACHED` if R0 is not `MET`.

## 8. Round log and countersignature record

The round log, maintainer decisions on rejected blocking objections, and the
countersignature are recorded in
[reviews/r1/countersignature.md](reviews/r1/countersignature.md). That file is
outside the hashed normative set, so recording a result does not change the
content that was countersigned.
