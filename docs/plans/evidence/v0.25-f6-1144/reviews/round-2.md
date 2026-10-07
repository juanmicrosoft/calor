# Codex adversarial review, round 2 (#1144 F6)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), cross-family adversarial review. Not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `12ead9eb`, with the round-1 record.
- **Verdict:** CHANGES-REQUESTED (5 MAJOR, 2 MINOR).

## Findings (verbatim)

Round-1 dispositions: **fixed** #1, #3, #4 (summary mode), #5 (listed early returns), #6 (success/outcome reset), #7, #8 (collision safely refused), #11, #13. **Incomplete** #2, #9, #10, #12.

The reordered filters preserve exception eligibility. `WithSuccess` correctly recomputes outcomes. Contexts are created per conversion; no new race between independent conversions was found.

- **MAJOR — Escalations still receive false passthrough attribution.** `RoslynSyntaxVisitor.cs:6776` excludes only `PreserveMember: true` escalations before evaluating the gate. Reproduced with `public class P { public int M(int x) { x >>>= 1; return x; } }`: lossy standard conversion preserves it without passthrough, but enabling passthrough reports `passthrough/unsupported-construct`. Round-1 #2’s claim that escalations never mark passthrough is false.

- **MAJOR — Merely reading the preservation getter contaminates later losses.** `ConversionContext.cs:376` arms `_passthroughGateOpened` without requiring preservation. `CSharpToCalorConverter.cs:614` does this even for successful native lossy/passthrough conversions. Reusing their public context for an unconditional record preservation labels it passthrough. Switching to interop/lossless also leaves the flag armed. Provenance needs to accompany the preservation operation.

- **MAJOR — Ordinary CLI write failures still discard loss details.** `ConvertCommand.cs:605` serializes losses only after writing. An `IOException` reaches the outer `MarkRefused`, which fixes success/outcome but leaves `losses` and existing loss counters absent despite populated rescue counts. Serialize the conversion summary before writing.

- **MAJOR — Batch abort mode discards every conversion path.** `BatchTool.cs:258` returns a plain error for `skipOnError: false` after conversion completes. Neither normal nor summary output retains provenance, including rescues performed before project refusal.

- **MAJOR — Markdown attribution remains incomplete.** `MigrationReportGenerator.cs:155` renders only triggered losses of **partial** files. Converter interop losses are excluded; project refusal moves rescued files into the failed section, which renders no losses. The aggregate line cannot identify which file/member took each path. Round-1 #9 is only partly fixed.

- **MINOR — Tests miss the failure cases above and outcome restoration.** `ConversionPathReportingTests.cs:169` tests only a member-preserving escalation, with a potentially vacuous `Assert.All`. Line 505 checks only aggregate Markdown text. There are no stale-getter, ordinary write-failure, batch-abort, alias-collision, or `WithSuccess(false).WithSuccess(true)` tests. Round-1 #10 remains partial; unchanged legacy counters and cross-surface null omission are also unverified.

- **MINOR — Public documentation still omits/misstates options.** `CHANGELOG.md:33` groups `calor_migrate` with tools accepting `passthroughOnError`, although it has no such option. `convert.mdx:99` omits the implemented `enabledBy: "interop-mode"` value. The corrected quoted CLI output matches the code.

The escalation finding was reproduced through the existing MCP binary. The full test suite was not run in this read-only review.

CHANGES-REQUESTED

## Dispositions

| Finding | Disposition |
|---|---|
| Escalations receive passthrough attribution | Fixed. The statement-boundary catch evaluates escalations with the attribution-free `PreservesCSharp`; only gate-caused crash containment (ConvertExpression) attributes passthrough. Test: `x >>>= 1` under lossy with and without passthrough stays `interop`. |
| Reading the getter contaminates later losses | Fixed. The converter's rescue gate reads `PreservesCSharp`; every remaining `ShouldPreserveCSharp` read leads to a preservation, and each read resets the flag (also on lossless/interop). The getter's remarks state the contract. |
| CLI write failure discards loss details | Fixed. `SetConversionSummary` runs before the write. Test `F6_REPORT_10_CliWriteFailure_KeepsLossesAndReportsRefused`. |
| Batch abort discards paths | Fixed. The abort error text appends the preservation-paths line. Test `F6_REPORT_06_McpBatchAbort_KeepsPreservationPaths`. |
| Markdown attribution incomplete | Fixed. Partial and failed files list every interop/rescue/passthrough loss with path, trigger and enabledBy. Test `F6_REPORT_04_CliMigrateMarkdown_NamesEachFilesPaths_EvenWhenRefused`. |
| Tests miss failure cases | Fixed in part: escalation, write failure, batch abort, refused Markdown, `WithSuccess` round trip added; vacuous `Assert.All` guarded. Not added: alias-collision (needs a parse-failing global member under lossy+passthrough; no natural input found) and a stale-getter test (no public reuse path after the fix). |
| Docs omit/misstate options | Fixed. CHANGELOG separates `calor_batch` from the tools without a passthrough option; convert.mdx lists `interop-mode` and the lossy attribution rule. |
