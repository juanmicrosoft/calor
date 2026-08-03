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

        var result = _probe.Probe("Calor0410", cand.MutatedSource, EffectClean, "Counter.cs");

        Assert.True(result.Determinable, $"probe should be determinable; note: {result.Note}");
        Assert.True(result.Addressable,
            $"the injected field-write must introduce Calor0410 on the converted arm. " +
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

    [Fact]
    public void DivByZero_GuardRemoval_IsAddressable_Calor0920()
    {
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
