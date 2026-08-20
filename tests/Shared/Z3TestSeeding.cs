using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Calor.Compiler.Verification.Z3;

namespace Calor.Tests.Shared;

/// <summary>
/// Seeds Z3 (via <see cref="Z3ContextFactory.DefaultContextSettings"/>) with a
/// fixed <c>random_seed</c> so every <c>Microsoft.Z3.Context</c> created by
/// Calor tests uses the same solver seed.
///
/// Z3 is <em>supposed</em> to be deterministic on the same query, but its
/// heuristics can pick different tactics under memory pressure. Historically
/// this made #897 present as "6 MCP tests flaky" and cost roughly two weeks of
/// misdiagnosis before the memory-admission scope was identified as the actual
/// cause. Seeding Z3 in tests doesn't stop that class of bug, but it removes
/// one non-determinism axis so triage can focus on the real cause first.
///
/// The seed value <c>42</c> is arbitrary; the important property is that it is
/// <em>fixed</em>. Do not change it without a reason — a change here reshapes
/// the search order for every Z3-backed test in the suite.
///
/// Origin: 2026-08-18 test-suite audit, finding F7 / recommendation R10
/// (`docs/plans/2026-08-18-test-suite-audit.md`, issue #1006).
///
/// This file is linked into every test assembly that touches Z3
/// (Calor.Compiler.Tests, Calor.Verification.Tests, Calor.Tasks.Tests,
/// Calor.RoundTrip.Harness.Tests) via <c>&lt;Compile Include&gt;</c> in each
/// project's csproj. A single canonical copy lives at
/// <c>tests/Shared/Z3TestSeeding.cs</c>.
/// </summary>
internal static class Z3TestSeeding
{
    /// <summary>
    /// Fixed Z3 <c>random_seed</c> used by all Calor test assemblies. See the
    /// class-level XML doc for rationale (issue #1006).
    /// </summary>
    public const string RandomSeed = "42";

    [ModuleInitializer]
    public static void Initialize()
    {
        // Only assign if nothing else already set it — a test that needs a
        // different seed can override via `Z3ContextFactory.DefaultContextSettings`
        // in its own initializer without being clobbered by ours.
        if (Z3ContextFactory.DefaultContextSettings != null)
            return;

        Z3ContextFactory.DefaultContextSettings = new Dictionary<string, string>
        {
            ["random_seed"] = RandomSeed,
        };
    }
}
