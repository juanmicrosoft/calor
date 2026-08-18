using Calor.Compiler.Verification.Z3;
using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Validates the differential verification-addressability probe end-to-end on SYNTHETIC fixtures
/// (no corpus): it converts the fixture C# to Calor and runs Calor's mechanical checks, confirming
/// the expected diagnostic is INTRODUCED by the mutation (fires on the mutated conversion, absent on
/// the clean one). This is the mechanical gate that partitions the expressible stratum — a logic bug
/// must never masquerade as expressible.
/// </summary>
[Collection("Sequential")] // converter/compiler + Z3: keep serial to avoid contention
public class TaskGenAddressabilityTests
{
    private readonly VerificationAddressability _probe = new();

    // ---- EffectViolation → Calor0410 (the flagship: a deterministic build-time signal) ----

    private const string EffectClean = """
        namespace S;
        public class Counter
        {
            private int _n;
            public int Next() { return _n + 1; }
        }
        """;

    [Fact]
    public void EffectViolation_IsAddressable_Calor0410_IntroducedByTheMutation()
    {
        // Generate the mutation via the real operator, then probe it.
        var cand = Assert.Single(
            ExpressibleMutationOperators.Enumerate(EffectClean, "Counter.cs"),
            c => c.Operator == MutationOperatorKind.EffectViolation);

        // Property 2 (native supply): the injected using/Directory.Exists/arithmetic must convert
        // WITHOUT interop fallback — otherwise clause (a) would exclude it as non-native.
        var conv = new Calor.Compiler.Migration.CSharpToCalorConverter(
            new Calor.Compiler.Migration.ConversionOptions { GracefulFallback = true, PreserveDocumentationComments = true, AutoGenerateIds = true })
            .Convert(cand.MutatedSource, "Counter.cs");
        Assert.True(conv.Success, "mutated file must convert to Calor");
        Assert.DoesNotContain("CSHARP", conv.CalorSource ?? ""); // no interop escalation → native

        // Property 3 (addressability differential): Calor0410 introduced by the mutation.
        var result = _probe.Probe("Calor0410", cand.MutatedSource, EffectClean, "Counter.cs");
        Assert.True(result.Determinable, $"probe should be determinable; note: {result.Note}");
        Assert.True(result.Addressable,
            $"the injected using-nested fs effect must introduce Calor0410 on the converted arm. " +
            $"mutated={string.Join(",", result.FiredOnMutated)} clean={string.Join(",", result.FiredOnClean)}; note: {result.Note}");
        Assert.Contains("Calor0410", result.FiredOnMutated);
        Assert.DoesNotContain("Calor0410", result.FiredOnClean);
    }

    [Fact]
    public void EffectViolation_NotAddressable_WhenCalor0410AlreadyFiresOnTheCleanConversion()
    {
        // A clean file that ALREADY has a lock-wrapped effect the converter's §E-walker skips:
        // enforcement fires Calor0410 on the CLEAN conversion too, so the differential probe must NOT
        // credit the diagnostic to the mutation (converter-baseline noise, not the mutation's effect).
        const string alreadyDirty = """
            using System;
            namespace S;
            public class Counter
            {
                private int _n;
                public int Next() { lock (this) { Console.WriteLine("audit"); } return _n + 1; }
            }
            """;
        var cand = Assert.Single(
            ExpressibleMutationOperators.Enumerate(alreadyDirty, "Counter.cs"),
            c => c.Operator == MutationOperatorKind.EffectViolation);

        var result = _probe.Probe("Calor0410", cand.MutatedSource, alreadyDirty, "Counter.cs");

        Assert.False(result.Addressable,
            $"a pre-existing Calor0410 must not be credited to the mutation; note: {result.Note}");
    }

    // ---- DivByZero → Calor0920 (guard removal, Z3-backed) ----

