using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

public class InternalSetTests
{
    [Fact]
    public void Property_InternalSet_ConvertsSuccessfully()
    {
        var csharp = @"
public class Foo
{
    public string Name { get; internal set; }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource.Length > 0);
    }

    [Fact]
    public void Property_InternalSet_PreservesAccessorVisibility()
    {
        var csharp = @"
public class Foo
{
    public int Value { get; internal set; }
    public string Label { get; private set; }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.NotNull(result.CalorSource);
        // Both properties should be present in the output
        Assert.Contains("Value", result.CalorSource);
        Assert.Contains("Label", result.CalorSource);
    }
}
