using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
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
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        return new Binder(diagnostics).Bind(module);
    }

    private static ModuleNode Module(IReadOnlyList<FunctionNode> functions) =>
        new(
            Span,
            "m1",
            "Test",
            Array.Empty<UsingDirectiveNode>(),
            functions,
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
