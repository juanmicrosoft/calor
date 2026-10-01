# Safe delegation M0 v0.23 — spend ledger

**Caps:** USD 200.00 marginal AI API/tool spend across all 0.23 gates
([r0-authorization.md](r0-authorization.md) Section 5). Existing
subscriptions are excluded from the cash figure; their usage is still logged.
**Procedure:** [review-protocol.md](review-protocol.md).

Append one row per invocation. Do not edit earlier rows; correct them with a
new row that cites the row it corrects.

| # | Date (UTC) | Gate | Round | Tool / version | Model | Tokens | Marginal USD | Billing basis | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 2026-10-01 | R0 | 1 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol | 35,454 | 0.00 | ChatGPT subscription (excluded) | [round-1-codex.md](reviews/R0/round-1-codex.md) |
| 2 | 2026-10-01 | R0 | 2 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol | 41,126 | 0.00 | ChatGPT subscription (excluded) | [round-2-codex.md](reviews/R0/round-2-codex.md) |
| 3 | 2026-10-01 | R0 | 3 | Codex CLI 0.159.2 (`codex exec`, read-only) | gpt-6.1-sol | 43,433 | 0.00 | ChatGPT subscription (excluded) | [round-3-codex.md](reviews/R0/round-3-codex.md) |

**Cumulative marginal USD:** 0.00

## Hours

Caps: maintainer 20 hours; AI agent sessions 60 wall-clock hours
([r0-authorization.md](r0-authorization.md) Section 5), with the accounting
rules stated there. Append one row per session; other gates' concurrent
sessions append their own rows.

| Date (UTC) | Gate | Session | Maintainer hours | Agent session hours | Basis |
|---|---|---|---|---|---|
| 2026-10-01 | R0 | Claude Code session drafting R0, amendment 001, protocol, boundary test, and Codex rounds 1-3 | not metered | 3.0 | Conservative estimate; session timestamps not captured |

**Cumulative:** maintainer not metered for this row (counts as 0 of 20 until
reported); agent 3.0 of 60 (estimate).
