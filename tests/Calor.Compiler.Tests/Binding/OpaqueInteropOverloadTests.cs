using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using DiagnosticSeverity = Calor.Compiler.Diagnostics.DiagnosticSeverity;

namespace Calor.Compiler.Tests;

public class OpaqueInteropOverloadTests
{
    [Theory]
    [InlineData("Write", false, false)]
    [InlineData("this.Write", false, false)]
    [InlineData("Write", true, false)]
    [InlineData("this.Write", true, false)]
    [InlineData("Write", false, true)]
    [InlineData("this.Write", false, true)]
    [InlineData("Write", true, true)]
    [InlineData("this.Write", true, true)]
    [InlineData("<structural-this>", false, false)]
    [InlineData("<structural-this>", true, false)]
    [InlineData("<structural-this>", false, true)]
    [InlineData("<structural-this>", true, true)]
    public void ApplicableNativeGeneric_WithPreservedParamsCompetitor_IsNotAuthoritative(
        string target, bool statement, bool preprocessor)
    {
        const string preserved = "public int Write(System.Exception? exception, params object?[]? values) => 1;";
        var opaque = $"§CSHARP{{{preserved}}}§/CSHARP";
        if (preprocessor)
            opaque = "§PP{true}\n      " + opaque;
        var source = $$"""
            §M{TestModule}
              §CL{Logger:pub}
                {{opaque}}
                §MT{Write<T>:pub} (?System.Exception:exception, T:values) -> i32
                  §E{alloc}
                  §B{allocated:[i32]} §ARR{i32:1}
                  §R 2
                §MT{Probe:pub} (?[?object]:values) -> i32
                  §E{}
                  {{(statement ? "" : "§R ")}}§C{ {{(target == "<structural-this>" ? "this.Write" : target)}} } §A (cast System.Exception? null) §A values §/C
                  {{(statement ? "§R 0" : "")}}
            """;
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        if (target == "<structural-this>")
        {
            ast = WithStructuralReceiver(ast, statement, new ThisExpressionNode(ast.Span));
            target = "<expression-call>";
        }
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var probe = bound.Functions.Single(function => function.Symbol.Name == "Logger.Probe");
        var expression = BoundNodeHelpers.DescendantsAndSelf(probe).OfType<BoundCallExpression>().SingleOrDefault();
        var callStatement = probe.Body.OfType<BoundCallStatement>().SingleOrDefault();
        Assert.Null(expression?.ResolvedSymbol ?? callStatement?.ResolvedSymbol);
        Assert.Empty(expression?.ResolvedSymbols ?? callStatement!.ResolvedSymbols);
        Assert.Empty(expression?.SelectedOverloadMatches ?? callStatement!.SelectedOverloadMatches);
        Assert.True(expression?.HasOpaqueOverloads ?? callStatement!.HasOpaqueOverloads);
        Assert.Null(expression?.ResolvedMetadataMethod);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[probe.SymbolId]);
        var unresolved = Assert.Single(graph.UnresolvedCalls.Where(call => call.Caller == probe.SymbolId));
        Assert.Equal(target, unresolved.Target);
        var astGraph = CallGraphAnalysis.Build(ast);
        var astProbe = ast.Classes.Single().Methods.Single(method => method.Name == "Probe");
        Assert.True(astGraph.IsBoundResolutionComplete);
        Assert.Empty(astGraph.ResolveCallSites($"Logger.{astProbe.Id}", target, unresolved.Span));
        Assert.Contains(astGraph.UnresolvedCalls, call =>
            call.CallerId == $"Logger.{astProbe.Id}" && call.Target == target && call.Span == unresolved.Span);
        var effects = new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true);
        effects.Enforce(ast);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Span == unresolved.Span
            && diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = new CSharpEmitter().Emit(ast);
        Assert.Contains(preserved, generated);
        var compilation = CSharpCompilation.Create("Opaque_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + generated)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var tree = compilation.SyntaxTrees.Single();
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());
        var method = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol);
        Assert.False(method.IsGenericMethod);
        Assert.True(method.Parameters[^1].IsParams);
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        var logger = System.Reflection.Assembly.Load(stream.ToArray()).GetType("TestModule.Logger", throwOnError: true)!;
        var instance = Activator.CreateInstance(logger);
        Assert.Equal(statement ? 0 : 1, logger.GetMethod("Probe")!.Invoke(instance, [new object?[] { "item" }]));
        Assert.Equal(statement ? 0 : 1, logger.GetMethod("Probe")!.Invoke(instance, [null]));
    }

    private static ModuleNode WithStructuralReceiver(ModuleNode module, bool statement, ExpressionNode receiver)
    {
        var cls = Assert.Single(module.Classes);
        var probe = cls.Methods.Single(method => method.Name == "Probe");
        var arguments = statement
            ? Assert.IsType<CallStatementNode>(probe.Body[0]).Arguments
            : Assert.IsType<CallExpressionNode>(Assert.IsType<ReturnStatementNode>(probe.Body[0]).Expression).Arguments;
        var call = new ExpressionCallNode(probe.Body[0].Span,
            new FieldAccessNode(probe.Body[0].Span, receiver, "Write"), arguments);
        StatementNode[] body = statement
            ? [new ExpressionStatementNode(call.Span, call), probe.Body[1]]
            : [new ReturnStatementNode(call.Span, call)];
        var rewrittenProbe = new MethodNode(probe.Span, probe.Id, probe.Name, probe.Visibility, probe.Modifiers,
            probe.TypeParameters, probe.Parameters, probe.Output, probe.Effects, probe.Preconditions,
            probe.Postconditions, body, probe.Attributes);
        var rewrittenClass = new ClassDefinitionNode(cls.Span, cls.Id, cls.Name,
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null, [], [], [], [], [],
            cls.Methods.Select(method => ReferenceEquals(method, probe) ? rewrittenProbe : method).ToArray(),
            [], [], cls.Attributes, [], visibility: cls.Visibility,
            interopBlocks: cls.InteropBlocks, preprocessorBlocks: cls.PreprocessorBlocks);
        return new ModuleNode(module.Span, module.Id, module.Name, [], [], [rewrittenClass], [], module.Attributes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralUnknownReceiver_IsNotTreatedAsCurrentInstance(bool statement)
    {
        var source = $$"""
            §M{TestModule}
              §CL{Logger:pub}
                §CSHARP{public int Write(System.Exception? exception, params object?[]? values) => 1;}§/CSHARP
                §MT{Write<T>:pub} (?System.Exception:exception, T:values) -> i32
                  §R 2
                §MT{Probe:pub} (?[?object]:values) -> i32
                  {{(statement ? "" : "§R ")}}§C{this.Write} §A (cast System.Exception? null) §A values §/C
                  {{(statement ? "§R 0" : "")}}
            """;
        var diagnostics = new DiagnosticBag();
        var parsed = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var ast = WithStructuralReceiver(parsed, statement, new ReferenceNode(parsed.Span, "unknownReceiver"));
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var probe = bound.Functions.Single(function => function.Symbol.Name == "Logger.Probe");
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(probe).OfType<BoundCallExpression>());
        Assert.False(call.HasOpaqueOverloads);
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[probe.SymbolId]);
        var unresolved = Assert.Single(graph.UnresolvedCalls.Where(site => site.Caller == probe.SymbolId));
        Assert.Equal("<expression-call>", unresolved.Target);
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Span == unresolved.Span
            && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("public int Pick(params object[] values) => 1;", true, false)]
    [InlineData("public int Pick<T,U>(T value, U other) => 1;", true, false)]
    [InlineData("public int Pick<T>(params T[] values) => 1;", true, true)]
    [InlineData("int IFoo.Pick(int value) => 1;", false, false)]
    public void PreservedCandidates_RespectExplicitArityAndInterfaceExclusions(
        string preserved, bool explicitGeneric, bool opaque)
    {
        var source = $$"""
            §M{TestModule}
              §CL{Value:pub}
                §CSHARP{ {{preserved}} }§/CSHARP
                §MT{Pick<T>:pub} (T:value) -> i32
                  §R 2
                §MT{Probe:pub} (i32:value) -> i32
                  §R §C{ {{(explicitGeneric ? "Pick<i32>" : "Pick")}} } §A value §/C
            """;
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "Value.Probe")).OfType<BoundCallExpression>());
        Assert.Equal(opaque, call.HasOpaqueOverloads);
        if (opaque)
        {
            Assert.Null(call.ResolvedSymbol);
            Assert.Empty(call.SelectedOverloadMatches);
        }
        else
        {
            Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "Value.Pick").Symbol,
                call.ResolvedSymbol);
            Assert.Single(call.SelectedOverloadMatches);
        }
    }

    [Fact]
    public void InapplicableExplicitGeneric_DoesNotFallBackToPreservedNonGeneric()
    {
        const string source = """
            §M{TestModule}
              §CL{Value:pub}
                §CSHARP{public int Pick(string value) => 1;}§/CSHARP
                §MT{Pick<T>:pub} (T:value) -> i32
                  §R 2
                §MT{Probe:pub} () -> i32
                  §R §C{Pick<i32>} §A "wrong" §/C
            """;
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        _ = new Binder(diagnostics).Bind(ast);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void PreprocessorInteropMethod_KeepsSameNameCallOpaque()
    {
        const string source = """
            §M{TestModule}
              §CL{Value:pub}
                §PP{true}
                  §CSHARP{public string Format(string? format, object? provider) => "";}§/CSHARP
                §/PP{true}
                §MT{Format:pub} () -> string
                  §R §C{Format} §A null §A null §/C
            """;

        var result = Program.Compile(
            source,
            "interop-overload.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                DeferGeneratedOutputValidation = true,
            });

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.NoMatchingOverload or DiagnosticCode.AmbiguousOverload);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(diagnostic => diagnostic.ToString())));
        Assert.Contains("Format(null, null)", result.GeneratedCode);
        Assert.Contains("Format(string? format, object? provider)", result.GeneratedCode);
        var validation = GeneratedCSharpCompiler.Validate(result.GeneratedCode);
        Assert.True(validation.SyntaxSuccess && validation.CompilationSuccess,
            string.Join("\n", validation.SyntaxErrors.Concat(validation.CompilationErrors)));
    }

    [Theory]
    [InlineData("Format")]
    [InlineData("this.Format")]
    public void ExplicitInterfaceInteropMethod_DoesNotHideNoMatchingOverload(string target)
    {
        var source = $$"""
            §M{TestModule}
              §CL{Value:pub}
                §CSHARP{string IFoo.Format(string value) => value;}§/CSHARP
                §MT{Format:pub} () -> string
                  §R ""
                §MT{Probe:pub} () -> string
                  §R §C{ {{target}} } §A "value" §/C
            """;

        var result = Program.Compile(
            source,
            "explicit-interface-overload.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                DeferGeneratedOutputValidation = true,
            });

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }
}
