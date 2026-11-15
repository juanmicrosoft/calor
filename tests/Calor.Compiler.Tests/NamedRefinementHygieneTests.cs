using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Calor.Compiler.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NamedRefinementHygieneTests
{

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgrammaticAst_CannotBypassHygiene(bool typeChecker)
    {
        var span = new TextSpan(17, 4, 2, 8);
        ExpressionNode[] expressions =
        [
            new ReferenceNode(span, "n"),
            new ThisExpressionNode(span),
            new FieldAccessNode(span, new ThisExpressionNode(span), "Minimum"),
            new FieldAccessNode(span, new SelfRefNode(span), "Minimum"),
            new CallExpressionNode(span, "predicate", [new SelfRefNode(span)]),
            new RawCSharpExpressionNode(span, "n > 0"),
            new UnaryOperationNode(span, UnaryOperator.PreIncrement, new SelfRefNode(span)),
            new ForallExpressionNode(span, [new QuantifierVariableNode(span, "Limits.Min", "i32")],
                new ReferenceNode(span, "Limits.Min")),
            new ForallExpressionNode(span, [new QuantifierVariableNode(span, "n", "ReceiverType")],
                new ReferenceNode(span, "n")),
            new LambdaExpressionNode(span, "l1",
                [new LambdaParameterNode(span, "n", "i32")], null, false,
                new ReferenceNode(span, "n"), null, new AttributeCollection())
        ];
        foreach (var expression in expressions)
        {
            var module = Parse("module Hygiene Hygiene block\n").With(update =>
                update.RefinementTypes =
                [new RefinementTypeNode(span, "r1", "Closed", "i32", expression, new AttributeCollection())]);
            var diagnostics = new DiagnosticBag();
            if (typeChecker)
                new TypeChecker(diagnostics).Check(module);
            else
                new ContractVerifier(diagnostics).Verify(module);
            var diagnostic = Assert.Single(diagnostics.Errors);
            Assert.Equal(DiagnosticCode.NamedRefinementNotClosed, diagnostic.Code);
            Assert.Equal(span, diagnostic.Span);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InlineCrossParameterPredicate_StillCompilesAndExecutes(bool typeCheck)
    {
        var result = Program.Compile("""
            module Hygiene Hygiene block
              func Take (int:n, int:x | (# > n)) → int
                !
                return x
            """, "hygiene.calr", Options(typeCheck));
        Assert.False(result.HasErrors, Errors(result));
        Assert.NotEmpty(result.GeneratedCode);
        var method = Load(result.GeneratedCode).GetType("Hygiene.HygieneModule")!.GetMethod("Take")!;
        Assert.Equal(2, method.Invoke(null, [1, 2]));
        Assert.IsType<ArgumentOutOfRangeException>(
            Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [2, 1])).InnerException);
    }

    private static string Source(string predicate) => $$"""
        §M{Hygiene}
          §RTYPE{Above:i32} {{predicate}}
          §F{Take:pub} (i32:n, Above:x) -> i32
            §E{}
            §R x
        """;

    private static CompilationResult Compile(string predicate, bool typeCheck = true)
        => Program.Compile(Source(predicate), "hygiene.calr", Options(typeCheck));

    private static CompilationOptions Options(bool typeCheck) => new()
    {
        EnableTypeChecking = typeCheck,
        StatusWriter = TextWriter.Null
    };

    private static string Errors(CompilationResult result) => string.Join("\n", result.Diagnostics.Errors);

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        return module;
    }

    private static Assembly Load(string code)
    {
        var compilation = CSharpCompilation.Create("Hygiene_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
