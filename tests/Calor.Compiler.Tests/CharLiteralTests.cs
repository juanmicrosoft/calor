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
            module Characters Characters
              module CharactersModule
                func Probe public () → char
                  !
                  let char:value = {{literal}}
                  return value
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
        var tokens = new Lexer(literal + "\nreturn 1", diagnostics).TokenizeAllForParser();
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
            module Characters Characters
              module CharactersModule
                func Offset public (char:c) → int
                  !
                  return c - '0'
                func Sum public () → int
                  !
                  return '0' + '1'
                func IsDigit public (char:c) → bool
                  !
                  return c >= '0' && c <= '9'
                func Match public (char:c) → int
                  !
                  return match c
                    case 'x' → 1
                    case _ → 2
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
            module Characters Characters
              func Offset public (char:c) → int
                !
                return c - "0"
            """, "characters.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("lpad", "007")]
    [InlineData("rpad", "700")]
    public void StringPadding_AcceptsCharacterArguments(string operation, string expected)
    {
        var method = operation switch
        {
            "lpad" => "PadLeft",
            "rpad" => "PadRight",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unregistered padding operation.")
        };
        var source = $$"""
            module Characters Characters
              module CharactersModule
                func Probe public () → string
                  !
                  return "7".{{method}}(3, '0')
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
            module Characters Characters
              module CharactersModule
                func Probe public () → decimal
                  !
                  let decimal:value = {{CurrentCharacterExpression(expression)}}
                  return value
            """;
        foreach (var text in RoundTrip(source))
            Assert.Equal(120m, Invoke(Compile(text), "CharactersModule", "Probe"));
    }

    [Fact]
    public void EnumInitializers_PreserveCharacterConstantsAndCasts()
    {
        const string source = """
            module Characters Characters
              enum Letters public
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
            module Characters Characters
              module CharactersModule
                func Probe public () → int
                  !
                  var int:sum = 0
                  for i from 0 to {{CurrentCharacterExpression(bound)}} step 1
                    set sum sum + i
                  return sum
            """;
        foreach (var text in RoundTrip(source))
            Assert.Equal(4950, Invoke(Compile(text), "CharactersModule", "Probe"));
    }

    [Fact]
    public void CSharpAttributeArguments_PreserveCharacterType()
    {
        const string source = """
            module Characters Characters
              class AttributeCarrier
                func Probe [@System.ComponentModel.DefaultValue('x')] public static () → char
                  !
                  return 'x'
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
            module Characters Characters
              module CharactersModule
                func Probe public (bool:flag) → int
                  !
                  return match flag
                    case true → {{CurrentCharacterExpression(whenTrue)}}
                    case _ → {{CurrentCharacterExpression(whenFalse)}}
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
        RawFrameAssertions.AssertFramed(conversion.CalorSource, present: false);
        var utf8 = new UTF8Encoding(false, true);
        var calor = utf8.GetString(utf8.GetBytes(conversion.CalorSource!));
        var assembly = Compile(calor, enforceEffects: false);
        Assert.Equal(1, Invoke(assembly, "CharMigration", "Probe"));
        Assert.Equal('\uD800', Invoke(assembly, "CharMigration", "Surrogate"));
        Assert.Equal(3, Invoke(assembly, "CharMigration", "Classify", 'x'));
        Assert.Equal(4, Invoke(assembly, "CharMigration", "Classify", 'y'));
    }

    private static string CurrentCharacterExpression(string expression) => expression switch
    {
        "'x'" or "'c'" or "0" => expression,
        "(char-lit \"x\")" => "'x'",
        "(char-lit \"c\")" => "'c'",
        "(char-from-code 120)" => "(char)120",
        "(char-code (char-lit \"x\"))" => "(int)'x'",
        "(- 'd' 1)" => "'d' - 1",
        _ => throw new ArgumentOutOfRangeException(nameof(expression), expression, "Unregistered character expression.")
    };

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
