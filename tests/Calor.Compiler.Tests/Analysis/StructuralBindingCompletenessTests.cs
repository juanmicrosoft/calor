using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

public sealed class StructuralBindingCompletenessTests
{
    private static TextSpan Span => new(0, 1, 1, 1);

    [Fact]
    public void DecimalLiteral_RetainsExactDecimalPayload()
    {
        const decimal value = 7922816251426433759354395033.5m;

        var bound = Bind(new DecimalLiteralNode(Span, value), out _);

        var literal = Assert.IsType<BoundDecimalLiteral>(bound);
        Assert.Equal(value, literal.Value);
        Assert.Equal("DECIMAL", literal.TypeName);
    }

    [Fact]
    public void CastAsIsAndPatternTests_RetainOperandAndTargetMetadata()
    {
        var operand = new ReferenceNode(Span, "value");

        var cast = Assert.IsType<BoundTypeOperationExpression>(
            Bind(new TypeOperationNode(Span, TypeOp.Cast, operand, "DECIMAL"), out _, ("value", "OBJECT")));
        Assert.Equal(TypeOp.Cast, cast.Operation);
        Assert.Equal("DECIMAL", cast.TypeName);
        Assert.Equal("DECIMAL", cast.TargetType);
        Assert.Equal("value", Assert.IsType<BoundVariableExpression>(Assert.Single(cast.Children)).Variable.Name);

        var asExpression = Assert.IsType<BoundTypeOperationExpression>(
            Bind(new TypeOperationNode(Span, TypeOp.As, operand, "Widget"), out _, ("value", "OBJECT")));
        Assert.Equal(TypeOp.As, asExpression.Operation);
        Assert.Equal("Widget", asExpression.TypeName);
        Assert.Single(asExpression.Children);

        var isExpression = Assert.IsType<BoundTypeOperationExpression>(
            Bind(new TypeOperationNode(Span, TypeOp.Is, operand, "Widget"), out _, ("value", "OBJECT")));
        Assert.Equal(TypeOp.Is, isExpression.Operation);
        Assert.Equal("BOOL", isExpression.TypeName);
        Assert.Equal("Widget", isExpression.TargetType);
        Assert.Single(isExpression.Children);

        var pattern = Assert.IsType<BoundIsPatternExpression>(
            Bind(new IsPatternNode(Span, operand, "Widget", "widget"), out _, ("value", "OBJECT")));
        Assert.Equal("BOOL", pattern.TypeName);
        Assert.Equal("Widget", pattern.TargetType);
        Assert.Equal("widget", pattern.VariableName);
        Assert.Single(pattern.Children);
    }

    [Fact]
    public void IndexAndCollections_RetainAllEvaluatedChildrenAndStableTypes()
    {
        var index = Assert.IsType<BoundArrayAccess>(
            Bind(
                new ArrayAccessNode(
                    Span,
                    new ReferenceNode(Span, "items"),
                    new ReferenceNode(Span, "index")),
                out _,
                ("items", "STRING[]"),
                ("index", "INT")));
        Assert.Equal("STRING", index.TypeName);
        Assert.Equal(2, index.Children.Count);
        Assert.True(BoundNodeHelpers.ContainsArrayAccess(index, out var array, out var indexExpression));
        Assert.Same(index.Array, array);
        Assert.Same(index.Index, indexExpression);

        var list = Assert.IsType<BoundListCreation>(
            Bind(
                new ListCreationNode(
                    Span,
                    "l",
                    "items",
                    "i32",
                    [new IntLiteralNode(Span, 1), new IntLiteralNode(Span, 2)],
                    new AttributeCollection()),
                out _));
        Assert.Equal("List<i32>", list.TypeName);
        Assert.Equal(2, list.Children.Count);

        var dictionary = Assert.IsType<BoundDictionaryCreation>(
            Bind(
                new DictionaryCreationNode(
                    Span,
                    "d",
                    "values",
                    "str",
                    "i32",
                    [
                        new KeyValuePairNode(
                            Span,
                            new StringLiteralNode(Span, "one"),
                            new IntLiteralNode(Span, 1)),
                        new KeyValuePairNode(
                            Span,
                            new StringLiteralNode(Span, "two"),
                            new IntLiteralNode(Span, 2)),
                    ],
                    new AttributeCollection()),
                out _));
        Assert.Equal("Dictionary<str,i32>", dictionary.TypeName);
        Assert.Equal(4, dictionary.Children.Count);
        Assert.Equal(2, dictionary.Entries.Count);
        Assert.Empty(dictionary.Attributes.All());
    }

