using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Dataflow.Analyses;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

public sealed class ExplicitDataflowAnalysisTests
{
    [Fact]
    public void Cfg_UsesStablePerGraphIdsExplicitTerminatorsAndTypedEdges()
    {
        var condition = Variable("condition", "BOOL", "parameter:condition", isParameter: true);
        var function = Function(
            "Stable",
            [condition],
            [
                new BoundIfStatement(
                    Span(1),
                    Reference(condition, 2),
                    [new BoundReturnStatement(Span(3), Integer(1, 4))],
                    [],
                    [new BoundReturnStatement(Span(5), Integer(0, 6))]),
            ]);

        var first = ControlFlowGraph.Build(function);
        var second = ControlFlowGraph.Build(function);

        Assert.Equal(Enumerable.Range(0, first.Blocks.Count), first.Blocks.Select(block => block.Id));
        Assert.Equal(first.Blocks.Select(block => block.Id), first.Blocks.Select(block => block.Ordinal));
        Assert.Equal(first.ToDot(), second.ToDot());
        Assert.All(first.Blocks, block => Assert.True(block.HasTerminator));
        Assert.IsType<ConditionalTerminator>(first.Entry.Terminator);
        Assert.Equal(
            [ControlFlowEdgeKind.True, ControlFlowEdgeKind.False],
            first.Entry.OutgoingEdges.Select(edge => edge.Kind));

        var returns = first.Blocks
            .Where(block => block.Terminator.Kind == ControlFlowTerminatorKind.Return)
            .ToArray();
        Assert.Equal(2, returns.Length);
        Assert.All(returns, block =>
        {
            var edge = Assert.Single(block.OutgoingEdges);
            Assert.Equal(ControlFlowEdgeKind.Return, edge.Kind);
            Assert.DoesNotContain(
                block.OutgoingEdges,
                candidate => candidate.Kind == ControlFlowEdgeKind.FallThrough);
        });
        first.Validate();
    }

