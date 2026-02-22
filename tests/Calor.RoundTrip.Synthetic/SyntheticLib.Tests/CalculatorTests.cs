using SyntheticLib;
using Xunit;

namespace SyntheticLib.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_ReturnsSum()
    {
        Assert.Equal(5, Calculator.Add(2, 3));
    }

    [Fact]
    public void Add_NegativeNumbers()
    {
        Assert.Equal(-1, Calculator.Add(-3, 2));
    }

    [Fact]
    public void Subtract_ReturnsDifference()
    {
        Assert.Equal(1, Calculator.Subtract(3, 2));
    }

    [Fact]
    public void Multiply_ReturnsProduct()
    {
        Assert.Equal(12, Calculator.Multiply(3, 4));
    }

    [Fact]
    public void Divide_ReturnsQuotient()
    {
        Assert.Equal(2.5, Calculator.Divide(5, 2));
    }

    [Fact]
    public void Divide_ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => Calculator.Divide(5, 0));
    }

    [Fact]
    public void Factorial_ReturnsCorrectValue()
    {
        Assert.Equal(1, Calculator.Factorial(0));
        Assert.Equal(1, Calculator.Factorial(1));
        Assert.Equal(6, Calculator.Factorial(3));
        Assert.Equal(120, Calculator.Factorial(5));
    }

    [Fact]
    public void Factorial_Negative_Throws()
    {
        Assert.Throws<ArgumentException>(() => Calculator.Factorial(-1));
    }

    [Fact]
    public void IsEven_Works()
    {
        Assert.True(Calculator.IsEven(2));
        Assert.True(Calculator.IsEven(0));
        Assert.False(Calculator.IsEven(3));
    }

    [Fact]
    public void Abs_Works()
    {
        Assert.Equal(5, Calculator.Abs(5));
        Assert.Equal(5, Calculator.Abs(-5));
        Assert.Equal(0, Calculator.Abs(0));
    }

    [Fact]
    public void Max_ReturnsLarger()
    {
        Assert.Equal(5, Calculator.Max(3, 5));
        Assert.Equal(5, Calculator.Max(5, 3));
    }

    [Fact]
    public void Min_ReturnsSmaller()
    {
        Assert.Equal(3, Calculator.Min(3, 5));
        Assert.Equal(3, Calculator.Min(5, 3));
    }

    [Fact]
    public void Clamp_ClampsValue()
    {
        Assert.Equal(5, Calculator.Clamp(5, 0, 10));
        Assert.Equal(0, Calculator.Clamp(-5, 0, 10));
        Assert.Equal(10, Calculator.Clamp(15, 0, 10));
    }
}
