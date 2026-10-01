# Safe delegation M0 v0.23 — adversarial review protocol

**Recorded:** 2026-10-01. **Version:** 1. **Authority:**
[r0-authorization.md](r0-authorization.md) and
[amendment-001-public-proxy.md](amendment-001-public-proxy.md).

Every 0.23 gate artifact passes this protocol before its gate can be `MET`.
The protocol is a quality control by AI models. It is not independent human
review, and no record may describe it as such.

## Participants

| Role | Who | Rule |
|---|---|---|
| Proposer | Claude Code (Anthropic) | Submits artifact versions and dispositions objections |
| Required reviewer | OpenAI Codex CLI via `codex exec` | Must be a different model family from the proposer |
| Supplementary reviewer | GitHub Copilot PR review | Optional; never replaces the required reviewer |
| Adjudicator of the final record | @juanmicrosoft | Merges or does not merge; cannot convert an unresolved blocking objection into `MET` |

## File layout

```text
docs/plans/safe-delegation-m0/v0.23/
  reviews/<gate>/round-<N>-<reviewer>.md
  spend-ledger.md
```

- `<gate>` is the gate ID with any prime written as `-prime`: `R0`, `R1`,
  `R2A-prime`, `R3`, `R2B-prime`, `R4`, `R5`. Other reviewable 0.23 artifacts
  use a short kebab-case name.
- `<N>` is 1-5, counted across all reviewers (see Round accounting). A sixth
  round is never run.
- `<reviewer>` is lowercase kebab-case: `codex` or `copilot`.

The boundary test checks the file names and the round limit; the gate
directory name is a convention and is not machine-checked.

## Round procedure

1. **Submit.** The proposer commits the artifact and records the commit SHA
   that contains the version under review.
2. **Review.** The proposer runs the reviewer in a read-only sandbox against
   that commit:

   ```bash
   codex exec -s read-only --ephemeral -C <worktree> \
     -o <scratch>/round-<N>.txt "<reviewer prompt>"
   ```

   The prompt names the artifact paths and the SHA, the governing issues, and
   this instruction, verbatim:

   > You are an adversarial reviewer from a different model family than the
   > author. Find flaws, internal contradictions, ambiguities, missing
   > requirements from the cited issues, and any bias that favors Calor or
   > the author's preferred outcome. Do not praise. Emit numbered objections,
   > each with severity BLOCKING, MAJOR, or MINOR, the exact location, and
   > the change that would resolve it. If round N > 1, first state for each
   > earlier objection whether its disposition resolves it.

3. **Record.** The proposer writes
   `reviews/<gate>/round-<N>-<reviewer>.md` with:
   - gate, round, artifact paths, reviewed commit SHA, date (UTC);
   - reviewer tool, version, and model if the tool shows it;
   - token use if shown;
   - the reviewer's output verbatim;
   - a disposition table: objection number, severity, disposition
     (`ACCEPTED` with the fixing commit, or `REJECTED` with the reason).
4. **Log spend.** Append one row to [spend-ledger.md](spend-ledger.md) for each
   invocation.
5. **Repeat or stop.**

## Severity and stopping

- **BLOCKING:** if correct, the gate value would be unjustified, a #1370 or
  R0 rule would be broken, or the record would overstate its evidence.
- **MAJOR:** a material defect that does not by itself invalidate the gate
  value.
- **MINOR:** wording, clarity, or presentation.

The proposer may reject any objection with a stated reason. A rejected
BLOCKING objection is shown to the reviewer in the next round. If the
reviewer re-raises it as BLOCKING, it remains unresolved.

Stop when:

- a round yields **no BLOCKING objection** (new or re-raised), and every
  MAJOR objection has a disposition: the gate may proceed to merge; or
- round 5 ends with any BLOCKING objection unresolved: the gate **cannot be
  `MET`**. The gate record lists every unresolved objection, and the gate
  closes `UNAVAILABLE` or stays open for a new artifact version only by a
  versioned amendment.

The maintainer may not override an unresolved BLOCKING objection to reach
`MET`. The maintainer may decline to merge for any reason.

## Supplementary Copilot review

Copilot comments on a gate PR are dispositioned in the PR conversation and
summarized in `round-<N>-copilot.md`, where `<N>` is the round whose artifact
version Copilot reviewed. They never replace a Codex round.

## Round accounting

The R0 limit is **5 adversarial review rounds per artifact, counting every
reviewer**. A round is one submitted artifact version (one commit SHA). Codex
and Copilot reviewing the same version are one round. Any additional version
submitted to any reviewer, including a Copilot review of an intermediate push,
consumes a new round. The proposer therefore requests Copilot review only on a
round's commit, and pushes intermediate fixes only when the next round
begins.

## Spend

Each invocation is logged with date, gate, round, tool and version, model if
shown, tokens if shown, marginal USD, and billing basis. Subscription-backed
use is logged at USD 0 marginal with its tokens. The cumulative marginal USD
across all 0.23 gates may not exceed USD 200.00 (R0 Section 5). An invocation
that could cross the cap is not run.
