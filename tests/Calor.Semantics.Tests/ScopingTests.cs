using Calor.Compiler;
using Calor.Compiler.IR;
using Xunit;

namespace Calor.Semantics.Tests;

/// <summary>
/// Tests for scoping semantics (S5-S6).
/// </summary>
public class ScopingTests
{
    /// <summary>
    /// S5: Lexical scoping - inner scope can read outer scope variables.
    /// </summary>
    [Fact]
    public void S5_LexicalScoping_InnerReadsOuter()
    {
        // Inner scope can use outer variable in return
        var source = @"
module Test Test
  func testAccess (bool:cond) → int
    let x = 42
    if cond
      return x + 1
    return x
";

        // When cond is true, returns x+1=43 (accessed x from outer scope in inner scope)
        var result1 = SemanticsTestHarness.Execute(source, "testAccess", new object[] { true });
        Assert.True(result1.Succeeded, $"Execution failed: {result1.Exception?.Message}");
        Assert.Equal(43, result1.ReturnValue);

        // When cond is false, returns x=42
        var result2 = SemanticsTestHarness.Execute(source, "testAccess", new object[] { false });
        Assert.True(result2.Succeeded, $"Execution failed: {result2.Exception?.Message}");
        Assert.Equal(42, result2.ReturnValue);
    }

    /// <summary>
    /// S6: Return from nested scope correctly unwinds to function boundary.
    /// </summary>
    [Fact]
    public void S6_ReturnFromNestedScope()
    {
        // Return statement inside if block should return from function

        var source = @"
module Test Test
  func testReturn (bool:condition) → int
    if condition
      return 42
    return 0
";

        // With condition=true, should return 42
        var result1 = SemanticsTestHarness.Execute(source, "testReturn", new object[] { true });
        Assert.True(result1.Succeeded, $"Execution failed: {result1.Exception?.Message}");
        Assert.Equal(42, result1.ReturnValue);

        // With condition=false, should return 0
        var result2 = SemanticsTestHarness.Execute(source, "testReturn", new object[] { false });
        Assert.True(result2.Succeeded, $"Execution failed: {result2.Exception?.Message}");
        Assert.Equal(0, result2.ReturnValue);
    }

    /// <summary>
    /// Verifies scope chain lookup works correctly across multiple nesting levels.
    /// </summary>
    [Fact]
    public void Scope_ChainLookup_FindsOuterVariable()
    {
        // Access variable from outer scope in deeply nested scope
        var source = @"
module Test Test
  func testLookup (bool:a, bool:b) → int
    let outer = 100
    if a
      if b
        return outer
      return outer + 10
    return outer + 20
";

        // a=true, b=true: return outer (100)
        var result1 = SemanticsTestHarness.Execute(source, "testLookup", new object[] { true, true });
        Assert.True(result1.Succeeded, $"Execution failed: {result1.Exception?.Message}");
        Assert.Equal(100, result1.ReturnValue);

        // a=true, b=false: return outer+10 (110)
        var result2 = SemanticsTestHarness.Execute(source, "testLookup", new object[] { true, false });
        Assert.True(result2.Succeeded, $"Execution failed: {result2.Exception?.Message}");
        Assert.Equal(110, result2.ReturnValue);

        // a=false: return outer+20 (120)
        var result3 = SemanticsTestHarness.Execute(source, "testLookup", new object[] { false, false });
        Assert.True(result3.Succeeded, $"Execution failed: {result3.Exception?.Message}");
        Assert.Equal(120, result3.ReturnValue);
    }

    /// <summary>
    /// Verifies function parameters are accessible in function body.
    /// </summary>
    [Fact]
    public void Scope_FunctionParameters_Accessible()
    {
        var source = @"
module Test Test
  func useParams (int:a, int:b) → int
    let sum = a + b
    return sum
";

        var result = SemanticsTestHarness.Execute(source, "useParams", new object[] { 10, 20 });
        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        Assert.Equal(30, result.ReturnValue);
    }

    /// <summary>
    /// Verifies that variables are properly initialized in CNF.
    /// </summary>
    [Fact]
    public void Scope_VariableInitialization_InCNF()
    {
        var source = @"
module Test Test
  func test () → int
    let x = 5
    let y = x * 2
    return y
";

        var cnf = SemanticsTestHarness.CompileToCnf(source);
        var func = cnf.Functions.First();

        // Validate that variables are assigned before use
        var validator = new CnfValidator();
        validator.ValidateFunction(func);

        Assert.True(validator.IsValid, $"CNF validation failed: {string.Join(", ", validator.Errors)}");
    }

    /// <summary>
    /// Verifies variables declared at the same level don't conflict.
    /// </summary>
    [Fact]
    public void Scope_SameLevel_DifferentNames()
    {
        var source = @"
module Test Test
  func test () → int
    let a = 1
    let b = 2
    let c = 3
    return (a + b) + c
";

        var result = SemanticsTestHarness.Execute(source, "test");
        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        Assert.Equal(6, result.ReturnValue);
    }
}
