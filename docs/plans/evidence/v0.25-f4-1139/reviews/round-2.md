# Codex review, round 2 (#1139)

Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral`, reasoning effort high), cross-family.
Input: the branch diff against origin/main at bef405ef (generated evidence excluded).

## Findings (verbatim)

Round-1 fixes verified by source inspection and read-only MCP probes using existing binaries. No remaining BLOCKING iterator-accessor issue found. Cross-file migration was inspected but not executed.

1. **NON-BLOCKING — `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:5867–5870`: equivalent partial declarations receive inconsistent labels.** Legal pairs using `@P`/`P`, `@C`/`C`, or indexer parameter types `System.Int32`/`int` produce `partial-property` for the definition and `iterator-accessor` for the implementation. Both compile without rescue, but contradict the same-file reporting claim. **Fix:** match partial members through Roslyn symbols; use normalized identifiers for the syntax fallback. Add equivalent-spelling regressions.

2. **NON-BLOCKING — `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:3223`: the property guard also captures partial events.** `EventDeclarationSyntax` inherits `BasePropertyDeclarationSyntax`. Consequently, a legal C# 14 partial event implementation is newly reported as `partial-property`, with a property-specific workaround. Its field-like defining declaration remains native. Partial-event compilation failure already existed before this change; this finding concerns the new classification. **Fix:** restrict this feature check to properties and indexers, or handle both event declarations under a separate feature.

3. **NIT — `tests/Calor.Compiler.Tests/Migration/IteratorAccessorConversionTests.cs:374`: cross-file partial migration remains uncovered.** Every migration row writes one `Fixture.cs`; `PartialParts` contains two declarations within that file. This cannot exercise cross-file project validation or `PartialClassMerger`. **Fix:** add a genuine two-file fixture, covering default migration and explicit lossy migration with merging enabled, then compile the complete output and compare behavior.

APPROVE
