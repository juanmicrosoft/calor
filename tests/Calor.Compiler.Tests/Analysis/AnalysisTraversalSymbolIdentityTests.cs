using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.BugPatterns.Patterns;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Dataflow.Analyses;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3.KInduction;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

public sealed class AnalysisTraversalSymbolIdentityTests
{
    private static TextSpan Span(int start) => new(start, start + 1, 1, start + 1);

    [Fact]
    public void BoundAnalyses_FindNestedSeedsThroughStructuralWrappers()
    {
        var parameter = Variable("opt", "Option<i32>", "parameter:opt", isParameter: true);
        var tainted = Variable("user_input", "STRING", "parameter:user", isParameter: true);
        var callee = FunctionSymbol("Target", "INT", "function:target");

        var division = new BoundBinaryExpression(
            Span(10),
            BinaryOperator.Divide,
            new BoundIntLiteral(Span(11), 1),
            new BoundIntLiteral(Span(12), 0),
            "INT");
        var index = new BoundArrayAccessExpression(
            Span(20),
            new BoundStructuralExpression(Span(21), "Array", "INT[]"),
            [new BoundIntLiteral(Span(22), -1)],
            "INT");
        var unwrap = new BoundCallExpression(
            Span(30),
            "opt.unwrap",
            [],
            "INT",
            receiverSymbol: parameter);
        var taintSink = new BoundCallExpression(
            Span(40),
            "db.execute",
            [
                new BoundStructuralExpression(
                    Span(41),
                    "Wrapper",
                    "STRING",
                    [new BoundVariableExpression(Span(42), tainted)]),
            ],
            "VOID");
        var internalCall = new BoundCallExpression(
            Span(50),
            "Target",
            [],
            "INT",
            resolvedSymbol: callee);

        var wrapper = new BoundStructuralExpression(
            Span(1),
            "DiverseWrapper",
            "OBJECT",
            [division, index, unwrap, taintSink, internalCall]);
        var caller = Function(
            FunctionSymbol(
                "Caller",
                "OBJECT",
                "function:caller",
                [parameter, tainted]),
            [new BoundReturnStatement(Span(0), wrapper)]);
        var target = Function(callee, []);
        var module = Module([target, caller]);

        var divisionDiagnostics = new DiagnosticBag();
        new DivisionByZeroChecker(FastBugOptions()).Check(caller, divisionDiagnostics);
        Assert.Contains(
            divisionDiagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.DivisionByZero);

        var indexDiagnostics = new DiagnosticBag();
        new IndexOutOfBoundsChecker(FastBugOptions()).Check(caller, indexDiagnostics);
        Assert.Contains(
            indexDiagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.IndexOutOfBounds);

        var nullDiagnostics = new DiagnosticBag();
        new NullDereferenceChecker(FastBugOptions()).Check(caller, nullDiagnostics);
        Assert.Contains(
            nullDiagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnsafeUnwrap);

        var taint = new TaintAnalysis(caller);
        Assert.Contains(
            taint.Vulnerabilities,
            vulnerability => vulnerability.Sink == TaintSink.SqlQuery
                && vulnerability.SourceVariable == tainted.Name);

        var graph = CallGraphAnalysis.BuildResolved(module);
        var edge = Assert.Single(graph.ForwardGraph[caller.SymbolId]);
        Assert.Equal(callee.Id, edge.Callee);
        Assert.Contains(
            graph.UnresolvedCalls,
            call => call.Target is "opt.unwrap" or "db.execute");
    }