    [Fact]
    public void NewExpression_RetainsArgumentsAndInitializerValues()
    {
        var expression = new NewExpressionNode(
            Span,
            "Person",
            ["T"],
            [new IntLiteralNode(Span, 7)],
            [
                new ObjectInitializerAssignment("Name", new StringLiteralNode(Span, "Ada")),
                new ObjectInitializerAssignment("Age", new IntLiteralNode(Span, 42)),
            ]);

        var bound = Assert.IsType<BoundNewExpression>(Bind(expression, out _));

        Assert.Equal("Person", bound.TypeName);
        Assert.Equal(["T"], bound.TypeArguments);
        Assert.Single(bound.Arguments);
        Assert.Equal(["Name", "Age"], bound.Initializers.Select(initializer => initializer.MemberName));
        Assert.Equal(3, bound.Children.Count);
        Assert.Collection(
            bound.Initializers,
            initializer => Assert.IsType<BoundStringLiteral>(initializer.Value),
            initializer => Assert.IsType<BoundIntLiteral>(initializer.Value));
    }

    [Fact]
    public void ConditionalMatchAndCoalesce_UseStableCommonResultTypes()
    {
        var incompatibleConditional = Assert.IsType<BoundConditionalExpression>(
            Bind(
                new ConditionalExpressionNode(
                    Span,
                    new BoolLiteralNode(Span, true),
                    new IntLiteralNode(Span, 1),
                    new StringLiteralNode(Span, "one")),
                out _));
        Assert.Equal("OBJECT", incompatibleConditional.TypeName);

        var throwingConditional = Assert.IsType<BoundConditionalExpression>(
            Bind(
                new ConditionalExpressionNode(
                    Span,
                    new BoolLiteralNode(Span, true),
                    new IntLiteralNode(Span, 1),
                    new ThrowExpressionNode(
                        Span,
                        new NewExpressionNode(Span, "Exception", [], []))),
                out _));
        Assert.Equal("INT", throwingConditional.TypeName);

        var coalesce = Assert.IsType<BoundStructuralExpression>(
            Bind(
                new NullCoalesceNode(
                    Span,
                    new NoneExpressionNode(Span, "i32"),
                    new IntLiteralNode(Span, 0)),
                out _));
        Assert.Equal("INT", coalesce.TypeName);

        var match = new MatchExpressionNode(
            Span,
            "m",
            new ReferenceNode(Span, "value"),
            [
                new MatchCaseNode(
                    Span,
                    new LiteralPatternNode(Span, new IntLiteralNode(Span, 1)),
                    null,
                    [new ReturnStatementNode(Span, new IntLiteralNode(Span, 1))]),
                new MatchCaseNode(
                    Span,
                    new WildcardPatternNode(Span),
                    null,
                    [new ReturnStatementNode(Span, new StringLiteralNode(Span, "other"))]),
            ],
            new AttributeCollection());
        var boundMatch = Assert.IsType<BoundMatchExpression>(
            Bind(match, out _, ("value", "INT")));
        Assert.Equal("OBJECT", boundMatch.TypeName);
        Assert.Equal(2, boundMatch.Cases.Count);
        Assert.All(boundMatch.Cases, matchCase => Assert.NotNull(matchCase.Result));
    }

