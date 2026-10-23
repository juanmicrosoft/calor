# Trusted Publishing PR (#1545): Codex round 2

Reviewed commit f1249f016, which holds the round-1 fixes, against base a6d5c2f0d. The command was
`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Raw final message:
`trusted-publishing-round-2-codex.raw.txt`.

**Verdict: REQUEST CHANGES.** One BLOCKING and one MINOR finding. Both are fixed in the next commit.

**Codex verified:**

- Round 1's fixes are complete. An empty or partial environment list is never CLOSED, and
  `nuget-release`'s secrets are still read. Invalid name variants fail closed.
- The attestation refuses repeated members (including escaped duplicates) and impossible dates.
- An unattested 403 is `V001`, and an API answer overrides any attestation.
- The README now explains that clearing the policy's environment broadens trust.
- The upstream validator and the `NuGet/login` pin match the documented behavior.
- No direct credential bypass exists under the stated settings.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Both API response loaders (`gh_fetch` and `dir_fetch`) used plain `json.loads`, which keeps the last of two repeated members. A workflow-permissions response with `"write"` then `"read"` produced CLOSED. This is malformed input, not a live bypass. | **Fixed.** Both loaders use the attestation's `unique_members` hook. A repeated member is a `ValueError`, which becomes `Unreadable`, so the result is UNKNOWN. Test: `test_api_responses_with_repeated_members_are_unreadable` covers saved responses for the workflow permissions, the repository secrets, and the environment, plus a mocked `gh` response. |
| 2 | MINOR | The `publish` job's comment said the exchange fails until the GitHub settings are finished. An unrestricted or auto-recreated `nuget-release` would still produce a matching environment claim. | **Fixed.** The comment now calls the `main`-only rule a required security control, not a fail-safe. Only a missing or mismatched nuget.org policy makes the exchange fail. |
