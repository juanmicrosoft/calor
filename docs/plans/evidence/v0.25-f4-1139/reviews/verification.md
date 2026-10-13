# Codex verification-only pass (#1139)

Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral`, reasoning effort high), cross-family.
Input: the branch diff against origin/main at 56cce329 (generated evidence excluded).

## Findings (verbatim)

1. **RESOLVED — partial partner labels.** [PartialMemberKey](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-afea6f0734d2070d0/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:5861) uses identifier `ValueText` and indexer parameter count. The new regression covers `@C`/`C`, `@P`/`P`, and `int`/`System.Int32`. A read-only probe using existing binaries reported four `iterator-accessor` preservations; compilation succeeded with auto-fix disabled.

2. **RESOLVED — partial events captured by the property check.** The class and interface guards now explicitly accept only properties and indexers; partner discovery excludes events too. Partial events no longer enter this property-specific check.

3. **NOT RESOLVED — cross-file migration coverage is incomplete.** The [new test](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-afea6f0734d2070d0/tests/Calor.Compiler.Tests/Migration/IteratorAccessorConversionTests.cs:343) creates two files, compiles their generated output together, and compares behavior. However, both rows retain default `Fidelity = Lossless`. [ProjectMigrator](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-afea6f0734d2070d0/src/Calor.Compiler/Migration/Project/ProjectMigrator.cs:255) invokes `PartialClassMerger` only with `Fidelity = Lossy`, so the requested explicit lossy merge path remains uncovered. This remains the existing nit.

**New BLOCKING findings: none.** Source inspection found no new illegal Calor, rescue dependence, semantic regression, or dishonest evidence claim. The recorded `srcTree` matches the measured commit and current source tree. Filesystem-writing tests were not rerun in this read-only pass.

APPROVE
