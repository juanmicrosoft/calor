# Safe delegation M0 v0.23 — spend ledger

**Caps:** USD 200.00 marginal AI API/tool spend across all 0.23 gates
([r0-authorization.md](r0-authorization.md) Section 5). Existing
subscriptions are excluded from the cash figure; their usage is still logged.
**Procedure:** [review-protocol.md](review-protocol.md).

Append one row per reviewer invocation. Proposer (Claude Code) usage is
logged at session granularity, one row per session, because a session issues
many model calls that are not individually reported. Do not edit earlier rows;
correct them with a new row that cites the row it corrects. A token figure
that the tool does not show is written "not shown".

| # | Date (UTC) | Gate | Round | Tool / version | Model | Tokens | Marginal USD | Billing basis | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 2026-10-01 | R0 | 1 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol | 35,454 | 0.00 | ChatGPT subscription (excluded) | [round-1-codex.md](reviews/R0/round-1-codex.md) |
| 2 | 2026-10-01 | R0 | 2 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol | 41,126 | 0.00 | ChatGPT subscription (excluded) | [round-2-codex.md](reviews/R0/round-2-codex.md) |
| 3 | 2026-10-01 | R0 | 3 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol | 43,433 | 0.00 | ChatGPT subscription (excluded) | [round-3-codex.md](reviews/R0/round-3-codex.md) |
| 4 | 2026-10-01 | R0 | 4 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol | 37,457 | 0.00 | ChatGPT subscription (excluded) | [round-4-codex.md](reviews/R0/round-4-codex.md) |
| 5 | 2026-10-01 | R0 | — | Claude Code (proposer session) | claude-opus-5-5 | not shown | 0.00 | Existing Claude Code subscription (excluded); maintainer to confirm the basis | Drafting, revisions, and running rounds 1-4 |
| 6 | 2026-10-01 | R1 | probe | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol (reasoning none) | 3,197 | 0.00 | ChatGPT subscription (excluded) | Competence probe on a planted-defect rule set, not an R1 artifact version ([r1/competence-probe.md](r1/competence-probe.md)) |
| 7 | 2026-10-01 | R1 | 1 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol (reasoning high) | 80,815 | 0.00 | ChatGPT subscription (excluded) | [round-1-codex.md](reviews/R1/round-1-codex.md); 13 BLOCKING, 2 MAJOR |
| 8 | 2026-10-01 | R1 | 2 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol (reasoning high) | 84,580 | 0.00 | ChatGPT subscription (excluded) | [round-2-codex.md](reviews/R1/round-2-codex.md); 3 BLOCKING, 2 MAJOR |
| 9 | 2026-10-01 | R1 | 3 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol (reasoning high) | 108,076 | 0.00 | ChatGPT subscription (excluded) | [round-3-codex.md](reviews/R1/round-3-codex.md); 3 BLOCKING |
| 10 | 2026-10-01 | R1 | 4 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol (reasoning high) | 112,044 | 0.00 | ChatGPT subscription (excluded) | [round-4-codex.md](reviews/R1/round-4-codex.md); 5 BLOCKING |
| 11 | 2026-10-01 | R1 | 5 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol (reasoning high) | 118,392 | 0.00 | ChatGPT subscription (excluded) | [round-5-codex.md](reviews/R1/round-5-codex.md); 2 BLOCKING, 1 MAJOR |
| 12 | 2026-10-01 | R1 | — | Claude Code (proposer session) | claude-opus-5-5 | not shown | 0.00 | Existing Claude Code subscription (excluded); maintainer to confirm the basis | Drafting, revisions, running the probe and rounds 1-5 |
| 13 | 2026-10-01 | R2A′ | 1 | Codex CLI 0.159.2 (`codex exec -s read-only`) | gpt-6.1-sol | 602,591 (input 599,290, of which 486,144 cached; output 3,301, including 328 reasoning) | 0.00 | ChatGPT subscription (excluded); the CLI reported no USD cost; maintainer to confirm the basis | [round-1-codex.md](reviews/R2A-prime/round-1-codex.md); 1 BLOCKING, 6 MAJOR. Migrated from the #1465 ledger format at the merge with R0 |
| 14 | 2026-10-01 | R2A′ | 2 | Codex CLI 0.159.2 (`codex exec -s read-only`) | gpt-6.1-sol | 262,551 (input 259,274, of which 202,880 cached; output 3,277, including 654 reasoning) | 0.00 | ChatGPT subscription (excluded); the CLI reported no USD cost; maintainer to confirm the basis | [round-2-codex.md](reviews/R2A-prime/round-2-codex.md); 0 BLOCKING, 5 MAJOR, 1 MINOR. Migrated from the #1465 ledger format at the merge with R0 |
| 15 | 2026-10-01 | R2A′ | — | Claude Code (proposer session) | claude-opus-5-5 | not shown | 0.00 | Existing Claude Code subscription (excluded); maintainer to confirm the basis | Drafting, enumeration run, revisions, and running rounds 1-2. Row added at the merge with R0; the #1465 ledger had no proposer row |

Rows 6-12 are reserved for R1 (#1372, PR #1468), which was drafted in a
parallel branch and numbered its rows first. R2A′ rows start at 13 so that
neither branch renumbers the other's rows when they are merged. GitHub
REST/GraphQL calls made by the R2A′ enumeration are free under the
authenticated rate limit and are not logged as spend.

**Cumulative marginal USD:** 0.00

## Hours

Caps: maintainer 20 hours; AI agent sessions 60 wall-clock hours
([r0-authorization.md](r0-authorization.md) Section 5), with the accounting
rules stated there. Append one row per session; other gates' concurrent
sessions append their own rows.

| Date (UTC) | Gate | Session | Maintainer hours | Agent session hours | Basis |
|---|---|---|---|---|---|
| 2026-10-01 | R0 | Claude Code proposer session (drafting, revisions, running rounds 1-4) | — | 3.0 | Conservative estimate; session timestamps not captured |
| 2026-10-01 | R0 | Codex review sessions, rounds 1-4 (counted in full although they ran inside the proposer session) | — | 1.0 | Conservative estimate, 0.25 h each |
| 2026-10-01 | R0 | Maintainer approval of R0 and amendment 001 terms; merge review | 2.0 | — | Conservative estimate pending the maintainer's report |
| 2026-10-01 | R1 | Claude Code proposer session (drafting, revisions, running the probe and rounds 1-5) | — | 2.0 | Conservative estimate; branch commits span 14:08-15:25 EDT, plus earlier reading |
| 2026-10-01 | R1 | Codex sessions: competence probe and rounds 1-5 (counted in full although they ran inside the proposer session) | — | 1.5 | Conservative estimate, 0.25 h each |
| 2026-10-01 | R2A′ | Claude Code proposer session (drafting, enumeration, revisions, running rounds 1-2) | — | 2.0 | Conservative estimate; branch commits span 14:05-14:52 EDT, plus earlier reading. Added at the merge with R0 |
| 2026-10-01 | R2A′ | Codex review sessions, rounds 1-2 (counted in full) | — | 0.5 | Conservative estimate, 0.25 h each |

**Cumulative:** maintainer 2.0 of 20 (estimate); agent 10.0 of 60 (estimate; R0 4.0 + R2A′ 2.5 + R1 3.5).
Unknown effort is never counted as zero; it is entered as a labeled
conservative estimate and reconciled when reported.