    [Fact]
    public void StructuralWrappers_BindNestedDiagnosticSeedsAndExposeThemToTraversal()
    {
        static ReferenceNode Missing() => new(Span, "missing");

        ExpressionNode[] expressions =
        [
            new ArrayCreationNode(
                Span,
                "a",
                "items",
                "i32",
                Missing(),
                [],
                new AttributeCollection()),
            new AwaitExpressionNode(Span, Missing()),
            new InterpolatedStringNode(
                Span,
                [new InterpolatedStringExpressionNode(Span, Missing(), "D2", "10")]),
            new LambdaExpressionNode(
                Span,
                "l",
                [],
                null,
                false,
                Missing(),
                null,
                new AttributeCollection()),
            new RecordCreationNode(
                Span,
                "Person",
                [new FieldAssignmentNode(Span, "Name", Missing())]),
            new StackAllocNode(Span, "i32", null, [Missing()]),
            new StringOperationNode(Span, StringOp.ToString, [Missing()]),
            new ForallExpressionNode(
                Span,
                [new QuantifierVariableNode(Span, "i", "i32")],
                Missing()),
            new WithExpressionNode(
                Span,
                new ReferenceNode(Span, "target"),
                [new WithPropertyAssignmentNode(Span, "Name", Missing())]),
        ];

        foreach (var expression in expressions)
        {
            var bound = Bind(expression, out var diagnostics);

            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
                    && diagnostic.Message.Contains("'missing'", StringComparison.Ordinal));
            Assert.Contains(
                BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundVariableExpression>(),
                variable => variable.Variable.Name == "missing");
            Assert.All(
                BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundExpression>(),
                descendant => Assert.False(string.IsNullOrWhiteSpace(descendant.TypeName)));
        }
    }

    [Fact]
    public void GeneralTraversal_ReachesDivisionInsideStructuralWrapper()
    {
        var expression = new SomeExpressionNode(
            Span,
            new ArrayCreationNode(
                Span,
                "a",
                "values",
                "i32",
                null,
                [
                    new BinaryOperationNode(
                        Span,
                        BinaryOperator.Divide,
                        new IntLiteralNode(Span, 1),
                        new IntLiteralNode(Span, 0)),
                ],
                new AttributeCollection()));

        var bound = Bind(expression, out _);

        Assert.True(BoundNodeHelpers.ContainsDivision(bound, out var division));
        Assert.NotNull(division);
        Assert.True(BoundNodeHelpers.IsLiteralZero(division.Right));
    }

    [Fact]
    public void MatchResult_RemainsReachableThroughChildNodes()
    {
        var match = new MatchExpressionNode(
            Span,
            "m",
            new IntLiteralNode(Span, 1),
            [
                new MatchCaseNode(
                    Span,
                    new WildcardPatternNode(Span),
                    null,
                    [
                        new ReturnStatementNode(
                            Span,
                            new BinaryOperationNode(
                                Span,
                                BinaryOperator.Divide,
                                new IntLiteralNode(Span, 1),
                                new IntLiteralNode(Span, 0))),
                    ]),
            ],
            new AttributeCollection());

        var bound = Bind(match, out _);

        Assert.True(BoundNodeHelpers.ContainsDivision(bound, out _));
    }

    [Fact]
    public void UsedVariableTraversal_ExcludesLambdaAndQuantifierBoundVariables()
    {
        var lambda = new LambdaExpressionNode(
            Span,
            "l",
            [new LambdaParameterNode(Span, "x", "INT")],
            null,
            false,
            new BinaryOperationNode(
                Span,
                BinaryOperator.Add,
                new ReferenceNode(Span, "x"),
                new ReferenceNode(Span, "captured")),
            null,
            new AttributeCollection());
        var boundLambda = Bind(lambda, out _, ("captured", "INT"));
        Assert.Equal(
            ["captured"],
            BoundNodeHelpers.GetUsedVariables(boundLambda).Select(variable => variable.Name));

        var quantifier = new ForallExpressionNode(
            Span,
            [new QuantifierVariableNode(Span, "i", "INT")],
            new BinaryOperationNode(
                Span,
                BinaryOperator.LessThan,
                new ReferenceNode(Span, "i"),
                new ReferenceNode(Span, "limit")));
        var boundQuantifier = Bind(quantifier, out _, ("limit", "INT"));
        Assert.Equal(
            ["limit"],
            BoundNodeHelpers.GetUsedVariables(boundQuantifier).Select(variable => variable.Name));
    }

    [Fact]
    public void UnsupportedExpression_EmitsOneExactDiagnosticAndRetainsAllChildren()
    {
        ExpressionNode MakeCall() => new UnregisteredExpressionNode(
            Span,
            new BinaryOperationNode(
                Span,
                BinaryOperator.Divide,
                new IntLiteralNode(Span, 1),
                new ReferenceNode(Span, "divisor")));

        var tuple = new TupleLiteralNode(Span, [MakeCall(), MakeCall()]);
        var bound = Bind(
            tuple,
            out var diagnostics,
            ("callee", "OBJECT"),
            ("divisor", "INT"));

        var incomplete = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCode.AnalysisUnsupportedNode)
            .ToArray();
        var diagnostic = Assert.Single(incomplete);
        Assert.Contains(nameof(UnregisteredExpressionNode), diagnostic.Message, StringComparison.Ordinal);

        var calls = Assert.IsType<BoundTupleLiteral>(bound).Elements
            .Select(child => Assert.IsType<BoundUnsupportedExpression>(child))
            .ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call => Assert.Single(call.Children));
        Assert.All(calls, call => Assert.True(BoundNodeHelpers.ContainsDivision(call, out _)));
        Assert.DoesNotContain(
            BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.Target.StartsWith("<unsupported:", StringComparison.Ordinal));
    }

    private sealed class UnregisteredExpressionNode : ExpressionNode
    {
        public ExpressionNode Value { get; }

        public UnregisteredExpressionNode(TextSpan span, ExpressionNode value)
            : base(span)
        {
            Value = value;
        }

        public override void Accept(IAstVisitor visitor) =>
            throw new NotSupportedException();

        public override T Accept<T>(IAstVisitor<T> visitor) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void InteropExpression_RetainsExactKindAndSource()
    {
        const string source = "DateTime.Now.Ticks";
        var bound = Bind(new RawCSharpExpressionNode(Span, source), out var diagnostics);

        var interop = Assert.IsType<BoundInteropExpression>(bound);
        Assert.Equal(nameof(RawCSharpExpressionNode), interop.NodeTypeName);
        Assert.Equal(source, interop.SourceText);
        var diagnostic = Assert.Single(
            diagnostics.Where(item => item.Code == DiagnosticCode.AnalysisUnsupportedNode));
        Assert.Contains(nameof(RawCSharpExpressionNode), diagnostic.Message, StringComparison.Ordinal);

        var fallback = Assert.IsType<BoundInteropExpression>(
            Bind(
                new FallbackExpressionNode(
                    Span,
                    "value?.Unknown()",
                    "conditional-access",
                    "Rewrite with §?."),
                out var fallbackDiagnostics));
        Assert.Equal(nameof(FallbackExpressionNode), fallback.NodeTypeName);
        Assert.Equal("value?.Unknown()", fallback.SourceText);
        Assert.Equal("conditional-access", fallback.Metadata["FeatureName"]);
        Assert.Equal("Rewrite with §?.", fallback.Metadata["Suggestion"]);
        Assert.Contains(
            fallbackDiagnostics,
            item => item.Code == DiagnosticCode.AnalysisUnsupportedNode
                && item.Message.Contains(nameof(FallbackExpressionNode), StringComparison.Ordinal));
    }

    [Fact]
    public void CallExpression_RetainsResolutionInputsForLaterSymbolSlice()
    {
        var call = new CallExpressionNode(
            Span,
            "Map",
            [new ReferenceNode(Span, "value")],
            ["item"],
            ["in"],
            ["T"]);

        var bound = Assert.IsType<BoundCallExpression>(
            Bind(call, out _, ("value", "T")));

        Assert.Equal(["item"], bound.ArgumentNames);
        Assert.Equal(["in"], bound.ArgumentModifiers);
        Assert.Equal(["T"], bound.TypeArguments);
        Assert.Equal("OBJECT", bound.TypeName);
        Assert.Single(bound.Children);
    }

    private static BoundExpression Bind(
        ExpressionNode expression,
        out DiagnosticBag diagnostics,
        params (string Name, string TypeName)[] parameters)
    {
        diagnostics = new DiagnosticBag();
        var parameterNodes = parameters
            .Select(parameter => new ParameterNode(
                Span,
                parameter.Name,
                parameter.TypeName,
                new AttributeCollection()))
            .ToArray();
        var function = new FunctionNode(
            Span,
            "f",
            "Test",
            Visibility.Public,
            parameterNodes,
            new OutputNode(Span, "OBJECT"),
            null,
            [new ReturnStatementNode(Span, expression)],
            new AttributeCollection());
        var module = new ModuleNode(
            Span,
            "m",
            "Test",
            [],
            [function],
            new AttributeCollection());

        var boundModule = new Binder(diagnostics).Bind(module);
        var returnStatement = Assert.IsType<BoundReturnStatement>(
            Assert.Single(Assert.Single(boundModule.Functions).Body));
        return Assert.IsAssignableFrom<BoundExpression>(returnStatement.Expression);
    }
}
