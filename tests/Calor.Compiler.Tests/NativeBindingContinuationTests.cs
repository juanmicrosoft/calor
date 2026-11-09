using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeBindingContinuationTests
{
    [Fact]
    public void DeepInitializerConsumesOnlyItsContinuationDedent()
    {
        var module = Parse("""
            module Continuation
              func First () → int
                let int:value =
                  1 + 2
                return value
              func Next () → int
                return 4
            """);
        Assert.Equal(2, module.Functions.Count);
        Assert.Equal(2, module.Functions[0].Body.Count);
        Assert.IsType<BinaryOperationNode>(Assert.IsType<BindStatementNode>(module.Functions[0].Body[0]).Initializer);
        Assert.IsType<ReturnStatementNode>(module.Functions[0].Body[1]);
    }

    [Fact]
    public void MissingInitializerDoesNotConsumeASibling()
    {
        var diagnostics = new DiagnosticBag();
        var module = Parse("""
            module Bad
              func F () → int
                let int:value =
                return 4
            """, diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Null(Assert.IsType<BindStatementNode>(module.Functions.Single().Body[0]).Initializer);
        Assert.IsType<ReturnStatementNode>(module.Functions.Single().Body[1]);
    }

    [Fact]
    public void MeasuredIndentedLambdaBindingEmitsReparsesAndRuns()
    {
        var module = Parse("""
            module Lambdas
              func Run (int:x) → int
                let Func<int,int>:f =
                  (int:y) =>
                    let int:z = y + 1
                    return z
                return f(x)
            """);
        Assert.Equal(2, module.Functions.Single().Body.Count);
        var binding = Assert.IsType<BindStatementNode>(module.Functions.Single().Body[0]);
        var lambda = Assert.IsType<LambdaExpressionNode>(binding.Initializer);
        Assert.NotNull(lambda.StatementBody);
        Assert.Equal(2, lambda.StatementBody.Count);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        Assert.Equal(2, Parse(emitted).Functions.Single().Body.Count);
        var generated = new CSharpEmitter().Emit(module);
        var compilation = CSharpCompilation.Create("CanonicalLambda" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(generated)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(bytes.ToArray());
        var method = Assert.Single(assembly.GetTypes().SelectMany(type => type.GetMethods())
            .Where(method => method.Name == "Run"));
        Assert.Equal(8, method.Invoke(null, [7]));
    }

    private static ModuleNode Parse(string source, DiagnosticBag? diagnostics = null)
    {
        var assertValid = diagnostics == null;
        diagnostics ??= new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        if (assertValid) Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