    [Fact]
    public void SymbolId_PreventsShadowedDataflowAndTaintContamination()
    {
        var outer = Variable("value", "INT", "local:outer");
        var inner = Variable("value", "INT", "local:inner");
        var body = new BoundStatement[]
        {
            new BoundBindStatement(Span(1), outer, new BoundIntLiteral(Span(2), 1)),
            new BoundIfStatement(
                Span(3),
                new BoundBoolLiteral(Span(4), true),
                [new BoundBindStatement(Span(5), inner, new BoundIntLiteral(Span(6), 2))],
                [],
                null),
            new BoundReturnStatement(Span(7), new BoundVariableExpression(Span(8), outer)),
        };
        var function = Function(FunctionSymbol("Flow", "INT", "function:flow"), body);
        var cfg = ControlFlowGraph.Build(function);

        var live = new LiveVariablesAnalysis(cfg);
        var dead = live.FindDeadAssignmentsWithSymbols().ToArray();
        Assert.Contains(dead, item => item.Variable.Id == inner.Id);
        Assert.DoesNotContain(dead, item => item.Variable.Id == outer.Id);

        var reaching = new ReachingDefinitionsAnalysis(cfg);
        Assert.Contains(reaching.AllDefinitions, definition => definition.VariableId == outer.Id);
        Assert.Contains(reaching.AllDefinitions, definition => definition.VariableId == inner.Id);
        Assert.All(
            reaching.AllDefinitions.Where(definition => definition.VariableId == outer.Id),
            definition => Assert.NotEqual(inner.Id, definition.VariableId));

        var initializedParameter = Variable(
            "same",
            "INT",
            "parameter:same",
            isParameter: true);
        var uninitializedShadow = Variable("same", "INT", "local:same");
        var uninitializedFunction = Function(
            FunctionSymbol(
                "Uninitialized",
                "INT",
                "function:uninitialized",
                [initializedParameter]),
            [
                new BoundIfStatement(
                    Span(20),
                    new BoundBoolLiteral(Span(21), true),
                    [
                        new BoundBindStatement(Span(22), uninitializedShadow, null),
                        new BoundReturnStatement(
                            Span(23),
                            new BoundVariableExpression(Span(24), uninitializedShadow)),
                    ],
                    [],
                    null),
                new BoundReturnStatement(
                    Span(25),
                    new BoundVariableExpression(Span(26), initializedParameter)),
            ]);
        var uninitialized = new UninitializedVariablesAnalysis(
            ControlFlowGraph.Build(uninitializedFunction),
            uninitializedFunction.Symbol.Parameters);
        Assert.Contains(
            uninitialized.UninitializedUses,
            use => use.VariableId == uninitializedShadow.Id
                && use.State == InitializationState.Uninitialized);
        Assert.DoesNotContain(
            uninitialized.UninitializedUses,
            use => use.VariableId == initializedParameter.Id);

        var taintedParameter = Variable(
            "user_input",
            "STRING",
            "parameter:tainted",
            isParameter: true);
        var safeShadow = Variable("user_input", "STRING", "local:safe");
        var taintFunction = Function(
            FunctionSymbol(
                "TaintShadow",
                "VOID",
                "function:taint-shadow",
                [taintedParameter]),
            [
                new BoundIfStatement(
                    Span(30),
                    new BoundBoolLiteral(Span(31), true),
                    [
                        new BoundBindStatement(
                            Span(32),
                            safeShadow,
                            new BoundStringLiteral(Span(33), "safe")),
                        new BoundCallStatement(
                            Span(34),
                            "db.execute",
                            [new BoundVariableExpression(Span(35), safeShadow)]),
                    ],
                    [],
                    null),
            ]);
        Assert.Empty(new TaintAnalysis(taintFunction).Vulnerabilities);
    }

    [Fact]
    public void ResolvedCallGraph_TargetsExactOverload_AndKeepsExternalExplicit()
    {
        var intOverload = FunctionSymbol(
            "Pick",
            "INT",
            "function:pick-int",
            [Variable("value", "INT", "parameter:pick-int", isParameter: true)]);
        var stringOverload = FunctionSymbol(
            "Pick",
            "STRING",
            "function:pick-string",
            [Variable("value", "STRING", "parameter:pick-string", isParameter: true)]);
        var callerSymbol = FunctionSymbol("Caller", "INT", "function:caller-overload");
        var caller = Function(
            callerSymbol,
            [
                new BoundReturnStatement(
                    Span(60),
                    new BoundStructuralExpression(
                        Span(61),
                        "Wrapper",
                        "INT",
                        [
                            new BoundCallExpression(
                                Span(62),
                                "Pick",
                                [new BoundIntLiteral(Span(63), 1)],
                                "INT",
                                resolvedSymbol: intOverload),
                            new BoundCallExpression(
                                Span(64),
                                "External.Pick",
                                [],
                                "OBJECT"),
                        ])),
            ]);

        var graph = CallGraphAnalysis.BuildResolved(
            Module([Function(intOverload, []), Function(stringOverload, []), caller]));

        var edge = Assert.Single(graph.ForwardGraph[callerSymbol.Id]);
        Assert.Equal(intOverload.Id, edge.Callee);
        Assert.NotEqual(stringOverload.Id, edge.Callee);
        var unresolved = Assert.Single(graph.UnresolvedCalls);
        Assert.Equal("External.Pick", unresolved.Target);
    }

