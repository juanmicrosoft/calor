using System.Reflection;
using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class CharLiteralTests
{
    [Theory]
    [InlineData("'0'", '0')]
    [InlineData("'é'", 'é')]
    [InlineData("'\\''", '\'')]
    [InlineData("'\"'", '"')]
    [InlineData("'\\\"'", '"')]
    [InlineData("'\\\\'", '\\')]
    [InlineData("'\\0'", '\0')]
    [InlineData("'\\a'", '\a')]
    [InlineData("'\\b'", '\b')]
    [InlineData("'\\f'", '\f')]
    [InlineData("'\\n'", '\n')]
    [InlineData("'\\r'", '\r')]
    [InlineData("'\\t'", '\t')]
    [InlineData("'\\v'", '\v')]
    [InlineData("'\\x41'", 'A')]
    [InlineData("'\\u0041'", 'A')]
    [InlineData("'\\U00000041'", 'A')]
    [InlineData("'\\uD800'", '\uD800')]
    [InlineData("'\\uDFFF'", '\uDFFF')]
    [InlineData("'\\u2028'", '\u2028')]
    public void CharacterLiteral_LexesAndExecutesWithoutStringCoercion(string literal, char expected)
    {
        var diagnostics = new DiagnosticBag();
        var token = new Lexer(literal, diagnostics).TokenizeAllForParser().First();
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.CharLiteral, token.Kind);
        Assert.True(token.IsLiteral);
        Assert.Equal(expected, Assert.IsType<char>(token.Value));

        var source = $$"""
            §M{m1:Characters}
              §F{f1:Probe:pub} () -> char
                §E{}
                §B{value:char} {{literal}}
                §R value
            """;
        foreach (var text in RoundTrip(source))
            Assert.Equal(expected, Invoke(Compile(text), "CharactersModule", "Probe"));
    }

    [Theory]
    [InlineData("''")]
    [InlineData("'ab'")]
    [InlineData("'😀'")]
    [InlineData("'\\q'")]
    [InlineData("'\\u123'")]
    [InlineData("'\\x'")]
    [InlineData("'\\U0001F600'")]
    [InlineData("'x")]
    [InlineData("'\\")]
    [InlineData("'\n'")]
    [InlineData("'\r'")]
    public void MalformedCharacterLiteral_ReportsDiagnosticAndPreservesNextLine(string literal)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(literal + "\n§R 1", diagnostics).TokenizeAllForParser();
        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidCharLiteral);
        Assert.Contains(tokens, token => token.Kind == TokenKind.Error);
        Assert.Contains(tokens, token => token.Kind == TokenKind.Return);
    }

    [Fact]
    public void EveryUtf16CodeUnit_SurvivesCanonicalEmissionAndUtf8Transport()
    {
        var utf8 = new UTF8Encoding(false, true);
        var emitter = new CalorEmitter();
        for (var code = 0; code <= char.MaxValue; code++)
        {
            var value = (char)code;
            var node = new CharOperationNode(TextSpan.Empty, CharOp.CharLiteral,
                [new StringLiteralNode(TextSpan.Empty, value.ToString())]);
            var literal = node.Accept(emitter);
            literal = utf8.GetString(utf8.GetBytes(literal));
            var diagnostics = new DiagnosticBag();
            var token = new Lexer(literal, diagnostics).TokenizeAllForParser().First();
            Assert.False(diagnostics.HasErrors, $"U+{code:X4}: {literal}");
            Assert.Equal(TokenKind.CharLiteral, token.Kind);
            Assert.Equal(value, Assert.IsType<char>(token.Value));
        }
    }

    [Fact]
    public void NumericPromotionAndPatterns_PreserveCharacterSemantics()
    {
        const string source = """
            §M{m1:Characters}
              §F{f1:Offset:pub} (char:c) -> i32
                §E{}
                §R (- c '0')
              §F{f2:Sum:pub} () -> i32
                §E{}
                §R (+ '0' '1')
              §F{f3:IsDigit:pub} (char:c) -> bool
                §E{}
                §R (&& (>= c '0') (<= c '9'))
              §F{f4:Match:pub} (char:c) -> i32
                §E{}
                §R §W{w1} c
                  §K 'x' → 1
                  §K _ → 2
            """;
        foreach (var text in RoundTrip(source))
        {
            var assembly = Compile(text);
            Assert.Equal(7, Invoke(assembly, "CharactersModule", "Offset", '7'));
            Assert.Equal(97, Invoke(assembly, "CharactersModule", "Sum"));
            Assert.Equal(true, Invoke(assembly, "CharactersModule", "IsDigit", '5'));
            Assert.Equal(false, Invoke(assembly, "CharactersModule", "IsDigit", 'a'));
            Assert.Equal(1, Invoke(assembly, "CharactersModule", "Match", 'x'));
            Assert.Equal(2, Invoke(assembly, "CharactersModule", "Match", 'y'));
        }
    }

    [Fact]
    public void StringLiteral_RemainsDistinctFromCharacterLiteral()
    {
        var result = Program.Compile("""
            §M{m1:Characters}
              §F{f1:Offset:pub} (char:c) -> i32
                §E{}
                §R (- c "0")
            """, "characters.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("lpad", "007")]
    [InlineData("rpad", "700")]
    public void StringPadding_AcceptsCharacterArguments(string operation, string expected)
    {
        var source = $$"""
            §M{m1:Characters}
              §F{f1:Probe:pub} () -> str
                §E{}
                §R ({{operation}} "7" 3 '0')
            """;
        foreach (var text in RoundTrip(source))
            Assert.Equal(expected, Invoke(Compile(text), "CharactersModule", "Probe"));
    }

    [Theory]
    [InlineData("'x'")]
    [InlineData("(char-lit \"x\")")]
    [InlineData("(char-from-code 120)")]
    [InlineData("(char-code (char-lit \"x\"))")]
    public void CharacterValues_WidenToDecimal(string expression)
    {
        var source = $$"""
            §M{m1:Characters}
              §F{f1:Probe:pub} () -> decimal
                §E{}
                §B{value:decimal} {{expression}}
                §R value
            """;
        foreach (var text in RoundTrip(source))
            Assert.Equal(120m, Invoke(Compile(text), "CharactersModule", "Probe"));
    }

    [Fact]
    public void EnumInitializers_PreserveCharacterConstantsAndCasts()
    {
        const string source = """
            §M{m1:Characters}
              §EN{e1:Letters:pub}
                A = 'A'
                Newline = '\n'
                Cast = (int)'B'
                Sum = 'A' + 2
            """;
        foreach (var text in RoundTrip(source))
        {
            var type = Compile(text).GetTypes().Single(t => t.Name == "Letters");
            Assert.Equal(65, Convert.ToInt32(Enum.Parse(type, "A")));
            Assert.Equal(10, Convert.ToInt32(Enum.Parse(type, "Newline")));
            Assert.Equal(66, Convert.ToInt32(Enum.Parse(type, "Cast")));
            Assert.Equal(67, Convert.ToInt32(Enum.Parse(type, "Sum")));
        }
    }

    [Theory]
    [InlineData("(char-lit \"c\")")]
    [InlineData("'c'")]
    [InlineData("(- 'd' 1)")]
    public void LoopAttributes_PreserveCharacterBounds(string bound)
    {
        var source = $$"""
            §M{m1:Characters}
              §F{f1:Probe:pub} () -> i32
                §E{}
                §B{~sum:i32} 0
                §L{for1:i:0:{{bound}}:1}
                  §ASSIGN sum (+ sum i)
                §R sum
            """;
        foreach (var text in RoundTrip(source))
            Assert.Equal(4950, Invoke(Compile(text), "CharactersModule", "Probe"));
    }

    [Fact]
    public void CSharpAttributeArguments_PreserveCharacterType()
    {
        const string source = """
            §M{m1:Characters}
              §CL{c1:AttributeCarrier}
                §MT{mt1:Probe:pub:stat}[@System.ComponentModel.DefaultValue('x')]
                  §O{char}
                  §E{}
                  §R 'x'
            """;
        foreach (var text in RoundTrip(source))
        {
            var method = Compile(text).GetTypes().Single(t => t.Name == "AttributeCarrier").GetMethod("Probe")!;
            var attribute = method.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>()!;
            Assert.Equal('x', Assert.IsType<char>(attribute.Value));
        }
    }

    [Theory]
    [InlineData("'x'", "0", 120, 0)]
    [InlineData("(char-lit \"x\")", "0", 120, 0)]
    [InlineData("0", "'x'", 0, 120)]
    [InlineData("0", "(char-lit \"x\")", 0, 120)]
    public void MatchExpressions_WidenCharacterAndIntegerArms(
        string whenTrue, string whenFalse, int trueValue, int falseValue)
    {
        var source = $$"""
            §M{m1:Characters}
              §F{f1:Probe:pub} (bool:flag) -> i32
                §E{}
                §R §W{w1} flag
                  §K true → {{whenTrue}}
                  §K _ → {{whenFalse}}
            """;
        foreach (var text in RoundTrip(source))
        {
            var assembly = Compile(text);
            Assert.Equal(trueValue, Invoke(assembly, "CharactersModule", "Probe", true));
            Assert.Equal(falseValue, Invoke(assembly, "CharactersModule", "Probe", false));
        }
    }

    [Fact]
    public void CSharpMigration_PreservesOverloadsConstantsAndSurrogates()
    {
        const string source = """
            public static class CharMigration
            {
                public static int Pick(char c) => 1;
                public static int Pick(string s) => 2;
                public static int Probe() => Pick('x');
                public static char Surrogate() => '\uD800';
                public static int Classify(char c) => c switch { 'x' => 3, _ => 4 };
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Issues));
        Assert.DoesNotContain("§CSHARP", conversion.CalorSource);
        var utf8 = new UTF8Encoding(false, true);
        var calor = utf8.GetString(utf8.GetBytes(conversion.CalorSource!));
        var assembly = Compile(calor, enforceEffects: false);
        Assert.Equal(1, Invoke(assembly, "CharMigration", "Probe"));
        Assert.Equal('\uD800', Invoke(assembly, "CharMigration", "Surrogate"));
        Assert.Equal(3, Invoke(assembly, "CharMigration", "Classify", 'x'));
        Assert.Equal(4, Invoke(assembly, "CharMigration", "Classify", 'y'));
    }

    private static IEnumerable<string> RoundTrip(string source)
    {
        yield return source;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        yield return new CalorEmitter().Emit(module);
    }

    private static object? Invoke(Assembly assembly, string type, string method, params object?[] arguments) =>
        assembly.GetTypes().Single(t => t.Name == type).GetMethod(method)!.Invoke(null, arguments);

    private static Assembly Compile(string source, bool enforceEffects = true)
    {
        var result = Program.Compile(source, "characters.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = enforceEffects,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Characters_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
