# PR A, exception EXC-RELEASE-GAPS-A-CHECKER: Codex verification-only pass

Granted by @juanmicrosoft on 2026-10-18: one fix plus one verification-only pass. Reviewing
ef9ccdd0b (PR A reapplied onto main 01acd188c, plus the `uses` fix). Raw final message:
`pr-a-exception-verification-codex.raw.txt`.

**Verdict: REQUEST-CHANGES** (1 MAJOR).

**Verified as fixed:**

- The folded `uses: >-` and quoted `"uses"` cases now fail.
- The committed workflows report CLOSED.
- The earlier regression controls pass.
- The README's check names, code descriptions, and API field names match the code. (`W005` is
  defined but never emitted.)

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | A multiline flow-style job body (a `{` on its own line at the job's key indent, with `uses:` on a more-indented line inside it) bypasses `W011`. The scan looks only at lines at the first content line's indentation. | **Not fixed.** Under the exception's terms, a BLOCKING or MAJOR result stops PR A. No PR is opened. Proposed fix, unreviewed: check every line of a job body (any indentation) for a decoded `uses` key. Accept only a key at the job's key indent that matches the one-line local form, or a key on a step line (a `- uses:` item, or a key under a `- ` step). Simpler and stricter: fail any job whose body contains `{` or `[` outside a quoted string at the key indent. Add the multiline flow-mapping case. |
