using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests that converted ternary expressions stay inline as native `cond ? a : b`.
/// (Deep-ternary decomposition into if/else was removed on purpose in 0.30.)
/// </summary>
public class TernaryDecompositionTests
{
    private readonly CSharpToCalorConverter _converter = new(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });

    private string ConvertToCalor(string csharp)
    {
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));
        var emitter = new CalorEmitter();
        return emitter.Emit(result.Ast!);
    }

    [Fact]
    public void SimpleTernary_Depth1_StaysInline()
    {
        var csharp = """
            public class C
            {
                public int M(bool a) => a ? 1 : 2;
            }
            """;

        var calor = ConvertToCalor(csharp);

        Assert.Contains("(a ? 1 : 2)", calor);
        Assert.DoesNotContain("_ternResult", calor);
    }

    [Fact]
    public void DoubleNested_Depth2_StaysInline()
    {
        var csharp = """
            public class C
            {
                public int M(bool a, bool b) => a ? (b ? 1 : 2) : 3;
            }
            """;

        var calor = ConvertToCalor(csharp);

        Assert.Contains("(a ? (b ? 1 : 2) : 3)", calor);
        Assert.DoesNotContain("_ternResult", calor);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }
}
