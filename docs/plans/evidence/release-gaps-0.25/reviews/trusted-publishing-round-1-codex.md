# Trusted Publishing PR (#1545): Codex round 1

Reviewed commit 91d59ddd0 against base a6d5c2f0d. The command was
`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Raw final message:
`trusted-publishing-round-1-codex.raw.txt`.

**Verdict: REQUEST CHANGES.** One BLOCKING, one MAJOR, and one MINOR finding. All three are fixed
in the next commit.

**Codex verified:**

- The upstream NuGetGallery validator matches the repository and the workflow file name, does not
  check the ref, and rejects a token that lacks a required environment claim.
- The `NuGet/login` pin is v1.2.0. The action masks the key, exposes the output
  `NUGET_API_KEY`, and fails when the exchange fails.
- The workflow changes are confined to credential handling.
- The earlier mutation cases are kept, and retiring S006 is appropriate.
- No direct credential bypass exists under the stated policy and settings.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | An environment list that omitted `nuget-release` (for example `total_count: 0`) skipped that environment's secret check, so a fixture holding `NUGET_API_KEY` there read CLOSED. Unnamed secret entries became the string `"None"`. This is a checker fail-open on inconsistent API input, not a live bypass. | **Fixed.** `nuget-release`'s secrets are always read unless the environment is known not to exist. An inventory that omits an existing `nuget-release` is `U004`, so the result is UNKNOWN, never CLOSED. An entry without a non-empty string name makes the list unreadable. Tests: `test_inventory_that_omits_the_release_environment_is_never_closed` and `test_unnamed_entries_are_unreadable`. |
| 2 | MAJOR | The attestation accepted repeated JSON members, where the later value wins (`"write"` then `"read"`), and impossible dates such as `2026-99-99`. | **Fixed.** An `object_pairs_hook` refuses repeated members, and `datetime.date.fromisoformat` validates the date. Both cases are `U006`. Tests: three duplicate-member cases and three bad dates in `test_invalid_attestation_is_unknown`. |
| 3 | MINOR | The README said any policy change fails closed. Clearing the policy's environment field broadens trust instead, because NuGetGallery skips the environment check when the policy names none. | **Fixed** in the README's "Behavior if a setting regresses". A deletion or a non-matching change fails closed. Clearing the environment field would let every older tag's `publish-nuget.yml` match by file name, and the script cannot see nuget.org. |
