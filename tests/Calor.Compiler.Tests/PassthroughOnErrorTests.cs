using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

public class PassthroughOnErrorTests
{
    [Fact]
    public void UnsupportedMember_WithPassthrough_EmitsCSharpBlock()
    {
        var options = new ConversionOptions
        {
            PassthroughOnError = true,
            GracefulFallback = true
        };
        var converter = new CSharpToCalorConverter(options);
        var csharp = @"
public class Foo
{
    ~Foo() { }
}";
        var result = converter.Convert(csharp, "Test.cs");
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§CSHARP{", result.CalorSource);
    }

    [Fact]
    public void UnsupportedMember_WithoutPassthrough_DoesNotEmitCSharpBlock()
    {
        var options = new ConversionOptions
        {
            PassthroughOnError = false,
            GracefulFallback = true
        };
        var converter = new CSharpToCalorConverter(options);
        var csharp = @"
public class Foo
{
    ~Foo() { }
}";
        var result = converter.Convert(csharp, "Test.cs");
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§CSHARP{", result.CalorSource);
    }
}
