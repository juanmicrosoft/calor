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

    private static ConversionResult Convert(
        string csharp,
        bool selectActiveBranchLossy = false,
        ConversionFidelity fidelity = ConversionFidelity.Lossy,
        IReadOnlyCollection<string>? definedSymbols = null)
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = fidelity,
            ModuleName = "W4SliceA",
            GracefulFallback = true,
            AutoGenerateIds = true,
            DefinedSymbols = definedSymbols ?? Array.Empty<string>(),
            PreprocessorMode = selectActiveBranchLossy
                ? PreprocessorConversionMode.SelectActiveBranchLossy
                : PreprocessorConversionMode.PreserveAllBranches,
        });
        return converter.Convert(csharp, "W4SliceA.cs");
    }

    // ==================================================================
    // D1 — #772 preprocessor / conditional-compilation
    // ==================================================================
    // Lossy selected-branch mode delegates condition evaluation to Roslyn and
    // records every removed conditional directive.

    [Fact]
    public void D1_Registry_Preprocessor_IsNotClaimedFull()
    {
        // The aggregate stays Partial until interface/enum member directives are
        // native rather than explicit interop boundaries.
        Assert.NotEqual(SupportLevel.Full, FeatureSupport.GetSupportLevel("preprocessor-directive"));
    }

    [Fact]
    public void D1_DeadFirstBranch_SelectedLossyUsesRoslynAndReportsLoss()
    {
        // The pathological case from #772 must choose the live #else branch through
        // Roslyn, never the first branch by source position.
        var csharp = "#if false\n" +
                     "public class A { public int X() => 1; }\n" +
                     "#else\n" +
                     "public class A { public int X() => 2; }\n" +
                     "#endif\n";

        var result = Convert(csharp, selectActiveBranchLossy: true);
        Assert.True(result.Success);

        var ppLosses = result.Context.Losses
            .Where(l => l.Kind == ConversionLossKind.PreprocessorStripped)
            .ToList();
        foreach (var l in ppLosses) _output.WriteLine(l.ToString());
        _output.WriteLine(result.CalorSource!);

        Assert.Contains("§R 2", result.CalorSource);
        Assert.DoesNotContain("§R 1", result.CalorSource);
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
        var result = Convert(csharp, selectActiveBranchLossy: true);
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Kind == ConversionLossKind.PreprocessorStripped);
    }

    // ==================================================================
    // D2 — #769 namespace topology / same-name type merge
    // ==================================================================

    [Fact]
    public void D2_Registry_DistinguishesNativeSingleScopeFromPartialTopology()
    {
        Assert.Equal(SupportLevel.Partial, FeatureSupport.GetSupportLevel("namespace"));
        Assert.Equal(
            SupportLevel.Full,
            FeatureSupport.GetSupportLevel("namespace-single-scope"));
        Assert.Equal(
            SupportLevel.Partial,
            FeatureSupport.GetSupportLevel("namespace-topology"));
    }

    [Fact]
    public void D2_SameNameAcrossNamespaces_StaysNativeAndDistinct()
    {
        var csharp = """
            namespace A { public class Foo { public int V() => 1; } }
            namespace B { public class Foo { public int V() => 2; } }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        _output.WriteLine(result.CalorSource);

        Assert.Contains(
            "namespace-topology",
            result.Context.GetExplanation().PartialFeatures);
        Assert.Empty(result.Context.Losses);
        Assert.Equal(2, result.Ast!.Classes.Count);
        Assert.Contains(
            result.Ast.Classes,
            type => type.FullyQualifiedSymbolIdentity == "global::A.Foo");
        Assert.Contains(
            result.Ast.Classes,
            type => type.FullyQualifiedSymbolIdentity == "global::B.Foo");
        Assert.DoesNotContain("§CSHARP", result.CalorSource);
        Assert.Contains("§CL{", result.CalorSource);
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
    public void D2_SameNameDifferentArity_StaysNative_AndRoundTrips()
    {
        // `A.Foo` (arity 0) and `B.Foo<T>` (arity 1) are distinct types that coexist
        // fine — different generic arity means no identity merge. The collision key
        // includes arity, so these must NOT be refused; they stay native.
        var csharp = """
            namespace A { public class Foo { public int V() => 1; } }
            namespace B { public class Foo<T> { public T V(T x) => x; } }
            """;

        var result = Convert(csharp);
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Feature == "namespace-collision");

        var roundTrip = TestHelpers.FullRoundTrip(csharp, "W4SliceA");
        Assert.True(roundTrip.RoslynSuccess,
            "Same-name-different-arity cross-namespace types must stay native and compile:\n"
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
        // The retired module-scope hoist lost generic identity. The containing
        // member must remain in §CSHARP interop.
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
        // Captures made the retired module-scope hoist invalid. Escalate loudly.
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

    // A member containing ANY local function escalates to §CSHARP interop. The
    // hoist-to-module lowering is unsound in both directions: its happy path
    // (no same-named member) build-breaks (the call site is orphaned from the
    // hoisted function → CS0103), and when a same-named member DOES exist the
    // orphaned call silently rebinds to it and compiles clean (LossCount==0, wrong
    // behaviour) — the §1 predicate-trust blocker. Escalate-all makes every
    // outcome honest interop; no correct native coverage is lost.

    private static void AssertLocalFunctionEscalated(ConversionResult result, string localName)
    {
        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        // Non-native: the whole containing member is preserved verbatim as raw C#
        // (a counted interop loss). The loss is labelled by the member kind; the
        // escalation is attributed to the local function in the conversion issues.
        Assert.Contains(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains(result.Issues, i => i.Feature == "local-function");
        Assert.Contains("§CSHARP", result.CalorSource);
        // Never hoisted as a native module function.
        Assert.DoesNotContain(result.Ast!.Functions, f => f.Name == localName);
    }

    public static TheoryData<string, string, bool> LocalFunctionForms => new()
    {
        {
            """
            public class C
            {
                public int M()
                {
                    int Add(int a, int b) => a + b;
                    return Add(1, 2);
                }
            }
            """,
            "Add",
            true
        },
        {
            """
            public class C
            {
                private int _offset = 2;
                public int M(int value)
                {
                    int Next() => ++value + this._offset;
                    return Next();
                }
            }
            """,
            "Next",
            true
        },
        {
            """
            public class C
            {
                public bool M(int value)
                {
                    bool Even(int n) => n == 0 || Odd(n - 1);
                    bool Odd(int n) => n != 0 && Even(n - 1);
                    return Even(value);
                }
            }
            """,
            "Even",
            true
        },
        {
            """
            public class C
            {
                public T M<T>(T value) where T : class
                {
                    T Identity<TLocal>(TLocal ignored, T result)
                        where TLocal : struct => result;
                    return Identity(1, value);
                }
            }
            """,
            "Identity",
            true
        },
        {
            """
            public class C
            {
                public async System.Threading.Tasks.Task<int> M()
                {
                    static async System.Threading.Tasks.Task<int> Local()
                    {
                        await System.Threading.Tasks.Task.Yield();
                        return 1;
                    }
                    return await Local();
                }
            }
            """,
            "Local",
            true
        },
        {
            """
            public class C
            {
                public System.Collections.Generic.IEnumerable<int> M()
                {
                    System.Collections.Generic.IEnumerable<int> Local()
                    {
                        yield return 1;
                    }
                    return Local();
                }
            }
            """,
            "Local",
            true
        },
        {
            """
            public class C
            {
                public int M(scoped ref int value, out int copy)
                {
                    static int Local(scoped ref int item, out int result)
                    {
                        result = item;
                        return ++item;
                    }
                    return Local(ref value, out copy);
                }
            }
            """,
            "Local",
            true
        },
        {
            """
            public class C
            {
                public unsafe int M(int* value)
                {
                    unsafe int Local(int* item) => *item;
                    return Local(value);
                }
            }
            """,
            "Local",
            true
        },
        {
            """
            public class C
            {
                public int M()
                {
                    [System.Obsolete("local")]
                    static int Local(int value) => value;
                    return Local(1);
                }
            }
            """,
            "Local",
            true
        }
    };

    public static TheoryData<string, string> LocalFunctionContainingContexts => new()
    {
        {
            """
            public class C
            {
                private int _value;
                public C()
                {
                    int Local() => 1;
                    _value = Local();
                }
            }
            """,
            "constructor"
        },
        {
            """
            public class C
            {
                public int Value
                {
                    get
                    {
                        int Local() => 1;
                        return Local();
                    }
                }
            }
            """,
            "property"
        },
        {
            """
            public class C
            {
                public static C operator +(C left, C right)
                {
                    C Local() => left;
                    return Local();
                }
            }
            """,
            "operator"
        },
        {
            """
            public class C
            {
                public int M()
                {
                    System.Func<int> value = () =>
                    {
                        int Local() => 1;
                        return Local();
                    };
                    return value();
                }
            }
            """,
            "method"
        }
    };

    [Theory]
    [MemberData(nameof(LocalFunctionForms))]
    public void D4_AllLocalFunctionForms_PreserveContainingMember(
        string csharp,
        string localName,
        bool roundTripCompiles)
    {
        var conversion = Convert(csharp, fidelity: ConversionFidelity.Lossless);

        AssertLocalFunctionEscalated(conversion, localName);
        var interopLoss = Assert.Single(conversion.Losses.Where(loss =>
            loss.Kind == ConversionLossKind.InteropPreserved));
        Assert.Equal("method", interopLoss.Feature);
        Assert.Contains(localName, conversion.CalorSource);

        if (roundTripCompiles)
        {
            var roundTrip = TestHelpers.FullRoundTrip(
                csharp,
                "LocalFunctionContainment");
            Assert.True(
                roundTrip.RoslynSuccess,
                string.Join("; ", roundTrip.RoslynErrors));
        }
    }

    [Theory]
    [MemberData(nameof(LocalFunctionContainingContexts))]
    public void D4_LocalFunctionsInAllContainingContexts_EscalateWholeMember(
        string csharp,
        string expectedInteropFeature)
    {
        var conversion = Convert(csharp, fidelity: ConversionFidelity.Lossless);

        Assert.True(
            conversion.Success,
            string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.Contains(
            conversion.Issues,
            issue => issue.Feature == "local-function");
        Assert.Contains(
            conversion.Losses,
            loss =>
                loss.Kind == ConversionLossKind.InteropPreserved &&
                loss.Feature == expectedInteropFeature);
        Assert.DoesNotContain(
            conversion.Losses,
            loss => loss.Kind == ConversionLossKind.Dropped);
        Assert.Contains("Local()", conversion.CalorSource);

        var roundTrip = TestHelpers.FullRoundTrip(
            csharp,
            "LocalFunctionContainingContext");
        Assert.True(
            roundTrip.RoslynSuccess,
            string.Join("; ", roundTrip.RoslynErrors));
    }

    [Fact]
    public void D4_ConditionalLocalFunction_IsPreservedWithoutDrops()
    {
        var csharp = """
            public class C
            {
                public int M()
                {
                #if FEATURE
                    int Local() => 1;
                    return Local();
                #else
                    return 2;
                #endif
                }
            }
            """;

        var conversion = Convert(
            csharp,
            fidelity: ConversionFidelity.Lossless,
            definedSymbols: ["FEATURE"]);

        Assert.True(conversion.Success);
        Assert.Contains("int Local() => 1", conversion.CalorSource);
        var interopLoss = Assert.Single(conversion.Losses.Where(loss =>
            loss.Kind == ConversionLossKind.InteropPreserved));
        Assert.Equal("conditional-unmodeled-placement", interopLoss.Feature);
        Assert.Contains("public int M()", conversion.CalorSource);
        Assert.DoesNotContain(
            conversion.Losses,
            loss => loss.Kind == ConversionLossKind.Dropped);
    }

    [Fact]
    public void D4_PlainLocalFunction_EscalatesToInterop()
    {
        // Even a plain non-generic, non-capturing local function escalates: the
        // hoist's own happy path build-breaks (orphaned call site), so there is no
        // correct native conversion to preserve.
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

        AssertLocalFunctionEscalated(Convert(csharp), "Add");
    }

    [Fact]
    public void D4_RecursiveLocalFunction_EscalatesToInterop()
    {
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

        AssertLocalFunctionEscalated(Convert(csharp), "Fac");
    }

    [Fact]
    public void D4_C1_LocalShadowsSameClassMethod_EscalatesToInterop_NotSilentRebind()
    {
        // C-1: the class has both `int Foo() => 999` and a local `int Foo() => 1`.
        // The source M() returns 1; a naive hoist would orphan the call and silently
        // rebind it to the class method (round-trip M() == 999) — a silent
        // substitution with zero recorded losses. Escalate-all refuses it loudly.
        var csharp = """
            public class C
            {
                public int Foo() => 999;
                public int M()
                {
                    int Foo() => 1;
                    return Foo();
                }
            }
            """;

        AssertLocalFunctionEscalated(Convert(csharp), "Foo");
    }

    [Fact]
    public void D4_C2_LocalShadowsInheritedMethod_EscalatesToInterop_NotSilentRebind()
    {
        // C-2: the local `Foo` shadows an INHERITED Base.Foo(). A hoist would orphan
        // the call and it would rebind to the base method — silent.
        var csharp = """
            public class Base { public int Foo() => 999; }
            public class Derived : Base
            {
                public int M()
                {
                    int Foo() => 1;
                    return Foo();
                }
            }
            """;

        AssertLocalFunctionEscalated(Convert(csharp), "Foo");
    }

    [Fact]
    public void D4_C3_LocalShadowsClassOverload_EscalatesToInterop_NotSilentRebind()
    {
        // C-3: the local `Foo(int)` shadows a class overload `Foo(int)`. A hoist
        // would orphan the call and rebind to the class overload — silent.
        var csharp = """
            public class C
            {
                public int Foo(int x) => 999;
                public int M()
                {
                    int Foo(int x) => x;
                    return Foo(1);
                }
            }
            """;

        AssertLocalFunctionEscalated(Convert(csharp), "Foo");
    }
}
