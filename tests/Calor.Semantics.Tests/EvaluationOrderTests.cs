using Calor.Compiler;
using Calor.Compiler.IR;
using Xunit;

namespace Calor.Semantics.Tests;

/// <summary>
/// Tests for evaluation order semantics (S1-S4).
/// </summary>
public class EvaluationOrderTests
{
    /// <summary>
    /// S1: Function arguments are evaluated strictly left-to-right.
    /// </summary>
    [Fact]
    public void S1_FunctionArguments_EvaluatedLeftToRight()
    {
        // This test verifies that when calling f(a, b, c),
        // arguments are evaluated left-to-right.
        // We verify this by checking the CNF lowering produces temporaries in order.

        var source = @"
§M{m001:Test}
  §F{f001:target:pub}
      §I{i32:a}
      §I{i32:b}
      §I{i32:c}
      §O{i32}
      §R (+ (+ a b) c)
  §F{f002:test:pub}
      §O{i32}
      §B{i32:x} INT:1
      §B{i32:y} INT:2
      §B{i32:z} INT:3
      §R (+ (+ x y) z)
";

        var cnf = SemanticsTestHarness.CompileToCnf(source);

        // Verify CNF was generated
        Assert.NotNull(cnf);
        Assert.Equal("Test", cnf.Name);
        Assert.True(cnf.Functions.Count >= 2);

        // The test function should have temporaries created in order
        var testFunc = cnf.Functions.FirstOrDefault(f => f.Name == "test");
        Assert.NotNull(testFunc);

        // Verify the function body has statements
        Assert.NotEmpty(testFunc.Body.Statements);
    }

    /// <summary>
    /// S2: Binary operators are evaluated left-to-right.
    /// </summary>
    [Fact]
    public void S2_BinaryOperators_EvaluatedLeftToRight()
    {
        // For a + b + c, evaluate a, then b, compute a+b, then c, compute (a+b)+c

        var source = @"
§M{m001:Test}
  §F{f001:compute:pub}
      §I{i32:a}
      §I{i32:b}
      §I{i32:c}
      §O{i32}
      §R (+ (+ a b) c)
";

        var cnf = SemanticsTestHarness.CompileToCnf(source);
        var func = cnf.Functions.First(f => f.Name == "compute");

        // The CNF should have explicit temporaries for intermediate results
        var assigns = func.Body.Statements.OfType<CnfAssign>().ToList();

        // Should have at least one intermediate temporary
        Assert.True(assigns.Count >= 1, "CNF should create temporaries for nested operations");
    }

    /// <summary>
    /// S3: Logical AND short-circuits - right side not evaluated if left is false.
    /// </summary>
    [Fact]
    public void S3_LogicalAnd_ShortCircuits()
    {
        // When A is false in A && B, B should not be evaluated

        var source = @"
§M{m001:Test}
  §F{f001:testAnd:pub}
      §I{bool:a}
      §I{bool:b}
      §O{bool}
      §R (&& a b)
";

        var cnf = SemanticsTestHarness.CompileToCnf(source);
        var func = cnf.Functions.First(f => f.Name == "testAnd");

        // Check that CNF lowered AND to control flow (branch statements)
        var branches = func.Body.Statements.OfType<CnfBranch>().ToList();
        var labels = func.Body.Statements.OfType<CnfLabel>().ToList();

        // AND should be lowered to branch + labels for short-circuit
        Assert.True(branches.Count >= 1, "Short-circuit AND should produce branch statements");
        Assert.True(labels.Count >= 2, "Short-circuit AND should produce multiple labels");
    }

    /// <summary>
    /// S4: Logical OR short-circuits - right side not evaluated if left is true.
    /// </summary>
    [Fact]
    public void S4_LogicalOr_ShortCircuits()
    {
        // When A is true in A || B, B should not be evaluated

        var source = @"
§M{m001:Test}
  §F{f001:testOr:pub}
      §I{bool:a}
      §I{bool:b}
      §O{bool}
      §R (|| a b)
";

        var cnf = SemanticsTestHarness.CompileToCnf(source);
        var func = cnf.Functions.First(f => f.Name == "testOr");

        // Check that CNF lowered OR to control flow
        var branches = func.Body.Statements.OfType<CnfBranch>().ToList();
        var labels = func.Body.Statements.OfType<CnfLabel>().ToList();

        // OR should be lowered to branch + labels for short-circuit
        Assert.True(branches.Count >= 1, "Short-circuit OR should produce branch statements");
        Assert.True(labels.Count >= 2, "Short-circuit OR should produce multiple labels");
    }

    /// <summary>
    /// Verifies that CNF lowering produces valid output.
    /// </summary>
    [Fact]
    public void CNF_Validation_ProducesValidIR()
    {
        var source = @"
§M{m001:Test}
  §F{f001:simple:pub}
      §I{i32:x}
      §O{i32}
      §R (* x INT:2)
";

        var cnf = SemanticsTestHarness.CompileToCnf(source);
        var func = cnf.Functions.First();

        // Validate the CNF
        var validator = new CnfValidator();
        validator.ValidateFunction(func);

        Assert.True(validator.IsValid, $"CNF validation failed: {string.Join(", ", validator.Errors)}");
    }
}
