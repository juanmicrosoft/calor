# PR B (#1546, #1547), Codex round 1

Reviewing 58a9a04fa, PR B's own diff. A first attempt ended with "model at capacity" and returned
no verdict, so it does not count as a round. Raw final message: `pr-b-round-1-codex.raw.txt`.

**Verdict: REQUEST-CHANGES** (1 MAJOR, 1 MINOR).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | The gate matched the version literally, but publish-nuget's `awk` matched it as a regex. For `0.25.0-rc.1`, a following `## [0.25.0-rc-1]` section is included by `awk` and not by the gate. G014 could pass and G011 then fail, with no hash able to satisfy both. | Fixed. There is now one renderer: `scripts/render_release_notes.py` calls the gate's `changelog_section` (a literal match). publish-nuget uses it; `awk` is gone. A duplicated version header fails (`G014`, and the renderer exits 1). Tests: `RenderedNotesAreTheSectionTheGateBinds` (2: a following section, and CRLF with trailing whitespace and a regex decoy), `CandidateChangelogWithADuplicateVersionHeaderFails`, `ReleaseNotesAreRenderedByTheGatesOwnRenderer`. |
| 2 | MINOR | Older refs, for example `v0.22.0`'s `benchmark.yml`, keep their own publication logic and the write-capable `GITHUB_TOKEN`. | Documented in the README as a residual, scoped to workflows on `main`, with the two repository settings that would close it (maintainer-owned). |
