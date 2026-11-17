using System.Text;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
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
        var opaque = RawMember(preserved);
        if (preprocessor)
            opaque = "pp true\n      " + opaque;
        var source = $$"""
            module TestModule TestModule
              class Logger
                {{opaque}}
                func Write<T> public (System.Exception?:exception, T:values) → int
                  ! alloc
                  let int[]:allocated = new int[1]
                  return 2
                func Probe public (object?[]?:values) → int
                  !
                  {{(statement ? "" : "return ")}}{{(target == "<structural-this>" ? "this.Write" : target)}}((System.Exception?)null, values)
                  {{(statement ? "return 0" : "")}}
            """;
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source, diagnostics);
        if (target == "<structural-this>")
            ast = WithStructuralReceiver(ast, statement, new ThisExpressionNode(ast.Span));
        var expectedTarget = target == "Write" ? "Write" : "<expression-call>";
        AssertOpaqueParamsCall(ast, expectedTarget, statement, preserved);

        if (target != "<structural-this>" && (statement || target == "this.Write"))
        {
            var probe = ast.Classes.Single().Methods.Single(method => method.Name == "Probe");
            var original = statement
                ? Assert.IsType<ExpressionStatementNode>(probe.Body[0]).Expression
                : Assert.IsType<ReturnStatementNode>(probe.Body[0]).Expression!;
            var arguments = original is CallExpressionNode named
                ? named.Arguments
                : Assert.IsType<ExpressionCallNode>(original).Arguments;
            StatementNode[] sdkBody = statement
                ? [new CallStatementNode(original.Span, target, false, arguments, new AttributeCollection()),
                    probe.Body[1]]
                : [new ReturnStatementNode(original.Span, new CallExpressionNode(original.Span, target, arguments))];
            AssertOpaqueParamsCall(WithProbeBody(ast, sdkBody), target, statement, preserved);
        }
    }

    private static void AssertOpaqueParamsCall(ModuleNode ast, string expectedTarget, bool statement, string preserved)
    {
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var probe = bound.Functions.Single(function => function.Symbol.Name == "TestModule.Logger.Probe");
        var expression = BoundNodeHelpers.DescendantsAndSelf(probe).OfType<BoundCallExpression>().SingleOrDefault();
        var callStatement = probe.Body.OfType<BoundCallStatement>().SingleOrDefault();
        Assert.True(expression != null || callStatement != null);
        Assert.Null(expression?.ResolvedSymbol ?? callStatement?.ResolvedSymbol);
        Assert.Empty(expression?.ResolvedSymbols ?? callStatement!.ResolvedSymbols);
        Assert.Empty(expression?.SelectedOverloadMatches ?? callStatement!.SelectedOverloadMatches);
        Assert.True(expression?.HasOpaqueOverloads ?? callStatement!.HasOpaqueOverloads);
        Assert.Null(expression?.ResolvedMetadataMethod);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[probe.SymbolId]);
        var unresolved = Assert.Single(graph.UnresolvedCalls.Where(call => call.Caller == probe.SymbolId));
        Assert.Equal(expectedTarget, unresolved.Target);
        var astGraph = CallGraphAnalysis.Build(ast);
        var astProbe = ast.Classes.Single().Methods.Single(method => method.Name == "Probe");
        Assert.True(astGraph.IsBoundResolutionComplete);
        Assert.Empty(astGraph.ResolveCallSites($"Logger.{astProbe.Id}", expectedTarget, unresolved.Span));
        Assert.Contains(astGraph.UnresolvedCalls, call =>
            call.CallerId == $"Logger.{astProbe.Id}" && call.Target == expectedTarget && call.Span == unresolved.Span);
        var effects = new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true);
        effects.Enforce(ast);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Span == unresolved.Span
            && diagnostic.Severity == DiagnosticSeverity.Error);
        var recompiled = Program.Compile(new CalorEmitter().Emit(ast), "opaque-overload-roundtrip.calr",
            new CompilationOptions { UnknownCallPolicy = UnknownCallPolicy.Strict, StrictEffects = true });
        Assert.Contains(recompiled.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Severity == DiagnosticSeverity.Error);

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
        var originalCall = statement
            ? Assert.IsType<ExpressionStatementNode>(probe.Body[0]).Expression
            : Assert.IsType<ReturnStatementNode>(probe.Body[0]).Expression!;
        var arguments = originalCall is CallExpressionNode named
            ? named.Arguments
            : Assert.IsType<ExpressionCallNode>(originalCall).Arguments;
        var call = new ExpressionCallNode(probe.Body[0].Span,
            new FieldAccessNode(probe.Body[0].Span, receiver, "Write"), arguments);
        StatementNode[] body = statement
            ? [new ExpressionStatementNode(call.Span, call), probe.Body[1]]
            : [new ReturnStatementNode(call.Span, call)];
        return WithProbeBody(module, body);
    }

    private static ModuleNode WithProbeBody(ModuleNode module, IReadOnlyList<StatementNode> body)
    {
        var cls = Assert.Single(module.Classes);
        var probe = cls.Methods.Single(method => method.Name == "Probe");
        var rewrittenProbe = probe.CopyMetadataTo(new MethodNode(probe.Span, probe.Id, probe.Name, probe.Visibility, probe.Modifiers,
            probe.TypeParameters, probe.Parameters, probe.Output, probe.Effects, probe.Preconditions,
            probe.Postconditions, body, probe.Attributes));
        var rewrittenClass = cls.CopyMetadataTo(new ClassDefinitionNode(cls.Span, cls.Id, cls.Name,
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null, [], [], [], [], [],
            cls.Methods.Select(method => ReferenceEquals(method, probe) ? rewrittenProbe : method).ToArray(),
            [], [], cls.Attributes, [], visibility: cls.Visibility,
            interopBlocks: cls.InteropBlocks, preprocessorBlocks: cls.PreprocessorBlocks));
        return module.With(update => update.Classes = [rewrittenClass]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralUnknownReceiver_IsNotTreatedAsCurrentInstance(bool statement)
    {
        var source = $$"""
            module TestModule TestModule
              class Logger
                {{RawMember("public int Write(System.Exception? exception, params object?[]? values) => 1;")}}
                func Write<T> public (System.Exception?:exception, T:values) → int
                  !
                  return 2
                func Probe public (object?[]?:values) → int
                  !
                  {{(statement ? "" : "return ")}}this.Write((System.Exception?)null, values)
                  {{(statement ? "return 0" : "")}}
            """;
        var diagnostics = new DiagnosticBag();
        var parsed = Parse(source, diagnostics);
        var ast = WithStructuralReceiver(parsed, statement, new ReferenceNode(parsed.Span, "unknownReceiver"));
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var probe = bound.Functions.Single(function => function.Symbol.Name == "TestModule.Logger.Probe");
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
            module TestModule TestModule
              class Value
                {{RawMember(preserved)}}
                func Pick<T> public (T:value) → int
                  !
                  return 2
                func Probe public (int:value) → int
                  !
                  return {{(explicitGeneric ? "Pick<int>" : "Pick")}}(value)
            """;
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source, diagnostics);
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions.Single(function =>
            function.Symbol.Name == "TestModule.Value.Probe")).OfType<BoundCallExpression>());
        Assert.Equal(opaque, call.HasOpaqueOverloads);
        if (opaque)
        {
            Assert.Null(call.ResolvedSymbol);
            Assert.Empty(call.SelectedOverloadMatches);
        }
        else
        {
            Assert.Same(bound.Functions.Single(function => function.Symbol.Name == "TestModule.Value.Pick").Symbol,
                call.ResolvedSymbol);
            Assert.Single(call.SelectedOverloadMatches);
        }
    }

    [Fact]
    public void InapplicableExplicitGeneric_DoesNotFallBackToPreservedNonGeneric()
    {
        var source = $$"""
            module TestModule TestModule
              class Value
                {{RawMember("public int Pick(string value) => 1;")}}
                func Pick<T> public (T:value) → int
                  !
                  return 2
                func Probe public () → int
                  !
                  return Pick<int>("wrong")
            """;
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source, diagnostics);
        _ = new Binder(diagnostics).Bind(ast);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void PreprocessorInteropMethod_KeepsSameNameCallOpaque()
    {
        var source = $$"""
            module TestModule TestModule
              class Value
                pp true
                  {{RawMember("""public string Format(string? format, object? provider) => "";""")}}
                func Format public () → string
                  !
                  return Format(null, null)
            """;
        _ = Parse(source, new DiagnosticBag());

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
            module TestModule TestModule
              class Value
                {{RawMember("string IFoo.Format(string value) => value;")}}
                func Format public () → string
                  !
                  return ""
                func Probe public () → string
                  !
                  return {{target}}("value")
            """;
        _ = Parse(source, new DiagnosticBag());

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

    private static string RawMember(string payload)
    {
        // A synthetic SDK boundary, not a claim of converter-origin evidence.
        var frame = new RawCSharpFrame(payload, "method",
            "Synthetic SDK preservation boundary", InteropMemberKind.Method);
        return $"raw {frame.ReasonMarker} {Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
    }

    private static ModuleNode Parse(string source, DiagnosticBag diagnostics)
    {
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var reparseDiagnostics = new DiagnosticBag();
        var emitted = new CalorEmitter().Emit(module);
        var reparsed = new Parser(new Lexer(emitted, reparseDiagnostics).TokenizeAllForParser(), reparseDiagnostics).Parse();
        Assert.False(reparseDiagnostics.HasErrors, string.Join("\n", reparseDiagnostics));
        Assert.Equal(new CSharpEmitter().Emit(module), new CSharpEmitter().Emit(reparsed));
        return module;
    }
}
