using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the Calor Extended Language Features.
/// Covers all 4 phases: Quick Wins, Core Features, Enhanced Contracts, and Future Extensions.
/// </summary>
public class ExtendedFeaturesTests
{
    [Theory]
    [InlineData("TD")]
    [InlineData("FX")]
    [InlineData("HK")]
    [InlineData("US")]
    [InlineData("/US")]
    [InlineData("UB")]
    [InlineData("/UB")]
    [InlineData("CX")]
    [InlineData("DC")]
    [InlineData("/DC")]
    [InlineData("CHOSEN")]
    [InlineData("REJECTED")]
    [InlineData("REASON")]
    [InlineData("CT")]
    [InlineData("/CT")]
    [InlineData("VS")]
    [InlineData("/VS")]
    [InlineData("HD")]
    [InlineData("/HD")]
    [InlineData("FC")]
    [InlineData("FILE")]
    [InlineData("LK")]
    [InlineData("AU")]
    [InlineData("TASK")]
    [InlineData("DATE")]
    public void RemovedMetadata_RequiresDocumentationComments(string marker)
    {
        var tokens = Tokenize("§" + marker, out var diagnostics);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, diagnostic.Code);
        Assert.Contains("///", diagnostic.Message);
        Assert.DoesNotContain(marker, SectionMarkerSuggestions.AllMarkers);

