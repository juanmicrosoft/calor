using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// The converter lowers <c>x op= y</c> itself when it has a semantic model. Without one it keeps
/// a compound store node, and the emitter writes that as the native plain store
/// <c>set y (y op v)</c>, never as the retired <c>§ASSIGN</c> form.
/// </summary>
public class CompoundAssignmentEmissionTests
{
    private const string CSharp = """
        class C
        {
            int F(int y, int a, int b)
            {
                y -= a - b;
                y <<= 2;
                return y;
            }
        }
        """;

    private static string ConvertWithoutSemanticModel()
    {
        var root = CSharpSyntaxTree.ParseText(CSharp).GetCompilationUnitRoot();
        var module = new RoslynSyntaxVisitor(new ConversionContext()).Convert(root, "M");
        return new CalorEmitter().Emit(module);
    }

    [Fact]
    public void UnloweredCompoundStores_ReachTheEmitter()
    {
        // Control: without a semantic model the AST really carries compound store nodes.
        var root = CSharpSyntaxTree.ParseText(CSharp).GetCompilationUnitRoot();
        var module = new RoslynSyntaxVisitor(new ConversionContext()).Convert(root, "M");

        var body = module.Classes.Single().Methods.Single().Body;
        Assert.Equal(2, body.OfType<CompoundAssignmentStatementNode>().Count());
    }

    [Fact]
    public void UnloweredCompoundStores_EmitNativeSetsThatParseBack()
    {
        var calor = ConvertWithoutSemanticModel();

        Assert.DoesNotContain('§', calor);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(calor, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(e => e.Message)) + "\n" + calor);

        var stores = module.Classes.Single().Methods.Single().Body.OfType<AssignmentStatementNode>().ToList();
        Assert.Equal(2, stores.Count);

        // y -= a - b  →  set y (y - (a - b)): the right-hand side stays grouped.
        var difference = Assert.IsType<BinaryOperationNode>(stores[0].Value);
        Assert.Equal(BinaryOperator.Subtract, difference.Operator);
        Assert.Equal("y", Assert.IsType<ReferenceNode>(difference.Left).Name);
        Assert.Equal(BinaryOperator.Subtract, Assert.IsType<BinaryOperationNode>(difference.Right).Operator);

        var shift = Assert.IsType<BinaryOperationNode>(stores[1].Value);
        Assert.Equal(BinaryOperator.LeftShift, shift.Operator);
    }
}
