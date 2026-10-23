# Codex adversarial review, round 1 (#1144 F6)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), cross-family adversarial review. Not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `bc3c7021`.
- **Verdict:** CHANGES-REQUESTED (10 MAJOR, 3 MINOR).

## Findings (verbatim)

1. **MAJOR — Rescue precedence ignores a required passthrough gate.** `CSharpToCalorConverter.cs:1343` reports `rescue/rescueUnusableMembers` whenever that option is true. With `Lossy + Standard + SelectActiveBranchLossy`, however, `ShouldPreserveCSharp` at line 611 requires passthrough. Converting `TrailingLabel` with both options enabled therefore claims “no passthrough request needed”; removing passthrough prevents the preservation. Precedence must account for both gates.

2. **MAJOR — Passthrough-only preservations still report `interop`, without trigger/enabledBy.** `RoslynSyntaxVisitor.cs:6937` preserves checked blocks through `ShouldPreserveCSharp` but uses the old `RecordLoss` overload. Under lossy standard mode, passthrough alone opens that gate. Member containment has the same problem: lines 3275 and 3122 preserve unsupported members through `CreateInteropBlock`, whose loss at line 13772 always defaults to `interop`. A destructor supplies a concrete unsupported-member fixture. D5 records this limitation but does not satisfy the reporting requirement.

3. **MAJOR — MCP roundtrip mode drops provenance entirely.** `ConvertTool.cs:879` constructs `RoundTripCheckOutput` without losses or a path summary; the converter result remains local to the conversion block. Parse rescues and converter interop disappear from this surface. Unlike convert/validate modes, its options also omit the supplied `passthroughOnError`.

4. **MAJOR — Batch summary mode discards the new fields.** `BatchTool.cs:319` returns neither per-file `conversionPaths` nor aggregate provenance. Even `failedFiles` omits it. A successful rescued batch with `summary: true` exposes no rescue attribution.

5. **MAJOR — CLI refusal omits per-loss provenance.** `ConvertCommand.cs:538` returns before `SetConversionSummary` at line 601. Refused conversions containing recorded preservations therefore have summary counts but no `data.losses[]` path/trigger/enabledBy. Validation and write-timeout returns also bypass that serialization.

6. **MAJOR — CLI write failures leave a successful path outcome.** `ConvertCommand.cs:585` handles cancellation only. An ordinary output-write exception reaches the outer catch at line 287, which does not replace `conversionPaths.outcome` or reset `data.success`. For example, targeting an existing directory exits 1 while retaining `native`/`preserved` and `success: true`.

7. **MAJOR — Successful MCP auto-fix retains `refused`.** `MigrateTool.cs:574` copies `existing.ConversionPaths` after a successful recompile. The preceding compile-failure branch already overwrote its outcome with `refused` at line 480. Consequently, `status: "fixed"` retains a refused outcome instead of restoring the outcome derived from its losses.

8. **MAJOR — The module-name fix can merge sources from different namespaces.** `CSharpToCalorConverter.cs:1611` aliases global `G` to `Custom.G` when `ModuleName="Custom"`. A real `namespace Custom { partial class G … }` then shares its source-map key. `TryTakeSource` at line 1568 concatenates both declarations when both are partial, although they represent different original types. This is reachable with lossy fidelity plus passthrough and a parse-failing global member; whole-file namespace protection at line 489 applies only to lossless fidelity. Preserve original source identity when matching.

9. **MAJOR — Markdown migration reports omit preservation paths.** `MigrationReportGenerator.cs:146` renders issue messages, but never renders `Losses`, `ConversionPaths`, or `Describe`. `calor migrate --report report.md` therefore lacks the promised path/trigger attribution despite the console addition.

