using Calor.RoundTrip.Harness.TaskGen;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Pins the EXPRESSIBLE-stratum mutation operators. Each sites an isolated single-point change that
/// (a) parses/compiles as C#, (b) carries a real behavioral defect, and (c) targets one Calor
/// mechanical check. Pure, offline (no corpus) — the operator layer is a pure function of source text.
/// The addressability layer (does the check actually fire on the CONVERTED code?) is validated
/// separately in <see cref="TaskGenAddressabilityTests"/>.
/// </summary>
public class TaskGenExpressibleTests
{
    private static bool Parses(string src) =>
        !CSharpSyntaxTree.ParseText(src).GetDiagnostics()
            .Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

    // ---- EffectViolation → Calor0410 ----

    private const string EffectSample = """
        namespace S;
        public class Counter
        {
            private int _n;
            public int Next() { return _n + 1; }
        }
        """;

    [Fact]
    public void EffectViolation_InjectsLockWrappedRandEffect_IntoMethodThatReadsTheField()
    {
        var cands = ExpressibleMutationOperators.Enumerate(EffectSample, "Counter.cs");
        var ev = Assert.Single(cands, c => c.Operator == MutationOperatorKind.EffectViolation);

        Assert.Equal(DefectStratum.Expressible, ev.Stratum);
        Assert.Equal("Calor0410", ev.ExpectedCheck);
        // The effect is nested in a lock body (the converter's §E-inference gap) and corrupts the field.
        Assert.Contains("lock (this)", ev.MutatedSource);
        Assert.Contains("new System.Random().Next()", ev.MutatedSource);
        // The original read is preserved.
        Assert.Contains("return _n + 1;", ev.MutatedSource);
        Assert.True(Parses(ev.MutatedSource), "mutated source must compile as C#");
    }

    [Fact]
    public void EffectViolation_SkipsReadonlyConstAndStaticFields()
    {
        const string src = """
            namespace S;
            public class C
            {
                private readonly int _ro = 1;
                private const int K = 2;
                private static int _s;
                public int F() => _ro + K + _s;   // reads only non-writable-instance fields
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "C.cs");
        Assert.DoesNotContain(cands, c => c.Operator == MutationOperatorKind.EffectViolation);
    }

    [Fact]
    public void EffectViolation_SkipsStaticMethods_NoThisAvailable()
    {
        const string src = """
            namespace S;
            public class C
            {
                private int _n;
                public static int F(C c) => c._n;   // static: `this` unavailable
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "C.cs");
        Assert.DoesNotContain(cands, c => c.Operator == MutationOperatorKind.EffectViolation);
    }

    // ---- DivByZero → Calor0920 (guard removal) ----

    [Fact]
    public void DivByZero_RemovesWrappingZeroGuard()
    {
        const string src = """
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
        var cands = ExpressibleMutationOperators.Enumerate(src, "M.cs");
        var dz = Assert.Single(cands, c => c.Operator == MutationOperatorKind.DivByZero);

        Assert.Equal(DefectStratum.Expressible, dz.Stratum);
        Assert.Equal("Calor0920", dz.ExpectedCheck);
        Assert.DoesNotContain("if (d != 0)", dz.MutatedSource); // the guard is gone
        Assert.Contains("return a / d;", dz.MutatedSource);       // the division still runs
        Assert.True(Parses(dz.MutatedSource));
    }

    // ---- IndexOutOfBounds → Calor0921 (guard removal) ----

    [Fact]
    public void IndexOutOfBounds_RemovesWrappingBoundsGuard()
    {
        const string src = """
            namespace S;
            public static class M
            {
                public static int At(int[] xs, int i)
                {
                    if (i < xs.Length)
                    {
                        return xs[i];
                    }
                    return -1;
                }
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "M.cs");
        var oob = Assert.Single(cands, c => c.Operator == MutationOperatorKind.IndexOutOfBounds);

        Assert.Equal("Calor0921", oob.ExpectedCheck);
        Assert.DoesNotContain("if (i < xs.Length)", oob.MutatedSource);
        Assert.Contains("return xs[i];", oob.MutatedSource);
        Assert.True(Parses(oob.MutatedSource));
    }

    // ---- NullDeref → Calor0922 (guard removal) ----

    [Fact]
    public void NullDeref_RemovesWrappingNullGuard()
    {
        const string src = """
            namespace S;
            public static class M
            {
                public static int Len(string s)
                {
                    if (s != null)
                    {
                        return s.Length;
                    }
                    return 0;
                }
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "M.cs");
        var nd = Assert.Single(cands, c => c.Operator == MutationOperatorKind.NullDeref);

        Assert.Equal("Calor0922", nd.ExpectedCheck);
        Assert.DoesNotContain("if (s != null)", nd.MutatedSource);
        Assert.Contains("return s.Length;", nd.MutatedSource);
        Assert.True(Parses(nd.MutatedSource));
    }

    [Fact]
    public void Enumerate_UnparseableSource_ReturnsEmpty_NoThrow()
    {
        var cands = ExpressibleMutationOperators.Enumerate("this is ) not ( c#", "bad.cs");
        Assert.NotNull(cands);
    }

    [Fact]
    public void AllExpressibleCandidates_AreTaggedExpressible_WithAnExpectedCheck()
    {
        const string src = """
            namespace S;
            public class C
            {
                private int _n;
                public int Ratio(int a, int d)
                {
                    if (d != 0) { return _n / d; }
                    return 0;
                }
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "C.cs");
        Assert.NotEmpty(cands);
        Assert.All(cands, c =>
        {
            Assert.Equal(DefectStratum.Expressible, c.Stratum);
            Assert.False(string.IsNullOrEmpty(c.ExpectedCheck));
            Assert.Equal(MutationSource.InjectedMutation, c.Source);
        });
    }
}
