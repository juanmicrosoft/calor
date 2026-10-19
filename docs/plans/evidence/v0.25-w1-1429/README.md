# 0.25 W1 (#1429): interoperability guides and support limits

**Status:** every family IN_PROGRESS; no READY record. **Consumes:** R0 scope 1.3.0, T1 register
1.0.0. **Independence:** reduced (R0 §9). Codex reviewed this work; no reviewer independent of the
maintainer did. **Base:** `origin/main` at `0b9568b3f` (first drafted on `aea06ff8f`; the rebase
changed no `src/`, `website/` or `CHANGELOG.md` file, and the transcripts were regenerated after it).

| Path | Content |
|---|---|
| `records.json` | Per-family W1 record: status (mirrors `scope.json`), pages and anchors, examples, transcripts, corrected claims, and what READY still needs |
| `fixtures/` | 9 C# inputs (`.cs.txt`, kept out of the Calor-first guard) behind the guide's examples and claims |
| `transcripts/` | Per fixture: `calor convert` text and `--format json`, the default compile of the CLI output, `calor migrate`, and MCP `calor_convert` with and without `passthroughOnError`; `W1-F6-project` adds CLI and MCP project migration and `calor_batch` |
| `reproduce/wording_scan.py` | R2's G012 scan (`scripts/verify_release_adjudication.py`) over the `[Unreleased]` changelog and the W1 pages, plus the review-independence wording R0 §9 forbids |
| `reviews/` | Codex review records for this PR |

**Enforced by** `tests/Calor.Compiler.Tests/InteropScope/W1GuideTranscriptTests.cs`:

- `Transcript_MatchesTheCommittedFixture` and `ProjectTranscript_MatchesTheCommittedFixture` rerun every
  surface and compare with `transcripts/`. A changed transcript fails until it is regenerated
  (`CALOR_REGENERATE_W1_TRANSCRIPTS=1`); a change is a review decision, and the pages quoting it change
  with it. CLI children run with a temporary HOME; in-process MCP calls fail closed when
  `~/.calor/manifests` holds a manifest.
- `WebsiteSources_AreExcerptsOfTheFixtures`: a `csharp` fence preceded by `{/* w1-source: ID */}` must occur
  verbatim, as one contiguous substring of the fixture (only CRLF to LF and end-newline trimming; no
  elision of any kind). The F1, F2, F3 and F5 inputs are required. Negative controls: a deleted
  statement, a commented-out line, a changed line, an added line, and an indented or exact `// ...`
  elision line, each applied to the guide text, must all fail.
- `WebsiteFences_QuoteTheCliOutput`: a website fence preceded by `{/* w1-transcript: ID */}` must equal
  that fixture's CLI output (`w1-transcript-lines`: every line of the fence is a line of it). The guide
  marks four complete programs (F1, F3, F4, F5), which `self-check docs` also compiles, and one F2
  fragment; all five markers are required.
