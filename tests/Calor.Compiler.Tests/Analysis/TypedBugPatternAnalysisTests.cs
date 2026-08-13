using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.BugPatterns.Patterns;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

public class TypedBugPatternAnalysisTests
{
    private static readonly TextSpan S = new(10, 1, 1, 1);

    [Fact]
    public void Division_ReassignmentIsStrongUpdate()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Divide:pub} (i32:x) -> i32
                §B{~divisor:i32} INT:2
                §ASSIGN divisor INT:0
                §R (/ x divisor)
            """);

        var diagnostics = Run(function, division: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero
            && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Division_NegatedFallthroughPathIsSafe()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Divide:pub} (i32:x, i32:divisor) -> i32
                §IF{i1} (== divisor INT:0)
                  §R INT:0
                §R (/ x divisor)
            """);

        var diagnostics = Run(function, division: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void Division_ZeroBranchIsUnsafe()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Divide:pub} (i32:x, i32:divisor) -> i32
                §IF{i1} (== divisor INT:0)
                  §R (/ x divisor)
                §R INT:0
            """);

        var diagnostics = Run(function, division: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero
            && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Division_DecimalGuardUsesTypedZeroState()
    {
        var divisor = Variable("divisor", "decimal", parameter: true);
        var condition = new BoundBinaryExpression(
            S,
            BinaryOperator.NotEqual,
            Ref(divisor),
            new BoundDecimalLiteral(S, 0m),
            "BOOL");
        var division = new BoundBinaryExpression(
            S,
            BinaryOperator.Divide,
            new BoundDecimalLiteral(S, 10m),
            Ref(divisor),
            "DECIMAL");
        var function = Function(
            [divisor],
            [
                new BoundIfStatement(
                    S,
                    condition,
                    [new BoundReturnStatement(S, division)],
                    [],
                    [new BoundReturnStatement(S, Int(0))]),
            ]);

        var diagnostics = Run(function, division: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void Division_DecimalConstantArithmeticPreservesExactNonZero()
    {
        var safeDivisor = new BoundBinaryExpression(
            S,
            BinaryOperator.Subtract,
            new BoundDecimalLiteral(S, 0.5m),
            new BoundDecimalLiteral(S, 0.2m),
            "DECIMAL");
        var zeroDivisor = new BoundBinaryExpression(
            S,
            BinaryOperator.Subtract,
            new BoundDecimalLiteral(S, 0.5m),
            new BoundDecimalLiteral(S, 0.5m),
            "DECIMAL");

        var safeDiagnostics = Run(
            DivisionFunction(
                safeDivisor,
                new BoundDecimalLiteral(S, 1m),
                "DECIMAL"),
            division: true);
        var unsafeDiagnostics = Run(
            DivisionFunction(
                zeroDivisor,
                new BoundDecimalLiteral(S, 1m),
                "DECIMAL"),
            division: true);

        Assert.DoesNotContain(safeDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
        Assert.Contains(unsafeDiagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void Division_AuthoritativeGuardIdsPreventOverloadNameLeakage()
    {
        var guardedParameter = Variable("divisor", "i32", parameter: true);
        var unguardedParameter = Variable("divisor", "i32", parameter: true);
        var guarded = NamedDivisionFunction("Divide", guardedParameter);
        var unguarded = NamedDivisionFunction("Divide", unguardedParameter);
        var options = new BugPatternOptions
        {
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckOffByOne = false,
            CheckMissingPreconditions = false,
            ReportOnlyVerified = true,
            UseZ3Verification = false,
            PreconditionGuardedParams = new Dictionary<string, HashSet<string>>
            {
                ["Divide"] = ["divisor"],
            },
            PreconditionGuardedParameterIds =
                new Dictionary<SymbolId, IReadOnlySet<SymbolId>>
                {
                    [guarded.Symbol.Id] = new HashSet<SymbolId>
                    {
                        guardedParameter.Id,
                    },
                },
        };

        var guardedDiagnostics = new DiagnosticBag();
        new BugPatternRunner(guardedDiagnostics, options).CheckFunction(guarded);
        var unguardedDiagnostics = new DiagnosticBag();
        new BugPatternRunner(unguardedDiagnostics, options).CheckFunction(unguarded);

        Assert.DoesNotContain(guardedDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
        Assert.Contains(unguardedDiagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Division_ConstantConditionPrunesInfeasibleEdge(
        bool conditionValue,
        bool expected)
    {
        var condition = new BoundBinaryExpression(
            S,
            BinaryOperator.LessThan,
            Int(conditionValue ? -1 : 1),
            Int(0),
            "BOOL");
        var division = new BoundBinaryExpression(
            S,
            BinaryOperator.Divide,
            Int(1),
            Int(0),
            "INT");
        var function = Function(
            [],
            [
                new BoundIfStatement(
                    S,
                    condition,
                    [new BoundReturnStatement(S, division)],
                    [],
                    [new BoundReturnStatement(S, Int(0))]),
            ]);

        var diagnostics = Run(function, division: true);

        Assert.Equal(
            expected,
            diagnostics.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.DivisionByZero));
    }

    [Fact]
    public void Division_ConditionalExpressionSkipsExactDeadArm()
    {
        var conditional = new BoundConditionalExpression(
            S,
            new BoundBoolLiteral(S, true),
            Int(7),
            DivideByLiteralZero(),
            "INT");
        var function = Function(
            [],
            [new BoundReturnStatement(S, conditional)]);

        var diagnostics = Run(function, division: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Theory]
    [InlineData(BinaryOperator.And, false)]
    [InlineData(BinaryOperator.Or, true)]
    public void Division_ShortCircuitSkipsExactDeadRightOperand(
        BinaryOperator operation,
        bool left)
    {
        var expression = new BoundBinaryExpression(
            S,
            operation,
            new BoundBoolLiteral(S, left),
            DivisionAsBoolean(),
            "BOOL");
        var function = Function(
            [],
            [new BoundReturnStatement(S, expression)]);

        var diagnostics = Run(function, division: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Division_VariableExpressionConditionInspectsReachableBugArm(
        bool conditionalExpression)
    {
        var condition = Variable("condition", "bool", parameter: true);
        BoundExpression expression = conditionalExpression
            ? new BoundConditionalExpression(
                S,
                Ref(condition),
                Int(7),
                DivideByLiteralZero(),
                "INT")
            : new BoundBinaryExpression(
                S,
                BinaryOperator.And,
                Ref(condition),
                DivisionAsBoolean(),
                "BOOL");
        var function = Function(
            [condition],
            [new BoundReturnStatement(S, expression)]);

        var diagnostics = Run(function, division: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void Division_CompoundDivideAndModuloUsePathSensitiveDivisorFacts()
    {
        var target = Variable("target", "i32");
        var divisor = Variable("divisor", "i32", parameter: true);
        var safeFunction = Function(
            [divisor],
            [
                new BoundBindStatement(S, target, Int(10)),
                new BoundIfStatement(
                    S,
                    new BoundBinaryExpression(
                        S,
                        BinaryOperator.NotEqual,
                        Ref(divisor),
                        Int(0),
                        "BOOL"),
                    [
                        new BoundCompoundAssignment(
                            S,
                            Ref(target),
                            CompoundAssignmentOperator.Divide,
                            Ref(divisor)),
                        new BoundReturnStatement(S, Ref(target)),
                    ],
                    [],
                    [new BoundReturnStatement(S, Int(0))]),
            ]);
        var possibleFunction = Function(
            [divisor],
            [
                new BoundBindStatement(S, target, Int(10)),
                new BoundCompoundAssignment(
                    S,
                    Ref(target),
                    CompoundAssignmentOperator.Divide,
                    Ref(divisor)),
                new BoundReturnStatement(S, Ref(target)),
            ]);
        var moduloZeroFunction = Function(
            [],
            [
                new BoundBindStatement(S, target, Int(10)),
                new BoundCompoundAssignment(
                    S,
                    Ref(target),
                    CompoundAssignmentOperator.Modulo,
                    Int(0)),
                new BoundReturnStatement(S, Ref(target)),
            ]);

        var safeDiagnostics = Run(safeFunction, division: true);
        var possibleDiagnostics = Run(possibleFunction, division: true);
        var zeroDiagnostics = Run(moduloZeroFunction, division: true);

        Assert.DoesNotContain(safeDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
        Assert.Contains(possibleDiagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
        Assert.Contains(zeroDiagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void Division_CompoundDivisorReassignmentAndIncompleteStateAreHonest()
    {
        var target = Variable("target", "i32");
        var divisor = Variable("divisor", "i32");
        var reassigned = Function(
            [],
            [
                new BoundBindStatement(S, target, Int(10)),
                new BoundBindStatement(S, divisor, Int(2)),
                new BoundAssignmentStatement(S, Ref(divisor), Int(0)),
                new BoundCompoundAssignment(
                    S,
                    Ref(target),
                    CompoundAssignmentOperator.Divide,
                    Ref(divisor)),
            ]);
        var incomplete = Function(
            [],
            [
                new BoundBindStatement(S, target, Int(10)),
                new BoundCompoundAssignment(
                    S,
                    Ref(target),
                    CompoundAssignmentOperator.Divide,
                    new BoundInteropExpression(
                        S,
                        "OpaqueDivisor",
                        "GetDivisor()",
                        "OBJECT")),
            ]);

        var reassignedDiagnostics = Run(reassigned, division: true);
        var incompleteDiagnostics = Run(
            incomplete,
            division: true,
            reportOnlyVerified: false);

        Assert.Contains(reassignedDiagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
        Assert.Contains(incompleteDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.BugPatternAnalysisIncomplete);
        Assert.Contains(incompleteDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZeroHint);
    }

    [Fact]
    public void Division_LeftOperandRefMutationInvalidatesRightDivisorState()
    {
        var divisor = Variable("divisor", "i32");
        var setZero = new BoundCallExpression(
            S,
            "SetZero",
            [Ref(divisor)],
            "INT",
            argumentModifiers: ["ref"]);
        var division = new BoundBinaryExpression(
            S,
            BinaryOperator.Divide,
            setZero,
            Ref(divisor),
            "INT");
        var function = Function(
            [],
            [
                new BoundBindStatement(S, divisor, Int(2)),
                new BoundReturnStatement(S, division),
            ]);

        var diagnostics = Run(function, division: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void UnsignedImpossibleNegativeBranchIsNotAnalyzed()
    {
        var value = Variable("value", "u32", parameter: true);
        var condition = new BoundBinaryExpression(
            S,
            BinaryOperator.LessThan,
            Ref(value),
            Int(0),
            "BOOL");
        var impossibleDivision = new BoundBinaryExpression(
            S,
            BinaryOperator.Divide,
            Int(1),
            Int(0),
            "INT");
        var function = Function(
            [value],
            [
                new BoundIfStatement(
                    S,
                    condition,
                    [new BoundReturnStatement(S, impossibleDivision)],
                    [],
                    [new BoundReturnStatement(S, Int(0))]),
            ]);

        var diagnostics = Run(function, division: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void Bounds_SequentialNegatedGuardsProveBothSides()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Read:pub} (i32[]:items, i32:index) -> i32
                §IF{i1} (< index INT:0)
                  §R INT:0
                §IF{i2} (>= index §LEN items)
                  §R INT:0
                §R §IDX items index
            """);

        var diagnostics = Run(function, bounds: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds);
    }

    [Fact]
    public void Bounds_UnrelatedLengthDoesNotProtectAccess()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Read:pub} (i32[]:items, i32[]:other, i32:index) -> i32
                §IF{i1} (&& (>= index INT:0) (< index §LEN other))
                  §R §IDX items index
                §R INT:0
            """);

        var diagnostics = Run(function, bounds: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds);
    }

    [Fact]
    public void Bounds_IndexReassignmentInvalidatesPriorChecks()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Read:pub} (i32[]:items, i32:index) -> i32
                §IF{i1} (&& (>= index INT:0) (< index §LEN items))
                  §ASSIGN index §LEN items
                  §R §IDX items index
                §R INT:0
            """);

        var diagnostics = Run(function, bounds: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds);
    }

    [Fact]
    public void Bounds_MultidimensionalUpperBoundIsDimensionSpecific()
    {
        var grid = Variable("grid", "i32[,]");
        var creation = new BoundMultiDimArrayCreation(
            S,
            "a1",
            "grid",
            "i32",
            2,
            [Int(2), Int(3)],
            []);
        var access = new BoundMultiDimArrayAccess(
            S,
            Ref(grid),
            [Int(1), Int(3)]);
        var function = Function(
            [],
            [
                new BoundBindStatement(S, grid, creation),
                new BoundReturnStatement(S, access),
            ]);

        var diagnostics = Run(function, bounds: true);

        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds
            && diagnostic.Message.Contains("dimension 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Bounds_RangeAndStringIndexUseRealSequenceLength()
    {
        var text = new BoundStringLiteral(S, "abc");
        var invalidRange = new BoundArrayAccess(
            S,
            text,
            new BoundRangeExpression(S, Int(0), Int(4)));
        var invalidIndex = new BoundArrayAccess(S, text, Int(-1));
        var function = Function(
            [],
            [
                new BoundExpressionStatement(S, invalidRange),
                new BoundReturnStatement(S, invalidIndex),
            ]);

        var diagnostics = Run(function, bounds: true);

        Assert.Equal(
            2,
            diagnostics.Count(diagnostic =>
                diagnostic.Code == DiagnosticCode.IndexOutOfBounds));
    }

    [Fact]
    public void Bounds_IndexFromEndDistinguishesZeroAndOne()
    {
        var text = new BoundStringLiteral(S, "abc");
        var invalid = new BoundArrayAccess(
            S,
            text,
            new BoundIndexFromEnd(S, Int(0)));
        var valid = new BoundArrayAccess(
            S,
            text,
            new BoundIndexFromEnd(S, Int(1)));
        var function = Function(
            [],
            [
                new BoundExpressionStatement(S, invalid),
                new BoundReturnStatement(S, valid),
            ]);

        var diagnostics = Run(function, bounds: true);

        Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds));
    }

    [Fact]
    public void Bounds_ConditionalReceiverMutationPrecedesIndexEvaluation()
    {
        var items = Variable("items", "i32[]");
        var index = Variable("index", "i32");
        var mutatingCondition = new BoundCallExpression(
            S,
            "SetIndex",
            [Ref(index)],
            "BOOL",
            argumentModifiers: ["ref"]);
        var receiver = new BoundConditionalExpression(
            S,
            mutatingCondition,
            Ref(items),
            Ref(items),
            "i32[]");
        var mutatingFunction = Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    items,
                    new BoundArrayCreation(
                        S,
                        "a",
                        "items",
                        "i32",
                        Int(1),
                        [])),
                new BoundBindStatement(S, index, Int(0)),
                new BoundReturnStatement(
                    S,
                    new BoundArrayAccess(S, receiver, Ref(index))),
            ]);
        var safeFunction = Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    items,
                    new BoundArrayCreation(
                        S,
                        "b",
                        "items",
                        "i32",
                        Int(1),
                        [])),
                new BoundBindStatement(S, index, Int(0)),
                new BoundReturnStatement(
                    S,
                    new BoundArrayAccess(
                        S,
                        new BoundConditionalExpression(
                            S,
                            new BoundBoolLiteral(S, true),
                            Ref(items),
                            Ref(items),
                            "i32[]"),
                        Ref(index))),
            ]);

        var mutatingDiagnostics = Run(mutatingFunction, bounds: true);
        var safeDiagnostics = Run(safeFunction, bounds: true);

        Assert.Single(mutatingDiagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds));
        Assert.DoesNotContain(safeDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds);
    }

    [Fact]
    public void Bounds_MultidimensionalIndicesEvaluateLeftToRightOnce()
    {
        var grid = Variable("grid", "i32[,]");
        var laterIndex = Variable("laterIndex", "i32");
        var firstIndex = new BoundConditionalExpression(
            S,
            new BoundCallExpression(
                S,
                "SetLaterIndex",
                [Ref(laterIndex)],
                "BOOL",
                argumentModifiers: ["ref"]),
            Int(0),
            Int(0),
            "INT");
        var function = Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    grid,
                    new BoundMultiDimArrayCreation(
                        S,
                        "g",
                        "grid",
                        "i32",
                        2,
                        [Int(1), Int(1)],
                        [])),
                new BoundBindStatement(S, laterIndex, Int(0)),
                new BoundReturnStatement(
                    S,
                    new BoundMultiDimArrayAccess(
                        S,
                        Ref(grid),
                        [firstIndex, Ref(laterIndex)])),
            ]);

        var diagnostics = Run(function, bounds: true);
        var finding = Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds));
        Assert.Contains("dimension 1", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bounds_DeferredDefinitionInspectsMayThrowSourceOnce(bool assignment)
    {
        var grid = Variable("grid", "i32[,]");
        var laterIndex = Variable("laterIndex", "i32");
        var result = Variable("result", "i32");
        var firstIndex = new BoundConditionalExpression(
            S,
            new BoundCallExpression(
                S,
                "First",
                [Ref(laterIndex)],
                "BOOL",
                argumentModifiers: ["ref"]),
            Int(0),
            Int(0),
            "INT");
        var access = new BoundMultiDimArrayAccess(
            S,
            Ref(grid),
            [firstIndex, Ref(laterIndex)]);
        var body = new List<BoundStatement>
        {
            new BoundBindStatement(
                S,
                grid,
                new BoundMultiDimArrayCreation(
                    S,
                    "g",
                    "grid",
                    "i32",
                    2,
                    [Int(1), Int(1)],
                    [])),
            new BoundBindStatement(S, laterIndex, Int(0)),
        };
        if (assignment)
        {
            body.Add(new BoundBindStatement(S, result, Int(0)));
            body.Add(new BoundAssignmentStatement(S, Ref(result), access));
        }
        else
        {
            body.Add(new BoundBindStatement(S, result, access));
        }
        body.Add(new BoundReturnStatement(S, Ref(result)));

        var diagnostics = Run(Function([], body), bounds: true);

        var finding = Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds));
        Assert.Contains("dimension 1", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OffByOne_RequiresMatchingRealAccess()
    {
        var items = Variable("items", "i32[]", parameter: true);
        var other = Variable("other", "i32[]", parameter: true);
        var i = Variable("i", "i32");
        var realAccess = new BoundArrayAccess(S, Ref(items), Ref(i));
        var unrelatedAccess = new BoundArrayAccess(S, Ref(other), Ref(i));
        var realLoop = new BoundForStatement(
            S,
            i,
            Int(0),
            new BoundArrayLength(S, Ref(items)),
            Int(1),
            [new BoundExpressionStatement(S, realAccess)]);
        var function = Function(
            [items, other],
            [
                realLoop,
                new BoundForStatement(
                    S,
                    Variable("j", "i32"),
                    Int(0),
                    new BoundArrayLength(S, Ref(items)),
                    Int(1),
                    [new BoundExpressionStatement(S, unrelatedAccess)]),
            ]);

        var diagnostics = Run(function, offByOne: true);

        Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.OffByOne));
    }

    [Fact]
    public void OffByOne_LengthMinusOneIsSafe()
    {
        var items = Variable("items", "i32[]", parameter: true);
        var i = Variable("i", "i32");
        var function = Function(
            [items],
            [
                new BoundForStatement(
                    S,
                    i,
                    Int(0),
                    new BoundBinaryExpression(
                        S,
                        BinaryOperator.Subtract,
                        new BoundArrayLength(S, Ref(items)),
                        Int(1),
                        "INT"),
                    Int(1),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, offByOne: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.OffByOne);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public void OffByOne_DescendingLoopModelsInclusiveLowerBound(
        long terminal,
        bool expected)
    {
        var items = Variable("items", "i32[]", parameter: true);
        var i = Variable("i", "i32");
        var from = new BoundBinaryExpression(
            S,
            BinaryOperator.Subtract,
            new BoundArrayLength(S, Ref(items)),
            Int(1),
            "INT");
        var function = Function(
            [items],
            [
                new BoundForStatement(
                    S,
                    i,
                    from,
                    Int(terminal),
                    Int(-1),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, offByOne: true);

        Assert.Equal(
            expected,
            diagnostics.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.OffByOne));
    }

    [Fact]
    public void OffByOne_InductionReassignmentIsExplicitlyIncomplete()
    {
        var items = Variable("items", "i32[]", parameter: true);
        var i = Variable("i", "i32");
        var function = Function(
            [items],
            [
                new BoundForStatement(
                    S,
                    i,
                    Int(0),
                    new BoundArrayLength(S, Ref(items)),
                    Int(1),
                    [
                        new BoundAssignmentStatement(S, Ref(i), Int(0)),
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, offByOne: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.OffByOne);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.BugPatternAnalysisIncomplete);
    }

    [Fact]
    public void Bounds_ForConditionPropagatesAliasedLengthMinusOne()
    {
        var items = Variable("items", "i32[]", parameter: true);
        var length = Variable("length", "i32");
        var upper = Variable("upper", "i32");
        var i = Variable("i", "i32");
        var function = Function(
            [items],
            [
                new BoundBindStatement(
                    S,
                    length,
                    new BoundArrayLength(S, Ref(items))),
                new BoundBindStatement(
                    S,
                    upper,
                    new BoundBinaryExpression(
                        S,
                        BinaryOperator.Subtract,
                        Ref(length),
                        Int(1),
                        "INT")),
                new BoundForStatement(
                    S,
                    i,
                    Int(0),
                    Ref(upper),
                    Int(1),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, bounds: true, offByOne: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.IndexOutOfBounds
                or DiagnosticCode.OffByOne);
    }

    [Fact]
    public void Bounds_EmptyArrayCanonicalLoopHasUnreachableBody()
    {
        var items = Variable("items", "i32[]");
        var length = Variable("length", "i32");
        var upper = Variable("upper", "i32");
        var i = Variable("i", "i32");
        var function = Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    items,
                    new BoundArrayCreation(
                        S,
                        "a",
                        "items",
                        "i32",
                        Int(0),
                        [])),
                new BoundBindStatement(
                    S,
                    length,
                    new BoundArrayLength(S, Ref(items))),
                new BoundBindStatement(
                    S,
                    upper,
                    new BoundBinaryExpression(
                        S,
                        BinaryOperator.Subtract,
                        Ref(length),
                        Int(1),
                        "INT")),
                new BoundForStatement(
                    S,
                    i,
                    Int(0),
                    Ref(upper),
                    Int(1),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, bounds: true, offByOne: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.IndexOutOfBounds
                or DiagnosticCode.OffByOne);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bounds_CanonicalLoopAdjacentCasesStillWarn(bool unsafeUpper)
    {
        var items = Variable("items", "i32[]", parameter: true);
        var length = Variable("length", "i32");
        var bound = Variable("bound", "i32");
        var i = Variable("i", "i32");
        var function = Function(
            [items],
            [
                new BoundBindStatement(
                    S,
                    length,
                    new BoundArrayLength(S, Ref(items))),
                new BoundBindStatement(
                    S,
                    bound,
                    unsafeUpper
                        ? Ref(length)
                        : new BoundBinaryExpression(
                            S,
                            BinaryOperator.Subtract,
                            Ref(length),
                            Int(1),
                            "INT")),
                new BoundForStatement(
                    S,
                    i,
                    unsafeUpper ? Int(0) : Int(-1),
                    Ref(bound),
                    Int(1),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, bounds: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.IndexOutOfBounds);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(2, true)]
    public void OffByOne_RequiresFeasibleInitialCondition(
        long start,
        bool expected)
    {
        var items = Variable("items", "i32[]");
        var i = Variable("i", "i32");
        var function = Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    items,
                    new BoundArrayCreation(
                        S,
                        "a",
                        "items",
                        "i32",
                        Int(2),
                        [])),
                new BoundForStatement(
                    S,
                    i,
                    Int(start),
                    new BoundArrayLength(S, Ref(items)),
                    Int(1),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, offByOne: true);

        Assert.Equal(
            expected,
            diagnostics.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.OffByOne));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void OffByOne_RequiresReachableEndpointForStepTwo(
        int length,
        bool expected)
    {
        var items = Variable("items", "i32[]");
        var i = Variable("i", "i32");
        var function = Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    items,
                    new BoundArrayCreation(
                        S,
                        "a",
                        "items",
                        "i32",
                        Int(length),
                        [])),
                new BoundForStatement(
                    S,
                    i,
                    Int(0),
                    new BoundArrayLength(S, Ref(items)),
                    Int(2),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, offByOne: true);

        Assert.Equal(
            expected,
            diagnostics.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.OffByOne));
    }

    [Fact]
    public void OffByOne_NonConstantStepIsIncompleteWithoutVerifiedFinding()
    {
        var items = Variable("items", "i32[]", parameter: true);
        var step = Variable("step", "i32", parameter: true);
        var i = Variable("i", "i32");
        var function = Function(
            [items, step],
            [
                new BoundForStatement(
                    S,
                    i,
                    Int(0),
                    new BoundArrayLength(S, Ref(items)),
                    Ref(step),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);

        var diagnostics = Run(function, offByOne: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.OffByOne);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.BugPatternAnalysisIncomplete);
    }

    [Fact]
    public void NullOption_AliasCheckFlowsUntilStrongUpdate()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Read:pub} (Option<i32>:input) -> i32
                §B{~original:Option<i32>} input
                §B{alias:Option<i32>} original
                §IF{i1} (== alias §NN{i32})
                  §R INT:0
                §R §C{original.Unwrap} §/C
            """);

        var diagnostics = Run(function, nulls: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.UnsafeUnwrap
                or DiagnosticCode.NullDereference);
    }

    [Fact]
    public void NullOption_ReassignmentInvalidatesAliasDerivedState()
    {
        var function = BindFunction("""
            §M{m:Typed}
              §F{f:Read:pub} (Option<i32>:input) -> i32
                §B{~original:Option<i32>} input
                §B{alias:Option<i32>} original
                §IF{i1} (!= alias §NN{i32})
                  §ASSIGN original §NN{i32}
                  §R §C{original.Unwrap} §/C
                §R INT:0
            """);

        var diagnostics = Run(function, nulls: true);

        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Fact]
    public void NullOption_NameSubstringIsNeverSemanticProof()
    {
        var receiver = Variable("containsunwrap", "i32", parameter: true);
        var misleading = new BoundCallExpression(
            S,
            "containsunwrap.unwrap",
            [],
            "i32",
            resolvedMethodName: "unwrap",
            receiverSymbol: receiver);
        var function = Function(
            [receiver],
            [new BoundReturnStatement(S, misleading)]);

        var diagnostics = Run(function, nulls: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.UnsafeUnwrap
                or DiagnosticCode.NullDereference);
    }

    [Fact]
    public void NullOption_RefOutExpressionCallsInvalidateEveryEvaluationContext()
    {
        foreach (var function in RefMutationFunctions())
        {
            var diagnostics = Run(function, nulls: true);

            Assert.Contains(diagnostics.Warnings, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
        }
    }

    [Fact]
    public void NullOption_AndConditionAppliesRightMutationAfterLeftRefinement()
    {
        var option = Variable("option", "Option<i32>");
        var condition = new BoundBinaryExpression(
            S,
            BinaryOperator.And,
            OptionCall(option, "IsSome", "BOOL"),
            new BoundCallExpression(
                S,
                "SetNone",
                [Ref(option)],
                "BOOL",
                argumentModifiers: ["ref"]),
            "BOOL");
        var function = Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    option,
                    new BoundSomeExpression(S, Int(1))),
                new BoundIfStatement(
                    S,
                    condition,
                    [
                        new BoundReturnStatement(
                            S,
                            OptionCall(option, "Unwrap", "i32")),
                    ],
                    [],
                    [new BoundReturnStatement(S, Int(0))]),
            ]);

        var diagnostics = Run(function, nulls: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOption_UnknownConditionalArmJoinsToMaybe(bool unknownFirst)
    {
        var condition = Variable("condition", "bool", parameter: true);
        var option = Variable("option", "Option<i32>");
        var unknown = new BoundCallExpression(
            S,
            "Make",
            [],
            "Option<i32>");
        var some = new BoundSomeExpression(S, Int(1));
        var initializer = new BoundConditionalExpression(
            S,
            Ref(condition),
            unknownFirst ? unknown : some,
            unknownFirst ? some : unknown,
            "Option<i32>");
        var function = Function(
            [condition],
            [
                new BoundBindStatement(S, option, initializer),
                new BoundReturnStatement(
                    S,
                    OptionCall(option, "Unwrap", "i32")),
            ]);

        var diagnostics = Run(function, nulls: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Fact]
    public void NullReference_UnknownConditionalArmDoesNotPreservePresent()
    {
        var condition = Variable("condition", "bool", parameter: true);
        var items = Variable("items", "i32[]");
        var knownArray = new BoundArrayCreation(
            S,
            "a",
            "items",
            "i32",
            Int(1),
            []);
        var unknownArray = new BoundInteropExpression(
            S,
            "UnknownArray",
            "MakeArray()",
            "i32[]");
        var function = Function(
            [condition],
            [
                new BoundBindStatement(
                    S,
                    items,
                    new BoundConditionalExpression(
                        S,
                        Ref(condition),
                        knownArray,
                        unknownArray,
                        "i32[]")),
                new BoundReturnStatement(
                    S,
                    new BoundArrayLength(S, Ref(items))),
            ]);

        var diagnostics = Run(function, nulls: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullDereference);
    }

    [Theory]
    [InlineData(BinaryOperator.Equal, false)]
    [InlineData(BinaryOperator.NotEqual, true)]
    public void NullOption_NoneFirstComparisonRefinesBothOrders(
        BinaryOperator operation,
        bool unwrapInThen)
    {
        var option = Variable("option", "Option<i32>", parameter: true);
        var comparison = new BoundBinaryExpression(
            S,
            operation,
            new BoundNoneLiteral(S, "Option<i32>"),
            Ref(option),
            "BOOL");
        var unwrap = new BoundReturnStatement(
            S,
            OptionCall(option, "Unwrap", "i32"));
        var fallback = new BoundReturnStatement(S, Int(0));
        var function = Function(
            [option],
            [
                new BoundIfStatement(
                    S,
                    comparison,
                    unwrapInThen ? [unwrap] : [fallback],
                    [],
                    unwrapInThen ? [fallback] : [unwrap]),
            ]);

        var diagnostics = Run(function, nulls: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Fact]
    public void Result_UnwrapErrRequiresErrVariant()
    {
        var okDiagnostics = Run(
            ResultUnwrapFunction(ok: true, methodName: "UnwrapErr"),
            nulls: true);
        var errDiagnostics = Run(
            ResultUnwrapFunction(ok: false, methodName: "UnwrapErr"),
            nulls: true);

        Assert.Contains(okDiagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
        Assert.DoesNotContain(errDiagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Fact]
    public void Result_InverseBranchRefinementsProveBothUnwrapDirections()
    {
        var result = Variable("result", "Result<i32,str>", parameter: true);
        var condition = ResultCall(result, "IsErr", "BOOL");
        var function = Function(
            [result],
            [
                new BoundIfStatement(
                    S,
                    condition,
                    [
                        new BoundReturnStatement(
                            S,
                            ResultCall(result, "UnwrapErr", "str")),
                    ],
                    [],
                    [
                        new BoundReturnStatement(
                            S,
                            ResultCall(result, "Unwrap", "i32")),
                    ]),
            ]);

        var diagnostics = Run(function, nulls: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Fact]
    public void Result_AliasCheckFlowsUntilReassignment()
    {
        var original = Variable("original", "Result<i32,str>", parameter: true);
        var alias = Variable("alias", "Result<i32,str>");
        var function = Function(
            [original],
            [
                new BoundBindStatement(S, alias, Ref(original)),
                new BoundIfStatement(
                    S,
                    ResultCall(alias, "IsErr", "BOOL"),
                    [
                        new BoundAssignmentStatement(
                            S,
                            Ref(original),
                            new BoundOkExpression(S, Int(1))),
                        new BoundReturnStatement(
                            S,
                            ResultCall(original, "UnwrapErr", "str")),
                    ],
                    [],
                    [new BoundReturnStatement(S, new BoundStringLiteral(S, "none"))]),
            ]);

        var diagnostics = Run(function, nulls: true);

        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Fact]
    public void Result_AliasPredicateRefinesOriginal()
    {
        var original = Variable("original", "Result<i32,str>", parameter: true);
        var alias = Variable("alias", "Result<i32,str>");
        var function = Function(
            [original],
            [
                new BoundBindStatement(S, alias, Ref(original)),
                new BoundIfStatement(
                    S,
                    ResultCall(alias, "IsErr", "BOOL"),
                    [
                        new BoundReturnStatement(
                            S,
                            ResultCall(original, "UnwrapErr", "str")),
                    ],
                    [],
                    [new BoundReturnStatement(S, new BoundStringLiteral(S, "ok"))]),
            ]);

        var diagnostics = Run(function, nulls: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnsafeUnwrap);
    }

    [Theory]
    [InlineData("i8", 127L, false)]
    [InlineData("u8", 255L, true)]
    [InlineData("i16", 32767L, false)]
    [InlineData("u16", 65535L, true)]
    [InlineData("i32", 2147483647L, false)]
    [InlineData("u32", 4294967295L, true)]
    [InlineData("i64", long.MaxValue, false)]
    public void Overflow_AllIntegralMaximumBoundaries(
        string type,
        long signedValue,
        bool unsigned)
    {
        var maximum = unsigned
            ? new BoundIntLiteral(
                S,
                signedValue,
                unchecked((ulong)signedValue),
                isUnsigned: true,
                type)
            : new BoundIntLiteral(S, signedValue, unchecked((ulong)signedValue), false, type);

        AssertBoundaryOverflow(type, maximum);
    }

    [Fact]
    public void Overflow_U64MaximumBoundary()
    {
        AssertBoundaryOverflow(
            "u64",
            new BoundIntLiteral(
                S,
                unchecked((long)ulong.MaxValue),
                ulong.MaxValue,
                isUnsigned: true,
                "u64"));
    }

    [Theory]
    [InlineData("u8")]
    [InlineData("u16")]
    [InlineData("u32")]
    [InlineData("u64")]
    public void Overflow_AllUnsignedLowerBoundaries(string type)
    {
        var zero = new BoundIntLiteral(
                S,
                0,
                0,
                isUnsigned: true,
                type);
        var target = Variable("value", type);
        var subtraction = new BoundBinaryExpression(
                S,
                BinaryOperator.Subtract,
                zero,
                Int(1),
                type);
        var function = Function(
                [],
                [new BoundBindStatement(S, target, subtraction)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Fact]
    public void Overflow_SmallIntegralOperandsPromoteToI32()
    {
        var left = new BoundIntLiteral(S, 127, 127, false, "i8");
        var right = new BoundIntLiteral(S, 1, 1, false, "i8");
        var addition = new BoundBinaryExpression(
                S,
                BinaryOperator.Add,
                left,
                right,
                "i8");
        var function = Function(
                [],
                [new BoundReturnStatement(S, addition)]);

        var diagnostics = Run(function, overflow: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Fact]
    public void Overflow_UintMaxPlusIntConstantUsesUintArithmetic()
    {
        var addition = new BoundBinaryExpression(
                S,
                BinaryOperator.Add,
                UInt(uint.MaxValue),
                Int(1),
                "LONG");
        var function = Function(
                [],
                [new BoundReturnStatement(S, addition)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow
                && diagnostic.Message.Contains("u32", StringComparison.Ordinal));
    }

    [Fact]
    public void Overflow_UintPlusLongConstantPromotesToI64()
    {
        var addition = new BoundBinaryExpression(
                S,
                BinaryOperator.Add,
                UInt(uint.MaxValue),
                SignedLiteral(1, "LONG"),
                "LONG");
        var function = Function(
                [],
                [new BoundReturnStatement(S, addition)]);

        var diagnostics = Run(function, overflow: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Fact]
    public void Overflow_UintPlusNegativeIntConstantPromotesToI64()
    {
        var addition = new BoundBinaryExpression(
                S,
                BinaryOperator.Add,
                UInt(uint.MaxValue),
                Int(-1),
                "LONG");
        var function = Function(
                [],
                [new BoundReturnStatement(S, addition)]);

        var diagnostics = Run(function, overflow: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Theory]
    [InlineData("INT")]
    [InlineData("LONG")]
    public void Overflow_UlongMaxPlusNonNegativeSignedConstantUsesU64(
        string constantType)
    {
        var addition = new BoundBinaryExpression(
                S,
                BinaryOperator.Add,
                ULong(ulong.MaxValue),
                SignedLiteral(1, constantType),
                "OBJECT");
        var function = Function(
                [],
                [new BoundReturnStatement(S, addition)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow
                && diagnostic.Message.Contains("u64", StringComparison.Ordinal));
    }

    [Fact]
    public void Overflow_UlongPlusNegativeSignedConstantIsIncomplete()
    {
        var addition = new BoundBinaryExpression(
                S,
                BinaryOperator.Add,
                ULong(1),
                Int(-1),
                "OBJECT");
        var function = Function(
                [],
                [new BoundReturnStatement(S, addition)]);

        var diagnostics = Run(function, overflow: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
        Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.BugPatternAnalysisIncomplete);
    }

    [Theory]
    [InlineData("u8", 255L, BinaryOperator.Add, false)]
    [InlineData("u8", 255L, BinaryOperator.Add, true)]
    [InlineData("u8", 255L, BinaryOperator.Subtract, false)]
    [InlineData("u8", 255L, BinaryOperator.Subtract, true)]
    [InlineData("u8", 255L, BinaryOperator.Multiply, false)]
    [InlineData("u8", 255L, BinaryOperator.Multiply, true)]
    [InlineData("u16", 65535L, BinaryOperator.Add, false)]
    [InlineData("u16", 65535L, BinaryOperator.Add, true)]
    [InlineData("u16", 65535L, BinaryOperator.Subtract, false)]
    [InlineData("u16", 65535L, BinaryOperator.Subtract, true)]
    [InlineData("u16", 65535L, BinaryOperator.Multiply, false)]
    [InlineData("u16", 65535L, BinaryOperator.Multiply, true)]
    public void Overflow_NarrowUnsignedAndU32PromoteBeforeMixedRules(
        string narrowType,
        long narrowMaximum,
        BinaryOperator operation,
        bool narrowFirst)
    {
        var narrow = new BoundIntLiteral(
                S,
                narrowMaximum,
                (ulong)narrowMaximum,
                isUnsigned: true,
                narrowType);
        var wide = UInt(uint.MaxValue);
        var expression = new BoundBinaryExpression(
                S,
                operation,
                narrowFirst ? narrow : wide,
                narrowFirst ? wide : narrow,
                "LONG");
        var function = Function(
                [],
                [new BoundReturnStatement(S, expression)]);

        var diagnostics = Run(function, overflow: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Fact]
    public void Overflow_DivisionWithInteriorZeroExclusionIsSoundAndDoesNotCrash()
    {
        var divisor = Variable("divisor", "i32", parameter: true);
        var condition = new BoundBinaryExpression(
                S,
                BinaryOperator.NotEqual,
                Ref(divisor),
                Int(0),
                "BOOL");
        var division = new BoundBinaryExpression(
                S,
                BinaryOperator.Divide,
                SignedLiteral(int.MinValue, "INT"),
                Ref(divisor),
                "INT");
        var addition = new BoundBinaryExpression(
                S,
                BinaryOperator.Add,
                division,
                Int(1),
                "INT");
        var function = Function(
                [divisor],
                [
                    new BoundIfStatement(
                        S,
                        condition,
                        [new BoundReturnStatement(S, addition)],
                        [],
                        [new BoundReturnStatement(S, Int(0))]),
                ]);

        var diagnostics = Run(
                function,
                division: true,
                overflow: true);

        Assert.DoesNotContain(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.DivisionByZero);
        Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow
                && diagnostic.Message.Contains("arithmetic", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("INT", int.MinValue, BinaryOperator.Divide)]
    [InlineData("INT", int.MinValue, BinaryOperator.Modulo)]
    [InlineData("LONG", long.MinValue, BinaryOperator.Divide)]
    [InlineData("LONG", long.MinValue, BinaryOperator.Modulo)]
    public void Overflow_MinValueNegativeOneCoversDivisionAndRemainder(
        string type,
        long minimum,
        BinaryOperator operation)
    {
        var expression = new BoundBinaryExpression(
                S,
                operation,
                SignedLiteral(minimum, type),
                SignedLiteral(-1, type),
                type);
        var function = Function(
                [],
                [new BoundReturnStatement(S, expression)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow
                && diagnostic.Message.Contains("MinValue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("INT", int.MinValue, CompoundAssignmentOperator.Divide)]
    [InlineData("INT", int.MinValue, CompoundAssignmentOperator.Modulo)]
    [InlineData("LONG", long.MinValue, CompoundAssignmentOperator.Divide)]
    [InlineData("LONG", long.MinValue, CompoundAssignmentOperator.Modulo)]
    public void Overflow_CompoundMinValueNegativeOneCoversDivisionAndRemainder(
        string type,
        long minimum,
        CompoundAssignmentOperator operation)
    {
        var value = Variable("value", type);
        var function = Function(
                [],
                [
                    new BoundBindStatement(
                        S,
                        value,
                        SignedLiteral(minimum, type)),
                    new BoundCompoundAssignment(
                        S,
                        Ref(value),
                        operation,
                        SignedLiteral(-1, type)),
                    new BoundReturnStatement(S, Ref(value)),
                ]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow
                && diagnostic.Message.Contains("MinValue", StringComparison.Ordinal));
    }

    [Fact]
    public void Overflow_ExplicitNumericCastsRespectExactTargetRange()
    {
        var decimalOverflow = new BoundTypeOperationExpression(
                S,
                TypeOp.Cast,
                new BoundDecimalLiteral(S, decimal.MaxValue),
                "i32");
        var narrowingOverflow = new BoundTypeOperationExpression(
                S,
                TypeOp.Cast,
                SignedLiteral((long)int.MaxValue + 1, "LONG"),
                "i32");
        var safeNarrowing = new BoundTypeOperationExpression(
                S,
                TypeOp.Cast,
                SignedLiteral(int.MaxValue, "LONG"),
                "i32");

        var decimalDiagnostics = Run(
                Function([], [new BoundReturnStatement(S, decimalOverflow)]),
                overflow: true);
        var narrowingDiagnostics = Run(
                Function([], [new BoundReturnStatement(S, narrowingOverflow)]),
                overflow: true);
        var safeDiagnostics = Run(
                Function([], [new BoundReturnStatement(S, safeNarrowing)]),
                overflow: true);

        Assert.Contains(decimalDiagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
        Assert.Contains(narrowingDiagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
        Assert.DoesNotContain(safeDiagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Fact]
    public void Overflow_SameSymbolSubtractionPreservesCorrelation()
    {
        var value = Variable("value", "i32", parameter: true);
        var difference = new BoundBinaryExpression(
                S,
                BinaryOperator.Subtract,
                Ref(value),
                Ref(value),
                "INT");
        var safeFunction = Function(
                [value],
                [new BoundReturnStatement(S, difference)]);
        var zeroDivisorFunction = Function(
                [value],
                [
                    new BoundReturnStatement(
                        S,
                        new BoundBinaryExpression(
                            S,
                            BinaryOperator.Divide,
                            Int(1),
                            difference,
                            "INT")),
                ]);

        var safeDiagnostics = Run(safeFunction, overflow: true);
        var zeroDiagnostics = Run(
                zeroDivisorFunction,
                division: true,
                overflow: true);

        Assert.DoesNotContain(safeDiagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
        Assert.Contains(zeroDiagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void Overflow_DistinctSameNamedSymbolsAreNotCorrelated()
    {
        var left = Variable("value", "i32", parameter: true);
        var right = Variable("value", "i32", parameter: true);
        var subtraction = new BoundBinaryExpression(
                S,
                BinaryOperator.Subtract,
                Ref(left),
                Ref(right),
                "INT");
        var function = Function(
                [left, right],
                [new BoundReturnStatement(S, subtraction)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Theory]
    [InlineData("INT", 2147483647L, 32L, false)]
    [InlineData("INT", 2147483647L, -32L, false)]
    [InlineData("INT", 2147483647L, 33L, true)]
    [InlineData("INT", 2147483647L, -1L, true)]
    [InlineData("INT", 2147483647L, 2147483647L, true)]
    [InlineData("LONG", long.MaxValue, 64L, false)]
    [InlineData("LONG", long.MaxValue, 65L, true)]
    public void Overflow_BinaryShiftMasksCountLikeCSharp(
        string type,
        long value,
        long count,
        bool expectedOverflow)
    {
        var shift = new BoundBinaryExpression(
                S,
                BinaryOperator.LeftShift,
                SignedLiteral(value, type),
                Int(count),
                type);
        var function = Function(
                [],
                [new BoundReturnStatement(S, shift)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Equal(
                expectedOverflow,
                diagnostics.Any(diagnostic =>
                    diagnostic.Code == DiagnosticCode.IntegerOverflow));
    }

    [Theory]
    [InlineData(CompoundAssignmentOperator.LeftShift)]
    [InlineData(CompoundAssignmentOperator.RightShift)]
    public void Overflow_CompoundShiftMasksCountBeforeStateUpdate(
        CompoundAssignmentOperator operation)
    {
        var value = Variable("value", "i32");
        var function = Function(
                [],
                [
                    new BoundBindStatement(S, value, Int(int.MaxValue)),
                    new BoundCompoundAssignment(S, Ref(value), operation, Int(32)),
                    new BoundReturnStatement(
                        S,
                        new BoundBinaryExpression(
                            S,
                            BinaryOperator.Add,
                            Ref(value),
                            Int(1),
                            "INT")),
                ]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Code == DiagnosticCode.IntegerOverflow
                && diagnostic.Message.Contains("arithmetic", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("i8", -128L)]
    [InlineData("i16", -32768L)]
    [InlineData("i32", -2147483648L)]
    [InlineData("i64", long.MinValue)]
    public void Overflow_AllSignedMinimumBoundaries(string type, long minimum)
    {
        var target = Variable("value", type);
        var literal = new BoundIntLiteral(
            S,
            minimum,
            unchecked((ulong)minimum),
            isUnsigned: false,
            type);
        var subtraction = new BoundBinaryExpression(
            S,
            BinaryOperator.Subtract,
            literal,
            Int(1),
            type);
        var function = Function(
            [],
            [new BoundBindStatement(S, target, subtraction)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    [Theory]
    [InlineData("i32", -2147483648L)]
    [InlineData("i64", long.MinValue)]
    public void Overflow_MinimumDividedByNegativeOne(string type, long minimum)
    {
        var left = new BoundIntLiteral(
            S,
            minimum,
            unchecked((ulong)minimum),
            isUnsigned: false,
            type);
        var division = new BoundBinaryExpression(
            S,
            BinaryOperator.Divide,
            left,
            new BoundIntLiteral(S, -1, unchecked((ulong)-1), false, type),
            type);
        var function = Function(
            [],
            [new BoundReturnStatement(S, division)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics.Warnings, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow
            && diagnostic.Message.Contains("MinValue / -1", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownSemanticsAreExplicitAndHintsUseSeparateCodes()
    {
        var opaque = new BoundInteropExpression(
            S,
            "CheckedExpression",
            "checked(value + 1)",
            "OBJECT");
        var division = new BoundBinaryExpression(
            S,
            BinaryOperator.Divide,
            Int(1),
            opaque,
            "OBJECT");
        var function = Function(
            [],
            [new BoundReturnStatement(S, division)]);

        var diagnostics = Run(
            function,
            division: true,
            reportOnlyVerified: false);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.BugPatternAnalysisIncomplete);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZeroHint
            && diagnostic.Severity == DiagnosticSeverity.Info);
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.DivisionByZero);
    }

    [Fact]
    public void AdversarialCorpusMeetsPrecisionAndRecallThresholds()
    {
        var cases = new[]
        {
            CorpusCase(DivisionFunction(Int(0)), DiagnosticCode.DivisionByZero, true),
            CorpusCase(DivisionFunction(Int(2)), DiagnosticCode.DivisionByZero, false),
            CorpusCase(ArrayFunction(Int(-1), length: 3), DiagnosticCode.IndexOutOfBounds, true),
            CorpusCase(ArrayFunction(Int(2), length: 3), DiagnosticCode.IndexOutOfBounds, false),
            CorpusCase(ArrayFunction(Int(3), length: 3), DiagnosticCode.IndexOutOfBounds, true),
            CorpusCase(OptionFunction(empty: true), DiagnosticCode.UnsafeUnwrap, true),
            CorpusCase(OptionFunction(empty: false), DiagnosticCode.UnsafeUnwrap, false),
            CorpusCase(OverflowFunction(int.MaxValue, 1), DiagnosticCode.IntegerOverflow, true),
            CorpusCase(OverflowFunction(10, 20), DiagnosticCode.IntegerOverflow, false),
            CorpusCase(OffByOneFunction(unsafeBound: true), DiagnosticCode.OffByOne, true),
            CorpusCase(OffByOneFunction(unsafeBound: false), DiagnosticCode.OffByOne, false),
        };

        var truePositive = 0;
        var falsePositive = 0;
        var falseNegative = 0;
        foreach (var item in cases)
        {
            var diagnostics = Run(item.Function);
            var found = diagnostics.Any(diagnostic => diagnostic.Code == item.Code);
            if (found && item.Expected)
                truePositive++;
            else if (found)
                falsePositive++;
            else if (item.Expected)
                falseNegative++;
        }

        var precision = truePositive / (double)(truePositive + falsePositive);
        var recall = truePositive / (double)(truePositive + falseNegative);
        Assert.True(precision >= 0.95, $"precision {precision:P2} is below 95%");
        Assert.True(recall >= 0.95, $"recall {recall:P2} is below 95%");
        Assert.Equal(0, falseNegative);
    }

    private static void AssertBoundaryOverflow(
        string type,
        BoundIntLiteral maximum)
    {
        var target = Variable("value", type);
        var addition = new BoundBinaryExpression(
            S,
            BinaryOperator.Add,
            maximum,
            Int(1),
            type);
        var function = Function(
            [],
            [new BoundBindStatement(S, target, addition)]);

        var diagnostics = Run(function, overflow: true);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.IntegerOverflow);
    }

    private static CorpusItem CorpusCase(
        BoundFunction function,
        string code,
        bool expected) =>
        new(function, code, expected);

    private static BoundFunction DivisionFunction(BoundExpression divisor) =>
        DivisionFunction(divisor, Int(10), "INT");

    private static BoundBinaryExpression DivideByLiteralZero() =>
        new(
            S,
            BinaryOperator.Divide,
            Int(1),
            Int(0),
            "INT");

    private static BoundBinaryExpression DivisionAsBoolean() =>
        new(
            S,
            BinaryOperator.Equal,
            DivideByLiteralZero(),
            Int(0),
            "BOOL");

    private static BoundFunction DivisionFunction(
        BoundExpression divisor,
        BoundExpression numerator,
        string resultType) =>
        Function(
            [],
            [
                new BoundReturnStatement(
                    S,
                    new BoundBinaryExpression(
                        S,
                        BinaryOperator.Divide,
                        numerator,
                        divisor,
                        resultType)),
            ]);

    private static BoundFunction NamedDivisionFunction(
        string name,
        VariableSymbol divisor)
    {
        var symbol = new FunctionSymbol(
            new SymbolId($"function:{name}:{Guid.NewGuid():N}"),
            name,
            "i32",
            [divisor]);
        return new BoundFunction(
            S,
            symbol,
            [
                new BoundReturnStatement(
                    S,
                    new BoundBinaryExpression(
                        S,
                        BinaryOperator.Divide,
                        Int(10),
                        Ref(divisor),
                        "INT")),
            ],
            new Scope());
    }

    private static BoundFunction ArrayFunction(
        BoundExpression index,
        int length)
    {
        var items = Variable("items", "i32[]");
        return Function(
            [],
            [
                new BoundBindStatement(
                    S,
                    items,
                    new BoundArrayCreation(
                        S,
                        "a",
                        "items",
                        "i32",
                        Int(length),
                        [])),
                new BoundReturnStatement(
                    S,
                    new BoundArrayAccess(S, Ref(items), index)),
            ]);
    }

    private static BoundFunction OptionFunction(bool empty)
    {
        var option = Variable("option", "Option<i32>");
        var initializer = empty
            ? (BoundExpression)new BoundNoneLiteral(S, "Option<i32>")
            : new BoundSomeExpression(S, Int(1));
        var unwrap = new BoundCallExpression(
            S,
            "option.Unwrap",
            [],
            "i32",
            resolvedMethodName: "Unwrap",
            receiverSymbol: option);
        return Function(
            [],
            [
                new BoundBindStatement(S, option, initializer),
                new BoundReturnStatement(S, unwrap),
            ]);
    }

    private static IEnumerable<BoundFunction> RefMutationFunctions()
    {
        yield return Build((option, dummy, mutation, unwrap) =>
        [
            new BoundBindStatement(S, dummy, mutation),
            new BoundReturnStatement(S, unwrap),
        ]);
        yield return Build((option, dummy, mutation, unwrap) =>
        [
            new BoundBindStatement(S, dummy, Int(0)),
            new BoundAssignmentStatement(S, Ref(dummy), mutation),
            new BoundReturnStatement(S, unwrap),
        ]);
        yield return Build((option, dummy, mutation, unwrap) =>
        [
            new BoundBindStatement(
                S,
                dummy,
                new BoundBinaryExpression(
                    S,
                    BinaryOperator.Add,
                    mutation,
                    Int(1),
                    "INT")),
            new BoundReturnStatement(S, unwrap),
        ]);
        yield return Build((option, dummy, mutation, unwrap) =>
        [
            new BoundTryStatement(
                S,
                [new BoundReturnStatement(S, mutation)],
                [],
                [new BoundExpressionStatement(S, unwrap)]),
        ]);
        yield return Build((option, dummy, mutation, unwrap) =>
        [
            new BoundIfStatement(
                S,
                new BoundCallExpression(
                    S,
                    "Mutate",
                    [Ref(option)],
                    "BOOL",
                    argumentModifiers: ["ref"]),
                [new BoundReturnStatement(S, unwrap)],
                [],
                [new BoundReturnStatement(S, unwrap)]),
        ]);

        static BoundFunction Build(
            Func<
                VariableSymbol,
                VariableSymbol,
                BoundCallExpression,
                BoundCallExpression,
                IReadOnlyList<BoundStatement>> bodyFactory)
        {
            var option = Variable("option", "Option<i32>");
            var dummy = Variable("dummy", "i32");
            var mutation = new BoundCallExpression(
                S,
                "Mutate",
                [Ref(option)],
                "INT",
                argumentModifiers: ["ref"]);
            var unwrap = OptionCall(option, "Unwrap", "i32");
            return Function(
                [],
                [
                    new BoundBindStatement(
                        S,
                        option,
                        new BoundSomeExpression(S, Int(1))),
                    .. bodyFactory(option, dummy, mutation, unwrap),
                ]);
        }
    }

    private static BoundCallExpression OptionCall(
        VariableSymbol receiver,
        string methodName,
        string resultType) =>
        new(
            S,
            $"{receiver.Name}.{methodName}",
            [],
            resultType,
            resolvedMethodName: methodName,
            receiverSymbol: receiver);

    private static BoundFunction ResultUnwrapFunction(
        bool ok,
        string methodName)
    {
        var result = Variable("result", "Result<i32,str>");
        var initializer = ok
            ? (BoundExpression)new BoundOkExpression(S, Int(1))
            : new BoundErrExpression(S, new BoundStringLiteral(S, "error"));
        return Function(
            [],
            [
                new BoundBindStatement(S, result, initializer),
                new BoundReturnStatement(
                    S,
                    ResultCall(
                        result,
                        methodName,
                        methodName == "UnwrapErr" ? "str" : "i32")),
            ]);
    }

    private static BoundCallExpression ResultCall(
        VariableSymbol receiver,
        string methodName,
        string resultType) =>
        new(
            S,
            $"{receiver.Name}.{methodName}",
            [],
            resultType,
            resolvedMethodName: methodName,
            receiverSymbol: receiver);

    private static BoundFunction OverflowFunction(long left, long right) =>
        Function(
            [],
            [
                new BoundReturnStatement(
                    S,
                    new BoundBinaryExpression(
                        S,
                        BinaryOperator.Add,
                        Int(left),
                        Int(right),
                        "INT")),
            ]);

    private static BoundFunction OffByOneFunction(bool unsafeBound)
    {
        var items = Variable("items", "i32[]", parameter: true);
        var i = Variable("i", "i32");
        BoundExpression upper = new BoundArrayLength(S, Ref(items));
        if (!unsafeBound)
        {
            upper = new BoundBinaryExpression(
                S,
                BinaryOperator.Subtract,
                upper,
                Int(1),
                "INT");
        }
        return Function(
            [items],
            [
                new BoundForStatement(
                    S,
                    i,
                    Int(0),
                    upper,
                    Int(1),
                    [
                        new BoundExpressionStatement(
                            S,
                            new BoundArrayAccess(S, Ref(items), Ref(i))),
                    ]),
            ]);
    }

    private static DiagnosticBag Run(
        BoundFunction function,
        bool division = true,
        bool bounds = true,
        bool nulls = true,
        bool overflow = true,
        bool offByOne = true,
        bool reportOnlyVerified = true)
    {
        var diagnostics = new DiagnosticBag();
        var runner = new BugPatternRunner(
            diagnostics,
            new BugPatternOptions
            {
                CheckDivisionByZero = division,
                CheckIndexOutOfBounds = bounds,
                CheckNullDereference = nulls,
                CheckOverflow = overflow,
                CheckOffByOne = offByOne,
                CheckMissingPreconditions = false,
                ReportOnlyVerified = reportOnlyVerified,
                UseZ3Verification = false,
            });
        runner.CheckFunction(function);
        return diagnostics;
    }

    private static BoundFunction BindFunction(string source)
    {
        var diagnostics = new DiagnosticBag();
        var parser = new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(),
            diagnostics);
        var module = parser.Parse();
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Errors));

        var bound = new Binder(diagnostics).Bind(module);
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Errors));
        return Assert.Single(bound.Functions);
    }

    private static BoundFunction Function(
        IReadOnlyList<VariableSymbol> parameters,
        IReadOnlyList<BoundStatement> body)
    {
        var symbol = new FunctionSymbol(
            new SymbolId($"function:{Guid.NewGuid():N}"),
            "Test",
            "i32",
            parameters);
        return new BoundFunction(S, symbol, body, new Scope());
    }

    private static VariableSymbol Variable(
        string name,
        string type,
        bool parameter = false) =>
        new(
            new SymbolId($"variable:{name}:{Guid.NewGuid():N}"),
            name,
            type,
            isMutable: true,
            isParameter: parameter);

    private static BoundVariableExpression Ref(VariableSymbol variable) =>
        new(S, variable);

    private static BoundIntLiteral Int(long value) => new(S, value);

    private static BoundIntLiteral UInt(uint value) =>
        new(S, value, value, isUnsigned: true, "UINT");

    private static BoundIntLiteral ULong(ulong value) =>
        new(
            S,
            unchecked((long)value),
            value,
            isUnsigned: true,
            "ULONG");

    private static BoundIntLiteral SignedLiteral(long value, string type) =>
        new(S, value, unchecked((ulong)value), isUnsigned: false, type);

    private readonly record struct CorpusItem(
        BoundFunction Function,
        string Code,
        bool Expected);
}