    [SkippableFact]
    public void DivByZero_GuardRemoval_IsAddressable_Calor0920()
    {
        // The div-by-zero checker's addressability verdict is Z3-backed, so this test needs the
        // native Z3 library. Repo convention (Calor.Compiler.Tests / Calor.Verification.Tests) is
        // to gate on Z3ContextFactory.IsAvailable, and a visible SKIP is honest where an
        // environmental red would not be.
        //
        // WHY IT SKIPS ON CI, precisely — this is FIXABLE BUILD PLUMBING, not a runner limitation.
        // CI does download Z3 successfully. But `src/Calor.Compiler/{z3,runtimes}/` are gitignored,
        // so on a fresh checkout Calor.Compiler.csproj's `<None Include="runtimes\**\*">` glob —
        // resolved at MSBuild EVALUATION time — matches nothing, while the DownloadZ3 target that
        // populates those directories runs later, at EXECUTION time. The managed Microsoft.Z3.dll
        // still flows to test hosts via <Reference Private="true">, so the wrapper loads and then
        // fails at P/Invoke. Seeding `src/Calor.Compiler/scripts/download-z3.sh` before `dotnet
        // restore` in CI would make this test (and ~365 others currently skipped repo-wide) run.
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        const string clean = """
            namespace S;
            public static class M
            {
                public static int Ratio(int a, int d)
                {
                    if (d != 0)
                    {
                        return a / d;
                    }
                    return 0;
                }
            }
            """;
        var cand = Assert.Single(
            ExpressibleMutationOperators.Enumerate(clean, "M.cs"),
            c => c.Operator == MutationOperatorKind.DivByZero);

        var result = _probe.Probe("Calor0920", cand.MutatedSource, clean, "M.cs");

        // Z3 must be available for the div-by-zero checker's precise verdict; if it is, the removed
        // guard makes the divisor provably-zeroable and Calor0920 is introduced.
        Assert.True(result.Determinable, $"probe should be determinable; note: {result.Note}");
        Assert.True(result.Addressable,
            $"removing the zero-guard must introduce Calor0920. " +
            $"mutated={string.Join(",", result.FiredOnMutated)} clean={string.Join(",", result.FiredOnClean)}; note: {result.Note}");
    }

    [Fact]
    public void Z3BackedCheck_WithoutZ3_IsIndeterminable_NotUnaddressable()
    {
        // The inverse of the test above, and the one that matters for measurement honesty: when the
        // native solver is absent, a Z3-backed check must report INDETERMINABLE, never "Calor has no
        // signal for this defect". The latter would be recorded as NotVerificationAddressable and
        // would feed the exclusion accounting a false statement about Calor's capability.
        // Where Z3 IS available this asserts the complementary property — the probe does not bail.
        var result = _probe.Probe("Calor0920", EffectClean, EffectClean, "Counter.cs");

        if (!Z3ContextFactory.IsAvailable)
        {
            Assert.False(result.Determinable);
            Assert.False(result.Addressable);
            Assert.Contains("indeterminable", result.Note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NOT evidence", result.Note);
        }
        else
        {
            // Identical sources: nothing is introduced, but the probe must have actually asked.
            Assert.False(result.Addressable);
            Assert.DoesNotContain("Z3-backed check and the native", result.Note);
        }
    }

    // ---- The partition holds: an unrecognised / non-firing check is NOT addressable ----

    [Fact]
    public void UnknownExpectedCheck_IsNotAddressable_AndNotDeterminable()
    {
        var result = _probe.Probe("Calor9999", EffectClean, EffectClean, "Counter.cs");
        Assert.False(result.Determinable);
        Assert.False(result.Addressable);
    }

    [Fact]
    public void IdenticalSources_NeverAddressable_NothingIsIntroduced()
    {
        // Clean == "mutated": no diagnostic can be introduced by a no-op, for any check family.
        var effect = _probe.Probe("Calor0410", EffectClean, EffectClean, "Counter.cs");
        Assert.False(effect.Addressable);
    }
}
