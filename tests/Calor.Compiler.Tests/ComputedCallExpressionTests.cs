using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class ComputedCallExpressionTests
{
    [Theory]
    [InlineData(false, false, "[object]")]
    [InlineData(false, true, "[object]")]
    [InlineData(true, false, "[object]")]
    [InlineData(true, true, "[object]")]
    [InlineData(true, false, "IEnumerable<object>")]
    [InlineData(true, false, "List<object>")]
    public void ForeachElementInference_DistinguishesUnknownFromActualObject(
        bool known, bool explicitType, string collectionType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} ({{collectionType}}:items) -> void
                §E{}
                §B{source} {{(known ? "items" : "§C{External.GetTypes} §/C")}}
                §EACH{value{{(explicitType ? ":object" : "")}}} source
                  §C{value.ToString} §/C
                  §C{Pick} §A value §/C
              §F{Pick:pub} (object:item) -> void
                §E{}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var receiverCall = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallStatement>()
            .Single(call => call.Target == "value.ToString");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallStatement>()
            .Single(call => call.Target == "Pick");
        if (known || explicitType)
        {
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
            Assert.IsNotType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Fact]
    public void NestedUnknownForeachElements_RetainEveryReceiverWarningAndRejectAuthority()
    {
        var (ast, bound, diagnostics) = Bind("""
            §M{Test}
              §F{Use:pub} (System.Reflection.Assembly:assembly) -> void
                §E{}
                §EACH{type} §C{assembly.GetTypes} §/C
                  §C{type.GetInterfaces} §/C
                  §C{type.GetGenericArguments} §/C
                  §EACH{interface} §C{type.GetInterfaces} §/C
                    §C{interface.GetGenericArguments} §/C
                    §C{Pick} §A interface §/C
              §F{Pick:pub} (object:item) -> void
                §E{}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use)
            .Where(node => node is BoundCallExpression or BoundCallStatement).ToArray();
        var receivers = calls.Select(node => node switch
            {
                BoundCallExpression call => call.Receiver,
                BoundCallStatement call => call.Receiver,
                _ => null
            })
            .Where(receiver => receiver?.Type is Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType)
            .ToArray();
        Assert.Equal(4, receivers.Length);
        Assert.Equal(receivers.Select(receiver => receiver!.Span.Start).Order(),
            diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved)
                .Select(diagnostic => diagnostic.Span.Start).Order());
        var pick = calls.OfType<BoundCallStatement>().Single(call => call.Target == "Pick");
        Assert.Null(pick.ResolvedSymbol);
        Assert.Empty(pick.SelectedOverloadMatches);
        Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
        var graph = CallGraphAnalysis.Build(ast);
        Assert.Empty(graph.ResolveCallSites(ast.Functions.Single(function => function.Name == "Use").Id,
            "Pick", pick.Span));
    }

    [Theory]
    [InlineData("object")]
    [InlineData("IList<ObjectDetails>")]
    public void UnknownInferredArgument_DoesNotRejectOrSelectNativeOverload(string parameterType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} () -> i32
                §E{}
                §B{details} §C{External.Make} §/C
                §B{alias} details
                §R §C{Pick} §A alias §/C
              §F{Pick:pub} ({{parameterType}}:value) -> i32
                §E{}
                §R 2
            """);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.ResolvedSymbols);
        Assert.Empty(call.SelectedOverloadMatches);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Null(call.ArgumentParameterIndices);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[use.SymbolId]);
        var astUse = ast.Functions.Single(function => function.Name == "Use");
        var astGraph = CallGraphAnalysis.Build(ast);
        Assert.Empty(astGraph.ResolveCallSites(astUse.Id, "Pick", call.Span));
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span);
    }

    [Theory]
    [InlineData("§B{value:OBJECT} §C{External.Make} §/C")]
    [InlineData("§B{value:object} §C{External.Make} §/C")]
    [InlineData("§B{value} §NEW{object} §/NEW")]
    [InlineData("§B{value} §C{Make} §/C")]
    [InlineData("§B{value} §C{make} §/C")]
    [InlineData("§B{value} §C make §/C")]
    [InlineData("§B{value} §C{System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue} §A §NEW{object} §/NEW §/C")]
    public void ActualObjectArgument_RetainsNativeSelectionAndMismatch(string binding)
    {
        foreach (var parameter in new[] { "object", "i32" })
        {
            var (_, bound, diagnostics) = Bind($$"""
                §M{Test}
                  §F{Use:pub} (Func<object>:make) -> i32
                    {{binding}}
                    §B{alias} value
                    §R §C{Pick} §A alias §/C
                  §F{Make:pub} () -> object
                    §R §NEW{object} §/NEW
                  §F{Pick:pub} ({{parameter}}:value) -> i32
                    §R 2
                """);
            var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
            var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
                .Single(call => call.Target == "Pick");
            if (parameter == "object")
            {
                Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.NoMatchingOverload);
                Assert.NotNull(pick.ResolvedSymbol);
                Assert.Single(pick.SelectedOverloadMatches);
                Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
                    site => site.Callee == pick.ResolvedSymbolId);
            }
            else
            {
                Assert.Contains(diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.NoMatchingOverload);
                Assert.Null(pick.ResolvedSymbol);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MutableInferredTypeAuthority_IsStaticAcrossReassignmentsAndJoins(bool unknown, bool branch)
    {
        var initializer = unknown ? "§C{External.Make} §/C" : "§NEW{object} §/NEW";
        var replacement = unknown ? "§NEW{object} §/NEW" : "§C{External.Make} §/C";
        var (_, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} (bool:gate) -> i32
                §B{~value} {{initializer}}
                {{(branch ? "§IF gate\n      §ASSIGN value " : "§B{~value} ")}}{{replacement}}
                §B{alias} value
                §R §C{Pick} §A alias §/C
              §F{Pick:pub} (object:value) -> i32
                §R 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var pick = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Equal(unknown, pick.HasUnresolvedArguments);
        Assert.Equal(!unknown, pick.ResolvedSymbol is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredTypeJoin_DoesNotRecoverUnknownAuthority(bool preprocessor)
    {
        var binding = preprocessor
            ? """
                §PP{FEATURE}
                  §B{value} §NEW{object} §/NEW
                §PPE
                  §B{value} §C{External.Make} §/C
                """
            : "§B{value} (? gate §NEW{object} §/NEW §C{External.Make} §/C)";
        var (_, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} (bool:gate) -> i32
                {{binding.Replace("\n", "\n    ", StringComparison.Ordinal)}}
                §R §C{Pick} §A value §/C
              §F{Pick:pub} (object:item) -> i32
                §R 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var pick = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.True(pick.HasUnresolvedArguments);
        Assert.Null(pick.ResolvedSymbol);
        Assert.Empty(pick.SelectedOverloadMatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownArgument_BlocksNativeAndMetadataFallback(bool statement)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} () -> i32
                §E{}
                §B{value} §C{External.Make} §/C
                §B{text} §C{System.Convert.ToString} §A value §/C
                {{(statement ? "" : "§B{result} ")}}§C{Pick} §A value §/C
                §R 0
              §F{Pick:pub} (object:item) -> i32
                §E{}
                §R 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var metadata = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "System.Convert.ToString");
        Assert.True(metadata.HasUnknownInferredType);
        Assert.Null(metadata.ResolvedMetadataMethod);
        Assert.Null(metadata.ArgumentParameterIndices);
        var expression = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .SingleOrDefault(call => call.Target == "Pick");
        var callStatement = use.Body.OfType<BoundCallStatement>().SingleOrDefault(call => call.Target == "Pick");
        Assert.Empty(expression?.ResolvedSymbols ?? callStatement!.ResolvedSymbols);
        Assert.Empty(expression?.SelectedOverloadMatches ?? callStatement!.SelectedOverloadMatches);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[use.SymbolId]);
        var span = expression?.Span ?? callStatement!.Span;
        var astGraph = CallGraphAnalysis.Build(ast);
        Assert.Empty(astGraph.ResolveCallSites(ast.Functions[0].Id, "Pick", span));
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == span);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == metadata.Span);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownArgument_DoesNotEraseADeclaredDelegateInvocationRow(bool unknown)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} (Func<object,i32>:make §E{cw}, object:seed) -> i32
                §E{}
                §B{value} {{(unknown ? "§C{External.Make} §/C" : "seed")}}
                §R §C{make} §A value §/C
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Single(call => call.Target == "make");
        var enforcement = new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true);
        enforcement.Enforce(ast);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span);
        var fact = enforcement.DeclarationFacts[ast.Functions.Single().Id];
        Assert.Equal(unknown, fact.InferredRow.IsUnknown);
        Assert.Equal(unknown ? ["unknown"] : new[] { "cw" }, fact.Forbidden);
        if (!unknown)
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == "Calor0410" && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));

        // Isolate this invocation's charge without teaching the binder the
        // producer's return type. Unknown remains top in the default pass above.
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        Assert.NotNull(loader.LoadFromJson("""
            {"mappings":[{"type":"External","methods":{"Make":[]}}]}
            """));
        var isolatedDiagnostics = new DiagnosticBag();
        var isolated = new EffectEnforcementPass(isolatedDiagnostics,
            policy: UnknownCallPolicy.Strict, strictEffects: true, resolver: new EffectResolver(loader));
        isolated.Enforce(ast);
        var isolatedFact = isolated.DeclarationFacts[ast.Functions.Single().Id];
        Assert.False(isolatedFact.InferredRow.IsUnknown);
        Assert.Equal(["cw"], isolatedFact.Forbidden);
        var forbidden = Assert.Single(isolatedDiagnostics.Errors,
            diagnostic => diagnostic.Code == "Calor0410");
        Assert.Contains("uses effect 'cw'", forbidden.Message, StringComparison.Ordinal);
        Assert.Contains("charged by invoking 'make' (row: cw)", forbidden.Message, StringComparison.Ordinal);
        Assert.Equal(unknown, Assert.IsType<BoundVariableExpression>(Assert.Single(call.Arguments)).HasUnknownInferredType);
    }

    [Fact]
    public void UnknownInferredLinqArgument_PreservesExecutableCSharpInference()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;
            namespace Probe;
            public class Orderer
            {
                public static int Run(Handler[] handlers)
                {
                    var handlerObjectsDetails = handlers.Select(static s => new ObjectDetails(s)).ToList();
                    var uniqueHandlers = RemoveOverridden(handlerObjectsDetails).ToArray();
                    return uniqueHandlers.Length;
                }
                private static IEnumerable<ObjectDetails> RemoveOverridden(IList<ObjectDetails> details)
                {
                    return details;
                }
            }
            """;
        const string externalTypes = """
            namespace Probe;
            public sealed class Handler { }
            public sealed class ObjectDetails
            {
                public ObjectDetails(Handler handler) { }
            }
            """;
        var (ast, bound, diagnostics) = Bind("""
            §M{Probe}
              §U{System.Linq}
              §U{System.Collections.Generic}
              §CL{Orderer:pub}
                §MT{Run:pub:static} ([Handler]:handlers) -> i32
                  §B{handlerObjectsDetails} §C §C{handlers.Select} §A §LAM{s:Handler} §NEW{ObjectDetails} §A s §/NEW §/LAM §/C.ToList §/C
                  §B{uniqueHandlers} §C §C{RemoveOverridden} §A handlerObjectsDetails §/C.ToArray §/C
                  §R uniqueHandlers.Length
                §MT{RemoveOverridden:pri:static} (IList<ObjectDetails>:details) -> IEnumerable<ObjectDetails>
                  §R details
            """);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        var call = calls.Single(call => call.Target.EndsWith("RemoveOverridden", StringComparison.Ordinal)
            || call.ResolvedMethodName == "RemoveOverridden");
        Assert.Null(call.ResolvedSymbol);
        Assert.Empty(call.SelectedOverloadMatches);
        var emitted = new CSharpEmitter().Emit(ast);
        foreach (var code in new[] { source, emitted })
        {
            var compilation = CSharpCompilation.Create("Inferred_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code),
                    CSharpSyntaxTree.ParseText(externalTypes)],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emission = compilation.Emit(stream);
            Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + code);
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            var handlers = Array.CreateInstance(assembly.GetType("Probe.Handler", throwOnError: true)!, 3);
            for (var index = 0; index < handlers.Length; index++)
                handlers.SetValue(Activator.CreateInstance(handlers.GetType().GetElementType()!), index);
            Assert.Equal(3, assembly.GetType("Probe.Orderer", throwOnError: true)!
                .GetMethod("Run")!.Invoke(null, [handlers]));
        }
    }

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
