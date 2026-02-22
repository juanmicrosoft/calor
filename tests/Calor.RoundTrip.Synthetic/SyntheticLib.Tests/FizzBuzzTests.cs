using SyntheticLib;
using Xunit;

namespace SyntheticLib.Tests;

public class FizzBuzzTests
{
    [Fact]
    public void Evaluate_Fizz()
    {
        Assert.Equal("Fizz", FizzBuzz.Evaluate(3));
        Assert.Equal("Fizz", FizzBuzz.Evaluate(9));
    }

    [Fact]
    public void Evaluate_Buzz()
    {
        Assert.Equal("Buzz", FizzBuzz.Evaluate(5));
        Assert.Equal("Buzz", FizzBuzz.Evaluate(10));
    }

    [Fact]
    public void Evaluate_FizzBuzz()
    {
        Assert.Equal("FizzBuzz", FizzBuzz.Evaluate(15));
        Assert.Equal("FizzBuzz", FizzBuzz.Evaluate(30));
    }

    [Fact]
    public void Evaluate_Number()
    {
        Assert.Equal("1", FizzBuzz.Evaluate(1));
        Assert.Equal("7", FizzBuzz.Evaluate(7));
    }

    [Fact]
    public void Range_ReturnsCorrectResults()
    {
        var results = FizzBuzz.Range(1, 5);
        Assert.Equal(5, results.Length);
        Assert.Equal("1", results[0]);
        Assert.Equal("2", results[1]);
        Assert.Equal("Fizz", results[2]);
        Assert.Equal("4", results[3]);
        Assert.Equal("Buzz", results[4]);
    }

    [Fact]
    public void Range_InvalidRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => FizzBuzz.Range(10, 5));
    }

    [Fact]
    public void CountFizzes_InRange()
    {
        Assert.Equal(4, FizzBuzz.CountFizzes(1, 15));
    }

    [Fact]
    public void CountBuzzes_InRange()
    {
        Assert.Equal(2, FizzBuzz.CountBuzzes(1, 15));
    }
}
