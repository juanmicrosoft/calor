using Xunit;

namespace Calor.Semantics.Tests;

/// <summary>
/// Tests for numeric semantics (S7-S8).
/// </summary>
public class NumericTests
{
    /// <summary>
    /// S7: Integer overflow should trap (throw OverflowException).
    /// Default behavior is safety-first.
    /// </summary>
    [Fact]
    public void S7_IntegerOverflow_Traps()
    {
        // Adding 1 to int.MaxValue should overflow
        var source = @"
§M{m001:Test}
  §F{f001:overflow:pub}
      §O{i32}
      §B{max} INT:2147483647
      §R (+ max INT:1)
";

        var result = SemanticsTestHarness.ExecuteChecked(source, "overflow");

        // Should throw OverflowException
        Assert.False(result.Succeeded, "Overflow should throw an exception");
        Assert.NotNull(result.Exception);
        Assert.True(
            result.Exception is OverflowException ||
            result.Exception.GetType().Name.Contains("Overflow"),
            $"Expected OverflowException but got: {result.Exception.GetType().Name}");
    }

    /// <summary>
    /// S8: INT to FLOAT is implicit widening conversion.
    /// </summary>
    [Fact]
    public void S8_NumericConversion_IntToFloat()
    {
        // Assigning int to float should work (implicit widening)
        // Using simple approach without typed bindings
        var source = @"
§M{m001:Test}
  §F{f001:convert:pub}
      §I{i32:i}
      §O{f64}
      §R i
";

        var result = SemanticsTestHarness.Execute(source, "convert", new object[] { 42 });

        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        // Compare as double since the return type is f64
        Assert.Equal(42.0, Convert.ToDouble(result.ReturnValue));
    }

    /// <summary>
    /// Division by zero should throw.
    /// </summary>
    [Fact]
    public void Division_ByZero_ThrowsException()
    {
        var source = @"
§M{m001:Test}
  §F{f001:divZero:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (/ a b)
";

        var result = SemanticsTestHarness.Execute(source, "divZero", new object[] { 42, 0 });

        Assert.False(result.Succeeded, "Division by zero should throw");
        Assert.NotNull(result.Exception);
        Assert.True(
            result.Exception is DivideByZeroException,
            $"Expected DivideByZeroException but got: {result.Exception.GetType().Name}");
    }

    /// <summary>
    /// Basic arithmetic operations should work correctly.
    /// </summary>
    [Fact]
    public void Arithmetic_BasicOperations()
    {
        var source = @"
§M{m001:Test}
  §F{f001:add:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (+ a b)
  §F{f002:mul:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (* a b)
";

        var addResult = SemanticsTestHarness.Execute(source, "add", new object[] { 3, 4 });
        Assert.True(addResult.Succeeded, $"Execution failed: {addResult.Exception?.Message}");
        Assert.Equal(7, addResult.ReturnValue);

        var mulResult = SemanticsTestHarness.Execute(source, "mul", new object[] { 3, 4 });
        Assert.True(mulResult.Succeeded, $"Execution failed: {mulResult.Exception?.Message}");
        Assert.Equal(12, mulResult.ReturnValue);
    }

    /// <summary>
    /// Comparison operators return correct boolean values.
    /// </summary>
    [Fact]
    public void Comparison_Operators()
    {
        var source = @"
§M{m001:Test}
  §F{f001:lessThan:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (< a b)
  §F{f002:equals:pub}
      §I{i32:a}
      §I{i32:b}
      §O{bool}
      §R (== a b)
";

        var ltResult = SemanticsTestHarness.Execute(source, "lessThan", new object[] { 3, 5 });
        Assert.True(ltResult.Succeeded, $"Execution failed: {ltResult.Exception?.Message}");
        Assert.Equal(true, ltResult.ReturnValue);

        var eqResult = SemanticsTestHarness.Execute(source, "equals", new object[] { 5, 5 });
        Assert.True(eqResult.Succeeded, $"Execution failed: {eqResult.Exception?.Message}");
        Assert.Equal(true, eqResult.ReturnValue);
    }

    /// <summary>
    /// Modulo operation works correctly.
    /// </summary>
    [Fact]
    public void Modulo_Operation()
    {
        var source = @"
§M{m001:Test}
  §F{f001:mod:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §R (% a b)
";

        var result = SemanticsTestHarness.Execute(source, "mod", new object[] { 10, 3 });
        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        Assert.Equal(1, result.ReturnValue);
    }
}
