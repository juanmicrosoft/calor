using Calor.Compiler.Migration;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// W1 Slice 3 (#773/#774/#770): conformance tests binding FeatureSupport
/// registry claims to actual converter behavior, and regression tests proving
/// unsupported constructs escalate to §CSHARP interop preservation instead of
/// silently substituting semantics (char→string, unknown-op→Add, pattern
/// broadening, compound-assignment operator drops).
/// </summary>
public class RegistryConformanceTests
{
    private readonly ITestOutputHelper _output;

    public RegistryConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static ConversionResult Convert(string csharp, bool stripPreprocessor = false)
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            ModuleName = "ConformanceTest",
            GracefulFallback = true,
            AutoGenerateIds = true,
            StripPreprocessor = stripPreprocessor
        });
        return converter.Convert(csharp, "Conformance.cs");
    }

    /// <summary>Full round trip: C# → Calor → parse → emitted C# (must all succeed).</summary>
    private RoundTripResult RoundTrip(string csharp)
    {
        var result = TestHelpers.FullRoundTrip(csharp, "ConformanceTest");
        Assert.True(result.ConversionSuccess,
            "Conversion failed: " + string.Join("; ", result.ConversionIssues));
        Assert.True(result.CalorParseSuccess,
            $"Emitted Calor does not parse:\n{result.CalorSource}");
        return result;
    }

    // ------------------------------------------------------------------
    // Records (#773): registry says NotSupported → the converter must
    // preserve records verbatim as §CSHARP, never emit the broken
    // class-without-constructor shape.
    // ------------------------------------------------------------------

    [Fact]
    public void Registry_Record_IsNotClaimedFull()
    {
        Assert.NotEqual(SupportLevel.Full, FeatureSupport.GetSupportLevel("record"));
    }

    [Fact]
    public void Record_IsPreservedAsInterop_NotBrokenClass()
    {
        var csharp = """
            public record Person(string Name, int Age);
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        _output.WriteLine(result.CalorSource);

        // Preserved verbatim as interop — original record text survives.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("record Person(string Name, int Age)", result.CalorSource);
        // The broken shape (a Calor class definition for the record) must be gone.
        Assert.DoesNotContain("§CL{", result.CalorSource);

        // Counted as a structured loss with a location (#770).
        var loss = Assert.Single(result.Context.Losses.Where(l => l.Feature == "record"));
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.NotNull(loss.Line);
    }

    [Fact]
    public void Record_RoundTrip_EmitsCompilableRecord()
    {
        var result = RoundTrip("""
            public record Person(string Name, int Age);
            """);

        // The emitted C# carries the original record and compiles.
        Assert.Contains("record Person", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Round-tripped record does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void NestedRecord_IsPreservedAsInterop()
    {
        var csharp = """
            public class Container
            {
                public record Inner(int Value);
                public int Count() { return 1; }
            }
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("record Inner(int Value)", result.CalorSource);
        // The rest of the class still converts natively.
        Assert.Contains("§MT{", result.CalorSource);
    }

    // ------------------------------------------------------------------
    // Preprocessor directives (#773): registry no longer claims Full, and
    // default-pipeline stripping records structured losses with locations.
    // ------------------------------------------------------------------

    [Fact]
    public void Registry_PreprocessorDirective_IsNotClaimedFull()
    {
        Assert.NotEqual(SupportLevel.Full, FeatureSupport.GetSupportLevel("preprocessor-directive"));
    }

    [Fact]
    public void PreprocessorStripping_RecordsLossPerConditionalDirective()
    {
        var csharp = """
            public class Config
            {
            #if DEBUG
                public int Mode() { return 1; }
            #else
                public int Mode() { return 2; }
            #endif
            }
            """;

        var result = Convert(csharp, stripPreprocessor: true);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));

        var ppLosses = result.Context.Losses
            .Where(l => l.Kind == ConversionLossKind.PreprocessorStripped)
            .ToList();
        foreach (var loss in ppLosses) _output.WriteLine(loss.ToString());

        // #if (line 3) and #else (line 5) both recorded; #else notes its dropped branch.
        Assert.Equal(2, ppLosses.Count);
        Assert.Contains(ppLosses, l => l.Description.Contains("#if DEBUG") && l.Line == 3);
        Assert.Contains(ppLosses, l => l.Description.Contains("#else") && l.Description.Contains("dropped"));
    }

    [Fact]
    public void PreprocessorStripping_CosmeticDirectives_NotCountedAsLoss()
    {
        var csharp = """
            public class Config
            {
            #region Fields
                private int _x;
            #endregion
            }
            """;

        var result = Convert(csharp, stripPreprocessor: true);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Context.Losses,
            l => l.Kind == ConversionLossKind.PreprocessorStripped);
    }

    // ------------------------------------------------------------------
    // Char literals (#774): native (char-lit "x") representation, never a
    // silent string literal.
    // ------------------------------------------------------------------

    [Fact]
    public void Registry_CharLiteral_IsFull()
    {
        Assert.Equal(SupportLevel.Full, FeatureSupport.GetSupportLevel("char-literal"));
    }

    [Fact]
    public void CharLiteral_ConvertsToNativeCharLit()
    {
        var result = RoundTrip("""
            public class Chars
            {
                public bool IsSlash(char c)
                {
                    return c == '/';
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        Assert.Contains("(char-lit \"/\")", result.CalorSource);

        // Emitted C# has a real char literal — char semantics preserved.
        _output.WriteLine(result.EmittedCSharp!);
        Assert.Contains("'/'", result.EmittedCSharp);
        Assert.DoesNotContain("c == \"/\"", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Char round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void CharLiteral_EscapedChar_RoundTrips()
    {
        var result = RoundTrip("""
            public class Chars
            {
                public bool IsNewline(char c)
                {
                    return c == '\n';
                }
            }
            """);

        _output.WriteLine(result.EmittedCSharp!);
        Assert.Contains("'\\n'", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Escaped char round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    // ------------------------------------------------------------------
    // Unknown operators (#774): never substituted — the containing member is
    // preserved as §CSHARP interop with the ORIGINAL semantics.
    // ------------------------------------------------------------------

    [Fact]
    public void UnsignedRightShift_EscalatesToMemberInterop()
    {
        var csharp = """
            public class Shifter
            {
                public int Shift(int x)
                {
                    return x >>> 2;
                }
                public int Untouched() { return 1; }
            }
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        // The member with >>> is preserved verbatim — original operator intact.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains(">>> 2", result.CalorSource);
        // The unsupported operator must NOT be silently converted to addition.
        Assert.DoesNotContain("(+ x 2)", result.CalorSource);
        // Sibling members still convert natively.
        Assert.Contains("Untouched", result.CalorSource);
        Assert.Contains("§MT{", result.CalorSource);

        Assert.Contains(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
    }

    [Fact]
    public void UnsignedRightShiftAssignment_EscalatesToMemberInterop()
    {
        var csharp = """
            public class Shifter
            {
                public int Shift(int x)
                {
                    x >>>= 1;
                    return x;
                }
            }
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains(">>>= 1", result.CalorSource);
        // Must not degrade to a plain assignment x = 1.
        Assert.DoesNotContain("§ASSIGN x 1", result.CalorSource);
    }

    [Fact]
    public void CompoundAssignment_BitwiseAndShift_ConvertNatively()
    {
        var result = RoundTrip("""
            public class Bits
            {
                public int Mask(int x)
                {
                    x &= 0xFF;
                    x <<= 2;
                    x |= 1;
                    return x;
                }
            }
            """);

        _output.WriteLine(result.EmittedCSharp!);
        // Operators preserved through the round trip (either compound or expanded form).
        Assert.True(
            result.EmittedCSharp!.Contains("&") && result.EmittedCSharp.Contains("<<") && result.EmittedCSharp.Contains("|"),
            $"Compound assignment operators lost in round trip:\n{result.EmittedCSharp}");
        Assert.True(result.RoslynSuccess,
            "Compound-assignment round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void CompoundAssignment_InExpressionContext_KeepsOperator()
    {
        // Old behavior: `total += x` in expression context hoisted `total = x`,
        // silently dropping the +. The compound operator must survive.
        var result = RoundTrip("""
            public class Acc
            {
                public int Sum(int x)
                {
                    int total = 0;
                    int y = (total += x);
                    return total + y;
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);
        // The emitted C# must not contain the operator-dropping form `total = x;`.
        Assert.DoesNotContain("total = x;", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Expression-context compound assignment does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    // ------------------------------------------------------------------
    // Switch labels and patterns (#774): unsupported shapes must never
    // broaden to wildcard — they escalate to member interop.
    // ------------------------------------------------------------------

    [Fact]
    public void PatternSwitchLabel_EscalatesInsteadOfWildcard()
    {
        var csharp = """
            public class Matcher
            {
                public string Classify(int x)
                {
                    switch (x)
                    {
                        case > 100:
                            return "big";
                        case 0:
                            return "zero";
                        default:
                            return "other";
                    }
                }
            }
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        // The member is preserved verbatim — `case > 100:` semantics intact.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("case > 100:", result.CalorSource);
        Assert.Contains(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
    }
}
