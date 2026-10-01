# v0.23 M0 spend ledger

Append-only. One row per paid model/API call made for a v0.23 gate. The
v0.23 inquiry cap is approximately USD 200 overall (R0, 2026-10-01).
`tokens` is the provider-reported total (input + output) where available.
GitHub REST/GraphQL calls are free under the authenticated rate limit and are
not logged here.

| date | gate | round | tool | model | tokens | notes |
|---|---|---|---|---|---|---|
| 2026-10-01 | R2A′ (#1373) | 1 | Codex CLI 0.159.2 `exec -s read-only` | gpt-6.1-sol | 602,591 (input 599,290 of which 486,144 cached; output 3,301 incl. 328 reasoning) | Hostile review of R2A′ doc, repo JSON, script; 1 blocking / 6 major. USD cost not reported by CLI |