    [Fact]
    public void AstCollectors_ReachCallsInsidePreviouslyUnhandledWrappers()
    {
        var nestedCall = new CallExpressionNode(
            Span(80),
            "External.Fetch",
            [new StringLiteralNode(Span(81), "value")]);
        var wrapper = new ForallExpressionNode(
            Span(82),
            [new QuantifierVariableNode(Span(83), "i", "i32")],
            new ImplicationExpressionNode(
                Span(84),
                new BoolLiteralNode(Span(85), true),
                nestedCall));
        var function = new FunctionNode(
            Span(86),
            "f",
            "Caller",
            Visibility.Public,
            [],
            new OutputNode(Span(87), "BOOL"),
            null,
            [new ReturnStatementNode(Span(88), wrapper)],
            new AttributeCollection());
        var module = new ModuleNode(
            Span(89),
            "m",
            "Module",
            [],
            [function],
            new AttributeCollection());

        Assert.Contains(
            ExternalCallCollector.Collect(module),
            call => call.TypeName == "External" && call.MethodName == "Fetch");
        Assert.Contains(
            CallGraphAnalysis.Build(module).UnresolvedCalls,
            call => call.Target == "External.Fetch");
    }

    [Fact]
    public void LegacyAstCallGraph_UsesBoundResolutionForExactOverload()
    {
        FunctionNode Overload(string id, string type) => new(
            Span(id.Length),
            id,
            "Pick",
            Visibility.Public,
            [new ParameterNode(Span(id.Length + 1), "value", type, new AttributeCollection())],
            new OutputNode(Span(id.Length + 2), type),
            null,
            [new ReturnStatementNode(Span(id.Length + 3), new ReferenceNode(Span(id.Length + 4), "value"))],
            new AttributeCollection());

        var caller = new FunctionNode(
            Span(110),
            "caller",
            "Caller",
            Visibility.Public,
            [],
            new OutputNode(Span(111), "INT"),
            null,
            [
                new ReturnStatementNode(
                    Span(112),
                    new CallExpressionNode(
                        Span(113),
                        "Pick",
                        [new IntLiteralNode(Span(114), 1)])),
            ],
            new AttributeCollection());
        var module = new ModuleNode(
            Span(115),
            "m",
            "Module",
            [],
            [Overload("int", "INT"), Overload("string", "STRING"), caller],
            new AttributeCollection());

        var graph = CallGraphAnalysis.Build(module);

        Assert.False(graph.FunctionNameToId.ContainsKey("Pick"));
        var callee = Assert.Single(graph.GetCallees(caller.Id));
        Assert.Equal("int", callee.CalleeId);
        Assert.DoesNotContain(
            graph.UnresolvedCalls,
            call => call.CallerId == caller.Id && call.Target == "Pick");
        Assert.Contains(caller.Id, graph.ReverseGraph["int"]);
        Assert.DoesNotContain(caller.Id, graph.ReverseGraph["string"]);
    }

    [Fact]
    public void UnsupportedBoundNode_PropagatesExplicitIncompleteResultsWithoutDuplicateBinderDiagnostics()
    {
        var unsupported = new BoundUnsupportedExpression(
            Span(90),
            "FutureExpression",
            "INT",
            [new BoundIntLiteral(Span(91), 1)],
            reason: "Future semantics are not modeled");
        var function = Function(
            FunctionSymbol("Incomplete", "INT", "function:incomplete"),
            [new BoundReturnStatement(Span(92), unsupported)]);
        var cfg = ControlFlowGraph.Build(function);

        Assert.False(new LiveVariablesAnalysis(cfg).IsComplete);
        Assert.False(new ReachingDefinitionsAnalysis(cfg).IsComplete);
        Assert.False(new UninitializedVariablesAnalysis(cfg).IsComplete);
        Assert.False(new TaintAnalysis(function).IsComplete);

        var diagnostics = new DiagnosticBag();
        diagnostics.ReportInfo(
            unsupported.Span,
            DiagnosticCode.AnalysisUnsupportedNode,
            "Binder already reported FutureExpression");
        new VerificationAnalysisPass(
                diagnostics,
                new VerificationAnalysisOptions
                {
                    EnableDataflow = true,
                    EnableBugPatterns = false,
                    EnableTaintAnalysis = false,
                    EnableKInduction = false,
                    EnableContractInference = false,
                })
            .AnalyzeBound(Module([function]));

        Assert.Single(
            diagnostics.Where(diagnostic =>
                diagnostic.Code is DiagnosticCode.AnalysisUnsupportedNode
                    or DiagnosticCode.AnalysisSkipped));
    }

