using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.KInduction;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (S2 R-NUM, amendments 1.3.1 and 1.3.2): overflow sensitivity is decided by rule from operand
/// widths, with no solver, so the verdict does not depend on solver time (the linux-arm64
/// z3-consumer-matrix failure on i32 * u32); and k-induction refuses a whole
/// loop whose bounds hold a literal outside int32. The tests need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2NumericDeterminismTests
{
    private static (List<(string Name, string Type)> Parameters, ExpressionNode Arithmetic) Parse(
        string leftType, string rightType, string arithmetic)
    {
        var source = $$"""
            §M{m1:R1Case}
              §F{f1:Probe:pub} ({{leftType}}:x, {{rightType}}:y) -> i32
                §E{}
                §S (== {{arithmetic}} {{arithmetic}})
                §R INT:0
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
        var function = Assert.Single(module.Functions);
        var equality = Assert.IsType<BinaryOperationNode>(Assert.Single(function.Postconditions).Condition);
        return ([("x", leftType), ("y", rightType)], equality.Left);
    }

    [Theory]
    [InlineData("i32", "u32", "(* x y)", false)]     // linux-arm64 case: i32 * u32 in 64 bits
    [InlineData("u32", "i32", "(- x y)", false)]
    [InlineData("i32", "i32", "(* x y)", true)]      // a 32-bit product can overflow
    [InlineData("i32", "i32", "(+ x y)", true)]
    [InlineData("i64", "i64", "(* x y)", true)]      // 64-bit nonlinear: once a solver-timeout shape
    public void OverflowSensitivity_IsDecidedByRule_WithoutTheSolver(
        string leftType, string rightType, string arithmetic, bool sensitive)
    {
        // Amendment 1.3.2: the width rule decides; no solver is created or consulted, so the
        // answer cannot depend on solver time, timeouts, or platform.
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var (parameters, expression) = Parse(leftType, rightType, arithmetic);
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        foreach (var (name, type) in parameters)
            translator.DeclareVariable(name, type);
        var safety = translator.GetCheckedArithmeticSafety(expression);
        Assert.NotNull(safety);
        Assert.Equal(sensitive, Z3Verifier.IsOverflowSensitive(ctx, [safety]));
    }

    [Theory]
    [InlineData("i32", "(+ x y)")]
    [InlineData("i64", "(* x y)")]
    public void OverflowSensitivePostcondition_IsAssumedByRule(string type, string arithmetic)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var (parameters, expression) = Parse(type, type, arithmetic);
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var ensures = new EnsuresNode(TextSpan.Empty,
            new BinaryOperationNode(TextSpan.Empty, BinaryOperator.Equal, expression, expression),
            null, new AttributeCollection());
        var outcome = verifier.VerifyPostcondition(parameters, "i32", Array.Empty<RequiresNode>(), ensures).EffectiveOutcome;
        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Equal([Z3Verifier.CheckedArithmeticAssumption], outcome.Assumptions);
    }

    [Theory]
    // Truncated-bound witness: 3000000000 truncates to a negative bound, giving a false Proven.
    [InlineData("(&& (<= i INT:3000000000) (>= i INT:0))")]
    // Dropped-conjunct witness: refusing only the bad conjunct gives a fabricated Disproven.
    [InlineData("(&& (<= i INT:-4294967296) (>= i INT:1))")]
    public void WhileLoopWithAnOutOfRangeBound_IsUnsupported(string condition)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var source = $$"""
            §M{m001:Test}
              §F{f001:Probe:pub}
                §O{i32}
                §B{~i:i64} INT:0
                §WH{w1} {{condition}}
                  §ASSIGN i (+ i INT:1)
                §R INT:0
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
        var function = new Binder(diagnostics).Bind(module).Functions.First();
        var loop = Assert.Single(function.Body.OfType<BoundWhileStatement>());
        var result = new KInductionProver(new KInductionOptions()).ProveInvariant(loop, "i < 0", function);
        Assert.Equal(KInductionStatus.Unsupported, result.Status);
    }
}
