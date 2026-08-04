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
    public void EffectViolation_InjectsUsingNestedFsEffect_ThatCorruptsTheReturnByOne()
    {
        var cands = ExpressibleMutationOperators.Enumerate(EffectSample, "Counter.cs");
        var ev = Assert.Single(cands, c => c.Operator == MutationOperatorKind.EffectViolation);

        Assert.Equal(DefectStratum.Expressible, ev.Stratum);
        Assert.Equal("Calor0410", ev.ExpectedCheck);
        // The fs effect is nested in a using body (the converter's §E-inference gap).
        Assert.Contains("using (var __calorSink", ev.MutatedSource);
        Assert.Contains("System.IO.Directory.Exists", ev.MutatedSource);
        // The return is deterministically corrupted by the taint (fixed +1), intrinsic to the effect.
        Assert.Contains("(_n + 1) + __calorTaint", ev.MutatedSource);
        Assert.True(Parses(ev.MutatedSource), "mutated source must compile as C#");
    }

    [Fact]
    public void EffectViolation_TargetsStaticMethods_Too()
    {
        const string src = """
            namespace S;
            public static class M
            {
                public static long Sum(long a, long b) { return a + b; }
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "M.cs");
        var ev = Assert.Single(cands, c => c.Operator == MutationOperatorKind.EffectViolation);
        Assert.Contains("(a + b) + __calorTaint", ev.MutatedSource);
        Assert.True(Parses(ev.MutatedSource));
    }

    [Fact]
    public void EffectViolation_CoversNonNumericReturns_AfterTheD_S0_5_2_Widening()
    {
        // Pre-widening this asserted that string/bool/void ALL yielded nothing, because the corruption
        // was arithmetic. The int/long restriction was never about addressability — the using-nested
        // Directory.* effect fires Calor0410 regardless of return type — so D-S0.5.2 widened the
        // CORRUPTION and the site universe followed. string and bool are now in scope.
        const string src = """
            namespace S;
            public class C
            {
                public string Name() { return "x"; }
                public bool Ok() { return true; }
                public void Go() { }
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "C.cs")
            .Where(c => c.Operator == MutationOperatorKind.EffectViolation).ToList();

        Assert.Equal(2, cands.Count);                                  // Name + Ok; Go has nothing to corrupt
        Assert.All(cands, c => Assert.Equal("Calor0410", c.ExpectedCheck));
        Assert.Contains(cands, c => c.MutatedSource.Contains("default(string)!"));
        Assert.Contains(cands, c => c.MutatedSource.Contains("^ (__calorTaint == 1)"));
    }

    [Fact]
    public void EffectViolation_StillExcludesReturnsItCannotCorruptSoundly()
    {
        // The exclusions that remain after the widening, each because the corruption would not compile
        // or would not be a single deterministic point. async is the subtle one: the declared type is
        // Task<T> but `return` yields T, so default(Task<T>) is a type error in that position.
        const string src = """
            using System.Threading.Tasks;
            namespace S;
            public class C
            {
                public void Go() { }
                public async Task<int> LaterAsync() { await Task.Yield(); return 1; }
                private int _f;
                public ref int Ref() { return ref _f; }
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "C.cs");
        Assert.DoesNotContain(cands, c => c.Operator == MutationOperatorKind.EffectViolation);
    }

    [Fact]
    public void EffectViolation_DoesNotCorruptReturnsInsideNestedLambda()
    {
        // The method's OWN return is the last one; the lambda's return must not be the corruption site.
        const string src = """
            using System;
            namespace S;
            public class C
            {
                public int F(int n)
                {
                    Func<int, int> g = x => { return x * 2; };
                    return g(n);
                }
            }
            """;
        var cands = ExpressibleMutationOperators.Enumerate(src, "C.cs");
        var ev = Assert.Single(cands, c => c.Operator == MutationOperatorKind.EffectViolation);
        // The method-owned `return g(n)` is corrupted; the lambda's `return x * 2` is left intact.
        Assert.Contains("(g(n)) + __calorTaint", ev.MutatedSource);
        Assert.Contains("return x * 2;", ev.MutatedSource);
        Assert.True(Parses(ev.MutatedSource));
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
