using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Tests;

internal static class BindingReceivingCases
{
    public static IEnumerable<object[]> StageB()
    {
        foreach (var (source, target, shape) in new[]
        {
            ("?[str]", "[str]", BindingReceivingShape.Array),
            ("[?str]", "[str]", BindingReceivingShape.Array),
            ("List<?str>", "List<str>", BindingReceivingShape.Generic),
            ("?Widget", "Widget", BindingReceivingShape.Nominal)
        })
        foreach (var boundary in new[]
        {
            BindingReceivingBoundary.Initializer,
            BindingReceivingBoundary.NativeReturn,
            BindingReceivingBoundary.MethodArgument
        })
            yield return [source, target, shape, boundary];
    }

    public static string Source(string inputType, string targetType, BindingReceivingBoundary boundary)
    {
        var statement = boundary switch
        {
            BindingReceivingBoundary.Initializer => $"§B{{required:{targetType}}} value",
            BindingReceivingBoundary.NativeReturn => "§R value",
            _ => "§C{Take} §A value §/C"
        };
        return $$"""
            §M{m1:StageBReceiving}
              §CL{c1:Widget:pub}
                §MT{id:Id:pub} () -> i32
                  §E{}
                  §R 1
              §F{take:Take:pub} ({{targetType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{inputType}}:value) -> {{(boundary == BindingReceivingBoundary.NativeReturn ? targetType : "void")}}
                §E{}
                {{statement}}
            """;
    }

    public static IEnumerable<object[]> NullableBoundaries()
    {
        yield return new object[] { DiagnosticCode.NullableToNonNullableBinding,
            BindingReceivingBoundary.Initializer,
            "§B{x:str} §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C", "void" };
        yield return new object[] { DiagnosticCode.NullableReturnFromNonNullable,
            BindingReceivingBoundary.NativeReturn,
            "§R §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C", "str" };
        yield return new object[] { DiagnosticCode.NullableArgumentToNonNullableParameter,
            BindingReceivingBoundary.MethodArgument,
            "§R §C{System.Int32.Parse} §A §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C §/C", "i32" };
    }
}
