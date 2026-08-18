using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

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
    public void SomeNullCoalesce_InfersPayloadType_AndResolvesOverload()
    {
        const string source = """
            §M{m1:Test}
              §F{f1:Pick:pri} (i32:value) -> i32
                §R value
              §F{f2:Pick:pri} (str:value) -> str
                §R value
              §F{f3:Use:pub} () -> i32
                §R §C{Pick} §A (?? §SM INT:1 INT:0) §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var pickInt = bound.Functions.Single(function =>
            function.Symbol.Name == "Pick"
            && TypeIdentity.Canonicalize(function.Symbol.ReturnType) == "INT");
        var use = bound.Functions.Single(function => function.Symbol.Name == "Use");
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(use.Body)).Expression);

        Assert.DoesNotContain(diagnostics, IsOverloadDiagnostic);
        Assert.Equal("INT", Assert.Single(call.Arguments).TypeName);
        Assert.Same(pickInt.Symbol, call.ResolvedSymbol);
    }

    [Fact]
    public void MemberOverloads_ResolveBareQualifiedAndStatementCalls()
    {
        const string source = """
            §M{m1:Test}
              §F{f1:UseQualified:pub} (Converter:c) -> i32
                §R §C{c.Convert} §A INT:1 §/C

              §CL{c1:Converter:pub}
                §FLD{i32:_value:priv}
                §MT{m1:Convert:pub} (i32:x) -> i32
                  §R x
                §MT{m2:Convert:pub} (str:x) -> str
                  §R x
                §MT{m3:Convert:pub} (i32:x, i32:y) -> bool
                  §R BOOL:true
                §MT{m4:Use:pub}
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
        Assert.Equal("OBJECT", call.TypeName);
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
            §M{m1:Test}
              §F{f1:Pick:pri} (i32:value) -> i32
                §R value
              §F{f2:Use:pub} () -> i32
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
            §M{m1:Test}
              §F{f1:Pick<T>:pri} (T:value, i32:other) -> T
                §R value
              §F{f2:Pick<T>:pri} (i32:value, T:other) -> T
                §R other
              §F{f3:Use:pub} () -> i32
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
            §M{m1:Test}
              §F{f1:Use:pub} () -> void
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
            §M{m1:Test}
              §F{f1:Create:pub} () -> object
                §R §NEW{Box<List<Widget>>} §/NEW
              §CL{c1:Widget:pub}
              §CL{c2:List:pub}<T>
              §CL{c3:Box:pub}<T>
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
    public void TypeIdentity_NormalizesAllOptionSpellingsRecursivelyForOverloads()
    {
        var spellings = new[]
        {
            "?i32",
            "i32?",
            "Option<i32>",
            "OPTION[inner=i32]",
        };

        Assert.All(spellings, spelling =>
            Assert.Equal("OPTION<INT>", TypeIdentity.Canonicalize(spelling)));
        Assert.Equal(
            "OPTION<OPTION<INT>>",
            TypeIdentity.Canonicalize("OPTION[inner=Option<i32>]"));

        var scope = new Scope();
        var expected = new FunctionSymbol(
            "Pick",
            "i32",
            [new VariableSymbol("value", "?i32", false, true)]);
        scope.DeclareOverload(expected);

        foreach (var spelling in spellings)
        {
            var resolution = scope.ResolveOverload("Pick", [spelling]);
            Assert.Same(expected, resolution.Function);
        }
    }

    [Fact]
    public void StatementGenericCalls_RetainTypeArgumentsAndResolveGenericArity()
    {
        const string source = """
            §M{m1:Test}
              §F{f1:Select<T>:pri} (T:value) -> T
                §R value
              §F{f2:Select<T,U>:pri} (T:value) -> T
                §R value
              §F{f3:Use:pub} () -> void
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
        Assert.Contains("§C{Select<i32>}", new CalorEmitter().Emit(module));
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
            §M{m1:Test}
              §F{stable:Keep:pub} (i32:value) -> i32
                §B{local:i32} value
                §R local
            """;
        const string inserted = """
            §M{m1:Test}
              §F{before:Before:pub} () -> i32
                §R 0
              §F{stable:Keep:pub} (i32:value) -> i32
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
            §M{m1:Test}
              §F{f1:Update:pub} () -> i32
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
            §M{m1:Test}
              §F{f1:Update:pub} () -> i32
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
            §M{m1:Test}
              §CL{c1:Base:pub}
                §FLD{i32:value:prot}
                §CTOR{ctor1:pub} (i32:x)
                  §ASSIGN value x
                §MT{m1:Pick:pub} (i32:x) -> i32
                  §R x
              §CL{c2:Derived:Base:pub}
                §CTOR{ctor2:pub} (i32:x)
                  §BASE §A x §/BASE
                §MT{m2:Use:pub} () -> i32
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
            §M{m1:Test}
              §CL{c1:Worker:pub}
                §PP{FEATURE}
                  §MT{m1:Do:pub} () -> i32
                    §R 1
                §/PP{FEATURE}
                §MT{m2:Run:pub} () -> i32
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
        Assert.Equal("OBJECT", call.TypeName);
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
        Assert.Equal("OBJECT", valueReference.TypeName);
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
            §M{m1:Test}
              §CL{c1:Base:pub}
                §MT{pick:Pick:prot} (i32:value) -> i32
                  §E{cw}
                  §P STR:"pick"
                  §R value
                §MT{hidden:Hidden:pri} () -> i32
                  §R 0
              §CL{c2:Derived:Base:pub}
                §MT{use:Use:pub} () -> i32
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
            §M{m1:Test}
              §CL{c1:Worker:pub}
                §PP{FEATURE}
                  §CTOR{ctor1:pub} ()
                    §P STR:"created"
                  §PROP{p1:Value:i32:pub}
                    §GET
                      §R INT:1
                    §/GET
                  §/PROP{p1}
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
            §M{m1:Test}
              §F{f1:Echo:pri} (str:value) -> str
                §R value
              §F{f2:Use:pub} ([str]:values) -> void
                §EACH{e1:item} values
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
            §M{m1:Test}
              §F{f1:Use:pub} () -> object
                §R §C §NEW{object} §/NEW.GetType §/C
            """;

        var bound = ParseAndBind(source, out var diagnostics);
        var use = Assert.Single(bound.Functions);
        var expressionCall = Assert.Single(
            BoundNodeHelpers.DescendantsAndSelf(use).OfType<BoundExpressionCall>());
        var graph = CallGraphAnalysis.BuildResolved(bound);
        var unresolved = Assert.Single(graph.UnresolvedCalls.Where(call =>
            call.Target == "<expression-call>"));

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        Assert.Empty(graph.ForwardGraph[use.SymbolId]);
        Assert.Equal(use.SymbolId, unresolved.Caller);
        Assert.Equal("<expression-call>", unresolved.Target);
        Assert.Equal(expressionCall.Span, unresolved.Span);

        var astDiagnostics = new DiagnosticBag();
        var astGraph = CallGraphAnalysis.Build(Parse(source, astDiagnostics));
        Assert.False(
            astDiagnostics.HasErrors,
            string.Join(", ", astDiagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains(astGraph.UnresolvedCalls, call =>
            call.CallerId == "f1"
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
