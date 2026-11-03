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
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class ComputedCallExpressionTests
{
    [Fact]
    public void ComputedMembers_RetainOverloadIdentityAndOrderedReceiverChildren()
    {
        var (ast, bound, diagnostics) = Bind("""
            §M{Test}
              §F{Use:pub} (Holder:h) -> i32
                §R §C §C{Get} §A h §/C.Pick §A INT:3 §/C
              §F{Get:pub} (Holder:h) -> Holder
                §R h
              §CL{Holder:pub}
                §MT{Pick:pub} (i32:value) -> i32
                  §R value
                §MT{Pick:pub} (str:value) -> str
                  §R value
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var outer = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(use.Body)).Expression);
        var inner = Assert.IsType<BoundCallExpression>(outer.EvaluatedReceiver);
        Assert.Same(inner, outer.Receiver);
        Assert.Same(inner, outer.Children[0]);
        Assert.Same(outer.Arguments[0], outer.Children[1]);
        Assert.Same(bound.Functions.Single(function =>
            function.Symbol.Name == "Holder.Pick" && function.Symbol.ReturnType == "i32").Symbol, outer.ResolvedSymbol);
        Assert.Equal(2, CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId].Count);
        Assert.Contains($"Holder.{ast.Classes[0].Methods[0].Id}",
            CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "<expression-call>", outer.Span));
    }

    [Fact]
    public void ComputedMember_InvalidArgumentStillReportsNativeOverloadError()
    {
        var (_, _, diagnostics) = Bind("""
            §M{Test}
              §F{Use:pub} (Holder:h) -> i32
                §R §C §C{Get} §A h §/C.Pick §A BOOL:true §/C
              §F{Get:pub} (Holder:h) -> Holder
                §R h
              §CL{Holder:pub}
                §MT{Pick:pub} (i32:value) -> i32
                  §R value
            """);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void ComputedMetadataArrayCall_RetainsExactSelectedMethodAndArrayResult()
    {
        var (_, bound, diagnostics) = Bind("""
            §M{Test}
              §F{Use:pub} () -> [str]
                §R §C §NEW{System.Globalization.CultureInfo} §A "en-US" §/NEW.DateTimeFormat.GetAllDateTimePatterns §/C
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.Equal("GetAllDateTimePatterns", call.ResolvedMetadataMethod?.Name);
        Assert.Equal("System.Globalization.DateTimeFormatInfo", call.ResolvedMetadataMethod?.ContainingType.ToDisplayString());
        Assert.Same(call.ResolvedMetadataMethod!.ReturnType, call.MetadataArrayResultType);
        Assert.Equal(SpecialType.System_String, call.MetadataArrayResultType!.ElementType.SpecialType);
    }

    [Fact]
    public void GenericComputedMetadataCall_RetainsExplicitTypesAndExactOverload()
    {
        var node = new ExpressionCallNode(default,
            new FieldAccessNode(default, new NewExpressionNode(default, "List", ["str"], []), "ToArray"), []);
        var function = new FunctionNode(default, "use", "Use", Visibility.Public, [],
            new OutputNode(default, "object"), null, [new ReturnStatementNode(default, node)], new AttributeCollection());
        var module = new ModuleNode(default, "test", "Test", [], [function], new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.NotNull(call.ResolvedMetadataMethod);
        Assert.Equal(SpecialType.System_String, call.MetadataArrayResultType!.ElementType.SpecialType);

        var generic = new ExpressionCallNode(default,
            new FieldAccessNode(default, new ReferenceNode(default, "value"), "Echo"),
            [new IntLiteralNode(default, 5)], ["item"], [null], ["i32"]);
        Assert.Equal("value.Echo<int>(item: 5)", generic.Accept(new CSharpEmitter()));
        var simplified = generic.Accept(new Calor.Compiler.Verification.ExpressionSimplifier());
        Assert.Equal(generic.TypeArguments, Assert.IsType<ExpressionCallNode>(simplified).TypeArguments);
        Assert.Equal("value.Echo<int>(item: 5)", generic.Accept(new CalorEmitter()));
    }

    [Fact]
    public void ExplicitGenericComputedCall_UsesTheConstructedSelectedMetadataMethod()
    {
        var call = new ExpressionCallNode(default,
            new FieldAccessNode(default, new NewExpressionNode(default, "System.Collections.ArrayList", [], []), "Cast"),
            [], typeArguments: ["i32"]);
        var function = new FunctionNode(default, "use", "Use", Visibility.Public, [],
            new OutputNode(default, "object"), null, [new ReturnStatementNode(default, call)], new AttributeCollection());
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(new ModuleNode(default, "test", "Test", [], [function], new AttributeCollection()));
        var selected = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal("Cast", selected.ResolvedMetadataMethod?.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.Single(selected.ResolvedMetadataMethod!.TypeArguments).SpecialType);
        Assert.Equal(call.TypeArguments, selected.TypeArguments);
    }

    [Fact]
    public void ComputedMetadataReceiver_UsesExactSelectedParametersAndIsCollected()
    {
        var (ast, bound, diagnostics) = Bind("""
            §M{Test}
              §F{Use:pub} () -> bool
                §R §C §NEW{object} §/NEW.Equals §A INT:3 §/C
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().Single();
        Assert.Equal("Equals", call.ResolvedMetadataMethod?.Name);
        Assert.False(call.ResolvedMetadataMethod!.IsStatic);
        Assert.Equal(["System.Object"], call.ResolvedParameterTypes);
        Assert.Contains(ExternalCallCollector.Collect(ast),
            collected => collected.TypeName == "System.Object" && collected.MethodName == "Equals");
    }

    [Fact]
    public void ComputedReceiver_TypeParameterDoesNotAcquireBclMetadata()
    {
        var (_, bound, diagnostics) = Bind("""
            §M{Test}
              §F{Use:pub}<Random> (Random:value) -> i32
                §R §C §C{Get<Random>} §A value §/C.Next §/C
              §F{Get:pub}<T> (T:value) -> T
                §R value
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.EvaluatedReceiver is not null);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Null(call.ResolvedSymbol);
    }

    [Fact]
    public void ComputedMemberArguments_AreStillTypeCheckedWhenTheMemberIsExternal()
    {
        var result = Program.Compile("""
            §M{Test}
              §F{Use:pub} () -> object
                §R §C §NEW{object} §/NEW.Equals §A (?? INT:1 INT:2) §/C
            """, "computed.calr", new CompilationOptions { EnableTypeChecking = true, EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Message.Contains("Null-coalescing requires", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("§E{alloc,cw}", false)]
    [InlineData("§E{alloc}", true)]
    public void ComputedInternalMethod_ChargesItsSelectedEffects(string effects, bool error)
    {
        var result = Program.Compile($$"""
            §M{Test}
              §F{Use:pub} () -> void
                {{effects}}
                §C §NEW{Holder} §/NEW.Loud §/C
              §CL{Holder:pub}
                §CTOR{pub} ()
                  §B{unused:i32} INT:0
                §MT{Loud:pub} () -> void
                  §E{cw}
                  §P "loud"
            """, "computed.calr", new CompilationOptions { EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal(error, result.HasErrors);
        if (error)
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
    }

    [Fact]
    public void ComputedDelegateTarget_InvalidatesCapturedMutationFacts()
    {
        foreach (var target in new[] { "later", "(? gate later later)" })
        {
            var (_, _, diagnostics) = Bind($$"""
                §M{Test}
                  §F{Use:pub} (?str:input, bool:gate) -> i32
                    §B{~value:?str} input
                    §B{later:Action} §LAM{}
                      §ASSIGN value null
                    §/LAM
                    §IF (!= value null)
                      §C {{target}} §/C
                      §B{str:observed} value
                      §R INT:1
                    §R INT:0
                """);
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0272");
        }
    }

    [Fact]
    public void ComputedFactory_InvokedReturnValueUsesItsDeclaredRow()
    {
        foreach (var binding in new[] { false, true })
        foreach (var permitsWrite in new[] { false, true })
        {
            var result = Program.Compile($$"""
                §M{Test}
                  §F{Use:pub} () -> i32
                    §E{alloc{{(permitsWrite ? ",cw" : "")}}}
                    {{(binding ? "§B{handler} §C §NEW{Holder} §/NEW.Make §/C\n    §R §C{handler} §/C" : "§R §C §C §NEW{Holder} §/NEW.Make §/C §/C")}}
                  §CL{Holder:pub}
                    §CTOR{pub} ()
                      §B{unused:i32} INT:0
                    §MT{Make:pub} () -> Func<i32> §E{cw}
                      §E{}
                      §R §LAM{} INT:0 §/LAM
                """, "computed.calr", new CompilationOptions { EnforceEffects = true, StatusWriter = TextWriter.Null });
            Assert.True(result.HasErrors == !permitsWrite,
                $"binding={binding}, permitsWrite={permitsWrite}: {string.Join("\n", result.Diagnostics)}");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
            if (!permitsWrite)
                Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
        }
    }

    [Fact]
    public void UnknownComputedMember_DoesNotBecomePure()
    {
        var result = Program.Compile("""
            §M{Test}
              §F{Use:pub} () -> void
                §E{alloc}
                §C §NEW{System.Random} §/NEW.DoesNotExist §/C
            """, "computed.calr", new CompilationOptions { EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void ConditionalDelegateTarget_IsGroupedBeforeInvocation()
    {
        var call = new ExpressionCallNode(default,
            new ConditionalExpressionNode(default, new ReferenceNode(default, "gate"),
                new ReferenceNode(default, "first"), new ReferenceNode(default, "second")),
            [new IntLiteralNode(default, 7)]);
        var expression = call.Accept(new CSharpEmitter());
        Assert.Equal("((gate ? first : second))(7)", expression);
        var code = $"class Probe {{ static int Run(bool gate, System.Func<int,int> first, System.Func<int,int> second) => {expression}; }}";
        var compilation = CSharpCompilation.Create("ComputedGrouping",
            [CSharpSyntaxTree.ParseText(code)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    private static (ModuleNode Ast, BoundModule Bound, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        return (ast, new Binder(diagnostics).Bind(ast), diagnostics);
    }
}
