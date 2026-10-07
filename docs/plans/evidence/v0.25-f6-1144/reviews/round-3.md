# Codex adversarial review, round 3 (#1144 F6)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), cross-family adversarial review. Not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `bdef0c37`, with the round-1 and round-2 records.
- **Verdict:** CHANGES-REQUESTED (3 MAJOR, 3 MINOR). This is the last review round allowed by the accepted capacity; a verification-only pass follows.

## Findings (verbatim)

Round-1 dispositions: **#1, #3–9, #11–13 verified; #2 incomplete; #10 remains partial.** Round-2 dispositions: **CLI write-loss serialization, Markdown attribution, and the specific documentation corrections verified; escalation, getter-state, and batch-abort fixes remain incomplete; testing remains partial.**

- **MAJOR — Escalations still receive false passthrough attribution.** `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:2336` still evaluates `ShouldPreserveCSharp` for interface-member escalations. Reproduced with `public interface I { void M(int x = 8 >>> 1); }`: lossy conversion succeeds with `interop`; adding passthrough succeeds with `passthrough/unsupported-construct`. Preservation occurs either way.

- **MAJOR — Reading the public getter still contaminates unrelated losses.** `src/Calor.Compiler/Migration/ConversionContext.cs:378` arms state consumed at `:449`. Reproduced through a returned public context: read `ShouldPreserveCSharp` under lossy/passthrough, change fidelity to lossless, then record an unconditional preservation—it reports passthrough. An intervening dropped loss also leaves the flag armed. Getter remarks do not eliminate this API behavior.

- **MAJOR — Batch abort still discards structured provenance.** `src/Calor.Compiler/Mcp/Tools/BatchTool.cs:261` returns only error text plus aggregate preservation counts. Per-file outcomes, options, `enabledBy`, and file attribution disappear; lossy/dropped paths disappear entirely. Round-2’s fix preserves a sentence, not the documented reporting schema.

- **MINOR — Outer catch can report refusal after output was committed.** `src/Calor.Compiler/Commands/ConvertCommand.cs:290` unconditionally calls `MarkRefused`. Exceptions after the write—including the benchmark’s second input read at `:677`—therefore report `refused` despite an existing converted output. Track whether the write completed.

- **MINOR — Documentation overstates automatic parse rescue.** `website/content/cli/mcp.mdx:78` says parse failures are rescued “on every surface.” The preservation gate still applies at `src/Calor.Compiler/Migration/CSharpToCalorConverter.cs:614`. Reproduced with F5-ARRAY-02 under lossy standard mode without passthrough: refused, zero rescues. Qualify the claim with fidelity/mode.

- **MINOR — Tests still miss discriminating cases.** `tests/Calor.Compiler.Tests/ConversionReporting/ConversionPathReportingTests.cs:177` exercises statement escalation, missing the interface catch above; `:546` accepts the incomplete abort text. No stale-getter or alias-collision test exists. The alias case is readily constructed from F5-ARRAY-02 by making global `G` partial and adding `namespace Custom { partial class G … }`. Full compile/fix restoration and cross-surface legacy-counter/null-omission assertions remain absent.

The reordered filters retain exception eligibility. Converter rescue reads are now attribution-free. The alias guard safely refuses collisions; `WithSuccess` restores native, preserved, and lossy outcomes correctly. No new race between independent conversion contexts was found. Quoted CLI output matches.

Targeted reproductions used the existing binary; the full suite was not run.

CHANGES-REQUESTED

## Dispositions

| Finding | Disposition |
|---|---|
| Interface-member escalation attributes passthrough | Fixed. The interface-member catch evaluates escalations with `PreservesCSharp`. Test `F6_REPORT_12_InterfaceMemberEscalation_UnderLossyPassthrough_StaysInterop` (the reviewer's reproduction). |
| Reading the getter contaminates unrelated losses | Fixed for the reproduced cases. Any recorded loss consumes the flag, and attribution re-checks the live options (lossy, standard mode, passthrough on). Test `F6_REPORT_13_StaleGateRead_DoesNotLabelALaterLoss` (fidelity change; intervening drop). Residual, recorded: a caller that reads the public getter under lossy+passthrough and then records an unrelated interop loss with nothing in between would see it attributed to passthrough; the converter itself reads the getter only where a preservation follows. |
| Batch abort discards structured provenance | Fixed. The abort result is JSON with `error`, `preservationPaths` and `files[].{sourcePath,status,conversionPaths}`; `isError` stays true. CHANGELOG entry records the shape change. Test `F6_REPORT_06_McpBatchAbort_KeepsPreservationPaths`. |
| Outer catch reports refusal after output was written | Fixed. `OutputWritten` is set after the write; `MarkRefused` keeps the outcome when it is set. |
| Docs overstate automatic parse rescue | Fixed. mcp.mdx qualifies it with "in the default lossless mode". |
| Tests miss discriminating cases | Fixed in part: interface escalation, stale gate read, alias collision (with a positive control) and the structured abort added. Not added: compile/fix restoration through `calor_migrate` (needs an auto-fixable compile failure; `WithSuccess` is unit-tested) and cross-surface legacy-counter assertions (existing counter tests in the suite pass unchanged). |
