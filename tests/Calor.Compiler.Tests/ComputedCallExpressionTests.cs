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
using BoundTypes = Calor.Compiler.Binding.BoundTypes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class ComputedCallExpressionTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("\"Q\"")]
    public void NativeStringIndex_BindsTheActualCharMethodAndPreservesRuntimeValue(string receiver)
    {
        var source = $$"""
            module NativeCharIndex
              func Test private (string:text) → string
                return {{receiver}}[0].ToString()
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var method = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(SpecialType.System_Char, method.ContainingType.SpecialType);
        Assert.Equal("ToString", method.Name);
        Assert.Empty(method.Parameters);
        Assert.Equal(SpecialType.System_String, method.ReturnType.SpecialType);
        var resolver = new EffectResolver();
        var receiverType = Assert.IsAssignableFrom<BoundExpression>(call.Receiver).Type;
        var exactKey = EffectResolverKey.FromBoundReceiver(receiverType, "ToString", []);
        Assert.Equal("System.Char", exactKey.DeclaringType);
        var exact = resolver.Resolve(exactKey);
        Assert.Equal(EffectResolutionStatus.PureExplicit, exact.Status);
        Assert.True(exact.Effects.IsEmpty);
        Assert.Equal(EffectResolutionStatus.Unknown,
            resolver.Resolve(EffectResolverKey.FromBoundReceiver(receiverType, "ToString", ["Int32"])).Status);

        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var compilation = CSharpCompilation.Create("CharIndex_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + compiled.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var test = Assert.Single(assembly.GetTypes().SelectMany(type => type.GetMethods(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)),
            method => method.Name == "Test");
        Assert.Equal("Q", test.Invoke(null, ["Q"]));
    }

    [Fact]
    public void NativeStringNamedTypeParameter_DoesNotBorrowStringIndexerMetadata()
    {
        var (_, bound, _) = Bind("""
            module GenericCharIndex
              func Test<@String> (@String:text) → string
                return text[0].ToString()
            """);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Program.Compile("""
            module GenericCharIndex
              func Test<@String> (@String:text) → string
                return text[0].ToString()
            """).HasErrors);
    }

    [Theory]
    [InlineData(BoundTypes.NullableAnnotation.NotAnnotated)]
    [InlineData(BoundTypes.NullableAnnotation.Annotated)]
    [InlineData(BoundTypes.NullableAnnotation.Oblivious)]
    public void IndexedPayloadType_DoesNotPromoteExistingAnnotations(BoundTypes.NullableAnnotation annotation)
    {
        var payload = new BoundTypes.NominalBoundType("STRING", annotation);
        var array = new BoundStructuralExpression(default, "Probe", "List<string>");
        var access = new BoundArrayAccess(default, array, new BoundIntLiteral(default, 0), resolvedType: payload);
        Assert.Same(payload, access.Type);
        Assert.False(access.HasUnknownInferredType);
        Assert.Equal(annotation != BoundTypes.NullableAnnotation.NotAnnotated,
            NullabilityChecker.IsPossiblyNullAssignedTo(access,
                new BoundTypes.NominalBoundType("STRING", BoundTypes.NullableAnnotation.NotAnnotated)));
    }

    [Fact]
    public void IndexedDeclaredField_PreservesExecutableCSharp()
    {
        var (ast, _, diagnostics) = Bind("""
            module Probe Probe
              class Chain public
                field List<string>:_memberNames private readonly = new List<string>(2)
                func Run public () → string
                  _memberNames.Add("root")
                  let string:last = _memberNames[0]
                  return _memberNames[0]
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        const string original = """
            namespace Probe;
            public class Chain
            {
                private readonly System.Collections.Generic.List<string> _memberNames = new(2);
                public string Run()
                {
                    _memberNames.Add("root");
                    string last = _memberNames[0];
                    return _memberNames[0];
                }
            }
            """;
        foreach (var code in new[] { original, new CSharpEmitter().Emit(ast) })
        {
            var compilation = CSharpCompilation.Create("Indexed_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emission = compilation.Emit(stream);
            Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + code);
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            var chain = assembly.GetType("Probe.Chain", throwOnError: true)!;
            Assert.Equal("root", chain.GetMethod("Run")!.Invoke(Activator.CreateInstance(chain), null));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IndexedDeclaredField_PreservesStringPayloadNullability(bool nullable, bool throughThis)
    {
        var element = nullable ? "string?" : "string";
        var receiver = throughThis ? "this._memberNames" : "_memberNames";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              class Chain public
                field List<{{element}}>:_memberNames private readonly = new List<{{element}}>(2)
                func Last public () → string
                  let string:last = {{receiver}}[0]
                  return {{receiver}}[0]
            """);
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(nullable ? 2 : 0, diagnostics.Errors.Count());
        var accesses = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>().ToArray();
        Assert.Equal(2, accesses.Length);
        foreach (var access in accesses)
        {
            Assert.False(access.HasUnknownInferredType);
            Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
                Assert.IsType<BoundTypes.NominalBoundType>(access.Type).NullableAnnotation);
        }
    }

    [Theory]
    [InlineData("items", false)]
    [InlineData("items", true)]
    [InlineData("Make()", false)]
    [InlineData("Make()", true)]
    public void IndexedInferredCollection_PreservesKnownStringPayload(string initializer, bool nullable)
    {
        var element = nullable ? "string?" : "string";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (List<{{element}}>:items) → string
                let source = {{initializer}}
                let value = source[0]
                let string:copy = value
                return value
              func Make public () → List<{{element}}>
                return new List<{{element}}>()
            """);
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(nullable ? 2 : 0, diagnostics.Errors.Count());
        var access = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>());
        Assert.False(access.HasUnknownInferredType);
        Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
            Assert.IsType<BoundTypes.NominalBoundType>(access.Type).NullableAnnotation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedDeclaredReference_PreservesPayloadIdentityAndNullability(bool nullable)
    {
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (List<{{(nullable ? "Widget?" : "Widget")}}>:items) → Widget
                let value = items[0]
                let Widget:copy = value
                return value
              class Widget public
            """);
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableToNonNullableBinding));
        Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable));
        var access = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>());
        var type = Assert.IsType<BoundTypes.NominalBoundType>(access.Type);
        Assert.NotNull(type.Declaration);
        Assert.Equal("Widget", type.Declaration.Name);
        Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
            type.NullableAnnotation);
    }

    [Theory]
    [InlineData("External.Make().Value", false)]
    [InlineData("External.GetFields()[0].Value", false)]
    [InlineData("await External.MakeAsync()", false)]
    [InlineData("await input", false)]
    [InlineData("External.Make() ?? input", false)]
    [InlineData("External.Make() ?? null", false)]
    [InlineData("null ?? External.Make()", false)]
    [InlineData("new Holder().Value", true)]
    [InlineData("((Holder)External.Make()).Value", true)]
    [InlineData("await task", true)]
    [InlineData("await valueTask", true)]
    [InlineData("await (Task<object>)External.Make()", true)]
    [InlineData("null ?? input", true)]
    [InlineData("input ?? null", true)]
    [InlineData("null ?? (object)External.Make()", true)]
    public void WrapperResultInference_RetainsOnlyEstablishedAuthority(string initializer, bool knownResult)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (object:input, Task<object>:task, ValueTask<object>:valueTask, External:External) → void
                !
                let value = {{initializer}}
                let alias = value
                alias.ToString()
                Pick(alias)
              func Pick public (object:item) → void
                !
              class Holder public struct
                field object:Value public
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(use, "alias", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        Assert.Equal(!knownResult, Assert.Single(pick.Arguments).HasUnknownInferredType);
        if (knownResult)
        {
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
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
            Assert.True(pick.HasUnresolvedArguments);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
        }
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Equal(!knownResult, diagnostics.Errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span));
    }

    [Theory]
    [InlineData("List", false, false, false)]
    [InlineData("Dictionary", false, false, false)]
    [InlineData("List", true, false, false)]
    [InlineData("Dictionary", true, false, false)]
    [InlineData("List", true, true, false)]
    [InlineData("Dictionary", true, true, false)]
    [InlineData("List", true, true, true)]
    [InlineData("Dictionary", true, true, true)]
    public void IndexedElementInference_RequiresActualBclCollectionIdentity(
        string family, bool sourceDeclared, bool hasIndexer, bool bclQualified)
    {
        var typeName = (bclQualified ? "System.Collections.Generic." : sourceDeclared ? "" : "Foreign.")
            + family + (family == "List" ? "<object>" : "<string,object>");
        var index = family == "List" ? "0" : "\"key\"";
        var declaration = sourceDeclared
            ? $"  class {family}<{(family == "List" ? "T" : "K,V")}> public\n"
                + (hasIndexer ? $$"""
                        this[{{(family == "List" ? "int" : "string")}}:index] → int public
                          get
                            return 1
                    """ : "")
            : "";
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{typeName}}:items) → void
                !
                let element = items[{{index}}]
                element.ToString()
                Pick(element)
              func Pick public (object:item) → void
                !
            {{declaration}}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(use, "element", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        if (bclQualified)
        {
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.True(pick.HasUnresolvedArguments);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Theory]
    [InlineData(false, "object[]", "object", false)]
    [InlineData(false, "List<object>", "object", false)]
    [InlineData(true, "object[]", "object", false)]
    [InlineData(true, "List<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.List<object>", "object", false)]
    [InlineData(true, "IList<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IList<object>", "object", false)]
    [InlineData(true, "IReadOnlyList<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IReadOnlyList<object>", "object", false)]
    [InlineData(true, "Dictionary<string,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.Dictionary<string,object>", "object", false)]
    [InlineData(true, "IDictionary<string,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IDictionary<string,object>", "object", false)]
    [InlineData(true, "IReadOnlyDictionary<string,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IReadOnlyDictionary<string,object>", "object", false)]
    [InlineData(true, "int[]", "int", false)]
    [InlineData(true, "List<int>", "int", false)]
    [InlineData(true, "object", "object", false)]
    [InlineData(true, "UnresolvedCollection<object>", "object", false)]
    [InlineData(true, "IEnumerable<object>", "object", false)]
    [InlineData(false, "object[,]", "object", true)]
    [InlineData(true, "object[,]", "object", true)]
    [InlineData(true, "object", "object", true)]
    public void IndexedElementInference_DistinguishesUnknownFromKnownElements(
        bool known, string collectionType, string elementType, bool multiDimensional)
    {
        var index = collectionType.Contains("Dictionary", StringComparison.Ordinal) ? "\"key\"" : "0";
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{collectionType}}:items, External:External) → void
                !
                let source = {{(known ? "items" : "External.GetFields()")}}
                let element = source[{{(multiDimensional ? "0, 0" : index)}}]
                let alias = element
                alias.ToString()
                Pick(alias)
              func Pick public ({{elementType}}:item) → void
                !
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(use, "alias", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        if (known && collectionType is not ("object" or "UnresolvedCollection<object>" or "IEnumerable<object>"))
        {
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
            Assert.False(Assert.Single(pick.Arguments).HasUnknownInferredType);
            Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
                call => call.Callee == pick.ResolvedSymbolId);
        }
        else
        {
            var warning = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved));
            Assert.Equal(receiverCall.ReceiverSpan, warning.Span);
            Assert.IsType<Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType>(receiverCall.Receiver!.Type);
            Assert.True(Assert.Single(pick.Arguments).HasUnknownInferredType);
            Assert.True(pick.HasUnresolvedArguments);
            Assert.Null(pick.ResolvedSymbol);
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Theory]
    [InlineData("raw", false)]
    [InlineData("raw", true)]
    [InlineData("fallback", false)]
    [InlineData("fallback", true)]
    [InlineData("native", false)]
    [InlineData("native", true)]
    public void OpaqueExpressionInference_DoesNotAcquireObjectAuthority(string kind, bool explicitType)
    {
        const string rawCode = "(new object())";
        var ast = Parse($$"""
            module Test
              func Use public () → void
                !
                let {{(explicitType ? "object:" : "")}}value = new object()
                let alias = value
                alias.ToString()
                Pick(alias)
                let text = System.Convert.ToString(alias)
              func Pick public (object:item) → void
                !
            """);
        if (kind != "native")
        {
            var use = ast.Functions[0];
            var body = use.Body.Select(statement =>
                statement is BindStatementNode { Name: "value", Initializer: { } initializer } binding
                    ? binding.CopyMetadataTo(new BindStatementNode(binding.Span, binding.Name, binding.TypeName, binding.IsMutable,
                        kind == "raw" ? new RawCSharpExpressionNode(initializer.Span, rawCode)
                            : new FallbackExpressionNode(initializer.Span, rawCode, "opaque-test"),
                        binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan))
                    : statement).ToArray();
            ast = WithBody(ast, use, body);
        }
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        if (kind != "native")
        {
            var interop = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundInteropExpression>());
            Assert.Equal(rawCode, interop.SourceText);
            Assert.True(interop.HasUnknownInferredType);
        }
        var useBound = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(useBound).OfType<BoundCallExpression>().ToArray();
        var receiverCall = MemberCall(useBound, "alias", "ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        var metadataCall = BoundNodeHelpers.DescendantsAndSelf(useBound).OfType<BoundCallExpression>()
            .Single(call => call.ResolvedTypeName == "System.Convert" && call.ResolvedMethodName == "ToString");
        if (explicitType || kind == "native")
        {
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved);
            Assert.NotNull(pick.ResolvedSymbol);
            Assert.Single(pick.SelectedOverloadMatches);
            Assert.False(metadataCall.HasUnresolvedArguments);
            Assert.False(Assert.Single(pick.Arguments).HasUnknownInferredType);
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
            Assert.Null(metadataCall.ResolvedMetadataMethod);
            Assert.True(metadataCall.HasUnresolvedArguments);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[useBound.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            foreach (var span in new[] { pick.Span, metadataCall.Span })
                Assert.Contains(diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == span);
        }
    }

    [Fact]
    public void OpaqueExpressionInference_PreservesExecutableCSharp()
    {
        const string rawCode = "(new Witness { Value = 7 })";
        var ast = Parse("""
            module Probe Probe
              class Runner public
                func Run public static () → int
                  let validator = new Witness()
                  return Pick(validator)
                func Pick private static (Witness:validator) → int
                  return validator.Value
            """);
        var runner = Assert.Single(ast.Classes);
        var run = runner.Methods[0];
        var binding = Assert.IsType<BindStatementNode>(run.Body[0]);
        var opaqueBinding = binding.CopyMetadataTo(new BindStatementNode(binding.Span, binding.Name, binding.TypeName,
            binding.IsMutable, new RawCSharpExpressionNode(binding.Initializer!.Span, rawCode),
            binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan));
        var opaqueRun = run.CopyMetadataTo(new MethodNode(run.Span, run.Id, run.Name, run.Visibility, run.Modifiers,
            run.TypeParameters, run.Parameters, run.Output, run.Effects, run.Preconditions, run.Postconditions,
            [opaqueBinding, run.Body[1]], run.Attributes, run.CSharpAttributes, identifierSpan: run.IdentifierSpan));
        var opaqueRunner = runner.CopyMetadataTo(new ClassDefinitionNode(runner.Span, runner.Id, runner.Name,
            runner.IsAbstract, runner.IsSealed, runner.IsPartial, runner.IsStatic, runner.BaseClass,
            runner.ImplementedInterfaces, runner.TypeParameters, runner.Fields, runner.Properties, runner.Constructors,
            [opaqueRun, runner.Methods[1]], runner.Events, runner.OperatorOverloads, runner.Attributes,
            runner.CSharpAttributes, visibility: runner.Visibility, identifierSpan: runner.IdentifierSpan,
            items: [opaqueRun, runner.Methods[1]]));
        ast = ast.With(module =>
        {
            module.Classes = [opaqueRunner];
            module.Items = ast.Items.Select(item => ReferenceEquals(item, runner) ? opaqueRunner : item).ToArray();
        });
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedSymbol);
        Assert.True(call.HasUnresolvedArguments);
        Assert.Equal(rawCode, Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound)
            .OfType<BoundInteropExpression>()).SourceText);
        const string original = """
            namespace Probe;
            public class Runner
            {
                public static int Run()
                {
                    var validator = (new Witness { Value = 7 });
                    return Pick(validator);
                }
                private static int Pick(Witness validator) => validator.Value;
            }
            """;
        var emitted = new CSharpEmitter().Emit(ast);
        Assert.Contains(rawCode, emitted, StringComparison.Ordinal);
        foreach (var code in new[] { original, emitted })
        {
            var compilation = CSharpCompilation.Create("Opaque_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code),
                    CSharpSyntaxTree.ParseText("namespace Probe; public class Witness { public int Value { get; set; } }")],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emission = compilation.Emit(stream);
            Assert.True(emission.Success, string.Join("\n", emission.Diagnostics) + "\n" + code);
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            Assert.Equal(7, assembly.GetType("Probe.Runner", throwOnError: true)!
                .GetMethod("Run")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData(false, false, "object[]")]
    [InlineData(false, true, "object[]")]
    [InlineData(true, false, "object[]")]
    [InlineData(true, true, "object[]")]
    [InlineData(true, false, "IEnumerable<object>")]
    [InlineData(true, false, "List<object>")]
    public void ForeachElementInference_DistinguishesUnknownFromActualObject(
        bool known, bool explicitType, string collectionType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{collectionType}}:items, External:External) → void
                !
                let source = {{(known ? "items" : "External.GetTypes()")}}
                each {{(explicitType ? "object:" : "")}}value in source
                  value.ToString()
                  Pick(value)
              func Pick public (object:item) → void
                !
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var receiverCall = MemberCall(use, "value", "ToString");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
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
        var ast = Parse("""
            module Test
              func Use public (System.Reflection.Assembly:assembly) → void
                !
                each type in assembly.GetTypes()
                  type.GetInterfaces()
                  type.GetGenericArguments()
                  each @interface in type.GetInterfaces()
                    @interface.GetGenericArguments()
                    Pick(@interface)
              func Pick public (object:item) → void
                !
            """);
        var function = ast.Functions[0];
        var loop = Assert.IsType<ForeachStatementNode>(Assert.Single(function.Body));
        var nativeProducer = Assert.IsType<ExpressionCallNode>(loop.Collection);
        var member = Assert.IsType<FieldAccessNode>(nativeProducer.TargetExpression);
        // The named-call SDK boundary deliberately has no Type[] result authority.
        var namedProducer = new CallExpressionNode(nativeProducer.Span, "assembly.GetTypes", [], null, null, null,
            calleeSpan: member.FieldNameSpan, receiverSpan: member.Target.Span);
        var sdkLoop = loop.CopyMetadataTo(new ForeachStatementNode(loop.Span, loop.Id, loop.VariableName,
            loop.VariableType, namedProducer, loop.Body, loop.Attributes, loop.IndexVariableName,
            loop.VariableSpan, loop.IndexVariableSpan, loop.VariableTypeSpan));
        ast = WithBody(ast, function, [sdkLoop]);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().ToArray();
        var receivers = calls.Select(call => call.Receiver)
            .Where(receiver => receiver?.Type is Calor.Compiler.Binding.BoundTypes.UnresolvedBoundType)
            .ToArray();
        Assert.Equal(4, receivers.Length);
        Assert.Equal(receivers.Select(receiver => receiver!.Span.Start).Order(),
            diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved)
                .Select(diagnostic => diagnostic.Span.Start).Order());
        var pick = calls.Single(call => call.Target == "Pick");
        Assert.Null(pick.ResolvedSymbol);
        Assert.Empty(pick.SelectedOverloadMatches);
        Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
        var graph = CallGraphAnalysis.Build(ast);
        Assert.Empty(graph.ResolveCallSites(ast.Functions.Single(function => function.Name == "Use").Id,
            "Pick", pick.Span));
    }

    [Fact]
    public void NativeReflectionForeach_RetainsActualMetadataArrayIdentity()
    {
        var (ast, bound, diagnostics) = Bind("""
            module Test
              func Use public (System.Reflection.Assembly:assembly) → void
                !
                each type in assembly.GetTypes()
                  type.GetInterfaces()
                  type.GetGenericArguments()
                  each @interface in type.GetInterfaces()
                    @interface.GetGenericArguments()
                    Pick(@interface)
              func Pick public (object:item) → void
                !
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var producer = MemberCall(use, "assembly", "GetTypes");
        var method = Assert.IsAssignableFrom<IMethodSymbol>(producer.ResolvedMetadataMethod);
        Assert.Equal("System.Reflection.Assembly", method.ContainingType.ToDisplayString());
        var array = Assert.IsAssignableFrom<IArrayTypeSymbol>(method.ReturnType);
        Assert.Equal("System.Type", array.ElementType.ToDisplayString());
        Assert.Same(array, producer.MetadataArrayResultType);
        foreach (var loop in BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundForeachStatement>())
        {
            Assert.False(loop.LoopVariable.HasUnknownInferredType);
            Assert.Equal("System.Type", loop.LoopVariable.InferredReferenceType!.RoslynSymbol!.ToDisplayString());
        }
        var pick = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>(),
            call => call.Target == "Pick");
        Assert.NotNull(pick.ResolvedSymbol);
        Assert.Single(pick.SelectedOverloadMatches);
        Assert.False(pick.HasUnresolvedArguments);
        Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
            site => site.Callee == pick.ResolvedSymbolId);
        Assert.Contains(ast.Functions[1].Id,
            CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeUnknownReceiver_RetainsPerSiteDiagnosticsWithoutMetadataAuthority(bool explicitType, bool synthesized)
    {
        var name = synthesized ? "_generated001" : "value";
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use (External:External) → void
                !
                let {{(explicitType ? "object:" : "")}}{{name}} = External.Make()
                {{name}}.ToString()
                {{name}}.ToString()
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
            .Where(call => call.ResolvedMethodName == "ToString").ToArray();
        Assert.Equal(2, calls.Length);
        var warnings = diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved).ToArray();
        Assert.Equal(explicitType || synthesized ? 0 : 2, warnings.Length);
        if (!explicitType && !synthesized)
            Assert.Equal(calls.Select(call => call.ReceiverSpan!.Value).OrderBy(span => span.Start),
                warnings.Select(diagnostic => diagnostic.Span).OrderBy(span => span.Start));
        foreach (var call in calls)
        {
            if (explicitType)
            {
                Assert.IsNotType<BoundTypes.UnresolvedBoundType>(call.Receiver!.Type);
                Assert.Equal(SpecialType.System_Object, call.ResolvedMetadataMethod!.ContainingType.SpecialType);
            }
            else
            {
                Assert.IsType<BoundTypes.UnresolvedBoundType>(call.Receiver!.Type);
                Assert.Null(call.ResolvedMetadataMethod);
            }
        }
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        Assert.NotNull(loader.LoadFromJson("""{"mappings":[{"type":"System.Object","methods":{"ToString":[]}}]}"""));
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true,
            resolver: new EffectResolver(loader)).Enforce(ast);
        foreach (var call in calls)
            Assert.Equal(!explicitType, diagnostics.Errors.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeEscapedStringTypeParameter_DoesNotBorrowIndexerMetadataOnEitherDeclaration(bool classParameter)
    {
        var (ast, bound, diagnostics) = Bind(classParameter ? """
            module Test
              class Holder<@String>
                func Use (@String:text) → string
                  return text[0].ToString()
            """ : """
            module Test
              func Use<@String> (@String:text) → string
                return text[0].ToString()
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var access = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundArrayAccess>());
        Assert.True(access.HasUnknownInferredType);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.Null(call.ResolvedSymbol);
        new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == call.Span);
    }

    [Theory]
    [InlineData("object", false)]
    [InlineData("object?", true)]
    [InlineData("string", false)]
    [InlineData("string?", true)]
    [InlineData("Widget", false)]
    [InlineData("Widget?", true)]
    public void NativeMatchCapture_PreservesEstablishedReferenceAnnotation(string type, bool nullable)
    {
        foreach (var pattern in new[] { "captured", "var captured" })
        {
            var (_, bound, diagnostics) = Bind($$"""
                module Test
                  func Use ({{type}}:source) → int
                    match source
                      case {{pattern}}
                        return Pick(captured)
                  func Pick ({{type.TrimEnd('?')}}:item) → int
                    return 2
                  class Widget
                """);
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            Assert.NotNull(call.ResolvedSymbol);
            var argument = Assert.IsType<BoundVariableExpression>(Assert.Single(call.Arguments));
            Assert.False(argument.HasUnknownInferredType);
            Assert.Equal(nullable ? BoundTypes.NullableAnnotation.Annotated : BoundTypes.NullableAnnotation.NotAnnotated,
                Assert.IsType<BoundTypes.NominalBoundType>(argument.Type).NullableAnnotation);
            Assert.Equal(nullable, diagnostics.Errors.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
            Assert.Equal(nullable ? 1 : 0, diagnostics.Errors.Count());
        }
    }

    [Theory]
    [InlineData("System.Object")]
    [InlineData("global::System.Object")]
    public void NativeQualifiedObject_IsKnownForOverloadMismatch(string type)
    {
        foreach (var parameter in new[] { "object", "int" })
        {
            var (_, bound, diagnostics) = Bind($$"""
                module Test
                  func Use ({{type}}:value) → int
                    return Pick(value)
                  func Pick ({{parameter}}:value) → int
                    return 2
                """);
            var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
            Assert.False(call.HasUnresolvedArguments);
            Assert.False(Assert.Single(call.Arguments).HasUnknownInferredType);
            Assert.Equal(parameter == "object", call.ResolvedSymbol is not null);
            Assert.Equal(parameter == "int", diagnostics.Errors.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.NoMatchingOverload));
            Assert.Equal(parameter == "int" ? 1 : 0, diagnostics.Errors.Count());
        }
    }

    [Theory]
    [InlineData("var captured", false)]
    [InlineData("captured", false)]
    [InlineData("{ Item: var captured }", false)]
    [InlineData("object captured", true)]
    public void MatchCapture_UnknownRecoveryDoesNotAcquireObjectAuthority(string pattern, bool explicitType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (External:External) → int
                !
                let source = External.Make()
                match source
                  case {{pattern}}
                    captured.ToString()
                    return Pick(captured)
              func Pick public (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var receiverCall = MemberCall(use, "captured", "ToString");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        if (explicitType)
        {
            Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.SignatureUnresolved);
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
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
            new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true).Enforce(ast);
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == pick.Span);
        }
    }

    [Theory]
    [InlineData("var captured", "object")]
    [InlineData("captured", "object")]
    [InlineData("var captured", "int")]
    [InlineData("captured", "int")]
    public void MatchCapture_KnownMatchedTypeRetainsItsAuthority(string pattern, string type)
    {
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public ({{type}}:source) → int
                match source
                  case {{pattern}}
                    captured.ToString()
                    return Pick(captured)
              func Pick public ({{type}}:item) → int
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.SignatureUnresolved);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.NotNull(pick.ResolvedSymbol);
        Assert.Single(pick.SelectedOverloadMatches);
        Assert.False(Assert.Single(pick.Arguments).HasUnknownInferredType);
        Assert.Contains(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId],
            call => call.Callee == pick.ResolvedSymbolId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchExpressionCapture_RetainsMatchedValueAuthority(bool known)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (object:input, External:External) → int
                let source = {{(known ? "input" : "External.Make()")}}
                return match source
                  case var captured → Pick(captured)
              func Pick public (object:item) → int
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Equal(known, pick.ResolvedSymbol is not null);
        Assert.Equal(!known, pick.HasUnresolvedArguments);
        if (!known)
        {
            Assert.Empty(pick.ResolvedSymbols);
            Assert.Empty(pick.SelectedOverloadMatches);
            Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
            Assert.Empty(CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "Pick", pick.Span));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MatchCaptureResult_DoesNotRecoverUnknownAuthority(bool known, bool returnCapture)
    {
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (object:input, External:External) → int
                let source = {{(known ? "input" : "External.Make()")}}
                let projected = match source
                  case var captured → {{(returnCapture ? "captured" : "input")}}
                let alias = projected
                return Pick(alias)
              func Pick public (object:item) → int
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var pick = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        Assert.Equal(known || !returnCapture, pick.ResolvedSymbol is not null);
        Assert.Equal(!known && returnCapture, pick.HasUnresolvedArguments);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("IList<ObjectDetails>")]
    public void UnknownInferredArgument_DoesNotRejectOrSelectNativeOverload(string parameterType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (External:External) → int
                !
                let details = External.Make()
                let alias = details
                return Pick(alias)
              func Pick public ({{parameterType}}:value) → int
                !
                return 2
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
    [InlineData("let System.Object:value = External.Make()")]
    [InlineData("let object:value = External.Make()")]
    [InlineData("let value = new object()")]
    [InlineData("let value = Make()")]
    [InlineData("let value = make()")]
    [InlineData("let value = (make)()")]
    [InlineData("let value = System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(new object())")]
    public void ActualObjectArgument_RetainsNativeSelectionAndMismatch(string binding)
    {
        foreach (var parameter in new[] { "object", "int" })
        {
            var (_, bound, diagnostics) = Bind($$"""
                module Test
                  func Use public (Func<object>:make, External:External) → int
                    {{binding}}
                    let alias = value
                    return Pick(alias)
                  func Make public () → object
                    return new object()
                  func Pick public ({{parameter}}:value) → int
                    return 2
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
        var initializer = unknown ? "External.Make()" : "new object()";
        var replacement = unknown ? "new object()" : "External.Make()";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (bool:gate, External:External) → int
                var value = {{initializer}}
                {{(branch ? "if gate\n      set value " : "var value = ")}}{{replacement}}
                let alias = value
                return Pick(alias)
              func Pick public (object:value) → int
                return 2
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
                pp FEATURE
                  let value = new object()
                pp else
                  let value = External.Make()
                """
            : "let value = gate ? new object() : External.Make()";
        var (_, bound, diagnostics) = Bind($$"""
            module Test
              func Use public (bool:gate, External:External) → int
                {{binding.Replace("\n", "\n    ", StringComparison.Ordinal)}}
                return Pick(value)
              func Pick public (object:item) → int
                return 2
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
            module Test
              func Use public (External:External) → int
                !
                let value = External.Make()
                let text = System.Convert.ToString(value)
                {{(statement ? "" : "let result = ")}}Pick(value)
                return 0
              func Pick public (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var metadata = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.ResolvedTypeName == "System.Convert" && call.ResolvedMethodName == "ToString");
        Assert.True(metadata.HasUnknownInferredType);
        Assert.Null(metadata.ResolvedMetadataMethod);
        Assert.Null(metadata.ArgumentParameterIndices);
        var expression = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>()
            .Single(call => call.Target == "Pick");
        if (statement)
            Assert.Same(expression, Assert.Single(use.Body.OfType<BoundExpressionStatement>()).Expression);
        else
            Assert.Same(expression, use.Body.OfType<BoundBindStatement>().Single(binding => binding.Variable.Name == "result").Initializer);
        Assert.Empty(expression.ResolvedSymbols);
        Assert.Empty(expression.SelectedOverloadMatches);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.Empty(graph.ForwardGraph[use.SymbolId]);
        var span = expression.Span;
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
            module Test
              func Use public (Func<object,int>:make ! cw, object:seed, External:External) → int
                !
                let value = {{(unknown ? "External.Make()" : "seed")}}
                return make(value)
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
        var use = ast.Functions.Single();
        var body = use.Body.Select(statement =>
        {
            if (!unknown || statement is not BindStatementNode { Name: "value", Initializer: ExpressionCallNode producer } binding)
                return statement;
            var member = Assert.IsType<FieldAccessNode>(producer.TargetExpression);
            return binding.CopyMetadataTo(new BindStatementNode(binding.Span, binding.Name, binding.TypeName, binding.IsMutable,
                new CallExpressionNode(producer.Span, "External.Make", [], null, null, null,
                    calleeSpan: member.FieldNameSpan, receiverSpan: member.Target.Span),
                binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan));
        }).ToArray();
        var isolatedAst = WithBody(ast, use, body);
        isolated.Enforce(isolatedAst);
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
    public void NativeUnselectedComputedMember_DoesNotBorrowNamedCallManifestAuthority()
    {
        var (ast, bound, diagnostics) = Bind("""
            module Test
              func Use (External:External) → int
                !
                let value = External.Make()
                return Pick(value)
              func Pick (object:item) → int
                !
                return 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var producer = MemberCall(use, "External", "Make");
        Assert.Null(producer.ResolvedMetadataMethod);
        Assert.Null(producer.ResolvedSymbol);
        Assert.True(producer.HasUnknownInferredType);
        var pick = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>(),
            call => call.Target == "Pick");
        Assert.Null(pick.ResolvedSymbol);
        Assert.Empty(pick.SelectedOverloadMatches);
        Assert.True(pick.HasUnresolvedArguments);
        var loader = new ManifestLoader(loadUserLevelManifests: false);
        Assert.NotNull(loader.LoadFromJson("""{"mappings":[{"type":"External","methods":{"Make":[]}}]}"""));
        var enforcement = new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Strict, strictEffects: true,
            resolver: new EffectResolver(loader));
        enforcement.Enforce(ast);
        Assert.True(enforcement.DeclarationFacts[ast.Functions[0].Id].InferredRow.IsUnknown);
        foreach (var span in new[] { producer.Span, pick.Span })
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownExternalCall && diagnostic.Span == span);
        Assert.Empty(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]);
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
            module Probe Probe
              using System.Linq
              using System.Collections.Generic
              class Orderer public
                func Run public static (Handler[]:handlers) → int
                  let handlerObjectsDetails = handlers.Select((Handler:s) => new ObjectDetails(s)).ToList()
                  let uniqueHandlers = RemoveOverridden(handlerObjectsDetails).ToArray()
                  return uniqueHandlers.Length
                func RemoveOverridden private static (IList<ObjectDetails>:details) → IEnumerable<ObjectDetails>
                  return details
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
            module Test
              func Use public (Holder:h) → int
                return Get(h).Pick(3)
              func Get public (Holder:h) → Holder
                return h
              class Holder public
                func Pick public (int:value) → int
                  return value
                func Pick public (string:value) → string
                  return value
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
            function.Symbol.Name == "Holder.Pick" && function.Symbol.ReturnType == "int").Symbol, outer.ResolvedSymbol);
        Assert.Equal(2, CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId].Count);
        Assert.Contains($"Holder.{ast.Classes[0].Methods[0].Id}",
            CallGraphAnalysis.Build(ast).ResolveCallSites(ast.Functions[0].Id, "<expression-call>", outer.Span));
    }

    [Fact]
    public void ComputedMember_InvalidArgumentStillReportsNativeOverloadError()
    {
        var (_, _, diagnostics) = Bind("""
            module Test
              func Use public (Holder:h) → int
                return Get(h).Pick(true)
              func Get public (Holder:h) → Holder
                return h
              class Holder public
                func Pick public (int:value) → int
                  return value
            """);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void ComputedMetadataArrayCall_RetainsExactSelectedMethodAndArrayResult()
    {
        var (_, bound, diagnostics) = Bind("""
            module Test
              func Use public () → string[]
                return new System.Globalization.CultureInfo("en-US").DateTimeFormat.GetAllDateTimePatterns()
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
            module Test
              func Use public () → bool
                return new object().Equals(3)
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
            module Test
              func Use<Random> public (Random:value) → int
                return Get<Random>(value).Next()
              func Get<T> public (T:value) → T
                return value
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
            module Test
              func Use public () → object
                return new object().Equals(1 ?? 2)
            """, "computed.calr", new CompilationOptions { EnableTypeChecking = true, EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Message.Contains("Null-coalescing requires", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("! alloc,cw", false)]
    [InlineData("! alloc", true)]
    public void ComputedInternalMethod_ChargesItsSelectedEffects(string effects, bool error)
    {
        var result = Program.Compile($$"""
            module Test
              func Use public () → void
                {{effects}}
                new Holder().Loud()
              class Holder public
                ctor public ()
                  let int:unused = 0
                func Loud public () → void
                  ! cw
                  Console.WriteLine("loud")
            """, "computed.calr", new CompilationOptions { EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal(error, result.HasErrors);
        if (error)
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
    }

    [Fact]
    public void ComputedDelegateTarget_InvalidatesCapturedMutationFacts()
    {
        foreach (var target in new[] { "later", "(gate ? later : later)" })
        {
            var (_, _, diagnostics) = Bind($$"""
                module Test
                  func Use public (string?:input, bool:gate) → int
                    var string?:value = input
                    let Action:later = () =>
                      set value null
                    if value != null
                      {{target}}()
                      let string:observed = value
                      return 1
                    return 0
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
                module Test
                  func Use public () → int
                    ! alloc{{(permitsWrite ? ",cw" : "")}}
                    {{(binding ? "let handler = new Holder().Make()\n    return handler()" : "return new Holder().Make()()")}}
                  class Holder public
                    ctor public ()
                      let int:unused = 0
                    func Make public () → Func<int> ! cw
                      !
                      return () => 0
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
            module Test
              func Use public () → void
                ! alloc
                new System.Random().DoesNotExist()
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
        var ast = Parse(source);
        return (ast, new Binder(diagnostics).Bind(ast), diagnostics);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return ast;
    }

    private static BoundCallExpression MemberCall(BoundNode root, string receiver, string member)
    {
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(root).OfType<BoundCallExpression>(),
            call => call.ResolvedMethodName == member
                && call.Receiver is BoundVariableExpression variable && variable.Variable.Name == receiver);
        Assert.Equal("<expression-call>", call.Target);
        Assert.Same(call.Receiver, call.EvaluatedReceiver);
        return call;
    }

    private static ModuleNode WithBody(ModuleNode ast, FunctionNode function, IReadOnlyList<StatementNode> body)
    {
        var replacement = function.CopyMetadataTo(new FunctionNode(function.Span, function.Id, function.Name,
            function.Visibility, function.Parameters, function.Output, function.Effects, body, function.Attributes));
        return ast.With(module =>
        {
            module.Functions = ast.Functions.Select(item => ReferenceEquals(item, function) ? replacement : item).ToArray();
            module.Items = ast.Items.Select(item => ReferenceEquals(item, function) ? replacement : item).ToArray();
        });
    }
}
