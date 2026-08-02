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
    // Prefix unary operators (#774): exhaustive SyntaxKind mapping — an
    // unknown prefix operator must never silently become negation, and unary
    // plus (no Calor node) escalates instead of being dropped.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("-x", "(- x")]     // unary minus  → negate
    [InlineData("!b", "(! b")]     // logical not
    [InlineData("~x", "(~ x")]     // bitwise not
    public void PrefixUnary_KnownOperators_ConvertNatively(string expr, string expectedCalor)
    {
        var isBool = expr.Contains('b');
        var type = isBool ? "bool" : "int";
        var result = RoundTrip($$"""
            public class U
            {
                public {{type}} M({{type}} x, bool b)
                {
                    return {{expr}};
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        Assert.Contains(expectedCalor, result.CalorSource);
        Assert.True(result.RoslynSuccess,
            "Prefix-unary round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void PrefixUnary_UnaryPlus_EscalatesToInterop()
    {
        // +x has no Calor node and could change user-defined operator+ overload
        // resolution. The old code silently mapped ANY unknown prefix operator
        // (including +) to negation, turning `+x` into `-x`. It must escalate.
        var csharp = """
            public class U
            {
                public int Pos(int x)
                {
                    return +x;
                }
                public int Untouched() { return 1; }
            }
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        // Preserved verbatim — the unary + survives, never becomes negation.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("+x", result.CalorSource);
        Assert.DoesNotContain("(- x)", result.CalorSource);
        // Sibling member still converts natively.
        Assert.Contains("Untouched", result.CalorSource);
        Assert.Contains(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
    }

    // ------------------------------------------------------------------
    // Registry no-silent-default proof (#774 requirement 8): every C# binary
    // operator either maps to its OWN Calor operator or escalates — never to a
    // different operator (the old default was addition).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("+", "int", "(+ a b)")]
    [InlineData("-", "int", "(- a b)")]
    [InlineData("*", "int", "(* a b)")]
    [InlineData("/", "int", "(/ a b)")]
    [InlineData("%", "int", "(% a b)")]
    [InlineData("&", "int", "(& a b)")]
    [InlineData("|", "int", "(| a b)")]
    [InlineData("^", "int", "(^ a b)")]
    [InlineData("<<", "int", "(<< a b)")]
    [InlineData(">>", "int", "(>> a b)")]
    [InlineData("==", "int", "(== a b)")]
    [InlineData("!=", "int", "(!= a b)")]
    [InlineData("<", "int", "(< a b)")]
    [InlineData("<=", "int", "(<= a b)")]
    [InlineData(">", "int", "(> a b)")]
    [InlineData(">=", "int", "(>= a b)")]
    [InlineData("&&", "bool", "(&& a b)")]
    [InlineData("||", "bool", "(|| a b)")]
    public void Registry_BinaryOperator_MapsToOwnOperator_NeverDefaultsToAdd(
        string op, string operandType, string expectedCalor)
    {
        var retType = op is "==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||"
            ? "bool" : operandType;
        var result = Convert($$"""
            public class Ops
            {
                public {{retType}} M({{operandType}} a, {{operandType}} b)
                {
                    return a {{op}} b;
                }
            }
            """);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        Assert.Contains(expectedCalor, result.CalorSource);
        // The old silent default turned unknown operators into addition — prove
        // no non-additive operator ever produced a `(+ a b)` form.
        if (op != "+")
            Assert.DoesNotContain("(+ a b)", result.CalorSource);
    }

    // ------------------------------------------------------------------
    // Numeric literal width / signedness (#774 requirement 7): the suffixes
    // that determine precision / overload resolution / signedness must survive
    // C# → Calor → C# FAITHFULLY (via the SINGLE:/LONG:/UINT:/ULONG: typed
    // literals), never silently narrowed, widened, or re-signed. Values chosen
    // so the suffix — not the magnitude — is the only thing carrying the type.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("float", "3.14f", "3.14f")]                 // single, NOT widened to double
    [InlineData("long", "5L", "5L")]                        // long that fits an int
    [InlineData("uint", "7u", "7U")]                        // uint that fits an int
    [InlineData("uint", "4000000000u", "4000000000U")]      // uint above int range, NOT a long
    [InlineData("ulong", "18446744073709551615UL", "UL")]   // unsigned 64-bit max
    [InlineData("decimal", "1.5m", "1.5m")]                 // decimal (already worked — regression guard)
    public void NumericLiteral_WidthAndSignedness_SurviveRoundTrip_Faithfully(
        string type, string literal, string expectedInCSharp)
    {
        var result = RoundTrip($$"""
            public class Nums
            {
                public {{type}} M()
                {
                    return {{literal}};
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);

        // The width/sign/precision is preserved end-to-end.
        Assert.Contains(expectedInCSharp, result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Numeric literal round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void FloatLiteral_NotWidenedToDouble_NoSeventeenDigitExpansion()
    {
        // The concrete C1 repro: 3.14f used to convert to §B{f} 3.140000104904175
        // → `double f = 3.14000...;`. It must stay a single-precision `3.14f`.
        var result = RoundTrip("""
            public class Precision
            {
                public float Pi()
                {
                    var f = 3.14f;
                    return f;
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);

        Assert.Contains("SINGLE:3.14", result.CalorSource);
        Assert.Contains("3.14f", result.EmittedCSharp);
        Assert.DoesNotContain("3.140000", result.EmittedCSharp);  // no widened double expansion
        Assert.True(result.RoslynSuccess,
            "Float precision round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void FloatLiteral_OverloadSelection_StaysSingle()
    {
        // Overload-selection context: Describe(3.14f) must keep binding the
        // float overload after the round trip, not silently rebind to double.
        var result = RoundTrip("""
            public class Overloads
            {
                public string Describe(float x) => "float";
                public string Describe(double x) => "double";
                public string Pick() => Describe(3.14f);
            }
            """);

        _output.WriteLine(result.EmittedCSharp!);
        // The argument keeps its `f` suffix, so overload resolution is unchanged.
        Assert.Contains("Describe(3.14f)", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Float overload round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void UnsignedIntLiteral_StaysUint_NotPromotedToLong()
    {
        // 4000000000u fits in a long, so the old bare-digit Calor emission
        // re-parsed it as `4000000000L` (long) — a silent signedness+width
        // change. The UINT: typed literal keeps it uint.
        var result = RoundTrip("""
            public class U
            {
                public uint Big() { return 4000000000u; }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);
        Assert.Contains("UINT:4000000000", result.CalorSource);
        Assert.Contains("4000000000U", result.EmittedCSharp);
        Assert.DoesNotContain("4000000000L", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Unsigned literal round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    // ------------------------------------------------------------------
    // Switch labels and patterns (#774): unsupported shapes must never
    // broaden to wildcard — they escalate to member interop; typed
    // declaration / type-only patterns keep their runtime type test.
    // ------------------------------------------------------------------

    [Fact]
    public void Registry_TypePattern_IsFull()
    {
        Assert.Equal(SupportLevel.Full, FeatureSupport.GetSupportLevel("type-pattern"));
    }

    [Fact]
    public void DeclarationPattern_PreservesTypeTest_NotBroadenedToVar()
    {
        // `object o switch { string s => ... }` must keep the `string` test.
        // The old path collapsed `string s` to a bare `var s`, which matches
        // EVERY value and would steal the int/other arms.
        var result = RoundTrip("""
            public class Matcher
            {
                public int Describe(object o)
                {
                    return o switch
                    {
                        string s => s.Length,
                        int => -1,
                        _ => 0
                    };
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);

        // Calor keeps the type test natively.
        Assert.Contains("§PTYPE{string:s}", result.CalorSource);
        Assert.Contains("§PTYPE{int}", result.CalorSource);

        // Emitted C# restores the real declaration/type patterns — the type
        // test survives and the arm is NOT broadened to `var s`.
        Assert.Contains("string s", result.EmittedCSharp);
        Assert.DoesNotContain("var s =>", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Type-pattern round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void DeclarationPattern_GenericType_RoundTripsThroughBothEmitters()
    {
        // The §PTYPE{Type<T>:x} form must survive the Calor parser too, so a
        // generic-typed declaration pattern keeps its type test end-to-end.
        var result = RoundTrip("""
            public class Box<T> { public T Value; }
            public class Matcher
            {
                public int Describe(object o)
                {
                    return o switch
                    {
                        Box<int> b => 1,
                        _ => 0
                    };
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);

        Assert.Contains("§PTYPE{Box<int>:b}", result.CalorSource);
        // The generic type test is restored, not broadened to `var b`.
        Assert.Contains("Box<int> b", result.EmittedCSharp);
        Assert.True(result.RoslynSuccess,
            "Generic type-pattern round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void TypeOnlyPattern_PreservesTypeTest()
    {
        var result = RoundTrip("""
            public class Matcher
            {
                public string Kind(object o)
                {
                    return o switch
                    {
                        int => "int",
                        string => "string",
                        _ => "other"
                    };
                }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);

        Assert.Contains("§PTYPE{int}", result.CalorSource);
        Assert.Contains("§PTYPE{string}", result.CalorSource);
        // Type-only arms carry no binding.
        Assert.DoesNotContain("§PTYPE{int:", result.CalorSource);
        Assert.True(result.RoslynSuccess,
            "Type-only-pattern round trip does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void PatternSwitchLabel_RelationalStatement_EscalatesInsteadOfWildcard()
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

        // Preserved verbatim — `case > 100:` semantics intact, never broadened.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("case > 100:", result.CalorSource);
        Assert.Contains(result.Context.Losses,
            l => l.Kind == ConversionLossKind.InteropPreserved);
    }

    // ------------------------------------------------------------------
    // #836 review fixes
    // ------------------------------------------------------------------

    [Fact]
    public void C1_PassthroughStatementPreservation_RawStatementSurvivesRoundTrip()
    {
        // #836 C1: in C#-preserving modes an escalated statement is contained
        // as a §RAW statement — previously Visit(RawCSharpNode) RETURNED the
        // §RAW text while body loops discard Accept() results, so the
        // statement VANISHED and the failed statement's partial conversions
        // (hoisted `§ASSIGN total (+ total x)`) leaked into the body.
        var csharp = """
            public class Acc
            {
                public int M(int x)
                {
                    int total = 0;
                    int y = (total += x) + (x >>> 2);
                    return total + y;
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            ModuleName = "ConformanceTest",
            GracefulFallback = true,
            AutoGenerateIds = true,
            StripPreprocessor = false,
            PassthroughOnError = true
        });
        var result = converter.Convert(csharp, "Conformance.cs");

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        // The raw statement is actually IN the output…
        Assert.Contains("§RAW", result.CalorSource);
        Assert.Contains(">>> 2", result.CalorSource);
        // …and the half-converted compound assignment did not leak next to it.
        Assert.DoesNotContain("(+ total x)", result.CalorSource);

        // Ledger matches reality.
        Assert.Contains(result.Context.Losses, l => l.Kind == ConversionLossKind.InteropPreserved);

        // Round trip: the raw statement text survives and the emitted C# compiles.
        var emitted = TestHelpers.CompileCalorToCSharp(result.CalorSource!);
        Assert.NotNull(emitted);
        _output.WriteLine(emitted!);
        Assert.Contains(">>> 2", emitted);
        var validation = Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(emitted!);
        Assert.True(validation.CompilationSuccess,
            "Round-tripped C# does not compile: " + string.Join("; ", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void C2_EscalationInsideActivePreprocessorBranch_NoDanglingDirective()
    {
        // #836 C2: a member escalating inside an active #if branch was wrapped
        // via ToFullString(), capturing the `#if` leading trivia with no
        // `#endif` → dangling directive inside §CSHARP → CS1027 on re-compile.
        var result = RoundTrip("""
            public class Cond
            {
            #if true
                public int Bad(int x)
                {
                    return x >>> 1;
                }
            #endif
                public int Good() { return 1; }
            }
            """);

        _output.WriteLine(result.CalorSource!);
        _output.WriteLine(result.EmittedCSharp!);

        // The escalated member is preserved without directive trivia.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains(">>> 1", result.CalorSource);

        // And the round-tripped C# compiles (a dangling #if would be CS1027).
        Assert.True(result.RoslynSuccess,
            "Round-tripped C# does not compile: " + string.Join("; ", result.RoslynErrors));
    }

    [Fact]
    public void M1_DisabledBranchMember_PreservedVerbatim_WithLoss()
    {
        // #836 M1: an unconvertible member in a DISABLED #if branch was
        // replaced by a renamed `_PP_Fallback_*` comment stub — original API
        // gone, zero losses, Success:true. It must be preserved verbatim and
        // ledgered.
        var csharp = """
            public class Cfg
            {
            #if DEBUG
                public int DebugOnly(int x)
                {
                    return x >>> 3;
                }
            #endif
                public int Always() { return 1; }
            }
            """;

        var result = Convert(csharp); // StripPreprocessor=false → §PP conversion path

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        // The disabled member's original text survives in the output…
        Assert.Contains(">>> 3", result.CalorSource);
        // …and the loss is ledgered.
        Assert.Contains(result.Context.Losses, l =>
            l.Kind == ConversionLossKind.InteropPreserved && l.Feature == "preprocessor-disabled");
    }

    [Fact]
    public void M2_EmitterSideRawFallback_IsCountedAsLoss()
    {
        // #836 M2: raw C# reaching the output through emitter/visitor paths
        // that bypass the ledger (§CS{…} from RawCSharpExpressionNode here)
        // must still be counted, so "zero losses" always means fully native.
        var csharp = """
            public class Test
            {
                public void M()
                {
                    int x = 0;
                    var r = __makeref(x);
                }
            }
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);
        Assert.Contains("__makeref", result.CalorSource);

        // The §CS{…} fallback has no visitor-side ledger entry — the
        // post-emission reconciliation must count it.
        Assert.Contains(result.Context.Losses, l => l.Kind == ConversionLossKind.EmitterFallback);
    }

    [Fact]
    public void M2_FullyNativeOutput_HasZeroEmitterFallbackLosses()
    {
        var result = Convert("""
            public class Clean
            {
                public int Add(int a, int b) { return a + b; }
            }
            """);

        Assert.True(result.Success);
        Assert.Empty(result.Context.Losses);
    }

    [Fact]
    public void CharLiteral_LoneSurrogate_EscalatesToInterop()
    {
        // #836 m1: '\uD83D' (lone high surrogate) previously became '�' via
        // the UTF-8 replacement fallback while reporting success. The member
        // escalates to interop, preserving the ESCAPED source text.
        var csharp = """
            public class Chars
            {
                public char High()
                {
                    return '\uD83D';
                }
            }
            """;

        var result = Convert(csharp);

        Assert.True(result.Success, string.Join("; ", result.Issues.Select(i => i.Message)));
        _output.WriteLine(result.CalorSource!);

        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("'\\uD83D'", result.CalorSource);
        Assert.DoesNotContain("�", result.CalorSource);
        Assert.Contains(result.Context.Losses, l => l.Kind == ConversionLossKind.InteropPreserved);
    }

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
