using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

public class GotoCaseTests
{
    [Fact]
    public void GotoCase_ConvertsSuccessfully()
    {
        var csharp = @"
public class Foo
{
    public int Bar(int x)
    {
        switch (x)
        {
            case 1:
                goto case 2;
            case 2:
                return x;
            default:
                return 0;
        }
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource.Length > 0);
    }

    [Fact]
    public void GotoDefault_ConvertsSuccessfully()
    {
        var csharp = @"
public class Foo
{
    public int Bar(int x)
    {
        switch (x)
        {
            case 1:
                goto default;
            default:
                return 0;
        }
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource.Length > 0);
    }
}