        Parse($"§M{{m1:Test}}\n  §F{{f1:Run:pub}} () -> void\n    §{marker}\n", out var parsedDiagnostics);
        Assert.Contains(parsedDiagnostics, d => d.Code == DiagnosticCode.UnknownSectionMarker
            && d.Message.Contains("///", StringComparison.Ordinal));
    }

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

    private static string Emit(ModuleNode module)
    {
        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    #region Phase 1: Quick Wins - Examples (§EX)

    [Fact]
    public void Lexer_RecognizesExampleKeyword()
    {
        var tokens = Tokenize("§EX", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Example, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesInlineExample()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Add:pub}
      §I{i32:a} §I{i32:b}
      §O{i32}
      §EX (+ 2 3) → 5
      §R (+ a b)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);
        var func = module.Functions[0];
        Assert.Single(func.Examples);
        Assert.IsType<ExampleNode>(func.Examples[0]);
    }

    [Fact]
    public void Parser_ParsesExampleWithId()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Add:pub}
      §I{i32:a} §I{i32:b}
      §O{i32}
      §EX{ex001} (+ 2 3) → 5
      §R (+ a b)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var example = module.Functions[0].Examples[0];
        Assert.Equal("ex001", example.Id);
    }

    [Fact]
    public void Emitter_EmitsExampleAsDebugAssert()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Add:pub}
      §I{i32:a} §I{i32:b}
      §O{i32}
      §EX (+ 2 3) → 5
      §R (+ a b)
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("Debug.Assert", code);
    }

    #endregion

    #region Phase 2: Core Features - Assumptions (§AS)

    [Fact]
    public void Lexer_RecognizesAssumeKeyword()
    {
        var tokens = Tokenize("§AS", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Assume, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesAssumeDeclaration()
    {
        var source = @"
§M{m001:Test}
  §AS{env} ""Database connection pool initialized""
  §F{f001:GetOrder:pub}
      §AS{data} ""orderId exists in database""
      §O{i32}
      §R 42
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Assumptions);
        Assert.Equal(AssumptionCategory.Env, module.Assumptions[0].Category);

        var func = module.Functions[0];
        Assert.Single(func.Assumptions);
        Assert.Equal(AssumptionCategory.Data, func.Assumptions[0].Category);
    }

    #endregion

    #region Phase 3: Enhanced Contracts - Versioning (§SN, §DP)

    [Fact]
    public void Lexer_RecognizesSinceKeyword()
    {
        var tokens = Tokenize("§SN", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Since, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesDeprecatedKeyword()
    {
        var tokens = Tokenize("§DP", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Deprecated, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesSinceDeclaration()
    {
        // Use quoted values for version
        var source = @"
§M{m001:Test}
  §F{f001:NewMethod:pub}
      §SN{""1.5.0""}
      §O{i32}
      §R 42
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Since);
        Assert.Equal("1.5.0", func.Since.Version);
    }

    [Fact]
    public void Parser_ParsesDeprecatedDeclaration()
    {
        // v2 positional format: {version:replacement}
        var source = @"
§M{m001:Test}
  §F{f001:OldMethod:pub}
      §DP{""1.5.0"":NewMethod}
      §O{i32}
      §R 42
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Deprecated);
        Assert.Equal("1.5.0", func.Deprecated.SinceVersion);
        Assert.Equal("NewMethod", func.Deprecated.Replacement);
    }

    [Fact]
    public void Emitter_EmitsDeprecatedAsObsoleteAttribute()
    {
        // v2 positional format: {version:replacement}
        var source = @"
§M{m001:Test}
  §F{f001:OldMethod:pub}
      §DP{""1.5.0"":NewMethod}
      §O{i32}
      §R 42
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("[System.Obsolete(", code);
        Assert.Contains("NewMethod", code);
    }

    #endregion

    #region Phase 4: Future Extensions - Properties (§PT)

    [Theory]
    [InlineData("forall", false)]
    [InlineData("Forall", true)]
    [InlineData("FORALL", true)]
    public void PropertyQuantifier_RequiresExactCase(string quantifier, bool hasError)
    {
        var module = Parse($$"""
            §M{m1:Properties}
              §F{f1:Check:pub} (i32:x, i32:y) -> bool
                §PT {{quantifier}} x,y: (== x y)
                §R true
            """, out var diagnostics);
        Assert.Equal(hasError, diagnostics.HasErrors);
        if (!hasError)
            Assert.Equal(new[] { "x", "y" }, module.Functions.Single().Properties.Single().Quantifiers);
        else
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Message.Contains("Use 'forall'"));
    }

    [Fact]
    public void Lexer_RecognizesPropertyKeyword()
    {
        var tokens = Tokenize("§PT", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.PropertyTest, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesPropertyDeclaration()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Reverse:pub}
      §PT (== 1 1)
      §O{i32}
      §R 42
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.Single(func.Properties);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Parser_ParsesComprehensiveExample()
    {
        // Updated to v2 positional format
        var source = @"
§M{m001:Demo}
  // TODO: Add examples

  §F{f001:Add:pub}
      §SN{""1.0.0""}
      §AS{data} ""Inputs are within i32 range""
      §I{i32:a} §I{i32:b}
      §O{i32}
      §EX (+ 2 3) → 5
      §EX (+ 0 0) → 0
      §R (+ a b)

  §F{f002:OldAdd:pub}
      §SN{""0.5.0""}
      §DP{""1.0.0"":Add}
      §I{i32:x} §I{i32:y}
      §O{i32}
      §R (+ x y)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal(2, module.Functions.Count);
        Assert.Empty(module.Issues);

        var addFunc = module.Functions[0];
        Assert.Equal("Add", addFunc.Name);
        Assert.NotNull(addFunc.Since);
        Assert.Null(addFunc.Complexity);
        Assert.Null(addFunc.Uses);
        Assert.Single(addFunc.Assumptions);
        Assert.Equal(2, addFunc.Examples.Count);

        var oldAddFunc = module.Functions[1];
        Assert.NotNull(oldAddFunc.Deprecated);
        Assert.Equal("Add", oldAddFunc.Deprecated.Replacement);
    }

    [Fact]
    public void Emitter_GeneratesValidCSharpWithExtendedFeatures()
    {
        // Updated to v2 positional format
        var source = @"
§M{m001:Demo}
  // TODO: Add examples

  §F{f001:Add:pub}
      §SN{""1.0.0""}
      §I{i32:a} §I{i32:b}
      §O{i32}
      §EX (+ 2 3) → 5
      §R (+ a b)

  §F{f002:OldAdd:pub}
      §DP{""1.0.0"":Add}
      §I{i32:x} §I{i32:y}
      §O{i32}
      §R (+ x y)
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);

        // Check various expected outputs
        Assert.Contains("namespace Demo", code);
        Assert.Contains("// SINCE: 1.0.0", code);
        Assert.Contains("Debug.Assert", code);
        Assert.Contains("[System.Obsolete(", code);
        Assert.Contains("public static int Add(", code);
        Assert.Contains("public static int OldAdd(", code);
    }

    #endregion
}
