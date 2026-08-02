using Calor.Compiler.Migration;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// WS-W4 Slice A (structural): the four structural SILENT→LOUD items from
/// docs/plans/wedge-w4-prereqs.md §2 — #772 preprocessor, #769 namespace
/// same-name merge, #775 records, #777 local functions.
///
/// The governing predicate (§1) trusts the eligibility predicate iff no silent
/// semantic substitution survives *native* conversion (a region with
/// LossCount == 0). Each test below proves a construct that WOULD have silently
/// diverged now either (a) converts faithfully with runtime equivalence, or
/// (b) escalates loudly to §CSHARP interop / a counted conversion loss (so the
/// predicate excludes it).
/// </summary>
public class WedgeW4SliceAStructuralTests
{
    private readonly ITestOutputHelper _output;

    public WedgeW4SliceAStructuralTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static ConversionResult Convert(string csharp, bool stripPreprocessor = false)
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            ModuleName = "W4SliceA",
            GracefulFallback = true,
            AutoGenerateIds = true,
            StripPreprocessor = stripPreprocessor,
        });
        return converter.Convert(csharp, "W4SliceA.cs");
    }

    // ==================================================================
    // D1 — #772 preprocessor / conditional-compilation
    // ==================================================================
    // The default pipeline strips conditional-compilation directives and keeps
    // the first #if branch UNEVALUATED. That branch selection is not something
    // the converter can evaluate deterministically, so it must be LOUD: each
    // stripped conditional directive is recorded as a PreprocessorStripped loss,
    // making the file non-native (LossCount > 0) → excluded by the predicate.

    [Fact]
    public void D1_Registry_Preprocessor_IsNotClaimedFull()
    {
        // Registry honesty: the false-green "preprocessor fully supported" claim
        // is downgraded — conditional compilation is not faithfully preserved.
        Assert.NotEqual(SupportLevel.Full, FeatureSupport.GetSupportLevel("preprocessor-directive"));
    }

    [Fact]
    public void D1_DeadFirstBranch_IsRefusedLoudly_NotSilentlyPicked()
    {
        // The pathological case from #772: `#if false ... #else ... #endif` would
        // silently convert DEAD code and discard LIVE code. It must not survive as
        // a native (zero-loss) conversion.
        var csharp = "#if false\n" +
                     "public class A { public int X() => 1; }\n" +
                     "#else\n" +
                     "public class A { public int X() => 2; }\n" +
                     "#endif\n";

        var result = Convert(csharp, stripPreprocessor: true);
        Assert.True(result.Success);

        var ppLosses = result.Context.Losses
            .Where(l => l.Kind == ConversionLossKind.PreprocessorStripped)
            .ToList();
        foreach (var l in ppLosses) _output.WriteLine(l.ToString());

        // At least one conditional directive recorded as a loss → the file is
        // non-native. The predicate's LossCount == 0 native gate excludes it, so
        // the wrongly-picked branch never enters the measurement silently.
        Assert.NotEmpty(ppLosses);
        Assert.True(result.Context.Losses.Count > 0,
            "Conditional compilation must make the file non-native (LossCount > 0).");
    }

    [Fact]
    public void D1_NoConditionalCompilation_StaysNative()
    {
        // A file with no conditional compilation is unaffected — the honesty pass
        // does not tax ordinary code.
        var csharp = "public class Plain { public int X() => 42; }\n";
        var result = Convert(csharp, stripPreprocessor: true);
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Kind == ConversionLossKind.PreprocessorStripped);
    }

    // ==================================================================
    // D2 — #769 namespace topology / same-name type merge
    // ==================================================================

    [Fact]
    public void D2_Registry_Namespace_IsNotClaimedFull()
    {
        // Namespace topology is flattened; the registry must not claim faithful
        // namespace preservation.
        Assert.NotEqual(SupportLevel.Full, FeatureSupport.GetSupportLevel("namespace"));
    }

    [Fact]
    public void D2_SameNameAcrossNamespaces_IsRefusedAsInterop_NotMerged()
    {
        // Two distinct `Foo` types in different namespaces would flatten into one
        // module and collapse to a single identity — a silent merge that changes
        // type identity. Both must be refused (preserved verbatim as §CSHARP).
        var csharp = """
            namespace A { public class Foo { public int V() => 1; } }
            namespace B { public class Foo { public int V() => 2; } }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        _output.WriteLine(result.CalorSource);

        var collisionLosses = result.Context.Losses
            .Where(l => l.Feature == "namespace-collision"
                        && l.Kind == ConversionLossKind.InteropPreserved)
            .ToList();

        // Both same-named types escalate to interop (a counted loss) → non-native.
        Assert.Equal(2, collisionLosses.Count);
        Assert.Contains("§CSHARP", result.CalorSource);
        // The native merged shape (a Calor class definition) must be gone.
        Assert.DoesNotContain("§CL{", result.CalorSource);
    }

    [Fact]
    public void D2_UniqueNamesAcrossNamespaces_StayNative_AndRoundTrip()
    {
        // Flattening carries no identity-merge risk when the bare names are unique,
        // so these may remain native — and must round-trip to compilable C#.
        var csharp = """
            namespace A { public class Foo { public int V() => 1; } }
            namespace B { public class Bar { public int V() => 2; } }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Feature == "namespace-collision");
        Assert.Empty(result.Context.Losses);

        var roundTrip = TestHelpers.FullRoundTrip(csharp, "W4SliceA");
        Assert.True(roundTrip.RoslynSuccess,
            "Unique cross-namespace types kept native must round-trip to compilable C#:\n"
            + string.Join("\n", roundTrip.RoslynErrors));
    }

    [Fact]
    public void D2_SameNameInSingleNamespacePartial_IsNotACollision()
    {
        // Two partial declarations of one type in the SAME namespace are a
        // legitimate merge, not a cross-namespace collision — they stay native.
        var csharp = """
            namespace A
            {
                public partial class Foo { public int X() => 1; }
                public partial class Foo { public int Y() => 2; }
            }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Feature == "namespace-collision");
    }

    // ==================================================================
    // D3 — #775 records: containment + registry honesty
    // ==================================================================

    [Fact]
    public void D3_Registry_Record_IsNotClaimedFull()
    {
        Assert.NotEqual(SupportLevel.Full, FeatureSupport.GetSupportLevel("record"));
    }

    [Fact]
    public void D3_RecordWithValueSemantics_IsPreservedAsInterop()
    {
        // A record whose behaviour is exercised through the synthesized value
        // semantics (with-expression, value equality) must not silently degrade
        // to a sealed class with reference semantics — it is preserved verbatim.
        var csharp = """
            public record Money(decimal Amount, string Currency);

            public static class Bank
            {
                public static Money Bump(Money m) => m with { Amount = m.Amount + 1 };
                public static bool Same(Money a, Money b) => a == b;
            }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        _output.WriteLine(result.CalorSource);

        var recordLoss = Assert.Single(
            result.Context.Losses.Where(l => l.Feature == "record"));
        Assert.Equal(ConversionLossKind.InteropPreserved, recordLoss.Kind);
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("record Money", result.CalorSource);
    }

    // ==================================================================
    // D4 — #777 local functions: containment + registry honesty
    // ==================================================================

    [Fact]
    public void D4_Registry_LocalFunction_IsNotClaimedFull()
    {
        Assert.NotEqual(SupportLevel.Full, FeatureSupport.GetSupportLevel("local-function"));
    }

    [Fact]
    public void D4_GenericLocalFunction_EscalatesContainingMemberToInterop()
    {
        // A generic local function loses its type parameters when hoisted to
        // module scope (silent identity loss). The containing member must escalate
        // to §CSHARP interop.
        var csharp = """
            public class C
            {
                public int M()
                {
                    T Id<T>(T x) => x;
                    return Id<int>(3);
                }
            }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        _output.WriteLine(result.CalorSource);

        Assert.Contains(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("Id<T>", result.CalorSource);
        // Not silently hoisted as a native module function.
        Assert.DoesNotContain(result.Ast!.Functions, f => f.Name == "Id");
    }

    [Fact]
    public void D4_CapturingLocalFunction_EscalatesContainingMemberToInterop()
    {
        // A local function that captures an enclosing local/parameter references a
        // name that does not exist at module scope after hoisting. Escalate loudly.
        var csharp = """
            public class E
            {
                public int M(int seed)
                {
                    int Get() => seed + 1;
                    return Get();
                }
            }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        _output.WriteLine(result.CalorSource);

        Assert.Contains(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.DoesNotContain(result.Ast!.Functions, f => f.Name == "Get");
    }

    [Fact]
    public void D4_PlainLocalFunction_IsNotEscalatedToInterop_ButBuildBreakIsTheLoudBackstop()
    {
        // A non-generic, non-capturing local function is left on the native hoist
        // path (NOT escalated to interop) — distinguishing it from the generic and
        // capturing cases above.
        var csharp = """
            public class D
            {
                public int M()
                {
                    int Add(int a, int b) => a + b;
                    return Add(1, 2);
                }
            }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        Assert.DoesNotContain("§CSHARP", result.CalorSource);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains(result.Ast!.Functions, f => f.Name == "Add");

        // The hoist relocates the function to the module's static class while the
        // call site stays in the original class, so the emitted C# does NOT resolve
        // the call (CS0103). That build break is the LOUD backstop: the file is
        // non-native (reverted), never a silent substitution. If a future emitter
        // change makes the hoist faithfully round-trip, this becomes true-native
        // (an improvement) and this assertion should be revisited.
        var roundTrip = TestHelpers.FullRoundTrip(csharp, "W4SliceA");
        Assert.False(roundTrip.RoslynSuccess,
            "Expected the hoisted-call-site build break to keep plain local functions "
            + "loudly non-native; if the hoist became faithful, update this test.");
    }

    [Fact]
    public void D4_RecursiveNonCapturingLocalFunction_IsNotEscalatedToInterop()
    {
        // Self-recursion is not a capture (the function's own name is not an
        // enclosing local) — the capture detector must not misfire on it, so it
        // stays on the native hoist path rather than escalating to interop.
        var csharp = """
            public class F
            {
                public int M(int n)
                {
                    int Fac(int k)
                    {
                        if (k <= 1) return 1;
                        return k * Fac(k - 1);
                    }
                    return Fac(n);
                }
            }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains(result.Ast!.Functions, f => f.Name == "Fac");
    }
}
