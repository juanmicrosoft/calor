using Calor.Enforcement.Tests;
using Xunit;

namespace Calor.Compiler.Tests;

public class NativeCodeGenerationMutationTests
{
    [Fact]
    public void Arithmetic_PreservesDistinctOperands()
    {
        const string source = """
            §M{m:Arithmetic}
              §F{f:Calculate:pub} (i32:left, i32:right) -> i32
                §E{}
                §R (+ (* left INT:3) right)
            """;

        var result = TestHarness.Execute(source, "Calculate", [7, 5], new CompilationOptions());

        Assert.Null(result.Exception);
        Assert.Equal(26, result.ReturnValue);
    }

    [Fact]
    public void BinaryOperands_ExecuteOnceInSourceOrder()
    {
        const string source = """
            §M{m:OperandOrder}
              §F{r:Record:priv} (List<i32>:trace, i32:value) -> i32
                §C{trace.Add} §A value §/C
                §R value
              §F{f:Calculate:pub} (List<i32>:trace) -> i32
                §R (- §C{Record} §A trace §A INT:1 §/C §C{Record} §A trace §A INT:2 §/C)
            """;
        var trace = new List<int>();

        var result = TestHarness.Execute(source, "Calculate", [trace]);

        Assert.Null(result.Exception);
        Assert.Equal(-1, result.ReturnValue);
        Assert.Equal([1, 2], trace);
    }

    [Theory]
    [InlineData(true, 11)]
    [InlineData(false, 22)]
    public void Conditional_ExecutesOnlySelectedArm(bool chooseFirst, int expected)
    {
        const string source = """
            §M{m:ConditionalOrder}
              §F{r:Record:priv} (List<i32>:trace, i32:value) -> i32
                §C{trace.Add} §A value §/C
                §R value
              §F{f:Choose:pub} (List<i32>:trace, bool:first) -> i32
                §R (? first §C{Record} §A trace §A INT:11 §/C §C{Record} §A trace §A INT:22 §/C)
            """;
        var trace = new List<int>();

        var result = TestHarness.Execute(source, "Choose", [trace, chooseFirst]);

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
        Assert.Equal([expected], trace);
    }
}
