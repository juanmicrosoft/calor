using Quotes;
using Xunit;

namespace CalorLib.Tests;

public class QuotesTests
{
    [Fact]
    public void Add_AddsIntegers()
    {
        Assert.Equal(5, QuotesModule.Add(2, 3));
        Assert.Equal(-1, QuotesModule.Add(2, -3));
    }

    [Fact]
    public void ClampToCap_ClampsAboveCap()
    {
        Assert.Equal(100, QuotesModule.ClampToCap(250, 100));
        Assert.Equal(50, QuotesModule.ClampToCap(50, 100));
    }

    [Fact]
    public void QuoteWithSurchargeDefective_IsCallable()
    {
        // The contract on this function is deliberately refutable (the verify
        // canary); call it with inputs that satisfy the postcondition so any
        // emitted runtime contract guard passes.
        Assert.Equal(50, QuotesModule.QuoteWithSurchargeDefective(30, 20, 60));
    }
}