10. **MAJOR — Several registered test claims remain nondiscriminating or untested.** `ConversionPathReportingTests.cs:344`:
    - **F6-REPORT-04/-05/-06:** failure projects contain duplicate global `Probe` declarations. Their refusal assertions do not isolate iterator round-trip failure; project collisions provide another failure cause.
    - **F6-REPORT-10:** tests malformed C# only, not the registered `--lossy` and mode-dependent outcomes.
    - **F6-REPORT-12:** lossless option combinations miss the passthrough-required precedence case above.
    - **F6-REPORT-13:** field-presence checks do not verify unchanged existing fields/counts or cross-surface null handling.
    - No F6 tests exercise roundtrip/validate modes, batch summary, Markdown/JSON migration reports, or full compile/fix outcome transitions.

11. **MINOR — New optional fields serialize inconsistently.** `MigrationReportGenerator.cs:271` includes `trigger: null` and `enabledBy: null`; MCP and CLI convert omit them. Put omission attributes on the shared `ConversionLoss` properties to standardize the additions without changing existing serialization.

12. **MINOR — Documentation overclaims supported behavior.** `mcp.mdx:79` discusses `calor_migrate` “even with passthroughOnError”, but that tool rejects additional schema properties and hardcodes passthrough false. `convert.mdx:128` quotes `Counter.cs`; the code prints the absolute `loss.File`. Its example test checks a substring and misses that discrepancy.

13. **MINOR — Library missing-file reports show incorrect options.** `CSharpToCalorConverter.cs:764` creates a default context instead of `CreateContext`. `ConvertFileAsync` on a missing file reports both options false even when the converter was configured with them true.

Static review only; build/tests were not run in the read-only checkout.

CHANGES-REQUESTED

## Dispositions

| # | Disposition |
|---|---|
| 1 | Fixed. `RescueProvenance` requires the preservation gate to be open without passthrough (lossless or interop mode) before it reports `rescue`. Test `F6_REPORT_12_Precedence_SelectedBranchLossy_BothOptions_IsPassthrough`. |
| 2 | Fixed generally. `ConversionContext.ShouldPreserveCSharp` records when only `PassthroughOnError` opened the gate; the next interop loss is labelled `passthrough/unsupported-construct/passthroughOnError`. Escalation is tested before the gate in every catch filter, so escalations never mark passthrough. Test `F6_REPORT_12_LossyStandard_PreservationOnlyThroughPassthrough_IsPassthrough` (destructor; `using` declaration as negative control). |
| 3 | Fixed. Roundtrip mode returns `lossSummary` and honours `passthroughOnError` (CHANGELOG entry). Test `F6_REPORT_13_McpValidateAndRoundtripModes_CarryPaths`. |
| 4 | Fixed. Summary mode adds `failedFiles[].conversionPaths` and an aggregate `preservationPaths` line. Test `F6_REPORT_06_McpBatchSummaryMode_ReportsPaths`. |
| 5 | Fixed. Refusal, validation-failure and timeout paths call `SetConversionSummary`, so `data.losses[]` carries provenance. |
| 6 | Fixed. The outer catch calls `MarkRefused` (success false, outcome refused). |
| 7 | Fixed. `ConversionPathSummary.WithSuccess` recomputes the outcome from counts; `fixed` restores it. |
| 8 | Fixed. A key holding both an aliased global declaration and a real-namespace declaration is ambiguous and not taken. |
| 9 | Fixed. Markdown report prints the preservation-paths line and per-loss path/trigger for partial files. Test `F6_REPORT_04_CliMigrateReports_MarkdownAndJsonCarryPaths`. |
| 10 | Fixed in part. Project fixtures now suffix colliding type names, `--lossy` CLI cells and roundtrip/validate/batch-summary/report tests added. Not added: a compile/fix-phase transition test (needs an auto-fixable compile failure; logic covered by `WithSuccess`). |
| 11 | Fixed. `JsonIgnore(WhenWritingNull)` on `ConversionLoss.Trigger`/`EnabledBy`. |
| 12 | Fixed. mcp.mdx no longer claims a `calor_migrate` passthrough option; convert.mdx shows the full path the CLI prints, and the test asserts the exact line. |
| 13 | Fixed. Missing-file results use `CreateContext`. Test `F6_REPORT_07_LibraryMissingFile_ReportsTheConfiguredOptions`. |
