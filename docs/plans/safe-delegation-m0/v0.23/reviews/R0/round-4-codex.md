# R0 review — round 4 — Codex

| Field | Value |
|---|---|
| Gate | R0 (#1371) and amendment 001 |
| Round | 4 |
| Artifacts | as round 1 |
| Reviewed commit | `d60d02d8a31f4d0aeffc69ccf60fa461380e0b39` |
| Date (UTC) | 2026-10-01 |
| Reviewer | OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral` |
| Model (as shown) | gpt-6.1-sol (provider openai) |
| Tokens (as shown) | 37,457 |
| Input | As round 3 |

## Reviewer output (verbatim)

Round-3 dispositions:

1. **Resolved.** R0 Section 10 and Amendment 001 Section 5 now require revalidation of the transitive prerequisite closure, explicitly naming all R5 prerequisites.
2. **Partially resolved.** The accounting start and measurement rules are defined, but the ledger still cannot establish cumulative hours under those rules. See objection 1.

**Rejected BLOCKING objections re-raised: none.**

1. **MAJOR — Hours accounting still understates or leaves unbounded the recorded work.**

   **Location:** `spend-ledger.md:23–31`; `r0-authorization.md`, Section 5, “Planning hours” row.

   Unmetered maintainer effort expressly “counts as 0” until reported. Unknown effort therefore leaves the entire 20-hour allowance available, defeating the required cap check. The single 3.0-hour entry names the Claude session and Codex rounds 1–3 without stating whether it includes each Codex session’s duration separately. R0 requires concurrent sessions to count in full; elapsed time for the parent session alone would undercount overlapping reviewer work.

   **Change:** Replace zero accounting for unknown maintainer effort with a labeled conservative estimate or require reconciliation before further substantive work. Record separate Codex session estimates, or provide an explicit breakdown demonstrating that the 3.0-hour total sums all counted sessions, including overlapping time.

2. **MAJOR — The invocation ledger omits proposer usage despite requiring all AI/tool invocations to be logged.**

   **Location:** `spend-ledger.md:5–15`; `review-protocol.md`, “Spend”; `r0-authorization.md`, Section 5, “Cash” row.

   The ledger contains only Codex reviewer invocations. Its hours entry establishes that Claude Code performed drafting and revisions, but no corresponding usage rows identify their billing basis, marginal spend, or token availability. Subscription exclusion can justify zero marginal spend; it does not waive the stated logging requirement. The asserted cumulative USD total consequently lacks complete coverage of the work recorded elsewhere.

   **Change:** Add proposer invocation records, explicitly marking unavailable token measurements and identifying the subscription or other billing basis. If proposer sessions are intentionally logged at a different granularity, define that exception and reconcile it with the “per invocation” requirement.

## Dispositions

Fixing commit for all ACCEPTED items: `a92f984d2f55e232768fdb4d9bb6c70870e0375f`.

| # | Severity | Disposition | Change or reason |
|---|---|---|---|
| 1 | MAJOR | ACCEPTED | Maintainer effort is entered as a labeled conservative estimate (2.0 h), never as zero. Codex sessions are logged as a separate row (4 x 0.25 h) and counted in full. R0 Section 5 states both rules. |
| 2 | MAJOR | ACCEPTED | The ledger logs proposer usage at session granularity, defined as an explicit exception to per-invocation logging, with "not shown" tokens and the billing basis marked for maintainer confirmation. |

## Stop condition

Round 4 raised no BLOCKING objection and re-raised no rejected BLOCKING
objection. Both MAJOR objections are dispositioned. Under
[review-protocol.md](../../review-protocol.md) the R0 artifact may proceed to
maintainer merge. One round of the 5-round allowance remains unused.