    [Fact]
    public void Binder_AttachesReceiverSymbolIdToMemberAndValueCalls()
    {
        var function = new FunctionNode(
            Span(120),
            "f",
            "Callers",
            Visibility.Public,
            [
                new ParameterNode(
                    Span(121),
                    "opt",
                    "Option<i32>",
                    new AttributeCollection()),
                new ParameterNode(
                    Span(122),
                    "callback",
                    "Func<i32>",
                    new AttributeCollection()),
            ],
            new OutputNode(Span(123), "INT"),
            null,
            [
                new CallStatementNode(
                    Span(124),
                    "callback",
                    false,
                    [],
                    new AttributeCollection()),
                new ReturnStatementNode(
                    Span(125),
                    new CallExpressionNode(Span(126), "opt.unwrap", [])),
            ],
            new AttributeCollection());
        var ast = new ModuleNode(
            Span(127),
            "m",
            "Module",
            [],
            [function],
            new AttributeCollection());
        var diagnostics = new DiagnosticBag();

        var boundFunction = Assert.Single(new Binder(diagnostics).Bind(ast).Functions);
        var callback = Assert.IsType<BoundCallStatement>(boundFunction.Body[0]);
        var unwrap = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundReturnStatement>(boundFunction.Body[1]).Expression);

        Assert.Equal(boundFunction.Symbol.Parameters[1].Id, callback.ReceiverSymbolId);
        Assert.Equal(boundFunction.Symbol.Parameters[0].Id, unwrap.ReceiverSymbolId);
    }

    [Fact]
    public void WhileTransition_DoesNotUseShadowedSameNameSymbol()
    {
        var loopVariable = Variable("i", "INT", "loop:outer");
        var shadow = Variable("i", "INT", "loop:shadow");
        var condition = new BoundBinaryExpression(
            Span(130),
            BinaryOperator.LessThan,
            new BoundVariableExpression(Span(131), loopVariable),
            new BoundIntLiteral(Span(132), 10),
            "BOOL");
        var loopInfo = WhileConditionAnalyzer.Analyze(condition);
        Assert.NotNull(loopInfo);
        var body = new BoundStatement[]
        {
            new BoundBindStatement(
                Span(133),
                shadow,
                new BoundBinaryExpression(
                    Span(134),
                    BinaryOperator.Add,
                    new BoundVariableExpression(Span(135), shadow),
                    new BoundIntLiteral(Span(136), 1),
                    "INT")),
        };

        Assert.Equal(loopVariable.Id, loopInfo!.LoopVariableId);
        Assert.Null(WhileConditionAnalyzer.AnalyzeTransition(body, loopInfo));
    }

    private static BugPatternOptions FastBugOptions() => new()
    {
        UseZ3Verification = false,
        ReportOnlyVerified = false,
    };

    private static VariableSymbol Variable(
        string name,
        string type,
        string id,
        bool isParameter = false) =>
        new(
            SymbolId.Create(id),
            name,
            type,
            isMutable: true,
            isParameter,
            declarationSpan: Span(id.Length));

    private static FunctionSymbol FunctionSymbol(
        string name,
        string returnType,
        string id,
        IReadOnlyList<VariableSymbol>? parameters = null) =>
        new(
            SymbolId.Create(id),
            name,
            returnType,
            parameters ?? [],
            declarationSpan: Span(id.Length + 100));

    private static BoundFunction Function(
        FunctionSymbol symbol,
        IReadOnlyList<BoundStatement> body) =>
        new(
            symbol.DeclarationSpan,
            symbol,
            body,
            new Scope());

    private static BoundModule Module(IReadOnlyList<BoundFunction> functions)
    {
        var symbols = functions
            .SelectMany(function => new Symbol[] { function.Symbol }
                .Concat(function.Symbol.Parameters))
            .ToDictionary(symbol => symbol.Id);
        return new BoundModule(Span(0), "Module", functions, symbols);
    }
}
