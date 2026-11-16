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

    #endregion

    #region Phase 2: Core Features - Assumptions (§AS)

    [Fact]
    public void Lexer_RecognizesAssumeKeyword()
    {
        var tokens = Tokenize("assume", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Assume, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesAssumeDeclaration()
    {
        var source = @"
module Test Test block
  assume{env} ""Database connection pool initialized""
  func GetOrder () → int
    !
    assume{data} ""orderId exists in database""
    return 42
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
    public void Parser_ParsesSinceDeclaration()
    {
        // Use quoted values for version
        var source = @"
module Test Test block
  /// since 1.5.0
  func NewMethod () → int
    !
    return 42
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
module Test Test block
  /// deprecated 1.5.0 NewMethod
  func OldMethod () → int
    !
    return 42
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
module Test Test block
  /// deprecated 1.5.0 NewMethod
  func OldMethod () → int
    !
    return 42
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("[System.Obsolete(", code);
        Assert.Contains("NewMethod", code);
    }
    #endregion
    #region Phase 4: Future Extensions - Properties (§PT)
    #endregion
    #region Integration Tests

    #endregion
}
