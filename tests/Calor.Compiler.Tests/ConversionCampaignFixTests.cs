using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for fixes discovered during the C# to Calor conversion campaign.
/// </summary>
public class ConversionCampaignFixTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Issue 304: Convert tuple deconstruction to individual §ASSIGN statements

    [Fact]
    public void Convert_TupleDeconstruction_EmitsIndividualAssignments()
    {
        var result = _converter.Convert(@"
public class Example
{
    private int _a;
    private int _b;

    public void SetValues(int x, int y)
    {
        (_a, _b) = (x, y);
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var source = result.CalorSource ?? "";
        // Should have two §ASSIGN statements, not §ERR
        Assert.DoesNotContain("§ERR", source);
        Assert.Contains("§ASSIGN", source);
    }

    [Fact]
    public void Convert_TupleDeconstruction_ThreeElements()
    {
        var result = _converter.Convert(@"
public class Example
{
    private int _a;
    private int _b;
    private int _c;

    public void SetValues(int x, int y, int z)
    {
        (_a, _b, _c) = (x, y, z);
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var source = result.CalorSource ?? "";
        Assert.DoesNotContain("§ERR", source);
    }

    #endregion
}
