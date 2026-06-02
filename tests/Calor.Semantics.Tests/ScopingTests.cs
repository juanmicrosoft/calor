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
§M{m001:Test}
  §F{f001:testAccess:pub}
      §I{bool:cond}
      §O{i32}
      §B{x} INT:42
      §IF{if1} cond
          §R (+ x INT:1)
      §R x
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
§M{m001:Test}
  §F{f001:testReturn:pub}
      §I{bool:condition}
      §O{i32}
      §IF{if1} condition
          §R INT:42
      §R INT:0
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
§M{m001:Test}
  §F{f001:testLookup:pub}
      §I{bool:a}
      §I{bool:b}
      §O{i32}
      §B{outer} INT:100
      §IF{if1} a
          §IF{if2} b
              §R outer
          §R (+ outer INT:10)
      §R (+ outer INT:20)
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
§M{m001:Test}
  §F{f001:useParams:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §B{sum} (+ a b)
      §R sum
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
§M{m001:Test}
  §F{f001:test:pub}
      §O{i32}
      §B{x} INT:5
      §B{y} (* x INT:2)
      §R y
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
§M{m001:Test}
  §F{f001:test:pub}
      §O{i32}
      §B{a} INT:1
      §B{b} INT:2
      §B{c} INT:3
      §R (+ (+ a b) c)
";

        var result = SemanticsTestHarness.Execute(source, "test");
        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        Assert.Equal(6, result.ReturnValue);
    }
}
