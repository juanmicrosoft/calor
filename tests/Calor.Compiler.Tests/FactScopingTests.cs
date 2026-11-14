using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Regression tests for obligation fact scoping. Guard facts must be confined
/// to the bodies they dominate: sibling branch guards must not combine into an
/// inconsistent (UNSAT) assumption set that vacuously discharges every
/// obligation in the function, and facts whose variables are rebound inside the
/// governed body must be dropped.
/// </summary>
public sealed class FactScopingTests
{
    [Fact]
    public void DoWhile_FirstIterationIsExactWithoutAssumingItsCondition()
    {
        var module = Parse("""
            module Test
              func Probe (int:n) → void
                !
                do
                  proof n == 0
                while n > 0
                proof n == 0
            """, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Select(d => d.Message)));
        var function = Assert.Single(module.Functions);
        var loop = Assert.IsType<DoWhileStatementNode>(function.Body[0]);
        var collector = new FactCollector();
        collector.CollectFromFunction(function);

        Assert.True(collector.IsExact(Assert.Single(loop.Body).Span));
        Assert.False(collector.IsExact(function.Body[1].Span));
        Assert.DoesNotContain(collector.ScopedFacts, fact =>
            fact.Fact is BinaryOperationNode { Operator: BinaryOperator.GreaterThan });
    }

    [Theory]
    [InlineData("1", BinaryOperator.GreaterOrEqual, BinaryOperator.LessOrEqual)]
    [InlineData("-1", BinaryOperator.LessOrEqual, BinaryOperator.GreaterOrEqual)]
    public void ExpressionBounds_AreScopedToTheCountedLoopBody(
        string step, BinaryOperator fromOperator, BinaryOperator toOperator)
    {
        var module = Parse($$"""
            module Test
              func Probe (int:start, int:end) → void
                !
                for i from start to end step {{step}}
                  proof i == i
                proof start == start
            """, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Select(d => d.Message)));
        var function = Assert.Single(module.Functions);
        var loop = Assert.IsType<ForStatementNode>(function.Body[0]);
        var collector = new FactCollector();
        collector.CollectFromFunction(function);

        Assert.Contains(collector.ScopedFacts, fact =>
            fact.Fact is BinaryOperationNode { Right: ReferenceNode { Name: "start" } } bound
            && bound.Operator == fromOperator && fact.AppliesTo(loop.Body[0].Span));
        Assert.Contains(collector.ScopedFacts, fact =>
            fact.Fact is BinaryOperationNode { Right: ReferenceNode { Name: "end" } } bound
            && bound.Operator == toOperator && fact.AppliesTo(loop.Body[0].Span));
        Assert.All(collector.ScopedFacts, fact => Assert.False(fact.AppliesTo(function.Body[1].Span)));
    }

    [Fact]
    public void ReEvaluatedFor_UsesItsGuardAndNeverInventsSnapshotBounds()
    {
        var module = Parse("""
            module Test
              func Probe (int:start, int:end) → void
                !
                for i = start; i < end; i = i + 2
                  proof i < end
                proof start == start
            """, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Select(d => d.Message)));
        var function = Assert.Single(module.Functions);
        var loop = Assert.IsType<ForStatementNode>(function.Body[0]);
        var collector = new FactCollector();
        collector.CollectFromFunction(function);

        var fact = Assert.Single(collector.ScopedFacts);
        Assert.Same(loop.Condition, fact.Fact);
        Assert.True(fact.AppliesTo(loop.Body[0].Span));
        Assert.False(fact.AppliesTo(function.Body[1].Span));
        Assert.DoesNotContain(collector.Facts, expression =>
            expression is BinaryOperationNode { Operator: BinaryOperator.GreaterOrEqual or BinaryOperator.LessOrEqual });
    }

    [Fact]
    public void ReEvaluatedFor_BodyMutationKillsTheGuard()
    {
        var module = Parse("""
            module Test
              func Probe (int:end) → void
                !
                for i = 0; i < end; i = i + 1
                  set i end
                  proof i < end
            """, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Select(d => d.Message)));
        var collector = new FactCollector();
        collector.CollectFromFunction(Assert.Single(module.Functions));
        Assert.Empty(collector.ScopedFacts);
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void SiblingGuards_HaveDisjointScopes()
    {
        var source = """
            module Test
              func Branchy private (int:x) → int
                if x == 1
                  return 1
                else if x == 2
                  return 2
                return 0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var collector = new FactCollector();
        collector.CollectFromFunction(func);

        // Both guard facts collected, but scoped to their own branches
        Assert.Equal(2, collector.ScopedFacts.Count);

        var first = collector.ScopedFacts[0];
        var second = collector.ScopedFacts[1];

        // No source position can be governed by both sibling guards
        Assert.True(first.ScopeEnd <= second.ScopeStart || second.ScopeEnd <= first.ScopeStart,
            $"Sibling guard scopes overlap: [{first.ScopeStart},{first.ScopeEnd}] vs [{second.ScopeStart},{second.ScopeEnd}]");
    }

    [Fact]
    public void GuardFact_DoesNotApply_OutsideItsBranch()
    {
        var source = """
            module Test
              func Guarded private (int:x) → int
                if x > 0
                  return 1
                return 0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var func = Assert.Single(module.Functions);
        var collector = new FactCollector();
        collector.CollectFromFunction(func);

        var guard = Assert.Single(collector.ScopedFacts);

        // The trailing return sits after the then-body; the guard must not
        // govern it
        var trailingReturn = func.Body[^1];
        Assert.False(guard.AppliesTo(trailingReturn.Span),
            "Guard fact leaked past the body it dominates");
    }

    [Fact]
    public void WhileCondition_Killed_WhenBodyRebindsItsVariable()
    {
        var source = """
            module Test
              func Counter private (int:n) → int
                var int:i = 0
                while i < n
                  set i i + 1
                return i
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(module.Functions);
        var collector = new FactCollector();
        collector.CollectFromFunction(func);

        // The while condition (< i n) mentions i, and the body rebinds i, so the
        // fact may not hold at obligation sites inside the body — it must be dropped
        Assert.DoesNotContain(collector.ScopedFacts, f =>
            f.Fact is BinaryOperationNode { Operator: BinaryOperator.LessThan });
    }

    [Fact]
    public void BranchGuard_Killed_WhenBodyAssignsItsVariable()
    {
        var source = """
            module Test
              func Mutate private (int:x) → void
                if x > 0
                  set x -1
                  proof x > 0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var collector = new FactCollector();
        collector.CollectFromFunction(Assert.Single(module.Functions));

        Assert.DoesNotContain(
            collector.ScopedFacts,
            fact => fact.Fact is BinaryOperationNode
            {
                Operator: BinaryOperator.GreaterThan
            });
    }

    [Fact]
    public void BranchGuard_Killed_WhenBodyMutatesVariableWithExplicitUpdate()
    {
        var source = """
            module Test
              func Mutate private (int:x) → void
                if x > 0
                  set x x - 1
                  proof x > 0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var collector = new FactCollector();
        collector.CollectFromFunction(Assert.Single(module.Functions));

        Assert.DoesNotContain(
            collector.ScopedFacts,
            fact => fact.Fact is BinaryOperationNode
            {
                Operator: BinaryOperator.GreaterThan
            });
    }

    [Fact]
    public void MethodBranchGuard_IsCollectedWithFunctionSemantics()
    {
        var source = """
            module Test
              class Checker
                func Check (int:x) → void
                  !
                  if x > 0
                    proof x > 0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");

        var method = Assert.Single(Assert.Single(module.Classes).Methods);
        var collector = new FactCollector();
        collector.CollectFromMethod(method);

        Assert.Contains(
            collector.ScopedFacts,
            fact => fact.Fact is BinaryOperationNode
            {
                Operator: BinaryOperator.GreaterThan
            });
    }

    [Fact]
    public void NegativeStepForLoop_UsesInclusiveDescendingBounds()
    {
        var source = """
            module Test
              func Descending private () → int
                for i from 2 to 0 step -1
                  System.Console.WriteLine(i >= 0)
                return 0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var collector = new FactCollector();
        collector.CollectFromFunction(Assert.Single(module.Functions));

        Assert.Contains(
            collector.Facts,
            fact => fact is BinaryOperationNode { Operator: BinaryOperator.LessOrEqual });
        Assert.Contains(
            collector.Facts,
            fact => fact is BinaryOperationNode { Operator: BinaryOperator.GreaterOrEqual });
    }

    [Fact]
    public void DynamicStepForLoop_DoesNotAssumeDirection()
    {
        var source = """
            module Test
              func Dynamic private (int:@step) → int
                for i from 0 to 10 step @step
                  proof i >= 0
                return 0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Select(d => d.Message)));

        var collector = new FactCollector();
        collector.CollectFromFunction(Assert.Single(module.Functions));

        Assert.DoesNotContain(
            collector.ScopedFacts,
            fact => fact.Fact is BinaryOperationNode
            {
                Left: ReferenceNode { Name: "i" }
            });
    }

    [SkippableFact]
    public void ContradictorySiblingGuards_DoNotVacuouslyDischargeObligations()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Before fact scoping, both sibling guards (x==1, x==2) were asserted
        // together for every obligation in the function, making the assumption
        // set UNSAT and vacuously discharging the unguarded index access.
        var source = """
            §M{Test}
              §ITYPE{SizedList:IntArr:n}
              §F{Bad:priv}
                  §I{SizedList:items}
                  §I{i32:x}
                  §O{i32}
                  §IF (== x 1)
                      §B{a:i32} INT:1
                  §EI (== x 2)
                      §B{b:i32} INT:2
                  §R §IDX items x
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            EnableTypeChecking = false,
            EnforceEffects = false
        };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var indexObl = options.ObligationResults.Obligations
            .FirstOrDefault(o => o.Kind == ObligationKind.IndexBounds);
        Assert.NotNull(indexObl);

        // The unguarded access must not be proven safe
        Assert.Equal(ObligationStatus.Failed, indexObl.Status);
    }

    [SkippableFact]
    public void GuardedIndexAccess_InsideBranch_StillUsesGuardFact()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // The scoping change must not break the legitimate case: an index
        // obligation inside the guarded body still sees the dominating guard.
        var source = """
            §M{Test}
              §ITYPE{SizedList:IntArr:n}
              §F{SafeGet:priv}
                  §I{SizedList:items}
                  §I{i32:i} | (>= # INT:0)
                  §O{i32}
                  §IF (< i n)
                      §R §IDX items i
                  §R INT:0
            """;

        var options = new CompilationOptions
        {
            VerifyRefinements = true,
            EnableTypeChecking = false,
            EnforceEffects = false
        };
        var result = Program.Compile(source, "test.calr", options);

        Assert.NotNull(options.ObligationResults);

        var indexObl = options.ObligationResults.Obligations
            .FirstOrDefault(o => o.Kind == ObligationKind.IndexBounds);
        Assert.NotNull(indexObl);
        // D14. Note WHICH sort carries it: the condition here is purely numeric over i32s —
        // it demotes because the receiver's type mints an UNINTERPRETED sort, not an array
        // one. (An earlier revision of this comment said "the ARRAY sort", which is wrong;
        // the counterexample prints `items=sizedlist!val!0`.)
        //
        // Asserting the OUTCOME, not just `NotEqual(Discharged)` — that weaker form passes
        // for Pending/Unsupported/Failed/Timeout too, i.e. it would pass if obligation
        // solving were deleted outright.
        Assert.Equal(Calor.Compiler.Verification.ProofStatus.Assumed, indexObl.Outcome!.Status);
        Assert.Contains(Calor.Compiler.Verification.Z3.Z3Verifier.NullableReferenceModelAssumption,
            indexObl.Outcome.Assumptions);
    }
}