    [Fact]
    public void Cfg_NestedLoopsRouteContinueAndBreakToNearestLegalTargets()
    {
        var inner = new BoundDoWhileStatement(
            Span(10),
            new BoundBoolLiteral(Span(11), false),
            [new BoundContinueStatement(Span(12))]);
        var outer = new BoundWhileStatement(
            Span(13),
            new BoundBoolLiteral(Span(14), true),
            [inner, new BoundBreakStatement(Span(15))]);
        var function = Function(
            "NestedLoops",
            [],
            [outer, new BoundReturnStatement(Span(16), Integer(0, 17))]);

        var cfg = ControlFlowGraph.Build(function);
        var continueBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator.Kind == ControlFlowTerminatorKind.Continue));
        var breakBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator.Kind == ControlFlowTerminatorKind.Break));
        var returnBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator.Kind == ControlFlowTerminatorKind.Return));

        Assert.Equal(ControlFlowEdgeKind.Continue, Assert.Single(continueBlock.OutgoingEdges).Kind);
        Assert.IsType<ConditionalTerminator>(Assert.Single(continueBlock.Successors).Terminator);
        Assert.Equal(ControlFlowEdgeKind.Break, Assert.Single(breakBlock.OutgoingEdges).Kind);
        Assert.Same(returnBlock, Assert.Single(breakBlock.Successors));
        Assert.All(
            new[] { continueBlock, breakBlock, returnBlock },
            block => Assert.DoesNotContain(
                block.OutgoingEdges,
                edge => edge.Kind == ControlFlowEdgeKind.FallThrough));
    }

    [Fact]
    public void Cfg_AllAbruptBranchesDoNotReconnectUnreachableStatements()
    {
        var condition = Variable("condition", "BOOL", "parameter:abrupt", isParameter: true);
        var unreachable = Variable("unreachable", "INT", "local:unreachable");
        var unreachableStatement = new BoundBindStatement(
            Span(18),
            unreachable,
            Integer(1, 19));
        var function = Function(
            "AbruptBranches",
            [condition],
            [
                new BoundIfStatement(
                    Span(20),
                    Reference(condition, 21),
                    [new BoundReturnStatement(Span(22), Integer(1, 23))],
                    [],
                    [new BoundThrowStatement(Span(24), Integer(2, 25))]),
                unreachableStatement,
            ]);

        var cfg = ControlFlowGraph.Build(function);

        Assert.DoesNotContain(
            cfg.Blocks.SelectMany(block => block.Statements),
            statement => ReferenceEquals(statement, unreachableStatement));
        var abruptBlocks = cfg.Blocks.Where(block => block.Terminator.IsAbrupt).ToArray();
        Assert.Equal(2, abruptBlocks.Length);
        Assert.All(abruptBlocks, block =>
            Assert.DoesNotContain(
                block.OutgoingEdges,
                edge => edge.Kind == ControlFlowEdgeKind.FallThrough));
    }

    [Fact]
    public void Cfg_ForForeachDoWhileAndMatchHaveStructuralShapes()
    {
        var collection = Variable("items", "List<INT>", "parameter:items", isParameter: true);
        var forVariable = Variable("i", "INT", "loop:i");
        var foreachVariable = Variable("item", "INT", "loop:item");
        var matchCase = new BoundMatchCase(
            Span(20),
            new BoundPattern(
                Span(21),
                "constant",
                expressions: [Integer(1, 22)]),
            isDefault: false,
            guard: null,
            body: [new BoundExpressionStatement(Span(23), Integer(1, 24))]);
        var defaultCase = new BoundMatchCase(
            Span(25),
            new BoundPattern(Span(26), "default"),
            isDefault: true,
            guard: null,
            body: [new BoundExpressionStatement(Span(27), Integer(0, 28))]);
        var function = Function(
            "Shapes",
            [collection],
            [
                new BoundForStatement(
                    Span(29),
                    forVariable,
                    Integer(0, 30),
                    Integer(3, 31),
                    Integer(1, 32),
                    []),
                new BoundForeachStatement(
                    Span(33),
                    foreachVariable,
                    Reference(collection, 34),
                    []),
                new BoundDoWhileStatement(
                    Span(35),
                    new BoundBoolLiteral(Span(36), false),
                    []),
                new BoundMatchStatement(
                    Span(37),
                    Reference(collection, 38),
                    [matchCase, defaultCase]),
                new BoundReturnStatement(Span(39), Integer(0, 40)),
            ]);

        var cfg = ControlFlowGraph.Build(function);

        Assert.Contains(cfg.Blocks.SelectMany(block => block.SyntheticOperations), operation =>
            operation.Kind == SyntheticOperationKind.ForInitialization
            && operation.DefinedVariable?.Id == forVariable.Id);
        Assert.Contains(cfg.Blocks.SelectMany(block => block.SyntheticOperations), operation =>
            operation.Kind == SyntheticOperationKind.ForStep
            && operation.DefinedVariable?.Id == forVariable.Id);
        Assert.Contains(cfg.Blocks.SelectMany(block => block.SyntheticOperations), operation =>
            operation.Kind == SyntheticOperationKind.ForeachIteration
            && operation.DefinedVariable?.Id == foreachVariable.Id);
        Assert.Contains(cfg.Blocks.SelectMany(block => block.SyntheticOperations), operation =>
            operation.Kind == SyntheticOperationKind.MatchTargetEvaluation);
        Assert.True(cfg.Blocks.Count(block =>
            block.Terminator is ConditionalTerminator) >= 4);
        Assert.Contains(cfg.Blocks.SelectMany(block => block.OutgoingEdges), edge =>
            edge.Kind == ControlFlowEdgeKind.BackEdge);
    }

    [Fact]
    public void Cfg_TryCatchFinallyRoutesNormalAndAbruptExitsThroughFinally()
    {
        var condition = Variable("condition", "BOOL", "parameter:condition", isParameter: true);
        var exception = Variable("ex", "Exception", "catch:ex");
        var local = Variable("value", "INT", "local:value");
        var cleanup = new BoundExpressionStatement(Span(50), Integer(99, 51));
        var tryStatement = new BoundTryStatement(
            Span(52),
            [
                new BoundCallStatement(Span(53), "MightThrow", []),
                new BoundUnsupportedStatement(Span(54), "FutureStatement"),
                new BoundIfStatement(
                    Span(55),
                    Reference(condition, 56),
                    [new BoundReturnStatement(Span(57), Integer(1, 58))],
                    [],
                    [new BoundBindStatement(Span(59), local, Integer(2, 60))]),
            ],
            [
                new BoundCatchClause(
                    Span(61),
                    "Exception",
                    exception,
                    [new BoundThrowStatement(Span(62), Reference(exception, 63))]),
            ],
            [cleanup]);
        var function = Function(
            "TryFinally",
            [condition],
            [tryStatement, new BoundReturnStatement(Span(64), Integer(0, 65))]);

        var cfg = ControlFlowGraph.Build(function);
        var callBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Statements.Any(statement => statement is BoundCallStatement)));
        Assert.IsType<DispatchTerminator>(callBlock.Terminator);
        Assert.Contains(callBlock.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.FallThrough);
        Assert.Contains(callBlock.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.Throw);

        var unsupportedBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator is UnsupportedTerminator));
        Assert.Contains(unsupportedBlock.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.Throw);

        var catchDispatch = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator is DispatchTerminator
            && block.OutgoingEdges.Any(edge => edge.Kind == ControlFlowEdgeKind.Catch)
            && block.Statements.Count == 0));
        Assert.Contains(catchDispatch.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.Catch);
        Assert.Contains(cfg.Blocks.SelectMany(block => block.SyntheticOperations), operation =>
            operation.Kind == SyntheticOperationKind.CatchInitialization
            && operation.DefinedVariable?.Id == exception.Id);

        var protectedReturn = Assert.Single(cfg.Blocks.Where(block =>
            block.Statements.Any(statement => statement.Span == Span(57))));
        Assert.Equal(ControlFlowEdgeKind.Finally, Assert.Single(protectedReturn.OutgoingEdges).Kind);
        var protectedThrow = Assert.Single(cfg.Blocks.Where(block =>
            block.Statements.Any(statement => statement.Span == Span(62))));
        Assert.Equal(ControlFlowEdgeKind.Finally, Assert.Single(protectedThrow.OutgoingEdges).Kind);

        var cleanupBlocks = cfg.Blocks.Where(block =>
            block.Statements.Contains(cleanup)).ToArray();
        Assert.True(cleanupBlocks.Length >= 3);
        Assert.Contains(cleanupBlocks, block =>
            Assert.Single(block.OutgoingEdges).Kind == ControlFlowEdgeKind.FallThrough);
        Assert.Contains(cleanupBlocks, block =>
            Assert.Single(block.OutgoingEdges).Kind == ControlFlowEdgeKind.Return);
        Assert.Contains(cleanupBlocks, block =>
            Assert.Single(block.OutgoingEdges).Kind == ControlFlowEdgeKind.Throw);
    }

    [Fact]
    public void Cfg_UsingRoutesReturnThroughDisposeFinally()
    {
        var input = Variable("input", "Resource", "parameter:input", isParameter: true);
        var resource = Variable("resource", "Resource", "using:resource");
        var usingStatement = new BoundUsingStatement(
            Span(70),
            resource,
            Reference(input, 71),
            [new BoundReturnStatement(Span(72), Reference(resource, 73))]);
        var function = Function("Using", [input], [usingStatement]);

        var cfg = ControlFlowGraph.Build(function);
        var returnBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator.Kind == ControlFlowTerminatorKind.Return));
        Assert.Equal(ControlFlowEdgeKind.Finally, Assert.Single(returnBlock.OutgoingEdges).Kind);
        var dispose = Assert.Single(returnBlock.Successors);
        Assert.Contains(dispose.SyntheticOperations, operation =>
            operation.Kind == SyntheticOperationKind.UsingDispose
            && operation.ReadsDefinedVariable);
        Assert.Contains(dispose.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.FallThrough);
        Assert.Contains(dispose.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.Throw);
    }

    [Fact]
    public void Cfg_FinallyInterceptsBreakAndContinue()
    {
        var condition = Variable("condition", "BOOL", "parameter:loop-finally", isParameter: true);
        var cleanup = new BoundExpressionStatement(Span(74), Integer(1, 75));
        var protectedLoop = new BoundWhileStatement(
            Span(76),
            new BoundBoolLiteral(Span(77), true),
            [
                new BoundTryStatement(
                    Span(78),
                    [
                        new BoundIfStatement(
                            Span(79),
                            Reference(condition, 80),
                            [new BoundBreakStatement(Span(81))],
                            [],
                            [new BoundContinueStatement(Span(82))]),
                    ],
                    [],
                    [cleanup]),
            ]);
        var function = Function(
            "LoopFinally",
            [condition],
            [protectedLoop, new BoundReturnStatement(Span(83), Integer(0, 84))]);

        var cfg = ControlFlowGraph.Build(function);
        var breakBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator.Kind == ControlFlowTerminatorKind.Break));
        var continueBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Terminator.Kind == ControlFlowTerminatorKind.Continue));
        Assert.Equal(ControlFlowEdgeKind.Finally, Assert.Single(breakBlock.OutgoingEdges).Kind);
        Assert.Equal(ControlFlowEdgeKind.Finally, Assert.Single(continueBlock.OutgoingEdges).Kind);

        var cleanupBlocks = cfg.Blocks.Where(block => block.Statements.Contains(cleanup)).ToArray();
        Assert.Contains(cleanupBlocks, block =>
            Assert.Single(block.OutgoingEdges).Kind == ControlFlowEdgeKind.Break);
        Assert.Contains(cleanupBlocks, block =>
            Assert.Single(block.OutgoingEdges).Kind == ControlFlowEdgeKind.Continue);
    }

    [Fact]
    public void Initialization_AssignmentAndDiamondJoinsUseSymbolIdentity()
    {
        var condition = Variable("condition", "BOOL", "parameter:condition", isParameter: true);
        var unsafeVariable = Variable("value", "INT", "local:unsafe");
        var unsafeFunction = Function(
            "UnsafeDiamond",
            [condition],
            [
                new BoundBindStatement(Span(80), unsafeVariable, null),
                new BoundIfStatement(
                    Span(81),
                    Reference(condition, 82),
                    [
                        new BoundAssignmentStatement(
                            Span(83),
                            Reference(unsafeVariable, 84),
                            Integer(1, 85)),
                    ],
                    [],
                    []),
                new BoundReturnStatement(Span(86), Reference(unsafeVariable, 87)),
            ]);

        var unsafeAnalysis = new UninitializedVariablesAnalysis(
            ControlFlowGraph.Build(unsafeFunction),
            unsafeFunction.Symbol.Parameters);
        var unsafeUse = Assert.Single(unsafeAnalysis.UninitializedUses.Where(use =>
            use.VariableId == unsafeVariable.Id));
        Assert.Equal(InitializationState.MaybeInitialized, unsafeUse.State);

        var safeVariable = Variable("value", "INT", "local:safe");
        var safeFunction = Function(
            "SafeDiamond",
            [condition],
            [
                new BoundBindStatement(Span(88), safeVariable, null),
                new BoundIfStatement(
                    Span(89),
                    Reference(condition, 90),
                    [
                        new BoundAssignmentStatement(
                            Span(91),
                            Reference(safeVariable, 92),
                            Integer(1, 93)),
                    ],
                    [],
                    [
                        new BoundAssignmentStatement(
                            Span(94),
                            Reference(safeVariable, 95),
                            Integer(2, 96)),
                    ]),
                new BoundReturnStatement(Span(97), Reference(safeVariable, 98)),
            ]);

        var safeAnalysis = new UninitializedVariablesAnalysis(
            ControlFlowGraph.Build(safeFunction),
            safeFunction.Symbol.Parameters);
        Assert.DoesNotContain(
            safeAnalysis.UninitializedUses,
            use => use.VariableId == safeVariable.Id);

        var neverVariable = Variable("value", "INT", "local:never");
        var neverFunction = Function(
            "NeverInitializedDiamond",
            [condition],
            [
                new BoundBindStatement(Span(99), neverVariable, null),
                new BoundIfStatement(
                    Span(100),
                    Reference(condition, 101),
                    [],
                    [],
                    []),
                new BoundReturnStatement(Span(102), Reference(neverVariable, 103)),
            ]);
        var neverAnalysis = new UninitializedVariablesAnalysis(
            ControlFlowGraph.Build(neverFunction),
            neverFunction.Symbol.Parameters);
        var neverUse = Assert.Single(neverAnalysis.UninitializedUses.Where(use =>
            use.VariableId == neverVariable.Id));
        Assert.Equal(InitializationState.Uninitialized, neverUse.State);
    }

    [Fact]
    public void Initialization_ExplicitParameterBoundaryControlsWhichParametersAreInitialized()
    {
        var parameter = Variable("value", "INT", "parameter:explicit-boundary", isParameter: true);
        var function = Function(
            "ParameterBoundary",
            [parameter],
            [new BoundReturnStatement(Span(104), Reference(parameter, 105))]);

        var initialized = new UninitializedVariablesAnalysis(
            ControlFlowGraph.Build(function),
            function.Symbol.Parameters);
        Assert.Empty(initialized.UninitializedUses);

        var excluded = new UninitializedVariablesAnalysis(
            ControlFlowGraph.Build(function),
            Array.Empty<VariableSymbol>());
        var use = Assert.Single(excluded.UninitializedUses);
        Assert.Equal(parameter.Id, use.VariableId);
        Assert.Equal(InitializationState.Uninitialized, use.State);
    }

    [Fact]
    public void Initialization_ThrowingRhsDefinesOnlyOnNormalContinuation()
    {
        var variable = Variable("value", "INT", "local:throwing-rhs");
        var assignment = new BoundAssignmentStatement(
            Span(99),
            Reference(variable, 100),
            new BoundCallExpression(
                Span(101),
                "GetValue",
                [],
                "INT"));
        var catchReturn = new BoundReturnStatement(Span(102), Reference(variable, 103));
        var function = Function(
            "ThrowingRhs",
            [],
            [
                new BoundBindStatement(Span(104), variable, null),
                new BoundTryStatement(
                    Span(105),
                    [assignment, new BoundReturnStatement(Span(106), Reference(variable, 107))],
                    [new BoundCatchClause(Span(108), null, null, [catchReturn])],
                    null),
            ]);

        var cfg = ControlFlowGraph.Build(function);
        var initialization = new UninitializedVariablesAnalysis(cfg);
        Assert.Contains(initialization.UninitializedUses, use =>
            use.VariableId == variable.Id
            && use.Span == catchReturn.Span
            && use.State == InitializationState.Uninitialized);
        Assert.DoesNotContain(initialization.UninitializedUses, use =>
            use.VariableId == variable.Id
            && use.Span == Span(106));

        var catchBlock = Assert.Single(cfg.Blocks.Where(block =>
            block.Statements.Contains(catchReturn)));
        var reaching = new ReachingDefinitionsAnalysis(cfg);
        Assert.DoesNotContain(
            reaching.GetReachingDefinitionsAtEntry(catchBlock),
            definition => ReferenceEquals(definition.Statement, assignment));
    }

    [Fact]
    public void Cfg_ReturnExpressionExceptionUsesThrowFlowBeforeReturn()
    {
        var function = Function(
            "ThrowingReturn",
            [],
            [
                new BoundTryStatement(
                    Span(109),
                    [
                        new BoundReturnStatement(
                            Span(110),
                            new BoundCallExpression(
                                Span(111),
                                "GetValue",
                                [],
                                "INT")),
                    ],
                    [
                        new BoundCatchClause(
                            Span(112),
                            null,
                            null,
                            [new BoundReturnStatement(Span(113), Integer(0, 114))]),
                    ],
                    null),
            ]);

        var cfg = ControlFlowGraph.Build(function);
        var evaluation = Assert.Single(cfg.Blocks.Where(block =>
            block.SyntheticOperations.Any(operation =>
                operation.Kind == SyntheticOperationKind.ExpressionEvaluation)));
        Assert.Contains(evaluation.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.Throw);
        Assert.Contains(evaluation.OutgoingEdges, edge => edge.Kind == ControlFlowEdgeKind.FallThrough);
        var returnContinuation = Assert.Single(evaluation.OutgoingEdges
            .Where(edge => edge.Kind == ControlFlowEdgeKind.FallThrough)
            .Select(edge => edge.Target));
        Assert.Equal(ControlFlowTerminatorKind.Return, returnContinuation.Terminator.Kind);
    }

    [Fact]
    public void Initialization_SyntheticForForeachCatchAndUsingDefinitionsAreVisible()
    {
        var collection = Variable("items", "List<INT>", "parameter:collection", isParameter: true);
        var resourceInput = Variable("input", "Resource", "parameter:resource", isParameter: true);
        var forVariable = Variable("i", "INT", "loop:for");
        var foreachVariable = Variable("item", "INT", "loop:foreach");
        var exception = Variable("ex", "Exception", "catch:exception");
        var resource = Variable("resource", "Resource", "using:resource-safe");
        var function = Function(
            "SyntheticInitialization",
            [collection, resourceInput],
            [
                new BoundForStatement(
                    Span(100),
                    forVariable,
                    Integer(0, 101),
                    Integer(1, 102),
                    Integer(1, 103),
                    [new BoundCallStatement(Span(104), "Use", [Reference(forVariable, 105)])]),
                new BoundForeachStatement(
                    Span(106),
                    foreachVariable,
                    Reference(collection, 107),
                    [new BoundCallStatement(Span(108), "Use", [Reference(foreachVariable, 109)])]),
                new BoundTryStatement(
                    Span(110),
                    [new BoundCallStatement(Span(111), "MightThrow", [])],
                    [
                        new BoundCatchClause(
                            Span(112),
                            null,
                            exception,
                            [new BoundCallStatement(Span(113), "Use", [Reference(exception, 114)])]),
                    ],
                    null),
                new BoundUsingStatement(
                    Span(115),
                    resource,
                    Reference(resourceInput, 116),
                    [new BoundCallStatement(Span(117), "Use", [Reference(resource, 118)])]),
                new BoundReturnStatement(Span(119), Integer(0, 120)),
            ]);

        var cfg = ControlFlowGraph.Build(function);
        var initialization = new UninitializedVariablesAnalysis(cfg, function.Symbol.Parameters);
        Assert.DoesNotContain(
            initialization.UninitializedUses,
            use => use.VariableId == forVariable.Id
                || use.VariableId == foreachVariable.Id
                || use.VariableId == exception.Id
                || use.VariableId == resource.Id);

        var reaching = new ReachingDefinitionsAnalysis(cfg);
        Assert.Contains(reaching.AllDefinitions, definition =>
            definition.VariableId == forVariable.Id
            && definition.SyntheticOperation?.Kind == SyntheticOperationKind.ForInitialization);
        Assert.Contains(reaching.AllDefinitions, definition =>
            definition.VariableId == foreachVariable.Id
            && definition.SyntheticOperation?.Kind == SyntheticOperationKind.ForeachIteration);
        Assert.Contains(reaching.AllDefinitions, definition =>
            definition.VariableId == exception.Id
            && definition.SyntheticOperation?.Kind == SyntheticOperationKind.CatchInitialization);
        Assert.Contains(reaching.AllDefinitions, definition =>
            definition.VariableId == resource.Id
            && definition.SyntheticOperation?.Kind == SyntheticOperationKind.UsingResourceInitialization);
    }

    [Fact]
    public void Liveness_SelfUpdateReadsOldValueBeforeStrongWrite()
    {
        var variable = Variable("value", "INT", "local:self-update");
        var declaration = new BoundBindStatement(Span(130), variable, Integer(0, 131));
        var assignment = new BoundCompoundAssignment(
            Span(132),
            Reference(variable, 133),
            CompoundAssignmentOperator.Add,
            Integer(1, 136));
        var function = Function(
            "SelfUpdate",
            [],
            [declaration, assignment, new BoundReturnStatement(Span(137), Reference(variable, 138))]);

        var analysis = new LiveVariablesAnalysis(ControlFlowGraph.Build(function));
        var dead = analysis.FindDeadAssignmentsWithSymbols().ToArray();

        Assert.DoesNotContain(dead, item => ReferenceEquals(item.Statement, declaration));
        Assert.DoesNotContain(dead, item => ReferenceEquals(item.Statement, assignment));
    }

    [Fact]
    public void ReachingDefinitions_StrongUpdateAndOrdinalsAreDeterministic()
    {
        var loopVariable = Variable("i", "INT", "loop:reaching");
        var function = Function(
            "Reaching",
            [],
            [
                new BoundForStatement(
                    Span(140),
                    loopVariable,
                    Integer(0, 141),
                    Integer(2, 142),
                    Integer(1, 143),
                    []),
                new BoundReturnStatement(Span(144), Integer(0, 145)),
            ]);

        var firstCfg = ControlFlowGraph.Build(function);
        var first = new ReachingDefinitionsAnalysis(firstCfg);
        var second = new ReachingDefinitionsAnalysis(ControlFlowGraph.Build(function));

        Assert.Equal(
            Enumerable.Range(0, first.AllDefinitions.Count),
            first.AllDefinitions.Select(definition => definition.DefinitionOrdinal));
        Assert.Equal(
            first.AllDefinitions.Select(definition => (
                definition.BlockId,
                definition.StatementIndex,
                definition.DefinitionOrdinal,
                definition.SyntheticOperation?.Kind)),
            second.AllDefinitions.Select(definition => (
                definition.BlockId,
                definition.StatementIndex,
                definition.DefinitionOrdinal,
                definition.SyntheticOperation?.Kind)));

        var stepBlock = Assert.Single(firstCfg.Blocks.Where(block =>
            block.SyntheticOperations.Any(operation =>
                operation.Kind == SyntheticOperationKind.ForStep)));
        var reachingAtStepExit = first.GetReachingDefinitionsAtExit(stepBlock)
            .Where(definition => definition.VariableId == loopVariable.Id)
            .ToArray();
        var stepDefinition = Assert.Single(reachingAtStepExit);
        Assert.Equal(SyntheticOperationKind.ForStep, stepDefinition.SyntheticOperation?.Kind);
    }

    [Fact]
    public void Dataflow_ReportsReachabilityConvergenceAndBoundedFailure()
    {
        var function = Function(
            "Convergence",
            [],
            [new BoundReturnStatement(Span(150), Integer(0, 151))]);
        var cfg = ControlFlowGraph.Build(function);
        var lattice = new SetLattice<string>();
        var transfer = new NoOpTransfer();
        var analysis = new DataflowAnalysis<ImmutableHashSet<string>>(
            lattice,
            transfer,
            DataflowDirection.Forward,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        var result = analysis.AnalyzeWithMetadata(cfg);
        Assert.True(result.IsConverged);
        Assert.True(result.Iterations >= cfg.ReachableBlocks.Count);
        Assert.Equal(cfg.ReachableBlocks, result.ReachableBlocks);
        Assert.Same(result, analysis.LastResult);

        var bounded = new DataflowAnalysis<ImmutableHashSet<string>>(
            lattice,
            transfer,
            DataflowDirection.Forward,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty,
            maxIterations: 1);
        Assert.Throws<DataflowConvergenceException>(() => bounded.Analyze(cfg));
        Assert.Null(bounded.LastResult);
    }

    [Fact]
    public void InitializationLattice_JoinIsAnUpperBoundInDiamondOrdering()
    {
        var variable = SymbolId.Create("lattice:value");
        var variables = new HashSet<SymbolId> { variable };
        var lattice = new InitializationLattice(variables);
        var uninitialized = InitializationFacts.Create(variables, []);
        var initialized = InitializationFacts.Create(variables, variables);
        var maybe = lattice.Join(uninitialized, initialized);

        Assert.Equal(InitializationState.MaybeInitialized, maybe.GetState(variable));
        Assert.True(lattice.LessOrEqual(uninitialized, maybe));
        Assert.True(lattice.LessOrEqual(initialized, maybe));
        Assert.False(lattice.LessOrEqual(maybe, initialized));
        Assert.False(lattice.LessOrEqual(initialized, uninitialized));
    }

    [Fact]
    public void Cfg_InvalidBreakFailsExplicitlyInsteadOfProducingPartialGraph()
    {
        var function = Function(
            "InvalidBreak",
            [],
            [new BoundBreakStatement(Span(160))]);

        Assert.Throws<ControlFlowGraphValidationException>(() =>
            ControlFlowGraph.Build(function));
    }

    [Fact]
    public void VerificationPass_ReportsCfgFailureAsInternalDiagnostic()
    {
        var function = Function(
            "InvalidBreakDiagnostic",
            [],
            [new BoundBreakStatement(Span(161))]);
        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(
            diagnostics,
            new VerificationAnalysisOptions
            {
                EnableDataflow = true,
                EnableBugPatterns = false,
                EnableTaintAnalysis = false,
                EnableContractInference = false,
                EnableKInduction = false,
            });

        pass.AnalyzeBound(Module(function));

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.AnalysisICE
            && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.AnalysisSkipped
            && diagnostic.Message.Contains("dataflow", StringComparison.OrdinalIgnoreCase));
    }

    private static TextSpan Span(int start) => new(start, start + 1, 1, start + 1);

    private static BoundIntLiteral Integer(long value, int start) =>
        new(Span(start), value);

    private static BoundVariableExpression Reference(VariableSymbol variable, int start) =>
        new(Span(start), variable);

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

    private static BoundFunction Function(
        string name,
        IReadOnlyList<VariableSymbol> parameters,
        IReadOnlyList<BoundStatement> body)
    {
        var symbol = new FunctionSymbol(
            SymbolId.Create($"function:{name}"),
            name,
            "INT",
            parameters,
            declarationSpan: Span(name.Length + 200));
        return new BoundFunction(symbol.DeclarationSpan, symbol, body, new Scope());
    }

    private static BoundModule Module(BoundFunction function)
    {
        var symbols = new Symbol[] { function.Symbol }
            .Concat(function.Symbol.Parameters)
            .ToDictionary(symbol => symbol.Id);
        return new BoundModule(Span(0), "Module", [function], symbols);
    }

    private sealed class NoOpTransfer : ITransferFunction<ImmutableHashSet<string>>
    {
        public ImmutableHashSet<string> Transfer(
            BoundStatement statement,
            ImmutableHashSet<string> input) => input;

        public ImmutableHashSet<string> TransferExpression(
            BoundExpression? expression,
            ImmutableHashSet<string> input) => input;
    }
}
