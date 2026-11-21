using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Xunit;

namespace Calor.Compiler.Tests;

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
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }
    #region Lexer Tests

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_ParsesRequiresContract()
    {
        var source = @"
module Test Test block
  func Square (int:x | (# >= 0)) → int
    !
    return (x * x)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        // #1618: the refinement on x is the function's precondition
        var requires = Assert.Single(Calor.Compiler.Verification.ContractVerifier.EffectivePreconditions(func.Parameters, func.Preconditions));
        Assert.Empty(func.Postconditions);
        Assert.IsType<BinaryOperationNode>(requires.Condition);
    }

    [Fact]
    public void Parser_ParsesEnsuresContract()
    {
        var source = @"
module Test Test block
  func Square (int:x) → int
    !
    => (result >= 0)
    return (x * x)
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
module Test Test block
  func Divide (int:a | (# >= 0), int:b | (# != 0)) → int
    !
    => (result >= 0)
    return (a / b)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        // #1618: one refinement per parameter, which the verifier sees as preconditions
        Assert.Equal(2, Calor.Compiler.Verification.ContractVerifier.EffectivePreconditions(func.Parameters, func.Preconditions).Count);
        Assert.Single(func.Postconditions);
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
        Assert.Contains("ContractViolationException", result);
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
        Assert.Contains("ContractViolationException", result);
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
module Test Test block
  func Square (int:x | (# >= 0)) → int
    !
    return (x * x)
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        // #1618: the refinement is emitted as a parameter entry check
        Assert.Contains("(x >= 0)", code);
        Assert.Contains("throw new ArgumentOutOfRangeException(nameof(x)", code);
    }

    [Fact]
    public void Emitter_EmitsFunctionWithPostcondition()
    {
        var source = @"
module Test Test block
  func Square (int:x) → int
    !
    => (result >= 0)
    return (x * x)
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("__calorPostconditionResult", code);
        Assert.Contains("throw new Calor.Runtime.ContractViolationException", code);
        Assert.Contains("return __calorPostconditionResult", code);
    }

    #endregion

    #region Contract Verifier Tests

    [Fact]
    public void ContractVerifier_AcceptsValidPrecondition()
    {
        var source = @"
module Test Test block
  func Square (int:x | (# >= 0)) → int
    !
    return (x * x)
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