- `FamilyRecords_AreConsistentWithTheScopeAndThePages`: six families in order, status equal to the scope
  readiness and never READY, every page and anchor present (the site's heading-id rule), every
  transcript present, a non-empty READY list.
- `WebsiteExampleCheckerTests` coverage pin: pages 96 -> 97, complete programs 58 -> 62, illustrative 33 -> 34.

## Pages

| Page | Change |
|---|---|
| `guides/csharp-interoperability.mdx` (new) | Task-first guide: by-reference calls, interpolation targets, local functions, arrays, iterator accessors, what stays C#, the three preservation paths and per-surface defaults, how to read counts |
| `guides/adding-calor-to-existing-projects.mdx` | How to read `conversionPaths`; a 0.25 feature table linking the guide |
| `guides/index.mdx` | Lists the guide |
| `cli/convert.mdx` | `--lossy`; counts, partial results and the JSON envelope; refused exit code; preservation granularity |
| `cli/mcp.mdx` | Defaults table, `issues[]`/counts, partial failures per tool |
| `cli/migrate.mdx` | `--lossy`; a two-file refusal example |
| `cli/feature-check.mdx` | Envelope output; the 0.25 interop registry table; four known registry gaps |
| `cli/structured-output.mdx` | Where each conversion surface reports issues and per-file results |
| `cli/self-check.mdx` | How converted examples are checked |
| `syntax-reference/calls.mdx` | `out var` statement-call limitation; bare-binding pitfall |
| `syntax-reference/string-operations.mdx` | Handler statements are kept as `§RAW`; `--no-fallback` refuses |
| `docs/cli/feature-check.md` | Removes the unqualified Full `string-interpolation` row and the stale `yield-return` claim; current example output |
| `CHANGELOG.md` `[Unreleased]` | W1 entry; corrects the #943 `out var` statement claim |

## Claims checked against behavior

Defaults: the CLI JSON and MCP `paths` of every transcript report `rescueUnusableMembers` true only for
`calor convert`, and `passthroughOnError` true only for MCP with the option, as T1 recorded.
Native/interop counts: `W1-F6-rescue` reports `nativeConversionCount` 3 with outcome `preserved`, so the
pages say to read `conversionPaths.outcome`. Partial failures: `W1-F6-project` (CLI migrate exit 1, no
`.calr` written; MCP migrate and batch `isError`, `failedFiles` 2, every file `refused`). Feature-check
output for every row of the feature-check table was read from `calor feature-check --list` on the base.
After Codex round 1: `--format json --verbose` printed progress to stdout before the envelope
(pinned in `W1-F6-rescue` `cli-convert-json-verbose`; fixed by #1570, which regenerated the record
to `stdoutIsOneJsonDocument: true`); MCP diagnostics live in `issues[]`
(`calor_convert`), `perFile[].errors[]`/`warnings[]` (`calor_migrate`) and `files[].issues[]`
(`calor_batch`), pinned in `W1-F6-project`; the local-function slice includes the ref-return,
parameter-attribute and value-use restrictions of `IsNativeLocalFunction`; the native hole subset
follows `IsNativeHoleOperand`; omitted-`in` and code-running by-reference operands keep only the
statement (`§RAW`, feature `in-parameter`).
After Codex round 2: `[NotNullWhen]` on an `out` parameter converts natively with the attribute inert
(`W1-F1-notnullwhen`); omitted `in` in an expression-bodied member preserves the member; verbatim
strings and non-plain doubles are not native holes; `post-validation-fallback` is not a registry
entry; diagnostic codes are pinned with counts; the fragment check is ordered and keeps indentation.

Found while checking (documented, not fixed: W1 changes no `src/`). **Update (F1 PR 2, #1571):**
findings 1, 2 and 4 are fixed in the converter (an out var call statement converts natively with a
`default`-initialized binding; an expression-bodied ref return is preserved; the five registry
descriptions say what converts), and the W1 pages, `records.json` and the F1 transcripts were
updated with `CALOR_REGENERATE_W1_TRANSCRIPTS=1`. Finding 3 (`--format json --verbose`) was fixed by #1570.

1. **`out var` in a call statement** (`int.TryParse(s, out var n);`): the RO-DECL-1 lowering writes
   `§B{~n:i32}` with no initializer, and the parser reads the next statement's call as its initializer
   (CS0029). CLI rescues the type; MCP and migrate refuse (`W1-F1-outvar-statement`). F1's
   FeatureSupport `out-var` text, `calls.mdx` and the changelog claimed it was native.
2. **Expression-bodied ref return** (`=> ref a[0]`): not preserved; emitted as `-> ref int` with the
   `ref` stripped, failing the round trip (`W1-F1-ref-return-expression`). Block bodies are preserved.
3. **`--format json --verbose`** was not a single JSON document on stdout (the converter's verbose
   progress lines). Fixed after W1 by #1570: the progress lines go to stderr, `W1-F6-rescue`
   `cli-convert-json-verbose` now records `stdoutIsOneJsonDocument: true`, and the known-gap notes
   on `cli/convert` and `cli/structured-output` were removed.
4. **FeatureSupport text** that does not match behavior: `ref-expression` (Full, "stripped"),
   `multidim-array` (Full, "fully supported"), `out-var` (lists the expression statement), `out-parameter`
   (says `[NotNullWhen]` parameters are preserved), `in-parameter` (Full with preserved call sites). Fixing them is a `src/` change; the feature-check page lists the gaps.

## What READY needs (per family: `records.json` `readyNeeds`)

Every family: the frozen 0.25 candidate in the 0.24 C1 form after W1 and the release-gap fixes, with
the scope 1.3.0 `acceptancePreconditions` re-proof; per-case records naming the candidate and current
`requirementSha256` (including the DOC-F rows: DOC-F1-01, DOC-F2-01, DOC-F3-01/02, DOC-F4-01,
DOC-F5-01, DOC-F6-01/02); this PR's tests and `self-check docs` passing on the candidate (transcripts
regenerated there if they differ); the mapped blocker witness runs; and the maintainer's claim approval
as a pull-request review URL, with independence reduced. W1 can neither freeze the candidate nor
approve claims, so it writes no READY record.

## Review exception EXC-W1-EXCERPT

The verification pass left one MAJOR open (W-V1: the excerpt check accepted skipped lines). On
2026-10-18 @juanmicrosoft granted **EXC-W1-EXCERPT**: one fix plus exactly one Codex verification-only
pass, for PR #1566 only. The fix uses contiguous chunks and adds a statement-deletion negative control.
Records: `reviews/exc-w1-excerpt.md` and `reviews/exc-w1-excerpt-verification.md`. The converter defects
listed above stay documented, not fixed; a separate F1 PR will fix them and update these pages.

## Review exception EXC-W1-VERBATIM

The EXC-W1-EXCERPT verification pass left W-V1b open: comment stripping turned a commented-out line into
a chunk boundary, so the edit passed. On 2026-10-18 @juanmicrosoft granted **EXC-W1-VERBATIM**: one fix
plus exactly one Codex verification-only pass. The fix is the verbatim-substring rule above. Records:
`reviews/exc-w1-verbatim.md` and `reviews/exc-w1-verbatim-verification.md`.

The verification pass left W-V1c open: the `// ...` elision splitter also accepted an indented elision,
so replacing a statement with `// ...` passed. The elision allowance was the coordinator's own
suggestion and the guide never used it, so the coordinator removed it (2026-10-18): a shown block must
now equal one contiguous fixture substring, with no elision. This only removes an allowance (the rule
became strictly stricter) and adds two negative controls (indented and exact `// ...`); it was not
re-reviewed by Codex, because EXC-W1-VERBATIM's single pass was already used.
