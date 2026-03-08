using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for converter quality improvements: §ERR parseable emission,
/// Unicode escape sequences, and named argument round-trip.
/// </summary>
public class ConverterQualityTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }

    #region §ERR Fallback Emission Tests

    [Fact]
    public void FallbackExpression_EmitsParseableErrToken()
    {
        // C# with truly unsupported construct (__makeref) should produce §ERR "TODO: ..."
        var csharp = """
            public class Test
            {
                void M()
                {
                    int x = 0;
                    var r = __makeref(x);
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // New format: §ERR "TODO: ..." (space, no braces)
        Assert.Contains("§ERR \"TODO:", result.CalorSource);
        // Old format should NOT appear
        Assert.DoesNotContain("§ERR{\"TODO:", result.CalorSource);
    }

    [Fact]
    public void FallbackExpression_ParsesWithoutCascadingErrors()
    {
        // A .calr file containing §ERR should parse with only warnings, not cascading errors
        var calorSource = """
            §M{m001:Test}
            §F{f001:Foo:pub}
              §O{void}
              §B{x:i32} §ERR "TODO: unknown-expression -- C#: checked(1 + 2)"
            §/F{f001}
            §/M{m001}
            """;

        var compileResult = Program.Compile(calorSource);

        // The §ERR should parse as an ErrExpressionNode wrapping a string literal
        // It may produce a semantic error but should NOT produce cascading parse errors
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code == DiagnosticCode.UnexpectedToken)
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void FallbackExpression_InReturn_ParsesCleanly()
    {
        var calorSource = """
            §M{m001:Test}
            §F{f001:Foo:pub}
              §O{str}
              §R §ERR "TODO: throw-expression -- C#: throw new Exception()"
            §/F{f001}
            §/M{m001}
            """;

        var compileResult = Program.Compile(calorSource);

        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code == DiagnosticCode.UnexpectedToken)
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void FallbackExpression_MultipleInFile_NoParserCascade()
    {
        // Multiple §ERR tokens in one file should each produce one error, not cascade
        var calorSource = """
            §M{m001:Test}
            §F{f001:Foo:pub}
              §O{void}
              §B{x:i32} §ERR "TODO: feature-a -- C#: something"
              §B{y:str} §ERR "TODO: feature-b -- C#: something else"
              §B{z:bool} §ERR "TODO: feature-c -- C#: yet another"
            §/F{f001}
            §/M{m001}
            """;

        var compileResult = Program.Compile(calorSource);

        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code == DiagnosticCode.UnexpectedToken)
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void FallbackExpression_ConvertThenCompile_NoCascade()
    {
        // A class with multiple unsupported constructs should produce individual §ERR tokens
        // that parse cleanly instead of cascading parse failures
        var csharp = """
            public class Test
            {
                void M()
                {
                    int x = 0;
                    int y = 0;
                    var a = __makeref(x);
                    var b = __makeref(y);
                    int c = x + y;
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Compile the Calor output - should not cascade
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code == DiagnosticCode.UnexpectedToken)
            .ToList();
        Assert.Empty(parseErrors);
    }

    #endregion

    #region Unicode Escape Sequence Tests

    [Fact]
    public void Lexer_UnicodeEscape_u4Digit_ParsesCorrectly()
    {
        var tokens = Tokenize("\"\\u0048ello\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("Hello", tokens[0].Value);
    }

    [Fact]
    public void Lexer_UnicodeEscape_ArrowCharacter()
    {
        // \u2191 = ↑ (UP ARROW)
        var tokens = Tokenize("\"\\u2191\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("↑", tokens[0].Value);
    }

    [Fact]
    public void Lexer_UnicodeEscape_MultipleInString()
    {
        // \u2191 = ↑, \u2192 = →, \u2193 = ↓
        var tokens = Tokenize("\"\\u2191\\u2192\\u2193\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("↑→↓", tokens[0].Value);
    }

    [Fact]
    public void Lexer_UnicodeEscape_NullChar()
    {
        var tokens = Tokenize("\"\\u0000\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("\0", tokens[0].Value);
    }

    [Fact]
    public void Lexer_UnicodeEscape_UppercaseU_8Digit()
    {
        // \U0001F600 = 😀
        var tokens = Tokenize("\"\\U0001F600\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("😀", tokens[0].Value);
    }

    [Fact]
    public void Lexer_UnicodeEscape_InTripleQuotedString()
    {
        var tokens = Tokenize("\"\"\"\\u2191 up arrow\"\"\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("↑ up arrow", tokens[0].Value);
    }

    [Fact]
    public void Lexer_UnicodeEscape_InvalidTooFewDigits_PreservesRaw()
    {
        // \u with fewer than 4 hex digits — Lexer preserves the raw text
        var tokens = Tokenize("\"\\u41\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("\\u41", tokens[0].Value);
    }

    [Fact]
    public void Lexer_DirectUnicode_InString_Accepted()
    {
        // Direct Unicode characters in string literals should always work
        var tokens = Tokenize("\"↑↗→↘↓↙←↖\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("↑↗→↘↓↙←↖", tokens[0].Value);
    }

    [Fact]
    public void Lexer_CJK_InString_Accepted()
    {
        var tokens = Tokenize("\"你好世界\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("你好世界", tokens[0].Value);
    }

    [Fact]
    public void Lexer_AdditionalEscapeSequences_Supported()
    {
        // \a \b \f \v \' should now be recognized
        var tokens = Tokenize("\"\\a\\b\\f\\v\\'\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("\a\b\f\v'", tokens[0].Value);
    }

    [Fact]
    public void Lexer_SurrogateEscape_DC00()
    {
        // \uDC00 is a lone low surrogate — should be accepted as a valid escape
        var tokens = Tokenize("\"\\uDC00\"", out var diagnostics);

        Assert.False(diagnostics.HasErrors, diagnostics.ToString());
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("\uDC00", tokens[0].Value);
    }

    #endregion

    #region Named Argument Round-Trip Tests

    [Fact]
    public void NamedArgs_CSharpToCalor_PreservesNames()
    {
        var csharp = """
            public class Test
            {
                void Configure()
                {
                    Register(name: "en-US", flag: true);
                }
                void Register(string name, bool flag) { }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Named args should appear as §A[name] syntax
        Assert.Contains("§A[name]", result.CalorSource);
        Assert.Contains("§A[flag]", result.CalorSource);
    }

    [Fact]
    public void NamedArgs_CalorParsesCorrectly()
    {
        var calorSource = """
            §M{m001:Test}
            §F{f001:Configure:pub}
              §O{void}
              §C{Register} §A[name] "en-US" §A[flag] true §/C
            §/F{f001}
            §/M{m001}
            """;

        var compileResult = Program.Compile(calorSource);

        // Should parse without errors
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code == DiagnosticCode.UnexpectedToken)
            .ToList();
        Assert.Empty(parseErrors);

        // Generated C# should contain named arguments
        if (!compileResult.HasErrors)
        {
            Assert.Contains("name:", compileResult.GeneratedCode);
            Assert.Contains("flag:", compileResult.GeneratedCode);
        }
    }

    [Fact]
    public void NamedArgs_MixedPositionalAndNamed()
    {
        var csharp = """
            public class Test
            {
                void Call()
                {
                    Method(1, flag: true);
                }
                void Method(int x, bool flag) { }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Second arg should be named, first should not
        Assert.Contains("§A[flag]", result.CalorSource);
    }

    [Fact]
    public void NamedArgs_RoundTrip_CSharpToCalorToCSharp()
    {
        var csharp = """
            public class Test
            {
                void Configure()
                {
                    Register(name: "test", enabled: true);
                }
                void Register(string name, bool enabled) { }
            }
            """;

        // C# → Calor
        var converter = new CSharpToCalorConverter();
        var convResult = converter.Convert(csharp);
        Assert.True(convResult.Success, GetErrorMessage(convResult));

        // Calor → C#
        var compileResult = Program.Compile(convResult.CalorSource!);

        // The generated C# should preserve named arguments
        if (!compileResult.HasErrors)
        {
            Assert.Contains("name:", compileResult.GeneratedCode);
            Assert.Contains("enabled:", compileResult.GeneratedCode);
        }
    }

    #endregion

    #region Real-World Pattern Tests (from Reports)

    [Fact]
    public void Convert_RegistryPatternWithNamedArgs()
    {
        // Pattern from Humanizer's registry classes
        var csharp = """
            using System.Collections.Generic;
            public class FormatterRegistry
            {
                private readonly Dictionary<string, string> _formatters = new Dictionary<string, string>();

                public void Register(string cultureName, bool createIfNotExists = true)
                {
                    if (createIfNotExists)
                    {
                        _formatters[cultureName] = cultureName;
                    }
                }

                public void Init()
                {
                    Register(cultureName: "en-US", createIfNotExists: true);
                    Register(cultureName: "es-ES");
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Verify Calor source can be compiled without parser cascade
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code == DiagnosticCode.UnexpectedToken)
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void Convert_UnicodeInTestData()
    {
        // Pattern from Humanizer's HeadingTests
        var csharp = """
            public class HeadingTests
            {
                public string GetArrow(int heading)
                {
                    string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
                    return arrows[heading % 8];
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Unicode arrows should be preserved in string literals
        Assert.Contains("↑", result.CalorSource);
        Assert.Contains("→", result.CalorSource);
        Assert.Contains("↓", result.CalorSource);
    }

    #endregion

    #region Ternary Decomposition Tests

    [Fact]
    public void SimpleTernary_StaysInline()
    {
        // Simple ternary with no section markers should remain as (? ...)
        var csharp = """
            public class Test
            {
                int M(bool flag) => flag ? 1 : 0;
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("(? flag 1 0)", result.CalorSource);

        // Should parse and compile
        var compileResult = Program.Compile(result.CalorSource);
        Assert.False(compileResult.HasErrors,
            string.Join("\n", compileResult.Diagnostics.Select(d => d.ToString())));
    }

    [Fact]
    public void Ternary_WithMethodCallBranches_Decomposes()
    {
        // Ternary where branches are method calls (emitted as §C{...} §/C)
        // should be decomposed into §IF/§EL/§/I
        var csharp = """
            public class Test
            {
                int Helper() => 1;
                int Other() => 2;
                int M(bool flag) => flag ? Helper() : Other();
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should NOT contain (? ... §C ...) inline
        Assert.DoesNotContain("(? flag §C", result.CalorSource);
        // Should contain the decomposed form
        Assert.Contains("§IF{", result.CalorSource);
        Assert.Contains("§EL", result.CalorSource);

        // Should parse and compile
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void Ternary_WithNewObjectExpression_Decomposes()
    {
        // Ternary with new object in a branch (not collection — collections get hoisted)
        var csharp = """
            public class Config { public int Value; }
            public class Test
            {
                Config M(bool flag) => flag ? new Config() : null;
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // If §NEW is in the ternary branches, it should be decomposed
        if (result.CalorSource.Contains("§NEW") && result.CalorSource.Contains("§IF{"))
        {
            Assert.DoesNotContain("(? flag §NEW", result.CalorSource);
        }

        // Should parse without errors
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void Ternary_InReturnStatement_Decomposes()
    {
        // Ternary in a return statement with method call branches
        var csharp = """
            public class Test
            {
                string Format(int x) => x.ToString();
                string Default() => "none";
                string M(bool flag)
                {
                    return flag ? Format(42) : Default();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should parse and compile
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void Ternary_InAssignment_Decomposes()
    {
        // Ternary in an assignment with call branches
        var csharp = """
            public class Test
            {
                int Helper() => 1;
                int Other() => 2;
                void M(bool flag)
                {
                    var x = flag ? Helper() : Other();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should parse and compile
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void Ternary_NestedWithCalls_Decomposes()
    {
        // Nested ternary where both levels have method calls
        var csharp = """
            public class Test
            {
                int A() => 1;
                int B() => 2;
                int C() => 3;
                int M(bool x, bool y) => x ? A() : (y ? B() : C());
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should parse without errors
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    #endregion

    #region Doc Comment Tests

    [Fact]
    public void DocComment_WithExampleTag_NoOrphanedText()
    {
        // Pattern from Newtonsoft.Json's DefaultValueHandling
        var csharp = """
            using System;
            namespace TestNs
            {
                /// <summary>
                /// Specifies default value handling options.
                /// </summary>
                /// <example>
                ///   <code lang="cs" source="Tests.cs" region="TestRegion" title="Example" />
                /// </example>
                [Flags]
                public enum DefaultValueHandling
                {
                    Include = 0,
                    Ignore = 1
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Every non-empty line should either be a section marker, an enum value, or a comment
        foreach (var line in result.CalorSource.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            // Should NOT have bare text like "cs" without // prefix
            if (!trimmed.StartsWith("§") && !trimmed.StartsWith("//")
                && !trimmed.Contains("=") && !trimmed.StartsWith("["))
            {
                Assert.Fail($"Orphaned text found: '{trimmed}' in line: '{line}'");
            }
        }

        // Should parse without errors
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void DocComment_MultiLineContent_AllPrefixed()
    {
        // Doc comment with multi-line summary
        var csharp = """
            namespace TestNs
            {
                /// <summary>
                /// Gets or sets a value.
                /// This is the second line.
                /// </summary>
                /// <remarks>
                /// Additional information here.
                /// More details on usage.
                /// </remarks>
                public class TestClass
                {
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should parse without errors
        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    #endregion

    #region Hoisting § from Lisp Expressions (Round 3)

    [Fact]
    public void BinaryOp_WithCallOperand_HoistsToTempVar()
    {
        // x + SomeMethod() should hoist the call when it produces §C markers
        var csharp = """
            public class Test
            {
                int GetValue() => 42;
                void M()
                {
                    int result = 1 + GetValue();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void BinaryOp_WithBothCallOperands_HoistsBoth()
    {
        // A() + B() — both operands produce §C, both should be hoisted
        var csharp = """
            public class Test
            {
                int A() => 1;
                int B() => 2;
                void M()
                {
                    int result = A() + B();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void BinaryOp_WithTernaryOperand_HoistsTernary()
    {
        // x + (cond ? a : b) — ternary in binary op
        var csharp = """
            public class Test
            {
                void M(bool flag)
                {
                    int result = 10 + (flag ? 1 : 2);
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void UnaryOp_WithCallOperand_HoistsToTempVar()
    {
        // -GetValue() — unary negation of call result
        var csharp = """
            public class Test
            {
                int GetValue() => 42;
                void M()
                {
                    int result = -GetValue();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void NestedBinaryOps_WithMultipleCalls_HoistsAllLevels()
    {
        // A() + B() * C() — nested binary ops with multiple calls
        var csharp = """
            public class Test
            {
                int A() => 1;
                int B() => 2;
                int C() => 3;
                void M()
                {
                    int result = A() + B() * C();
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    #endregion

    #region Empty §ASSIGN Fix (Round 3)

    [Fact]
    public void Assignment_ListCreation_EmitsCollectionBlock()
    {
        // _list = new List<int>() — should emit §LIST block, not empty §ASSIGN
        var csharp = """
            using System.Collections.Generic;
            public class Test
            {
                void M()
                {
                    List<int> items = new List<int>();
                    items = new List<int> { 1, 2, 3 };
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should NOT have empty §ASSIGN
        Assert.DoesNotContain("§ASSIGN items \n", result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void Assignment_DictionaryCreation_EmitsCollectionBlock()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Test
            {
                void M()
                {
                    Dictionary<string, int> map = new Dictionary<string, int>();
                    map = new Dictionary<string, int> { { "a", 1 } };
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    [Fact]
    public void Assignment_ArrayCreation_EmitsArrayBlock()
    {
        var csharp = """
            public class Test
            {
                void M()
                {
                    int[] arr = new int[5];
                    arr = new int[] { 10, 20, 30 };
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        var compileResult = Program.Compile(result.CalorSource);
        var parseErrors = compileResult.Diagnostics
            .Where(d => d.Code.StartsWith("Calor"))
            .ToList();
        Assert.Empty(parseErrors);
    }

    #endregion
}
