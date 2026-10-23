# Codex verification pass (#1144 F6)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), verification-only pass after round 3. Not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `d15101fd`, with the three round records. A first attempt stopped on a Codex usage limit before any output; it was rerun after the reset and is not counted.
- **Verdict:** VERIFIED-WITH-RESIDUALS. No BLOCKING finding.
- **Suite evidence (not run by the reviewer):** at `d15101fd`, `dotnet test tests/Calor.Compiler.Tests` passed 13,060 of 13,069 (9 skipped, 0 failed; matches `eng/test-manifest.json`), and `dotnet test tests/Calor.Conversion.Tests` passed 476 of 476, on macOS arm64.

## Result (verbatim)

Verification at `d15101fd` was static and read-only; tests were inspected, not executed.

| Round-3 finding | Status | Evidence |
|---|---|---|
| Interface-member escalation falsely attributes passthrough | **VERIFIED** | The catch uses attribution-free `PreservesCSharp` for escalations: `RoslynSyntaxVisitor.cs:2338`. The reproduction tests both flag settings with nonempty interop assertions: `ConversionPathReportingTests.cs:187`. |
| Public getter contaminates unrelated losses | **VERIFIED**, with recorded residual | Attribution rechecks current options, and every recorded loss clears the flag: `ConversionContext.cs:449`. Tests cover fidelity changes and an intervening drop: `ConversionPathReportingTests.cs:200`. An immediate unrelated interop loss still receives passthrough attribution, exactly as recorded. |
| Batch abort discards structured provenance | **VERIFIED** | Abort returns JSON containing `error`, `preservationPaths`, and per-file source, status and conversion summary, with `isError: true`: `BatchTool.cs:259`. The updated test parses JSON and checks retained rescue counts and refusal: `ConversionPathReportingTests.cs:594`. The shape change is documented: `CHANGELOG.md:31`. |
| Outer catch reports refusal after output was written | **VERIFIED** | `OutputWritten` is set after the awaited write: `ConvertCommand.cs:601`. `MarkRefused` resets command success but preserves the committed file’s outcome: `ConvertCommand.cs:860`. |
| Documentation overstates automatic parse rescue | **VERIFIED** | The statement now explicitly says “In the default lossless mode”: `mcp.mdx:78`. This matches the preservation gate: `ConversionContext.cs:385`. |
| Tests miss discriminating cases | **VERIFIED** as the recorded partial fix | Interface, stale-getter and alias-collision tests were added; the alias test includes a positive rescue control: `ConversionPathReportingTests.cs:187`, `ConversionPathReportingTests.cs:216`. Structured abort coverage is present. Full MCP compile/fix restoration and cross-surface legacy-counter coverage remain absent as recorded; outcome restoration has unit coverage at `ConversionPathReportingTests.cs:239`. |

No regression of an earlier round-1 or round-2 fix was found in the post-round-3 delta. Passthrough-only preservation and statement-escalation attribution remain intact (`tests:152`); loss serialization still precedes writing (`ConvertCommand.cs:579`). Rescue precedence, alias protection, MCP mode reporting, migration outcome restoration, Markdown attribution and null omission fixes remain unchanged.

**BLOCKING:** None found. Previously recorded residuals remain; this pass does not substantiate the disposition’s claim that the existing suite passes.

VERIFIED-WITH-RESIDUALS

## Residuals carried to the maintainer

1. A library caller that reads `ConversionContext.ShouldPreserveCSharp` itself under lossy fidelity with passthrough, then records an unrelated interop loss with nothing in between, sees that loss attributed to passthrough. The converter reads the getter only where a preservation follows.
2. No end-to-end `calor_migrate` compile/fix restoration test (needs an auto-fixable compile failure); `WithSuccess` restoration is unit-tested.
3. No cross-surface assertion that legacy counters are unchanged; the existing counter tests pass unchanged.
