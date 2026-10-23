# Trusted Publishing PR (#1545): Codex round 3 (last full round)

Reviewed commit 24538cca6, which holds the round-2 fixes, against base a6d5c2f0d. The command was
`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Raw final message:
`trusted-publishing-round-3-codex.raw.txt`.

**Verdict: REQUEST CHANGES.** Two BLOCKING, one MAJOR, and one MINOR finding. All four are fixed
in the next commit. Findings 1–3 are about how the checker handles inconsistent or malformed API
input. Codex found no direct credential bypass under the stated policy and settings. The
verification pass checks these fixes.

**Codex verified:**

- The round-1 and round-2 fixes are present.
- An ordinary unattested 403 is `V001`, and an API answer overrides any attestation.
- Boolean/string confusion in the protection fields and the workflow-permission fields fails
  closed.
- The workflow changes are confined to credential handling, and the earlier mutation coverage is
  kept.
- The upstream validator and the `NuGet/login` pin match the documented behavior. A key that
  expires before the push fails closed.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | An inventory with `total_count: 2` and `nuget-release` listed twice passed, and `set()` collapsed the two entries. An omitted `staging` environment that held `NUGET_API_KEY` was never read. `True` and `2.0` were also accepted as counts. | **Fixed.** `names()` refuses a name listed twice, comparing without case as GitHub does. `count_ok()` requires `type(total_count) is int` and a value of 0 or more, in `listed()` and on every page. Test: `test_paginated_inventory_must_be_stable_and_unique`, covering saved and mocked responses, duplicates, case-folded duplicates, and the counts `True`, `2.0`, `-1`, `"2"`, and `None`. |
| 2 | BLOCKING | Pagination kept only the last page's `total_count`. Page 1 with 100 safe entries and `total_count: 101`, then an empty page with `total_count: 100`, passed, so an entry that shifted onto an already-read page went unchecked. | **Fixed.** The first page fixes `total_count`. Any change on a later page is `Unreadable`, and more than 100 pages is `Unreadable`. Same test. |
| 3 | MAJOR | The 403 test searched stdout and stderr for the text `HTTP 403`. A 502 whose message mentioned 403 became `Forbidden`, and the attestation then stood in (`A001`). | **Fixed.** `gh api` now runs with `--include`, and the status comes only from the HTTP status line (`split_response`). The status decides the result: 404 is NotFound and 403 is Forbidden. Any other status, or any non-zero exit, is Unreadable. Output with no status line is Unreadable too. A saved `.error` fixture counts as Forbidden only when it starts with `HTTP 403`. Tests: `test_live_status_comes_from_the_status_line_only` (6 cases), plus three non-403 texts mentioning 403, which stay `U005` even with the attestation. The live run on 2026-10-19 still parses every listing and reads the real 403 as `V001`. |
| 4 | MINOR | The README confused caller and callee for reusable workflows. | **Fixed.** `job_workflow_ref` names the called workflow, and `repository` names the caller. Both directions are refused, for different reasons, and both are now spelled out. |
