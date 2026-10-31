using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class TypeSystemTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Lexer Tests

    [Theory]
    [InlineData("Calor.Runtime.Option")]
    [InlineData("Calor.Runtime.Option`1")]
    [InlineData("Calor.Runtime.Result")]
    [InlineData("Calor.Runtime.Result`2")]
    public void RemovedRuntimeWrappers_AreNotExported(string name)
        => Assert.Null(typeof(Calor.Runtime.ContractViolationException).Assembly.GetType(name));

    [Theory]
    [InlineData("§SM")]
    [InlineData("§NN")]
    [InlineData("§OK")]
    [InlineData("§ERR")]
    public void Lexer_RejectsRemovedAbsenceAndErrorKeywords(string marker)
    {
        var tokens = Tokenize(marker, out var diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesMatchKeyword()
    {
        var tokens = Tokenize("§W", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Match, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEndMatchKeyword()
    {
        var tokens = Tokenize("§/W", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndMatch, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesCaseKeyword()
    {
        var tokens = Tokenize("§K", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Case, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Theory]
    [InlineData("D")]
    [InlineData("/D")]
    [InlineData("FL")]
    public void Lexer_RejectsRetiredRecordCreationMarkers(string marker)
    {
        var tokens = Tokenize($"§{marker}", out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Message.Contains("§NEW", StringComparison.Ordinal)
            && diagnostic.Message.Contains("§A[name]", StringComparison.Ordinal));
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RejectsDeadTypeKeyword()
    {
        var tokens = Tokenize("§T", out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RejectsDeadVariantKeyword()
    {
        var tokens = Tokenize("§V", out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    #endregion

    #region Parser Tests - Nullable Types

    [Fact]
    public void Parser_ParsesNullableValue()
    {
        var source = @"
§M{Test}
  §F{GetValue:pub}
      §O{?i32}
      §R INT:42
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Single(func.Body);

        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var intLiteral = Assert.IsType<IntLiteralNode>(returnStmt.Expression);
        Assert.Equal(42, intLiteral.Value);
    }

    [Fact]
    public void Parser_ParsesNull()
    {
        var source = @"
§M{Test}
  §F{GetNothing:pub}
      §O{?i32}
      §R null
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        Assert.Equal("null", Assert.IsType<ReferenceNode>(returnStmt.Expression).Name);
    }

    #endregion

    #region Emitter Tests - Records

    [Fact]
    public void Emitter_EmitsRecordCreation()
    {
        var recordCreation = new RecordCreationNode(
            TextSpan.Empty,
            "Person",
            new List<FieldAssignmentNode>
            {
                new(TextSpan.Empty, "Name", new StringLiteralNode(TextSpan.Empty, "Alice")),
                new(TextSpan.Empty, "Age", new IntLiteralNode(TextSpan.Empty, 30))
            });

        var emitter = new CSharpEmitter();
        var result = recordCreation.Accept(emitter);

        Assert.Equal("new Person(Name: \"Alice\", Age: 30)", result);
    }

    [Fact]
    public void Emitter_EmitsFieldAccess()
    {
        var fieldAccess = new FieldAccessNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "person"),
            "Name");

        var emitter = new CSharpEmitter();
        var result = fieldAccess.Accept(emitter);

        Assert.Equal("person.Name", result);
    }

    #endregion

    #region TypeMapper - Expanded Format Tests

    [Theory]
    [InlineData("NULLABLE[inner=INT]", "int?")]
    [InlineData("NULLABLE[inner=STRING]", "string?")]
    [InlineData("NULLABLE[inner=BOOL]", "bool?")]
    [InlineData("NULLABLE[inner=FLOAT]", "double?")]
    public void CalorToCSharp_ExpandedNullableFormat_MapsCorrectly(string input, string expected)
    {
        var result = Migration.TypeMapper.CalorToCSharp(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalorToCSharp_NestedNullableFormat_MapsCorrectly()
    {
        var result = Migration.TypeMapper.CalorToCSharp("NULLABLE[inner=NULLABLE[inner=INT]]");
        Assert.Equal("int??", result);
    }

    [Theory]
    [InlineData("INT[bits=8][signed=true]", "sbyte")]
    [InlineData("INT[bits=8][signed=false]", "byte")]
    [InlineData("INT[bits=16][signed=true]", "short")]
    [InlineData("INT[bits=16][signed=false]", "ushort")]
    [InlineData("INT[bits=32][signed=false]", "uint")]
    [InlineData("INT[bits=64][signed=true]", "long")]
    [InlineData("INT[bits=64][signed=false]", "ulong")]
    public void CalorToCSharp_ExpandedIntFormat_MapsCorrectly(string input, string expected)
    {
        var result = Migration.TypeMapper.CalorToCSharp(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("FLOAT[bits=32]", "float")]
    [InlineData("FLOAT[bits=64]", "double")]
    public void CalorToCSharp_ExpandedFloatFormat_MapsCorrectly(string input, string expected)
    {
        var result = Migration.TypeMapper.CalorToCSharp(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalorToCSharp_NullableWithExpandedIntInner_MapsCorrectly()
    {
        var result = Migration.TypeMapper.CalorToCSharp("NULLABLE[inner=INT[bits=64][signed=true]]");
        Assert.Equal("long?", result);
    }

    #endregion

    #region TypeMapper - Interface vs Concrete Collection Tests

    [Theory]
    [InlineData("IList", "IList")]
    [InlineData("IDictionary", "IDict")]
    [InlineData("ICollection", "ICollection")]
    [InlineData("ISet", "ISet")]
    [InlineData("List", "List")]
    [InlineData("Dictionary", "Dict")]
    [InlineData("HashSet", "Set")]
    [InlineData("IEnumerable", "Seq")]
    public void CSharpToCalor_CollectionTypes_PreservesInterfaceDistinction(string csharp, string calor)
    {
        var result = Migration.TypeMapper.CSharpToCalor(csharp);
        Assert.Equal(calor, result);
    }

    [Theory]
    [InlineData("IList", "IList")]
    [InlineData("IDict", "IDictionary")]
    [InlineData("ICollection", "ICollection")]
    [InlineData("ISet", "ISet")]
    [InlineData("List", "List")]
    [InlineData("Dict", "Dictionary")]
    [InlineData("Set", "HashSet")]
    [InlineData("Seq", "IEnumerable")]
    public void CalorToCSharp_CollectionTypes_PreservesInterfaceDistinction(string calor, string csharp)
    {
        var result = Migration.TypeMapper.CalorToCSharp(calor);
        Assert.Equal(csharp, result);
    }

    [Fact]
    public void CSharpToCalor_GenericIList_PreservesInterface()
    {
        var result = Migration.TypeMapper.CSharpToCalor("IList<int>");
        Assert.Equal("IList<i32>", result);
    }

    [Fact]
    public void CSharpToCalor_GenericList_StaysConcrete()
    {
        var result = Migration.TypeMapper.CSharpToCalor("List<int>");
        Assert.Equal("List<i32>", result);
    }

    [Fact]
    public void CalorToCSharp_GenericIList_RoundTrips()
    {
        var calor = Migration.TypeMapper.CSharpToCalor("IList<string>");
        Assert.Equal("IList<str>", calor);

        var csharp = Migration.TypeMapper.CalorToCSharp(calor);
        Assert.Equal("IList<string>", csharp);
    }

    [Fact]
    public void CSharpToCalor_GenericArrayType_MapsCorrectly()
    {
        var result = Migration.TypeMapper.CSharpToCalor("KeyValuePair<string, long>[]");
        Assert.Equal("KeyValuePair<str, i64>[]", result);
    }

    [Fact]
    public void CalorToCSharp_GenericArrayType_RoundTrips()
    {
        var calor = Migration.TypeMapper.CSharpToCalor("KeyValuePair<string, long>[]");
        var csharp = Migration.TypeMapper.CalorToCSharp(calor);
        Assert.Equal("KeyValuePair<string, long>[]", csharp);
    }

    #endregion

    #region End-to-End: Nullable Return Types

    [Fact]
    public void Emitter_NullableReturnType_EmitsNullable()
    {
        var source = """
            §M{Test}
              §F{FindValue:pub}
                  §I{i32:id}
                  §O{?i32}
                  §R INT:42
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("int?", csharp);
        Assert.DoesNotContain("NULLABLE[", csharp);
    }

    [Fact]
    public void Emitter_NullableStringReturnType_EmitsNullable()
    {
        var source = """
            §M{Test}
              §F{FindName:pub}
                  §O{?str}
                  §R STR:"hello"
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("string?", csharp);
        Assert.DoesNotContain("NULLABLE[", csharp);
    }

    [Fact]
    public void Emitter_NullableParameter_EmitsNullable()
    {
        var source = """
            §M{Test}
              §F{Process:pub}
                  §I{?i32:value}
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("int? value", csharp);
        Assert.DoesNotContain("NULLABLE[", csharp);
    }

    [Theory]
    [InlineData("i32!str")]
    [InlineData("str!str")]
    public void RemovedResultShorthand_DoesNotExpandOrCompile(string typeName)
    {
        var source = $$"""
            §M{Test}
              §F{TryParse:pub}
                  §I{str:input}
                  §O{{{typeName}}}
                  §R INT:42
            """;

        var module = Parse(source, out _);
        var output = Assert.Single(module.Functions).Output;
        Assert.NotNull(output);
        Assert.Contains("!", output.TypeName);
        Assert.DoesNotContain("RESULT[", output.TypeName);
        Assert.DoesNotContain("Result<", output.TypeName);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void Emitter_SizedIntReturnType_EmitsCorrectType()
    {
        var source = """
            §M{Test}
              §F{GetLong:pub}
                  §O{i64}
                  §R INT:100
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("long", csharp);
        Assert.DoesNotContain("INT[", csharp);
    }

    [Fact]
    public void Emitter_SizedFloatReturnType_EmitsFloat()
    {
        var source = """
            §M{Test}
              §F{GetFloat:pub}
                  §O{f32}
                  §R FLOAT:1.5
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("float", csharp);
        Assert.DoesNotContain("FLOAT[", csharp);
    }

    private static string FormatDiagnostics(DiagnosticBag diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}"));
    }

    #endregion
}
