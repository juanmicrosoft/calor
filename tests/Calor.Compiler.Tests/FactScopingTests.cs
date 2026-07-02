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
            §M{m001:Test}
              §F{f001:Branchy:priv}
                  §I{i32:x}
                  §O{i32}
                  §IF{if1} (== x 1)
                      §R INT:1
                  §EI (== x 2)
                      §R INT:2
                  §R INT:0
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
            §M{m001:Test}
              §F{f001:Guarded:priv}
                  §I{i32:x}
                  §O{i32}
                  §IF{if1} (> x 0)
                      §R INT:1
                  §R INT:0
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var func = Assert.Single(module.Functions);
        var collector = new FactCollector();
        collector.CollectFromFunction(func);

        var guard = Assert.Single(collector.ScopedFacts);

        // The trailing §R INT:0 sits after the then-body; the guard must not
        // govern it
        var trailingReturn = func.Body[^1];
        Assert.False(guard.AppliesTo(trailingReturn.Span),
            "Guard fact leaked past the body it dominates");
    }

    [Fact]
    public void WhileCondition_Killed_WhenBodyRebindsItsVariable()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Counter:priv}
                  §I{i32:n}
                  §O{i32}
                  §B{~i:i32} INT:0
                  §WH{w1} (< i n)
                      §B{i:i32} (+ i INT:1)
                  §R i
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

    [SkippableFact]
    public void ContradictorySiblingGuards_DoNotVacuouslyDischargeObligations()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Before fact scoping, both sibling guards (x==1, x==2) were asserted
        // together for every obligation in the function, making the assumption
        // set UNSAT and vacuously discharging the unguarded index access.
        var source = """
            §M{m001:Test}
              §ITYPE{it1:SizedList:IntArr:n}
              §F{f001:Bad:priv}
                  §I{SizedList:items}
                  §I{i32:x}
                  §O{i32}
                  §IF{if1} (== x 1)
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
        Assert.NotEqual(ObligationStatus.Discharged, indexObl.Status);
    }

    [SkippableFact]
    public void GuardedIndexAccess_InsideBranch_StillUsesGuardFact()
    {
        Skip.IfNot(Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // The scoping change must not break the legitimate case: an index
        // obligation inside the guarded body still sees the dominating guard.
        var source = """
            §M{m001:Test}
              §ITYPE{it1:SizedList:IntArr:n}
              §F{f001:SafeGet:priv}
                  §I{SizedList:items}
                  §I{i32:i} | (>= # INT:0)
                  §O{i32}
                  §IF{if1} (< i n)
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
        Assert.Equal(ObligationStatus.Discharged, indexObl.Status);
    }
}
