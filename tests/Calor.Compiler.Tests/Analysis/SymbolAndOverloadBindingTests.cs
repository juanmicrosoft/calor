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
using Diagnostic = Calor.Compiler.Diagnostics.Diagnostic;

namespace Calor.Compiler.Tests.Analysis;

public sealed class SymbolAndOverloadBindingTests
{
    private static readonly TextSpan Span = new(0, 1, 1, 1);

    [Fact]
    public void TopLevelOverloads_ResolveByArityAndExactType_ToCanonicalSymbols()
    {
        var pickInt = Function("f1", "Pick", "i32", [Parameter("x", "i32")],
            [new ReturnStatementNode(Span, new ReferenceNode(Span, "x"))]);
        var pickString = Function("f2", "Pick", "str", [Parameter("x", "str")],
            [new ReturnStatementNode(Span, new ReferenceNode(Span, "x"))]);
        var pickPair = Function(
            "f3",
            "Pick",
            "bool",
            [Parameter("x", "i32"), Parameter("y", "i32")],
            [new ReturnStatementNode(Span, new BoolLiteralNode(Span, true))]);
        var use = Function(
            "f4",
            "Use",
            "void",
            [],
            [
                Binding("a", "i32", Call("Pick", [new IntLiteralNode(Span, 1)])),
                Binding("b", "str", Call("Pick", [new StringLiteralNode(Span, "x")])),
                Binding("c", "bool", Call("Pick", [
                    new IntLiteralNode(Span, 1),
                    new IntLiteralNode(Span, 2),
                ])),
            ]);

        var bound = Bind(Module([pickInt, pickString, pickPair, use]), out var diagnostics);
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound.Functions[3])
            .OfType<BoundCallExpression>()
            .ToArray();

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Same(bound.Functions[0].Symbol, calls[0].ResolvedSymbol);
        Assert.Same(bound.Functions[1].Symbol, calls[1].ResolvedSymbol);
        Assert.Same(bound.Functions[2].Symbol, calls[2].ResolvedSymbol);
        Assert.Equal(bound.Functions[0].SymbolId, calls[0].ResolvedSymbolId);
        Assert.Equal(bound.Functions[1].SymbolId, calls[1].ResolvedSymbolId);
        Assert.Equal(bound.Functions[2].SymbolId, calls[2].ResolvedSymbolId);
        Assert.Equal(3, bound.Functions.Take(3).Select(function => function.SymbolId).Distinct().Count());
        Assert.Equal(
            4,
            bound.Functions.Take(3)
                .SelectMany(function => function.Symbol.Parameters)
                .Select(parameter => parameter.Id)
                .Distinct()
                .Count());
    }

    [Fact]
    public void NullCoalesce_UsesFallbackTypeToResolveOverload()
    {
        const string source = """
            §M{Test}
              §F{Pick:priv} (i32:value) -> i32
                §R value
              §F{Pick:priv} (str:value) -> str
                §R value
              §F{Use:pub} () -> i32
                §R §C{Pick} §A (?? null INT:0) §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(use.Body)).Expression);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        Assert.Equal("INT", Assert.Single(call.Arguments).Type.DisplayString);
        Assert.Same(bound.Functions[0].Symbol, call.ResolvedSymbol);
    }

    [Fact]
    public void MemberOverloads_ResolveBareQualifiedAndStatementCalls()
    {
        const string source = """
            §M{Test}
              §F{UseQualified:pub} (Converter:c) -> i32
                §R §C{c.Convert} §A INT:1 §/C

              §CL{Converter:pub}
                §FLD{i32:_value:priv}
                §MT{Convert:pub} (i32:x) -> i32
                  §R x
                §MT{Convert:pub} (str:x) -> str
                  §R x
                §MT{Convert:pub} (i32:x, i32:y) -> bool
                  §R BOOL:true
                §MT{Use:pub}
                  §C{Convert} §A INT:1 §/C
                  §C{Convert} §A STR:"x" §/C
                  §C{Convert} §A INT:1 §A INT:2 §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var methods = bound.Functions
            .Where(function => function.Symbol.Name == "Converter.Convert")
            .ToArray();
        var qualifiedCall = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions[0].Body)).Expression);
        var statementCalls = bound.Functions
            .Single(function => function.Symbol.Name == "Converter.Use")
            .Body
            .OfType<BoundCallStatement>()
            .ToArray();

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Equal(3, methods.Length);
        Assert.Same(methods[0].Symbol, qualifiedCall.ResolvedSymbol);
        Assert.Same(methods[0].Symbol, statementCalls[0].ResolvedSymbol);
        Assert.Same(methods[1].Symbol, statementCalls[1].ResolvedSymbol);
        Assert.Same(methods[2].Symbol, statementCalls[2].ResolvedSymbol);
        Assert.All(statementCalls, call => Assert.NotNull(call.ResolvedSymbolId));

        var field = Assert.Single(bound.SymbolsById.Values
            .OfType<VariableSymbol>()
            .Where(symbol => symbol.Name == "_value"));
        Assert.False(field.Id.IsNone);
    }

    [Fact]
    public void DuplicateSignature_IsDiagnosed_AndBothDeclarationsKeepUniqueSymbols()
    {
        var first = Function("f1", "Duplicate", "i32", [Parameter("x", "i32")], []);
        var second = Function("f2", "Duplicate", "str", [Parameter("x", "int")], []);

        var bound = Bind(Module([first, second]), out var diagnostics);

        Assert.Single(diagnostics.Where(
            diagnostic => diagnostic.Code == DiagnosticCode.DuplicateFunctionSignature));
        Assert.Equal(2, bound.Functions.Count);
        Assert.NotEqual(bound.Functions[0].SymbolId, bound.Functions[1].SymbolId);
        Assert.NotSame(bound.Functions[0].Symbol, bound.Functions[1].Symbol);
    }

    [Fact]
    public void KnownInternalNoMatch_DoesNotUseFirstDeclaration()
    {
        var pick = Function("f1", "Pick", "i32", [Parameter("x", "i32")], []);
        var use = Function(
            "f2",
            "Use",
            "void",
            [],
            [Binding("value", null, Call("Pick", [new StringLiteralNode(Span, "wrong")]))]);

        var bound = Bind(Module([pick, use]), out var diagnostics);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions[1].Body)).Initializer);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        Assert.Null(call.ResolvedSymbol);
        Assert.Null(call.ResolvedSymbolId);
        Assert.Equal("OBJECT", call.Type.DisplayString);
    }

    [Fact]
    public void AmbiguousGenericMatch_IsExplicit()
    {
        var first = Function(
            "f1",
            "Pick",
            "T",
            [Parameter("value", "T"), Parameter("other", "i32")],
            [],
            ["T"]);
        var second = Function(
            "f2",
            "Pick",
            "T",
            [Parameter("value", "i32"), Parameter("other", "T")],
            [],
            ["T"]);
        var use = Function(
            "f3",
            "Use",
            "void",
            [],
            [Binding("value", null, Call("Pick", [
                new IntLiteralNode(Span, 1),
                new IntLiteralNode(Span, 2),
            ]))]);

        var bound = Bind(Module([first, second, use]), out var diagnostics);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions[2].Body)).Initializer);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
        Assert.Null(call.ResolvedSymbol);
        Assert.Null(call.ResolvedSymbolId);
    }

    [Fact]
    public void ProgramCompile_KnownInternalNoMatch_FailsBeforeEmission()
    {
        const string source = """
            §M{Test}
              §F{Pick:priv} (i32:value) -> i32
                §R value
              §F{Use:pub} () -> i32
                §R §C{Pick} §A STR:"wrong" §/C
            """;

        var result = Calor.Compiler.Program.Compile(source);

        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Single(result.Diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload));
    }

    [Fact]
    public void ProgramCompile_KnownInternalAmbiguity_FailsBeforeEmission()
    {
        const string source = """
            §M{Test}
              §F{Pick<T>:priv} (T:value, i32:other) -> T
                §R value
              §F{Pick<T>:priv} (i32:value, T:other) -> T
                §R other
              §F{Use:pub} () -> i32
                §R §C{Pick} §A INT:1 §A INT:2 §/C
            """;

        var result = Calor.Compiler.Program.Compile(source);

        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Single(result.Diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.AmbiguousOverload));
    }

    [Fact]
    public void ProgramCompile_ExternalInteropCall_RemainsEmissionEligible()
    {
        const string source = """
            §M{Test}
              §F{Use:pub} () -> void
                §C{Console.WriteLine} §A STR:"ok" §/C
            """;

        var result = Calor.Compiler.Program.Compile(
            source,
            filePath: null,
            options: new CompilationOptions { EnforceEffects = false });

        Assert.False(result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.NoMatchingOverload
                or DiagnosticCode.AmbiguousOverload);
        Assert.Contains("Console.WriteLine(\"ok\");", result.GeneratedCode);
    }

    [Fact]
    public void NewExpressionGenericTypeReferences_RetainExactNestedIdentities()
    {
        const string source = """
            §M{Test}
              §F{Create:pub} () -> object
                §R §NEW{Box<List<Widget>>} §/NEW
              §CL{Widget:pub}
              §CL{List:pub}<T>
              §CL{Box:pub}<T>
                §FLD{i32:_dummy:priv}
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var creation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(
                Assert.Single(bound.Functions.Single().Body)).Expression);
        var listReference = Assert.Single(creation.TypeReference.TypeArguments);
        var widgetReference = Assert.Single(listReference.TypeArguments);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Equal("Box", source.Substring(
            creation.TypeReference.Span.Start,
            creation.TypeReference.Span.Length));
        Assert.Equal("List", source.Substring(
            listReference.Span.Start,
            listReference.Span.Length));
        Assert.Equal("Widget", source.Substring(
            widgetReference.Span.Start,
            widgetReference.Span.Length));
        Assert.Equal(
            bound.SymbolsById.Values.OfType<TypeSymbol>()
                .Single(symbol => symbol.Name == "Box").Id,
            creation.TypeReference.ResolvedTypeSymbolId);
        Assert.Equal(
            bound.SymbolsById.Values.OfType<TypeSymbol>()
                .Single(symbol => symbol.Name == "List").Id,
            listReference.ResolvedTypeSymbolId);
        Assert.Equal(
            bound.SymbolsById.Values.OfType<TypeSymbol>()
                .Single(symbol => symbol.Name == "Widget").Id,
            widgetReference.ResolvedTypeSymbolId);

        var ast = Parse(source, new DiagnosticBag());
        var astCreation = Assert.IsType<NewExpressionNode>(
            Assert.IsType<ReturnStatementNode>(
                Assert.Single(ast.Functions.Single().Body)).Expression);
        Assert.Equal(
            "new Box<List<Widget>>()",
            new CSharpEmitter().Visit(astCreation));
    }

    [Fact]
    public void GenericArityAndParameterModifiers_DiscriminateOverloads()
    {
        var oneGeneric = Function(
            "f1",
            "Select",
            "T",
            [Parameter("value", "T")],
            [],
            ["T"]);
        var twoGeneric = Function(
            "f2",
            "Select",
            "T",
            [Parameter("value", "T")],
            [],
            ["T", "U"]);
        var byRef = Function(
            "f3",
            "Touch",
            "i32",
            [Parameter("value", "i32", ParameterModifier.Ref)],
            []);
        var byOut = Function(
            "f4",
            "Touch",
            "i32",
            [Parameter("value", "i32", ParameterModifier.Out)],
            []);
        var use = Function(
            "f5",
            "Use",
            "void",
            [],
            [
                Binding("value", "i32", new IntLiteralNode(Span, 1)),
                Binding("a", "i32", Call(
                    "Select",
                    [new IntLiteralNode(Span, 1)],
                    typeArguments: ["i32"])),
                Binding("b", "i32", Call(
                    "Select",
                    [new IntLiteralNode(Span, 1)],
                    typeArguments: ["i32", "str"])),
                Binding("c", "i32", Call(
                    "Touch",
                    [new ReferenceNode(Span, "value")],
                    argumentModifiers: ["ref"])),
                Binding("d", "i32", Call(
                    "Touch",
                    [new ReferenceNode(Span, "value")],
                    argumentModifiers: ["out"])),
            ]);

        var bound = Bind(
            Module([oneGeneric, twoGeneric, byRef, byOut, use]),
            out var diagnostics);
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound.Functions[4])
            .OfType<BoundCallExpression>()
            .ToArray();

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Same(bound.Functions[0].Symbol, calls[0].ResolvedSymbol);
        Assert.Same(bound.Functions[1].Symbol, calls[1].ResolvedSymbol);
        Assert.Same(bound.Functions[2].Symbol, calls[2].ResolvedSymbol);
        Assert.Same(bound.Functions[3].Symbol, calls[3].ResolvedSymbol);
    }

    [Fact]
    public void TypeIdentity_NormalizesNullableSpellingsWithoutConflatingRuntimeOption()
    {
        var spellings = new[]
        {
            "?str",
            "string?",
            "NULLABLE[inner=STRING]",
        };

        Assert.All(spellings, spelling =>
            Assert.Equal("STRING?", TypeIdentity.Canonicalize(spelling)));
        Assert.Equal(
            "Option<STRING>?",
            TypeIdentity.Canonicalize("NULLABLE[inner=Option<str>]"));
        Assert.Equal("Option<STRING?>", TypeIdentity.Canonicalize("Option<?str>"));

        var scope = new Scope();
        var expected = new FunctionSymbol(
            "Pick",
            "i32",
            [new VariableSymbol("value", "?str", false, true)]);
        scope.DeclareOverload(expected);

        foreach (var spelling in spellings)
        {
            var resolution = scope.ResolveOverload("Pick", [spelling]);
            Assert.Same(expected, resolution.Function);
        }

        foreach (var spelling in new[] { "Option<str>", "Option<string>" })
        {
            Assert.Equal("Option<STRING>", TypeIdentity.Canonicalize(spelling));
            Assert.Null(scope.ResolveOverload("Pick", [spelling]).Function);
        }
    }

    [Fact]
    public void StatementGenericCalls_RetainTypeArgumentsAndResolveGenericArity()
    {
        const string source = """
            §M{Test}
              §F{Select<T>:priv} (T:value) -> T
                §R value
              §F{Select<T,U>:priv} (T:value) -> T
                §R value
              §F{Use:pub} () -> void
                §C{Select<i32>} §A INT:1 §/C
                §C{Select<i32,str>} §A INT:1 §/C
            """;

        var diagnostics = new DiagnosticBag();
        var module = Parse(source, diagnostics);
        var astCalls = module.Functions[2].Body.OfType<CallStatementNode>().ToArray();
        var bound = new Binder(diagnostics).Bind(module);
        var boundCalls = bound.Functions
            .Single(function => function.Symbol.Name == "Use")
            .Body
            .OfType<BoundCallStatement>()
            .ToArray();

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Equal(["i32"], astCalls[0].TypeArguments);
        Assert.Equal(["i32", "str"], astCalls[1].TypeArguments);
        Assert.Equal("Select", astCalls[0].Target);
        Assert.Equal("Select", astCalls[1].Target);
        Assert.Equal(["i32"], boundCalls[0].TypeArguments);
        Assert.Equal(["i32", "str"], boundCalls[1].TypeArguments);
        Assert.Equal(1, boundCalls[0].ResolvedSymbol!.GenericArity);
        Assert.Equal(2, boundCalls[1].ResolvedSymbol!.GenericArity);
        Assert.Equal("Select<int>(1);", new CSharpEmitter().Visit(astCalls[0]));
        Assert.Contains("§C{Select<int>}", new CalorEmitter().Emit(module));
    }

    [Fact]
    public void ExternalCall_RemainsExplicitlyUnresolvedWithoutInternalOverloadDiagnostic()
    {
        var use = Function(
            "f1",
            "Use",
            "void",
            [],
            [Binding("value", null, Call(
                "External.Library",
                [new IntLiteralNode(Span, 1)]))]);

        var bound = Bind(Module([use]), out var diagnostics);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions[0].Body)).Initializer);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Null(call.ResolvedSymbol);
        Assert.Null(call.ResolvedSymbolId);
        Assert.Equal("External.Library", call.Target);
    }

    [Fact]
    public void SymbolIds_AreStableAcrossBinds_AndUniqueForShadowedVariables()
    {
        var function = Function(
            "f1",
            "Shadow",
            "i32",
            [Parameter("x", "i32")],
            [
                Binding("value", "i32", new ReferenceNode(Span, "x")),
                new IfStatementNode(
                    Span,
                    "if1",
                    new BoolLiteralNode(Span, true),
                    [Binding("value", "i32", new IntLiteralNode(Span, 2))],
                    [],
                    null,
                    new AttributeCollection()),
                new ReturnStatementNode(Span, new ReferenceNode(Span, "value")),
            ]);
        var module = Module([function]);

        var first = Bind(module, out _);
        var second = Bind(module, out _);
        var firstIds = first.SymbolsById.Keys.Select(id => id.Value).Order().ToArray();
        var secondIds = second.SymbolsById.Keys.Select(id => id.Value).Order().ToArray();
        var shadowed = first.SymbolsById.Values
            .OfType<VariableSymbol>()
            .Where(symbol => symbol.Name == "value")
            .ToArray();

        Assert.Equal(firstIds, secondIds);
        Assert.Equal(2, shadowed.Length);
        Assert.NotEqual(shadowed[0].Id, shadowed[1].Id);
        Assert.All(first.SymbolsById.Keys, id => Assert.False(id.IsNone));
    }

    [Fact]
    public void DeclarationSymbols_UseIdentifierSpans_AndStableAstIdsIgnoreSiblingInsertion()
    {
        const string original = """
            §M{Test}
              §F{Keep:pub} (i32:value) -> i32
                §B{local:i32} value
                §R local
            """;
        const string inserted = """
            §M{Test}
              §F{Before:pub} () -> i32
                §R 0
              §F{Keep:pub} (i32:value) -> i32
                §B{local:i32} value
                §R local
            """;

        var first = ParseAndBind(original, out _);
        var second = ParseAndBind(inserted, out _);
        var firstFunction = first.Functions.Single(function => function.Symbol.Name == "Keep");
        var secondFunction = second.Functions.Single(function => function.Symbol.Name == "Keep");
        var parameter = Assert.Single(firstFunction.Symbol.Parameters);
        var local = first.SymbolsById.Values
            .OfType<VariableSymbol>()
            .Single(symbol => symbol.Name == "local");

        Assert.Equal("Keep", original.Substring(
            firstFunction.Symbol.DeclarationSpan.Start,
            firstFunction.Symbol.DeclarationSpan.Length));
        Assert.Equal("value", original.Substring(
            parameter.DeclarationSpan.Start,
            parameter.DeclarationSpan.Length));
        Assert.Equal("local", original.Substring(
            local.DeclarationSpan.Start,
            local.DeclarationSpan.Length));
        Assert.Equal(firstFunction.SymbolId, secondFunction.SymbolId);
        Assert.DoesNotContain("function%3A", firstFunction.SymbolId.Value);
    }

    [Fact]
    public void MutableRebind_BindsAsAssignmentToOriginalSymbol()
    {
        const string source = """
            §M{Test}
              §F{Update:pub} () -> i32
                §B{~value:i32} 1
                §B{~value} 2
                §R value
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var function = Assert.Single(bound.Functions);
        var declaration = Assert.IsType<BoundBindStatement>(function.Body[0]);
        var assignment = Assert.IsType<BoundAssignmentStatement>(function.Body[1]);
        var target = Assert.IsType<BoundVariableExpression>(assignment.Target);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.DuplicateDefinition
                or DiagnosticCode.BindRebindTypeMismatch);
        Assert.Same(declaration.Variable, target.Variable);
        Assert.Single(bound.SymbolsById.Values
            .OfType<VariableSymbol>()
            .Where(symbol => symbol.Name == "value"));
    }

    [Fact]
    public void ImmutableAndTypeInvalidRebinds_AreDiagnosed()
    {
        const string source = """
            §M{Test}
              §F{Update:pub} () -> i32
                §B{fixed:i32} 1
                §B{~fixed} 2
                §B{~value:i32} 1
                §B{~value} STR:"wrong"
                §R value
            """;

        _ = ParseAndBind(source, out var diagnostics);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.BindReassignsImmutable);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.BindRebindTypeMismatch);
    }

    [Fact]
    public void NormalParamsArray_BeatsGenericScalar_ToExactSymbolAndArgumentMap()
    {
        var array = Function("array", "Pick", "i32",
            [Parameter("values", "?[?object]", ParameterModifier.Params)], []);
        var generic = Function("generic", "Pick", "i32",
            [Parameter("value", "T")], [], ["T"]);
        var use = Function("use", "Use", "i32", [Parameter("values", "?[?object]")],
            [new ReturnStatementNode(Span, Call("Pick", [new ReferenceNode(Span, "values")]))]);

        var bound = Bind(Module([array, generic, use]), out var diagnostics);
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound.Functions[^1])
            .OfType<BoundCallExpression>());

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Same(bound.Functions[0].Symbol, call.ResolvedSymbol);
        Assert.Equal(bound.Functions[0].SymbolId, call.ResolvedSymbolId);
        var mapping = Assert.Single(Assert.Single(call.SelectedOverloadMatches).Arguments);
        Assert.Equal(0, mapping.ArgumentIndex);
        Assert.Equal(0, mapping.ParameterIndex);
        Assert.Equal(TypeIdentity.Canonicalize("?[?object]"), TypeIdentity.Canonicalize(mapping.ParameterType));
        Assert.False(mapping.IsExpandedParams);
    }

    public static IEnumerable<object[]> NormalParamsCases()
    {
        foreach (var (native, csharp) in new[]
        {
            ("[object]", "object[]"),
            ("[?object]", "object?[]"),
            ("?[object]", "object[]?"),
            ("?[?object]", "object?[]?"),
        })
        foreach (var reversed in new[] { false, true })
        foreach (var named in new[] { false, true })
        foreach (var statement in new[] { false, true })
            yield return [native, csharp, reversed, named, statement];
    }

    [Theory]
    [MemberData(nameof(NormalParamsCases))]
    public void NormalParamsForwarding_MatchesRoslynSelectionMappingRuntimeAndEffects(
        string nativeArray, string csharpArray, bool reversed, bool named, bool statement)
    {
        var array = $$"""
              §F{Pick:pub} (?System.Exception:exception, str:message, {{nativeArray}}:values:params) -> i32
                §E{}
                §R 1
            """;
        const string generic = """
              §F{Pick<T>:pub} (?System.Exception:exception, str:message, T:values) -> i32
                §E{alloc}
                §B{allocated:[i32]} §ARR{i32:1}
                §R 2
            """;
        var nativeArguments = named
            ? """§A[values] values §A[message] "x" §A[exception] (cast System.Exception? null)"""
            : """§A (cast System.Exception? null) §A "x" §A values""";
        var source = "§M{ParamsOracle}\n" + (reversed ? generic + "\n" + array : array + "\n" + generic)
            + $$"""

              §F{Use:pub} ({{nativeArray}}:values) -> i32
                §E{}
                {{(statement ? "" : "§R ")}}§C{Pick} {{nativeArguments}} §/C
                {{(statement ? "§R 0" : "")}}
            """;
        var diagnostics = new DiagnosticBag();
        var ast = Parse(source, diagnostics);
        var bound = new Binder(diagnostics).Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>().SingleOrDefault();
        var callStatement = use.Body.OfType<BoundCallStatement>().SingleOrDefault();
        var match = Assert.Single(call?.SelectedOverloadMatches ?? callStatement!.SelectedOverloadMatches);
        var selected = bound.Functions.Single(function =>
            function.Symbol.Name == "Pick" && function.Symbol.GenericArity == 0);
        Assert.Same(selected.Symbol, match.Function);
        Assert.Equal(selected.SymbolId, call?.ResolvedSymbolId ?? callStatement!.ResolvedSymbolId);
        Assert.Equal(named ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 },
            match.Arguments.Select(argument => argument.ParameterIndex).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, match.Arguments.Select(argument => argument.ArgumentIndex).ToArray());
        Assert.All(match.Arguments, argument => Assert.False(argument.IsExpandedParams));
        Assert.Equal(TypeIdentity.Canonicalize(nativeArray),
            TypeIdentity.Canonicalize(match.Arguments[named ? 0 : 2].ParameterType));
        var graph = CallGraphAnalysis.BuildResolved(bound);
        Assert.True(CallGraphAnalysis.Build(ast).IsBoundResolutionComplete);
        Assert.Equal(selected.SymbolId, Assert.Single(graph.ForwardGraph[use.SymbolId]).Callee);
        var effects = new EffectEnforcementPass(diagnostics);
        effects.Enforce(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal("[pure]", effects.DeclarationFacts.Values.Single(fact => fact.Name == "Use").InferredRow.ToDisplayString());
        Assert.Equal("memory:allocation", effects.DeclarationFacts.Values.Single(fact => fact.Name == "Pick<T>")
            .InferredRow.ToDisplayString());

        var csharpArguments = named
            ? """values: values, message: "x", exception: (System.Exception?)null"""
            : """(System.Exception?)null, "x", values""";
        var csharpArrayOverload = $"public static int Pick(System.Exception? exception, string message, params {csharpArray} values) => 1;";
        const string csharpGenericOverload = "public static int Pick<T>(System.Exception? exception, string message, T values) { var allocated = new int[1]; return 2; }";
        var oracle = $$"""
            public static class ParamsOracle
            {
                {{(reversed ? csharpGenericOverload : csharpArrayOverload)}}
                {{(reversed ? csharpArrayOverload : csharpGenericOverload)}}
                public static int Use({{csharpArray}} values)
                {
                    {{(statement ? "" : "return ")}}Pick({{csharpArguments}});
                    {{(statement ? "return 0;" : "")}}
                }
            }
            """;
        var oracleCompilation = CompileParamsCSharp(oracle);
        Assert.Equal(0, SelectedParamsMethod(oracleCompilation).Arity);
        var result = Calor.Compiler.Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var emittedCompilation = CompileParamsCSharp(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode);
        Assert.Equal(0, SelectedParamsMethod(emittedCompilation).Arity);
        var expected = statement ? 0 : 1;
        Assert.Equal(expected, InvokeParamsUse(oracleCompilation, "ParamsOracle", new object?[] { "item" }));
        Assert.Equal(expected, InvokeParamsUse(emittedCompilation, "ParamsOracle.ParamsOracleModule", new object?[] { "item" }));
        if (nativeArray.StartsWith('?'))
        {
            Assert.Equal(expected, InvokeParamsUse(oracleCompilation, "ParamsOracle", null));
            Assert.Equal(expected, InvokeParamsUse(emittedCompilation, "ParamsOracle.ParamsOracleModule", null));
        }
    }

    private static CSharpCompilation CompileParamsCSharp(string source) =>
        CSharpCompilation.Create("Params_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

    private static IMethodSymbol SelectedParamsMethod(CSharpCompilation compilation)
    {
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var tree = compilation.SyntaxTrees.Single();
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());
        return Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol);
    }

    private static object? InvokeParamsUse(CSharpCompilation compilation, string typeName, object? values)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return System.Reflection.Assembly.Load(stream.ToArray()).GetType(typeName, throwOnError: true)!
            .GetMethod("Use")!.Invoke(null, [values]);
    }

    [Theory]
    [InlineData("expanded-scalar")]
    [InlineData("expanded-many")]
    [InlineData("expanded-empty")]
    [InlineData("expanded-nullable-array")]
    [InlineData("expanded-nullable-elements")]
    [InlineData("generic-only")]
    [InlineData("explicit-generic")]
    [InlineData("named-scalar")]
    [InlineData("unknown-name")]
    [InlineData("duplicate-name")]
    [InlineData("ref")]
    [InlineData("out")]
    [InlineData("in")]
    [InlineData("wrong-modifier")]
    [InlineData("optional-equivalent")]
    [InlineData("optional-params-omitted")]
    [InlineData("better-generic-conversion")]
    [InlineData("ambiguous-shapes")]
    [InlineData("ambiguous-generics")]
    [InlineData("ambiguous-optionals")]
    public void ParamsBoundaries_MatchRoslynWithoutBlanketNonGenericPreference(string scenario)
    {
        var array = Function("array", "Pick", "i32",
            [Parameter("values", "[i32]", ParameterModifier.Params)], []);
        var generic = Function("generic", "Pick", "i32",
            [Parameter("values", "T")], [], ["T"]);
        FunctionNode[] functions = [array, generic];
        var declarations = "public static int Pick(params int[] values) => 1; public static int Pick<T>(T values) => 2;";
        string[] types = ["i32"];
        string?[]? names = null;
        string?[]? modifiers = null;
        string[]? typeArguments = null;
        var invocation = "Pick(value)";
        var expectedIndex = 1;
        var expectedKind = OverloadResolutionKind.Resolved;
        var expanded = false;
        switch (scenario)
        {
            case "expanded-scalar":
                functions[0] = Function("array", "Pick", "i32",
                    [Parameter("values", "[object]", ParameterModifier.Params)], []);
                declarations = "public static int Pick(params object[] values) => 1; public static int Pick<T>(T values) => 2;";
                break;
            case "expanded-many":
            case "expanded-nullable-array":
            case "expanded-nullable-elements":
                types = ["i32", "i32"];
                invocation = "Pick(value, value)";
                expectedIndex = 0;
                expanded = true;
                if (scenario == "expanded-nullable-array")
                {
                    functions[0] = Function("array", "Pick", "i32",
                        [Parameter("values", "?[i32]", ParameterModifier.Params)], []);
                    declarations = "public static int Pick(params int[]? values) => 1; public static int Pick<T>(T values) => 2;";
                }
                else if (scenario == "expanded-nullable-elements")
                {
                    functions[0] = Function("array", "Pick", "i32",
                        [Parameter("values", "?[?i32]", ParameterModifier.Params)], []);
                    declarations = "public static int Pick(params int?[]? values) => 1; public static int Pick<T>(T values) => 2;";
                    types = ["?i32", "?i32"];
                    invocation = "Pick((int?)value, (int?)value)";
                }
                break;
            case "expanded-empty":
                types = [];
                invocation = "Pick()";
                expectedIndex = 0;
                expanded = true;
                break;
            case "generic-only":
                functions = [generic];
                declarations = "public static int Pick<T>(T values) => 2;";
                types = ["[i32]"];
                invocation = "Pick(values)";
                expectedIndex = 0;
                break;
            case "explicit-generic":
                types = ["[i32]"];
                typeArguments = ["[i32]"];
                invocation = "Pick<int[]>(values)";
                break;
            case "named-scalar":
                functions[0] = Function("array", "Pick", "i32",
                    [Parameter("values", "[object]", ParameterModifier.Params)], []);
                declarations = "public static int Pick(params object[] values) => 1; public static int Pick<T>(T values) => 2;";
                names = ["values"];
                invocation = "Pick(values: value)";
                break;
            case "unknown-name":
                names = ["missing"];
                invocation = "Pick(missing: value)";
                expectedKind = OverloadResolutionKind.NoMatch;
                break;
            case "duplicate-name":
                types = ["i32", "i32"];
                names = ["values", "values"];
                invocation = "Pick(values: value, values: value)";
                expectedKind = OverloadResolutionKind.NoMatch;
                break;
            case "ref":
            case "out":
            case "in":
                var modifier = scenario switch
                {
                    "ref" => ParameterModifier.Ref,
                    "out" => ParameterModifier.Out,
                    _ => ParameterModifier.In,
                };
                functions =
                [
                    Function("array", "Pick", "i32", [Parameter("values", "[i32]", modifier)], []),
                    Function("generic", "Pick", "i32", [Parameter("values", "T", modifier)], [], ["T"]),
                ];
                var assignment = scenario == "out" ? "values = default!;" : "";
                declarations = $"public static int Pick({scenario} int[] values) {{ {assignment} return 1; }} "
                    + $"public static int Pick<T>({scenario} T values) {{ {assignment} return 2; }}";
                types = ["[i32]"];
                modifiers = [scenario];
                invocation = $"Pick({scenario} values)";
                expectedIndex = 0;
                break;
            case "wrong-modifier":
                types = ["[i32]"];
                modifiers = ["ref"];
                invocation = "Pick(ref values)";
                expectedKind = OverloadResolutionKind.NoMatch;
                break;
            case "optional-equivalent":
                functions =
                [
                    Function("array", "Pick", "i32",
                        [Parameter("values", "[i32]"), Parameter("count", "i32", defaultValue: new IntLiteralNode(Span, 0))], []),
                    Function("generic", "Pick", "i32",
                        [Parameter("values", "T"), Parameter("count", "i32", defaultValue: new IntLiteralNode(Span, 0))], [], ["T"]),
                ];
                declarations = "public static int Pick(int[] values, int count = 0) => 1; public static int Pick<T>(T values, int count = 0) => 2;";
                types = ["[i32]"];
                invocation = "Pick(values)";
                expectedIndex = 0;
                break;
            case "optional-params-omitted":
                functions =
                [
                    Function("array", "Pick", "i32",
                        [Parameter("count", "i32", defaultValue: new IntLiteralNode(Span, 0)),
                            Parameter("values", "[i32]", ParameterModifier.Params)], []),
                    Function("generic", "Pick", "i32",
                        [Parameter("count", "i32"), Parameter("values", "T")], [], ["T"]),
                ];
                declarations = "public static int Pick(int count = 0, params int[] values) => 1; public static int Pick<T>(int count, T values) => 2;";
                types = ["[i32]"];
                names = ["values"];
                invocation = "Pick(values: values)";
                expectedIndex = 0;
                break;
            case "better-generic-conversion":
                functions[0] = Function("object", "Pick", "i32", [Parameter("values", "object")], []);
                declarations = "public static int Pick(object values) => 1; public static int Pick<T>(T values) => 2;";
                types = ["[i32]"];
                invocation = "Pick(values)";
                break;
            case "ambiguous-shapes":
                functions =
                [
                    Function("concrete", "Pick", "i32", [Parameter("left", "i32"), Parameter("right", "object")], []),
                    Function("generic", "Pick", "i32", [Parameter("left", "object"), Parameter("right", "T")], [], ["T"]),
                ];
                declarations = "public static int Pick(int left, object right) => 1; public static int Pick<T>(object left, T right) => 2;";
                types = ["i32", "i32"];
                invocation = "Pick(value, value)";
                expectedKind = OverloadResolutionKind.Ambiguous;
                break;
            case "ambiguous-generics":
                functions =
                [
                    Function("left", "Pick", "i32", [Parameter("left", "T"), Parameter("right", "i32")], [], ["T"]),
                    Function("right", "Pick", "i32", [Parameter("left", "i32"), Parameter("right", "T")], [], ["T"]),
                ];
                declarations = "public static int Pick<T>(T left, int right) => 1; public static int Pick<T>(int left, T right) => 2;";
                types = ["i32", "i32"];
                invocation = "Pick(value, value)";
                expectedKind = OverloadResolutionKind.Ambiguous;
                break;
            case "ambiguous-optionals":
                functions =
                [
                    Function("number", "Pick", "i32", [Parameter("values", "[i32]"),
                        Parameter("other", "i32", defaultValue: new IntLiteralNode(Span, 0))], []),
                    Function("text", "Pick", "i32", [Parameter("values", "[i32]"),
                        Parameter("other", "str", defaultValue: new StringLiteralNode(Span, ""))], []),
                ];
                declarations = "public static int Pick(int[] values, int other = 0) => 1; public static int Pick(int[] values, string other = \"\") => 2;";
                types = ["[i32]"];
                invocation = "Pick(values)";
                expectedKind = OverloadResolutionKind.Ambiguous;
                break;
        }

        var bound = Bind(Module(functions), out var diagnostics);
        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        var scope = new Scope();
        foreach (var function in bound.Functions)
            Assert.True(scope.DeclareOverload(function.Symbol));
        var result = scope.ResolveOverload("Pick", types, names, modifiers, typeArguments,
            (parameter, _) => TypeIdentity.Canonicalize(parameter) == "OBJECT" ? 1 : null);
        Assert.Equal(expectedKind, result.Kind);
        var oracle = CompileParamsCSharp($$"""
            public static class BoundaryOracle
            {
                {{declarations}}
                public static int Use(int[] values, int value) => {{invocation}};
            }
            """);
        var tree = oracle.SyntaxTrees.Single();
        var call = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());
        var symbol = oracle.GetSemanticModel(tree).GetSymbolInfo(call);
        if (expectedKind != OverloadResolutionKind.Resolved)
        {
            Assert.Null(result.Function);
            Assert.Empty(result.Matches);
            Assert.Null(symbol.Symbol);
            Assert.Contains(oracle.GetDiagnostics(), diagnostic =>
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            if (expectedKind == OverloadResolutionKind.Ambiguous)
            {
                Assert.Equal(2, result.Candidates.Count);
                Assert.Contains(oracle.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0121");
            }
            return;
        }

        Assert.Same(bound.Functions[expectedIndex].Symbol, result.Function);
        var match = Assert.Single(result.Matches);
        Assert.Equal(types.Length, match.Arguments.Count);
        Assert.All(match.Arguments, argument => Assert.Equal(expanded, argument.IsExpandedParams));
        Assert.NotNull(result.Function);
        Assert.Equal(result.Function.GenericArity, SelectedParamsMethod(oracle).Arity);
        var expectedMarker = scenario == "generic-only" ? 2 : expectedIndex + 1;
        using var stream = new MemoryStream();
        var emission = oracle.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        Assert.Equal(expectedMarker, System.Reflection.Assembly.Load(stream.ToArray())
            .GetType("BoundaryOracle", throwOnError: true)!.GetMethod("Use")!.Invoke(null, [new[] { 4 }, 3]));
    }

    [Fact]
    public void ApplicableNormalParamsForm_IsNotReplacedByCheaperExpandedMapping()
    {
        var scope = new Scope();
        var function = new FunctionSymbol("Pick", "i32",
            [new VariableSymbol("values", "object[]", false, modifier: ParameterModifier.Params)]);
        Assert.True(scope.DeclareOverload(function));
        var result = scope.ResolveOverload("Pick", ["str[]"], implicitConversionCost: (parameter, _) =>
            TypeIdentity.Canonicalize(parameter) switch
            {
                "OBJECT[]" => 300,
                "OBJECT" => 0,
                _ => null,
            });

        Assert.Same(function, result.Function);
        Assert.False(Assert.Single(Assert.Single(result.Matches).Arguments).IsExpandedParams);
        var oracle = CompileParamsCSharp("""
            public static class NormalOracle
            {
                public static int Pick(params object[] values) => values.Length;
                public static int Use(string[] values) => Pick(values);
            }
            """);
        var tree = oracle.SyntaxTrees.Single();
        var call = Assert.Single(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>());
        var operation = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Operations.IInvocationOperation>(
            oracle.GetSemanticModel(tree).GetOperation(call));
        Assert.Equal(Microsoft.CodeAnalysis.Operations.ArgumentKind.Explicit,
            Assert.Single(operation.Arguments).ArgumentKind);
        Assert.Equal(2, InvokeParamsUse(oracle, "NormalOracle", new[] { "first", "second" }));
    }

    [Fact]
    public void NormalParamsTie_PreservesConditionalAlternativesAndTheirMappings()
    {
        var scope = new Scope();
        var first = new FunctionSymbol(SymbolId.Create("first"), "Pick", "i32",
            [new VariableSymbol("values", "[i32]", false, modifier: ParameterModifier.Params)],
            conditionalAlternative: new ConditionalAlternative("feature", 0));
        var second = new FunctionSymbol(SymbolId.Create("second"), "Pick", "i32",
            [new VariableSymbol("values", "[i32]", false, modifier: ParameterModifier.Params)],
            conditionalAlternative: new ConditionalAlternative("feature", 1));
        var generic = new FunctionSymbol("Pick", "i32", [new VariableSymbol("values", "T", false)], ["T"]);
        Assert.True(scope.DeclareOverload(generic));
        Assert.True(scope.DeclareOverload(first));
        Assert.True(scope.DeclareOverload(second));

        var result = scope.ResolveOverload("Pick", ["[i32]"]);

        Assert.Equal(OverloadResolutionKind.Resolved, result.Kind);
        Assert.Equal(new[] { first, second }, result.Functions);
        Assert.Equal(2, result.Matches.Count);
        Assert.All(result.Matches, match =>
        {
            var mapping = Assert.Single(match.Arguments);
            Assert.Equal(0, mapping.ParameterIndex);
            Assert.False(mapping.IsExpandedParams);
            Assert.Equal(TypeIdentity.Canonicalize("[i32]"), TypeIdentity.Canonicalize(mapping.ParameterType));
        });
    }

    [Fact]
    public void OptionalAndParamsOverloads_MapOmittedExpandedNamedAndModifiedArguments()
    {
        var optional = Function(
            "optional",
            "Choose",
            "i32",
            [
                Parameter("value", "i32"),
                Parameter("fallback", "i32", defaultValue: new IntLiteralNode(Span, 0)),
            ],
            []);
        var paramsOverload = Function(
            "params",
            "Choose",
            "str",
            [Parameter("values", "[str]", ParameterModifier.Params)],
            []);
        var refOptional = Function(
            "ref",
            "Touch",
            "i32",
            [
                Parameter("value", "i32", ParameterModifier.Ref),
                Parameter("count", "i32", defaultValue: new IntLiteralNode(Span, 1)),
            ],
            []);
        var use = Function(
            "use",
            "Use",
            "void",
            [],
            [
                Binding("value", "i32", new IntLiteralNode(Span, 1)),
                Binding("a", "i32", Call("Choose", [new IntLiteralNode(Span, 1)])),
                Binding("b", "str", Call("Choose", [
                    new StringLiteralNode(Span, "a"),
                    new StringLiteralNode(Span, "b"),
                ])),
                Binding("c", "i32", Call(
                    "Choose",
                    [new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 9)],
                    argumentNames: [null, "fallback"])),
                Binding("d", "i32", Call(
                    "Touch",
                    [new ReferenceNode(Span, "value")],
                    argumentModifiers: ["ref"])),
            ]);

        var bound = Bind(Module([optional, paramsOverload, refOptional, use]), out var diagnostics);
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound.Functions[^1])
            .OfType<BoundCallExpression>()
            .ToArray();

        Assert.True(bound.Functions[0].Symbol.Parameters[1].IsOptional);
        Assert.True(bound.Functions[1].Symbol.Parameters[0].Modifier.HasFlag(ParameterModifier.Params));
        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Same(bound.Functions[0].Symbol, calls[0].ResolvedSymbol);
        Assert.Same(bound.Functions[1].Symbol, calls[1].ResolvedSymbol);
        Assert.Same(bound.Functions[0].Symbol, calls[2].ResolvedSymbol);
        Assert.Same(bound.Functions[2].Symbol, calls[3].ResolvedSymbol);
    }

    [Fact]
    public void BaseMemberAndBaseConstructorCalls_ResolveAgainstTrackedBaseClass()
    {
        const string source = """
            §M{Test}
              §CL{Base:pub}
                §FLD{i32:value:prot}
                §CTOR{pub} (i32:x)
                  §ASSIGN value x
                §MT{Pick:pub} (i32:x) -> i32
                  §R x
              §CL{Derived:Base:pub}
                §CTOR{pub} (i32:x)
                  §BASE §A x §/BASE
                §MT{Use:pub} () -> i32
                  §R §C{base.Pick} §A INT:1 §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var baseCtor = bound.Functions.Single(function =>
            function.Symbol.Name == "Base..ctor");
        var baseMethod = bound.Functions.Single(function =>
            function.Symbol.Name == "Base.Pick");
        var derivedCtor = bound.Functions.Single(function =>
            function.Symbol.Name == "Derived..ctor");
        var use = bound.Functions.Single(function =>
            function.Symbol.Name == "Derived.Use");
        var ctorCall = Assert.IsType<BoundCallStatement>(derivedCtor.Body[0]);
        var methodCall = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(use.Body)).Expression);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Same(baseCtor.Symbol, ctorCall.ResolvedSymbol);
        Assert.Same(baseMethod.Symbol, methodCall.ResolvedSymbol);
    }

    [Fact]
    public void PreprocessorWrappedMethods_AreRegisteredBoundAndResolvedTogether()
    {
        const string source = """
            §M{Test}
              §CL{Worker:pub}
                §PP{FEATURE}
                  §MT{Do:pub} () -> i32
                    §R 1
                §/PP{FEATURE}
                §MT{Run:pub} () -> i32
                  §R §C{Do} §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var wrapped = bound.Functions.Single(function =>
            function.Symbol.Name == "Worker.Do");
        var run = bound.Functions.Single(function =>
            function.Symbol.Name == "Worker.Run");
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(run.Body)).Expression);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Same(wrapped.Symbol, call.ResolvedSymbol);
        Assert.Equal(
            wrapped.SymbolId,
            Assert.Single(CallGraphAnalysis.BuildResolved(bound).ForwardGraph[run.SymbolId]).Callee);
    }

    [Fact]
    public void MutuallyExclusivePreprocessorMethods_ShareSignatureWithoutFalseDuplicate()
    {
        var feature = Method(
            "feature",
            "Pick",
            Visibility.Public,
            [new ReturnStatementNode(Span, new IntLiteralNode(Span, 1))]);
        var fallback = Method(
            "fallback",
            "Pick",
            Visibility.Public,
            [
                new PrintStatementNode(Span, new StringLiteralNode(Span, "fallback")),
                new ReturnStatementNode(Span, new IntLiteralNode(Span, 2)),
            ],
            new EffectsNode(
                Span,
                new Dictionary<string, string> { ["io"] = "console_write" }));
        var runMethod = Method(
            "run",
            "Run",
            Visibility.Public,
            [new ReturnStatementNode(Span, Call("Pick", []))]);
        var module = Module(
        [
            Class(
                "c1",
                "Worker",
                [runMethod],
                preprocessorBlocks:
                [
                    MemberPreprocessor(
                        [feature],
                        MemberPreprocessor([fallback])),
                ]),
        ]);

        var bound = Bind(module, out var diagnostics);
        var alternatives = bound.Functions
            .Where(function => function.Symbol.Name == "Worker.Pick")
            .ToArray();
        var run = bound.Functions.Single(function =>
            function.Symbol.Name == "Worker.Run");
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(run.Body)).Expression);
        var graphEdges = CallGraphAnalysis.BuildResolved(bound).ForwardGraph[run.SymbolId];

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DuplicateFunctionSignature);
        Assert.Equal(2, alternatives.Length);
        Assert.Equal(
            alternatives.Select(function => function.SymbolId).OrderBy(id => id.Value).ToArray(),
            call.ResolvedSymbols.Select(symbol => symbol.Id).OrderBy(id => id.Value).ToArray());
        Assert.Equal(
            alternatives.Select(function => function.SymbolId).OrderBy(id => id.Value).ToArray(),
            graphEdges.Select(edge => edge.Callee).OrderBy(id => id.Value).ToArray());

        var effectDiagnostics = new DiagnosticBag();
        new EffectEnforcementPass(effectDiagnostics).Enforce(module);
        Assert.Contains(effectDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
    }

    [Fact]
    public void MutuallyExclusivePreprocessorMethods_WithDifferentReturns_UseObject()
    {
        var feature = Method(
            "feature",
            "Pick",
            Visibility.Public,
            [new ReturnStatementNode(Span, new IntLiteralNode(Span, 1))]);
        var fallback = Method(
            "fallback",
            "Pick",
            Visibility.Public,
            [new ReturnStatementNode(Span, new StringLiteralNode(Span, "fallback"))],
            returnType: "str");
        var runMethod = Method(
            "run",
            "Run",
            Visibility.Public,
            [new ReturnStatementNode(Span, Call("Pick", []))],
            returnType: "object");
        var module = Module(
        [
            Class(
                "c1",
                "Worker",
                [runMethod],
                preprocessorBlocks:
                [
                    MemberPreprocessor(
                        [feature],
                        MemberPreprocessor([fallback])),
                ]),
        ]);

        var bound = Bind(module, out var diagnostics);
        var alternatives = bound.Functions
            .Where(function => function.Symbol.Name == "Worker.Pick")
            .ToArray();
        var run = bound.Functions.Single(function =>
            function.Symbol.Name == "Worker.Run");
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(run.Body)).Expression);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Equal("OBJECT", call.Type.DisplayString);
        Assert.Equal(
            alternatives.Select(function => function.SymbolId).OrderBy(id => id.Value).ToArray(),
            call.ResolvedSymbols.Select(symbol => symbol.Id).OrderBy(id => id.Value).ToArray());
    }

    [Fact]
    public void MutuallyExclusiveFieldsPropertiesAndConstructors_AreExplicitAlternatives()
    {
        static ClassFieldNode Field(string typeName) =>
            new(
                Span,
                "Value",
                typeName,
                Visibility.Public,
                null,
                new AttributeCollection());

        static PropertyNode Property(string id, string typeName) =>
            new(
                Span,
                id,
                "Name",
                typeName,
                Visibility.Public,
                getter: null,
                setter: null,
                initer: null,
                defaultValue: null,
                new AttributeCollection());

        static ConstructorNode Constructor(string id) =>
            new(
                Span,
                id,
                Visibility.Public,
                Array.Empty<ParameterNode>(),
                Array.Empty<RequiresNode>(),
                initializer: null,
                Array.Empty<StatementNode>(),
                new AttributeCollection());

        var feature = new MemberPreprocessorBlockNode(
            Span,
            "FEATURE",
            [Field("i32")],
            [Property("p1", "str")],
            [Constructor("c1")],
            Array.Empty<MethodNode>(),
            Array.Empty<EventDefinitionNode>(),
            Array.Empty<OperatorOverloadNode>(),
            elseBranch: new MemberPreprocessorBlockNode(
                Span,
                "ELSE",
                [Field("str")],
                [Property("p2", "i32")],
                [Constructor("c2")],
                Array.Empty<MethodNode>(),
                Array.Empty<EventDefinitionNode>(),
                Array.Empty<OperatorOverloadNode>()));
        var read = Method(
            "read",
            "Read",
            Visibility.Public,
            [new ReturnStatementNode(Span, new ReferenceNode(Span, "Value"))],
            returnType: "object");
        var create = Method(
            "create",
            "Create",
            Visibility.Public,
            [
                new ReturnStatementNode(
                    Span,
                    new NewExpressionNode(
                        Span,
                        "Worker",
                        Array.Empty<string>(),
                        Array.Empty<ExpressionNode>())),
            ],
            returnType: "Worker");
        var cls = new ClassDefinitionNode(
            Span,
            "c1",
            "Worker",
            isAbstract: false,
            isSealed: false,
            isPartial: false,
            isStatic: false,
            baseClass: null,
            Array.Empty<string>(),
            Array.Empty<TypeParameterNode>(),
            Array.Empty<ClassFieldNode>(),
            Array.Empty<PropertyNode>(),
            Array.Empty<ConstructorNode>(),
            [read, create],
            Array.Empty<EventDefinitionNode>(),
            Array.Empty<OperatorOverloadNode>(),
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public,
            preprocessorBlocks: [feature]);

        var bound = Bind(Module([cls]), out var diagnostics);
        var valueReference = Assert.IsType<BoundVariableExpression>(
            Assert.IsType<BoundReturnStatement>(
                Assert.Single(bound.Functions.Single(function =>
                    function.Symbol.Name == "Worker.Read").Body)).Expression);
        var creation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(
                Assert.Single(bound.Functions.Single(function =>
                    function.Symbol.Name == "Worker.Create").Body)).Expression);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.DuplicateDefinition
                or DiagnosticCode.DuplicateFunctionSignature);
        Assert.Equal(2, valueReference.ResolvedSymbols.Count);
        Assert.Equal("OBJECT", valueReference.Type.DisplayString);
        Assert.Equal(2, creation.ResolvedConstructors.Count);
        Assert.All(valueReference.ResolvedSymbols, symbol =>
            Assert.NotNull(symbol.ConditionalAlternative));
        Assert.All(creation.ResolvedConstructors, symbol =>
            Assert.NotNull(symbol.ConditionalAlternative));
        Assert.Equal(
            2,
            bound.SymbolsById.Values
                .OfType<VariableSymbol>()
                .Count(symbol => symbol.Name == "Name" && symbol.IsProperty));
    }

    [Fact]
    public void DuplicatePreprocessorMethods_InSameBranchRemainErrors()
    {
        var module = Module(
        [
            Class(
                "c1",
                "Worker",
                [],
                preprocessorBlocks:
                [
                    MemberPreprocessor(
                        [
                            Method(
                                "first",
                                "Pick",
                                Visibility.Public,
                                [new ReturnStatementNode(
                                    Span,
                                    new IntLiteralNode(Span, 1))]),
                            Method(
                                "second",
                                "Pick",
                                Visibility.Public,
                                [new ReturnStatementNode(
                                    Span,
                                    new IntLiteralNode(Span, 2))]),
                        ],
                        MemberPreprocessor(
                        [
                            Method(
                                "fallback",
                                "Pick",
                                Visibility.Public,
                                [new ReturnStatementNode(
                                    Span,
                                    new IntLiteralNode(Span, 3))]),
                        ])),
                ]),
        ]);

        _ = Bind(module, out var diagnostics);

        Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.DuplicateFunctionSignature));
    }

    [Fact]
    public void InheritedBareAndThisCalls_ResolveExactAccessibleBaseMember()
    {
        const string source = """
            §M{Test}
              §CL{Base:pub}
                §MT{Pick:prot} (i32:value) -> i32
                  §E{cw}
                  §P STR:"pick"
                  §R value
                §MT{Hidden:priv} () -> i32
                  §R 0
              §CL{Derived:Base:pub}
                §MT{Use:pub} () -> i32
                  §B{first:i32} §C{Pick} §A INT:1 §/C
                  §B{second:i32} §C{this.Pick} §A INT:2 §/C
                  §C{Hidden} §/C
                  §R (+ first second)
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var basePick = bound.Functions.Single(function =>
            function.Symbol.Name == "Base.Pick");
        var use = bound.Functions.Single(function =>
            function.Symbol.Name == "Derived.Use");
        var calls = BoundNodeHelpers.DescendantsAndSelf(use)
            .Where(node => node is BoundCallExpression or BoundCallStatement)
            .ToArray();
        var resolvedPickCalls = calls
            .Select(node => node switch
            {
                BoundCallExpression expression => expression.ResolvedSymbol,
                BoundCallStatement statement => statement.ResolvedSymbol,
                _ => null,
            })
            .Where(symbol => symbol?.Name == "Base.Pick")
            .ToArray();
        var hiddenCall = Assert.Single(calls.OfType<BoundCallStatement>());

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Equal(2, resolvedPickCalls.Length);
        Assert.All(resolvedPickCalls, symbol => Assert.Same(basePick.Symbol, symbol));
        Assert.Null(hiddenCall.ResolvedSymbol);
        Assert.Equal(
            new[] { basePick.SymbolId, basePick.SymbolId },
            CallGraphAnalysis.BuildResolved(bound).ForwardGraph[use.SymbolId]
                .Select(edge => edge.Callee)
                .ToArray());

        var effectDiagnostics = new DiagnosticBag();
        var module = Parse(source, effectDiagnostics);
        var callGraph = CallGraphAnalysis.Build(module);
        var legacyUse = callGraph.Functions.Values.Single(function =>
            function.Name == "Use");
        var legacyHidden = callGraph.Functions.Values.Single(function =>
            function.Name == "Hidden");
        Assert.True(callGraph.IsBoundResolutionComplete);
        Assert.Contains(callGraph.UnresolvedCalls, call =>
            call.CallerId == legacyUse.Id && call.Target == "Hidden");
        Assert.DoesNotContain(
            legacyUse.Id,
            callGraph.ReverseGraph[legacyHidden.Id]);
        new EffectEnforcementPass(effectDiagnostics).Enforce(module);
        Assert.Contains(effectDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Use", StringComparison.Ordinal));
    }

    [Fact]
    public void PreprocessorWrappedConstructorsAndProperties_AreBoundIntoModule()
    {
        const string source = """
            §M{Test}
              §CL{Worker:pub}
                §PP{FEATURE}
                  §CTOR{pub} ()
                    §P STR:"created"
                  §PROP{Value:i32:pub}
                    §GET
                      §R INT:1
                    §/GET
                  §/PROP
                §/PP{FEATURE}
            """;

        var bound = ParseAndBind(source, out var diagnostics);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Contains(bound.Functions, function =>
            function.Symbol.Name == "Worker..ctor"
            && function.MemberKind == BoundMemberKind.Constructor);
        Assert.Contains(bound.Functions, function =>
            function.Symbol.Name == "Worker.Value.get"
            && function.MemberKind == BoundMemberKind.PropertyGetter);
    }

    [Fact]
    public void ForeachElementInference_ResolvesInternalOverloadWithoutNameFallback()
    {
        const string source = """
            §M{Test}
              §F{Echo:priv} (str:value) -> str
                §R value
              §F{Use:pub} ([str]:values) -> void
                §EACH{item} values
                  §B{copy:str} §C{Echo} §A item §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var echo = bound.Functions.Single(function => function.Symbol.Name == "Echo");
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.Single(
            BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>());

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Same(echo.Symbol, call.ResolvedSymbol);
    }

    [Fact]
    public void ResolvedCallGraph_UsesSymbolIds_AndRetainsExternalCalls()
    {
        var callee = Function("f1", "Callee", "i32", [Parameter("x", "i32")], []);
        var caller = Function(
            "f2",
            "Caller",
            "void",
            [],
            [
                Binding("value", "i32", Call("Callee", [new IntLiteralNode(Span, 1)])),
                new CallStatementNode(
                    Span,
                    "External.Log",
                    false,
                    [new ReferenceNode(Span, "value")],
                    new AttributeCollection()),
            ]);
        var bound = Bind(Module([callee, caller]), out var diagnostics);

        var graph = CallGraphAnalysis.BuildResolved(bound);
        var edge = Assert.Single(graph.ForwardGraph[bound.Functions[1].SymbolId]);
        var unresolved = Assert.Single(graph.UnresolvedCalls);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Equal(bound.Functions[0].SymbolId, edge.Callee);
        Assert.Equal(bound.Functions[1].SymbolId, Assert.Single(
            graph.ReverseGraph[bound.Functions[0].SymbolId]));
        Assert.Equal("External.Log", unresolved.Target);
    }

    [Fact]
    public void ResolvedCallGraph_BinderExpressionCall_RemainsExplicitlyUnresolved()
    {
        const string source = """
            §M{Test}
              §F{Use:pub} () -> object
                §R §C §NEW{object} §/NEW.GetType §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var use = Assert.Single(bound.Functions);
        var expressionCall = Assert.Single(
            BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundCallExpression>());
        Assert.NotNull(expressionCall.EvaluatedReceiver);
        Assert.Equal("GetType", expressionCall.ResolvedMetadataMethod?.Name);
        var graph = CallGraphAnalysis.BuildResolved(bound);
        var unresolved = Assert.Single(graph.UnresolvedCalls.Where(call =>
            call.Target == "<expression-call>"));

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        Assert.Empty(graph.ForwardGraph[use.SymbolId]);
        Assert.Equal(use.SymbolId, unresolved.Caller);
        Assert.Equal("<expression-call>", unresolved.Target);
        Assert.Equal(expressionCall.Span, unresolved.Span);

        var astDiagnostics = new DiagnosticBag();
        var ast = Parse(source, astDiagnostics);
        var astGraph = CallGraphAnalysis.Build(ast);
        Assert.False(
            astDiagnostics.HasErrors,
            string.Join(", ", astDiagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(astGraph.UnresolvedCalls, call =>
            call.CallerId == Assert.Single(ast.Functions).Id
            && call.Target == "<expression-call>"
            && call.Span == expressionCall.Span);
    }

    private static bool IsOverloadDiagnostic(Diagnostic diagnostic) =>
        diagnostic.Code is DiagnosticCode.DuplicateFunctionSignature
            or DiagnosticCode.AmbiguousOverload
            or DiagnosticCode.NoMatchingOverload;

    private static BoundModule Bind(ModuleNode module, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        return new Binder(diagnostics).Bind(module);
    }

    private static BoundModule ParseAndBind(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var module = Parse(source, diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        return new Binder(diagnostics).Bind(module);
    }

    private static ModuleNode Parse(string source, DiagnosticBag diagnostics)
    {
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        return new Parser(tokens, diagnostics).Parse();
    }

    private static ModuleNode Module(IReadOnlyList<FunctionNode> functions) =>
        new(
            Span,
            "m1",
            "Test",
            Array.Empty<UsingDirectiveNode>(),
            functions,
            new AttributeCollection());

    private static ModuleNode Module(IReadOnlyList<ClassDefinitionNode> classes) =>
        new(
            Span,
            "m1",
            "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            classes,
            Array.Empty<FunctionNode>(),
            new AttributeCollection());

    private static ClassDefinitionNode Class(
        string id,
        string name,
        IReadOnlyList<MethodNode> methods,
        string? baseClass = null,
        IReadOnlyList<MemberPreprocessorBlockNode>? preprocessorBlocks = null) =>
        new(
            Span,
            id,
            name,
            isAbstract: false,
            isSealed: false,
            isPartial: false,
            isStatic: false,
            baseClass,
            Array.Empty<string>(),
            Array.Empty<TypeParameterNode>(),
            Array.Empty<ClassFieldNode>(),
            Array.Empty<PropertyNode>(),
            Array.Empty<ConstructorNode>(),
            methods,
            Array.Empty<EventDefinitionNode>(),
            Array.Empty<OperatorOverloadNode>(),
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public,
            preprocessorBlocks: preprocessorBlocks);

    private static MemberPreprocessorBlockNode MemberPreprocessor(
        IReadOnlyList<MethodNode> methods,
        MemberPreprocessorBlockNode? elseBranch = null) =>
        new(
            Span,
            "FEATURE",
            Array.Empty<ClassFieldNode>(),
            Array.Empty<PropertyNode>(),
            Array.Empty<ConstructorNode>(),
            methods,
            Array.Empty<EventDefinitionNode>(),
            Array.Empty<OperatorOverloadNode>(),
            elseBranch);

    private static MethodNode Method(
        string id,
        string name,
        Visibility visibility,
        IReadOnlyList<StatementNode> body,
        EffectsNode? effects = null,
        string returnType = "i32") =>
        new(
            new TextSpan(
                100 + id.Aggregate(0, (value, character) => value + character),
                10,
                1,
                1),
            id,
            name,
            visibility,
            MethodModifiers.None,
            Array.Empty<TypeParameterNode>(),
            Array.Empty<ParameterNode>(),
            new OutputNode(Span, returnType),
            effects,
            Array.Empty<RequiresNode>(),
            Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection());

    private static FunctionNode Function(
        string id,
        string name,
        string returnType,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<StatementNode> body,
        IReadOnlyList<string>? typeParameters = null) =>
        new(
            Span,
            id,
            name,
            Visibility.Public,
            typeParameters?.Select(typeParameter =>
                new TypeParameterNode(
                    Span,
                    typeParameter,
                    Array.Empty<TypeConstraintNode>()))
                .ToArray()
                ?? Array.Empty<TypeParameterNode>(),
            parameters,
            new OutputNode(Span, returnType),
            null,
            Array.Empty<RequiresNode>(),
            Array.Empty<EnsuresNode>(),
            body,
            new AttributeCollection());

    private static ParameterNode Parameter(
        string name,
        string typeName,
        ParameterModifier modifier = ParameterModifier.None,
        ExpressionNode? defaultValue = null) =>
        new(
            Span,
            name,
            typeName,
            modifier,
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>(),
            defaultValue);

    private static BindStatementNode Binding(
        string name,
        string? typeName,
        ExpressionNode initializer) =>
        new(
            Span,
            name,
            typeName,
            isMutable: false,
            initializer,
            new AttributeCollection());

    private static CallExpressionNode Call(
        string target,
        IReadOnlyList<ExpressionNode> arguments,
        IReadOnlyList<string?>? argumentNames = null,
        IReadOnlyList<string?>? argumentModifiers = null,
        IReadOnlyList<string>? typeArguments = null) =>
        new(
            Span,
            target,
            arguments,
            argumentNames,
            argumentModifiers,
            typeArguments);
}
