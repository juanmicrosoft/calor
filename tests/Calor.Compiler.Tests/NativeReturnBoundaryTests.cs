using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeReturnBoundaryTests
{
    [Fact]
    public void BareReturn_LeavesFollowingCallAsASeparateStatement()
    {
        var diagnostics = new DiagnosticBag();
        var lambda = ParseLambda("() =>\n  return\n  Work()", diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Collection(lambda.StatementBody!,
            statement => Assert.Null(Assert.IsType<ReturnStatementNode>(statement).Expression),
            statement => Assert.IsType<CallExpressionNode>(
                Assert.IsType<ExpressionStatementNode>(statement).Expression));
    }

    [Fact]
    public void BareReturn_DoesNotConsumeNextLineEffectClause()
    {
        var diagnostics = new DiagnosticBag();
        var lambda = ParseLambda("() =>\n  return\n  ! cw", diagnostics);
        Assert.Null(Assert.IsType<ReturnStatementNode>(lambda.StatementBody![0]).Expression);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Return_RetainsSameLineUnaryOperand()
    {
        var diagnostics = new DiagnosticBag();
        var lambda = ParseLambda("() =>\n  return !flag", diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        var statement = Assert.IsType<ReturnStatementNode>(Assert.Single(lambda.StatementBody!));
        Assert.IsType<UnaryOperationNode>(statement.Expression);
    }

    private static LambdaExpressionNode ParseLambda(string source, DiagnosticBag diagnostics)
    {
        var parser = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics);
        return Assert.IsType<LambdaExpressionNode>(parser.ParseStandaloneExpression());
    }
}
