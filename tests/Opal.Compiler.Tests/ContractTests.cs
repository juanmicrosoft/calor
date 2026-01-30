using Opal.Compiler.Ast;
using Opal.Compiler.CodeGen;
using Opal.Compiler.Diagnostics;
using Opal.Compiler.Parsing;
using Opal.Compiler.Verification;
using Xunit;

namespace Opal.Compiler.Tests;

public class ContractTests
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
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Lexer Tests

    [Fact]
    public void Lexer_RecognizesRequiresKeyword()
    {
        var tokens = Tokenize("§REQUIRES", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Requires, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEnsuresKeyword()
    {
        var tokens = Tokenize("§ENSURES", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Ensures, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesInvariantKeyword()
    {
        var tokens = Tokenize("§INVARIANT", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Invariant, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_ParsesRequiresContract()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Square}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §REQUIRES (>= x INT:0)
  §BODY
    §RETURN (* x x)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Single(func.Preconditions);
        Assert.Empty(func.Postconditions);

        var requires = func.Preconditions[0];
        Assert.IsType<BinaryOperationNode>(requires.Condition);
    }

    [Fact]
    public void Parser_ParsesEnsuresContract()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Square}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §ENSURES (>= result INT:0)
  §BODY
    §RETURN (* x x)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Empty(func.Preconditions);
        Assert.Single(func.Postconditions);

        var ensures = func.Postconditions[0];
        Assert.IsType<BinaryOperationNode>(ensures.Condition);
    }

    [Fact]
    public void Parser_ParsesMultipleContracts()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Divide}{visibility=public}
  §IN{name=a}{type=INT}
  §IN{name=b}{type=INT}
  §OUT{type=INT}
  §REQUIRES (!= b INT:0)
  §REQUIRES (>= a INT:0)
  §ENSURES (>= result INT:0)
  §BODY
    §RETURN (/ a b)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        Assert.Equal(2, func.Preconditions.Count);
        Assert.Single(func.Postconditions);
    }

    [Fact]
    public void Parser_ParsesContractWithMessage()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Square}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §REQUIRES{message=""x must be nonnegative""} (>= x INT:0)
  §BODY
    §RETURN (* x x)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var requires = func.Preconditions[0];
        Assert.Equal("x must be nonnegative", requires.Message);
    }

    #endregion

    #region Emitter Tests

    [Fact]
    public void Emitter_EmitsPreconditionCheck()
    {
        var requires = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var emitter = new CSharpEmitter();
        var result = requires.Accept(emitter);

        Assert.Contains("if (!", result);
        Assert.Contains("ArgumentException", result);
        Assert.Contains("(x >= 0)", result);
    }

    [Fact]
    public void Emitter_EmitsPostconditionCheck()
    {
        var ensures = new EnsuresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "result"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            null,
            new AttributeCollection());

        var emitter = new CSharpEmitter();
        var result = ensures.Accept(emitter);

        Assert.Contains("if (!", result);
        Assert.Contains("InvalidOperationException", result);
        Assert.Contains("(result >= 0)", result);
    }

    [Fact]
    public void Emitter_EmitsContractWithCustomMessage()
    {
        var requires = new RequiresNode(
            TextSpan.Empty,
            new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new IntLiteralNode(TextSpan.Empty, 0)),
            "x must be non-negative",
            new AttributeCollection());

        var emitter = new CSharpEmitter();
        var result = requires.Accept(emitter);

        Assert.Contains("x must be non-negative", result);
    }

    [Fact]
    public void Emitter_EmitsFunctionWithPrecondition()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Square}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §REQUIRES (>= x INT:0)
  §BODY
    §RETURN (* x x)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        // The emitter wraps the binary expression in parentheses
        Assert.Contains("(x >= 0)", code);
        Assert.Contains("throw new ArgumentException", code);
    }

    [Fact]
    public void Emitter_EmitsFunctionWithPostcondition()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Square}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §ENSURES (>= result INT:0)
  §BODY
    §RETURN (* x x)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("__result__", code);
        Assert.Contains("throw new InvalidOperationException", code);
        Assert.Contains("return __result__", code);
    }

    #endregion

    #region Contract Verifier Tests

    [Fact]
    public void ContractVerifier_AcceptsValidPrecondition()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Square}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §REQUIRES (>= x INT:0)
  §BODY
    §RETURN (* x x)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);

        var verifyDiags = new DiagnosticBag();
        var verifier = new ContractVerifier(verifyDiags);
        verifier.Verify(module);

        // Should have no errors (warnings about unknown references are ok)
        Assert.False(verifyDiags.HasErrors);
    }

    #endregion
}
