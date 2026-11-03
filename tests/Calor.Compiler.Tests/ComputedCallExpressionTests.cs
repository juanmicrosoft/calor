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
            §M{Probe}
              §CL{Chain:pub}
                §FLD{List<string>:_memberNames:priv:readonly} = §NEW{List<string>} §A 2 §/NEW
                §MT{Run:pub} () -> str
                  §C{_memberNames.Add} §A "root" §/C
                  §B{last:str} §IDX{_memberNames} 0
                  §R §IDX{_memberNames} 0
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
        var receiver = throughThis ? "§THIS._memberNames" : "_memberNames";
        var (_, bound, diagnostics) = Bind($$"""
            §M{Test}
              §CL{Chain:pub}
                §FLD{List<{{element}}>:_memberNames:priv:readonly} = §NEW{List<{{element}}>} §A 2 §/NEW
                §MT{Last:pub} () -> str
                  §B{last:str} §IDX {{receiver}} 0
                  §R §IDX {{receiver}} 0
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
    [InlineData("§C{Make} §/C", false)]
    [InlineData("§C{Make} §/C", true)]
    public void IndexedInferredCollection_PreservesKnownStringPayload(string initializer, bool nullable)
    {
        var element = nullable ? "string?" : "string";
        var (_, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} (List<{{element}}>:items) -> str
                §B{source} {{initializer}}
                §B{value} §IDX{source} 0
                §B{copy:str} value
                §R value
              §F{Make:pub} () -> List<{{element}}>
                §R §NEW{List<{{element}}>} §/NEW
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
            §M{Test}
              §F{Use:pub} (List<{{(nullable ? "?Widget" : "Widget")}}>:items) -> Widget
                §B{value} §IDX{items} 0
                §B{copy:Widget} value
                §R value
              §CL{Widget:pub}
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
    [InlineData("(§C{External.Make} §/C).Value", false)]
    [InlineData("(§IDX §C{External.GetFields} §/C 0).Value", false)]
    [InlineData("§AWAIT §C{External.MakeAsync} §/C", false)]
    [InlineData("§AWAIT input", false)]
    [InlineData("(?? §C{External.Make} §/C input)", false)]
    [InlineData("(?? §C{External.Make} §/C null)", false)]
    [InlineData("(?? null §C{External.Make} §/C)", false)]
    [InlineData("(§NEW{Holder} §/NEW).Value", true)]
    [InlineData("(cast Holder §C{External.Make} §/C).Value", true)]
    [InlineData("§AWAIT task", true)]
    [InlineData("§AWAIT valueTask", true)]
    [InlineData("§AWAIT (cast Task<object> §C{External.Make} §/C)", true)]
    [InlineData("(?? null input)", true)]
    [InlineData("(?? input null)", true)]
    [InlineData("(?? null (cast object §C{External.Make} §/C))", true)]
    public void WrapperResultInference_RetainsOnlyEstablishedAuthority(string initializer, bool knownResult)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} (object:input, Task<object>:task, ValueTask<object>:valueTask) -> void
                §E{}
                §B{value} {{initializer}}
                §B{alias} value
                §C{alias.ToString} §/C
                §C{Pick} §A alias §/C
              §F{Pick:pub} (object:item) -> void
                §E{}
              §CL{Holder:pub:struct}
                §FLD{object:Value:pub}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallStatement>().ToArray();
        var receiverCall = calls.Single(call => call.Target == "alias.ToString");
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
            + family + (family == "List" ? "<object>" : "<str,object>");
        var index = family == "List" ? "0" : "\"key\"";
        var declaration = sourceDeclared
            ? $"  §CL{{{family}:pub}}<{(family == "List" ? "T" : "K,V")}>\n"
                + (hasIndexer ? $$"""
                        §IXER{i32:pub}
                          §I{{{(family == "List" ? "i32" : "str")}}:index}
                          §GET
                            §R 1
                    """ : "")
            : "";
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} ({{typeName}}:items) -> void
                §E{}
                §B{element} §IDX{items} {{index}}
                §C{element.ToString} §/C
                §C{Pick} §A element §/C
              §F{Pick:pub} (object:item) -> void
                §E{}
            {{declaration}}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallStatement>().ToArray();
        var receiverCall = calls.Single(call => call.Target == "element.ToString");
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
    [InlineData(false, "[object]", "object", false)]
    [InlineData(false, "List<object>", "object", false)]
    [InlineData(true, "[object]", "object", false)]
    [InlineData(true, "List<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.List<object>", "object", false)]
    [InlineData(true, "IList<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IList<object>", "object", false)]
    [InlineData(true, "IReadOnlyList<object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IReadOnlyList<object>", "object", false)]
    [InlineData(true, "Dictionary<str,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.Dictionary<str,object>", "object", false)]
    [InlineData(true, "IDictionary<str,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IDictionary<str,object>", "object", false)]
    [InlineData(true, "IReadOnlyDictionary<str,object>", "object", false)]
    [InlineData(true, "System.Collections.Generic.IReadOnlyDictionary<str,object>", "object", false)]
    [InlineData(true, "[i32]", "i32", false)]
    [InlineData(true, "List<i32>", "i32", false)]
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
            §M{Test}
              §F{Use:pub} ({{collectionType}}:items) -> void
                §E{}
                §B{source} {{(known ? "items" : "§C{External.GetFields} §/C")}}
                §B{element} {{(multiDimensional ? "§IDX2D source 0 0" : $"§IDX{{source}} {index}")}}
                §B{alias} element
                §C{alias.ToString} §/C
                §C{Pick} §A alias §/C
              §F{Pick:pub} ({{elementType}}:item) -> void
                §E{}
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallStatement>().ToArray();
        var receiverCall = calls.Single(call => call.Target == "alias.ToString");
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
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer($$"""
            §M{Test}
              §F{Use:pub} () -> void
                §E{}
                §B{value{{(explicitType ? ":object" : "")}}} {{(kind == "native" ? "§NEW{object} §/NEW" : $"§CS{{{rawCode}}}")}}
                §B{alias} value
                §C{alias.ToString} §/C
                §C{Pick} §A alias §/C
                §B{text} §C{System.Convert.ToString} §A alias §/C
              §F{Pick:pub} (object:item) -> void
                §E{}
            """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        if (kind == "fallback")
        {
            var use = ast.Functions[0];
            var body = use.Body.Select(statement =>
                statement is BindStatementNode { Initializer: RawCSharpExpressionNode raw } binding
                    ? new BindStatementNode(binding.Span, binding.Name, binding.TypeName, binding.IsMutable,
                        new FallbackExpressionNode(raw.Span, raw.CSharpCode, "opaque-test"),
                        binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan)
                    : statement).ToArray();
            ast = new ModuleNode(ast.Span, ast.Id, ast.Name, [],
                [new FunctionNode(use.Span, use.Id, use.Name, use.Visibility, use.Parameters,
                    use.Output, use.Effects, body, use.Attributes), ast.Functions[1]], ast.Attributes);
        }
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        if (kind != "native")
        {
            var interop = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundInteropExpression>());
            Assert.Equal(rawCode, interop.SourceText);
            Assert.True(interop.HasUnknownInferredType);
        }
        var useBound = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(useBound).OfType<BoundCallStatement>().ToArray();
        var receiverCall = calls.Single(call => call.Target == "alias.ToString");
        var pick = calls.Single(call => call.Target == "Pick");
        var metadataCall = BoundNodeHelpers.DescendantsAndSelf(useBound).OfType<BoundCallExpression>()
            .Single(call => call.Target == "System.Convert.ToString");
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
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Probe}
              §CL{Runner:pub}
                §MT{Run:pub:static} () -> i32
                  §B{validator} §CS{{{rawCode}}}
                  §R §C{Pick} §A validator §/C
                §MT{Pick:priv:static} (Witness:validator) -> i32
                  §R validator.Value
            """);
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
    [InlineData("§VAR{captured}", false)]
    [InlineData("captured", false)]
    [InlineData("§PPROP{} §PMATCH{Item} §VAR{captured}", false)]
    [InlineData("§PTYPE{object:captured}", true)]
    public void MatchCapture_UnknownRecoveryDoesNotAcquireObjectAuthority(string pattern, bool explicitType)
    {
        var (ast, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} () -> i32
                §E{}
                §B{source} §C{External.Make} §/C
                §W source
                  §K {{pattern}}
                    §C{captured.ToString} §/C
                    §R §C{Pick} §A captured §/C
              §F{Pick:pub} (object:item) -> i32
                §E{}
                §R 2
            """);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var receiverCall = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallStatement>()
            .Single(call => call.Target == "captured.ToString");
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
    [InlineData("§VAR{captured}", "object")]
    [InlineData("captured", "object")]
    [InlineData("§VAR{captured}", "i32")]
    [InlineData("captured", "i32")]
    public void MatchCapture_KnownMatchedTypeRetainsItsAuthority(string pattern, string type)
    {
        var (_, bound, diagnostics) = Bind($$"""
            §M{Test}
              §F{Use:pub} ({{type}}:source) -> i32
                §W source
                  §K {{pattern}}
                    §C{captured.ToString} §/C
                    §R §C{Pick} §A captured §/C
              §F{Pick:pub} ({{type}}:item) -> i32
                §R 2
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
            §M{Test}
              §F{Use:pub} (object:input) -> i32
                §B{source} {{(known ? "input" : "§C{External.Make} §/C")}}
                §R §W source
                  §K §VAR{captured} → §C{Pick} §A captured §/C
              §F{Pick:pub} (object:item) -> i32
                §R 2
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
            §M{Test}
              §F{Use:pub} (object:input) -> i32
                §B{source} {{(known ? "input" : "§C{External.Make} §/C")}}
                §B{projected} §W source
                  §K §VAR{captured} → {{(returnCapture ? "captured" : "input")}}
                §B{alias} projected
                §R §C{Pick} §A alias §/C
              §F{Pick:pub} (object:item) -> i32
                §R 2
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
