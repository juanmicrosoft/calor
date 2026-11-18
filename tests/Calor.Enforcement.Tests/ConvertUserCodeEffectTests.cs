using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// 0.30 i1: the restored 0.29 manifest keeps <c>System.Convert "*": []</c>, but the overloads that
/// reach user code are charged <c>unknown</c>. <c>Convert.ToString(object)</c> calls the
/// argument's virtual <c>ToString</c>; <c>ToInt32(object)</c> and the other <c>ToX(object)</c>
/// overloads, <c>ChangeType</c>, <c>IsDBNull</c> and <c>GetTypeCode</c> call a user's
/// <c>IConvertible</c>; an <c>IFormatProvider</c> argument has its <c>GetFormat</c> called.
///
/// <para>The manifest names each such method <c>["unknown"]</c> and lists its primitive overloads
/// as exact pure entries. Exact entries win over the name entry, which wins over <c>"*"</c>. The
/// fallback is the unknown one on purpose: a call site keys on its argument types, so a user
/// class argument (<c>ToString(Box)</c>) matches no exact entry and must not fall through to a
/// pure answer.</para>
/// </summary>
public class ConvertUserCodeEffectTests
{
    private static string PureFunction(string parameters, string returnType, string expression)
        => "module ConvertProbe\n"
            + "  class Box\n"
            + "    func ToString public override () → string\n"
            + "      ! cw\n"
            + "      Console.WriteLine(\"side effect\")\n"
            + "      return \"box\"\n"
            + "\n"
            + $"  func Probe ({parameters}) → {returnType}\n"
            + "    !\n"
            + $"    return {expression}\n";

    [Theory]
    [InlineData("object:value", "string?", "Convert.ToString(value)")]
    [InlineData("Box:value", "string?", "Convert.ToString(value)")]
    [InlineData("object:value, System.IFormatProvider:provider", "string?", "Convert.ToString(value, provider)")]
    [InlineData("object:value", "int", "Convert.ToInt32(value)")]
    [InlineData("object:value", "bool", "Convert.ToBoolean(value)")]
    [InlineData("object:value", "double", "Convert.ToDouble(value)")]
    [InlineData("object:value", "object?", "Convert.ChangeType(value, typeof(int))")]
    [InlineData("object:value", "bool", "Convert.IsDBNull(value)")]
    [InlineData("int:value, System.IFormatProvider:provider", "string", "Convert.ToString(value, provider)")]
    [InlineData("string:value, System.IFormatProvider:provider", "int", "Convert.ToInt32(value, provider)")]
    public void UserReachableOverload_InPureFunction_IsForbiddenUnknown(
        string parameters, string returnType, string expression)
    {
        var result = TestHarness.Compile(PureFunction(parameters, returnType, expression));
        var forbidden = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains("'Probe' uses effect 'unknown'", forbidden.Message);
        // Calor0410 points at the function's row (`!`, line 9).
        Assert.Equal(9, forbidden.Span.Line);
    }

    [Theory]
    [InlineData("int:value", "string", "Convert.ToString(value)")]
    [InlineData("int:value", "string", "Convert.ToString(42)")]
    [InlineData("double:value", "string", "Convert.ToString(value)")]
    [InlineData("bool:value", "string", "Convert.ToString(value)")]
    [InlineData("string:value", "int", "Convert.ToInt32(value)")]
    [InlineData("double:value", "int", "Convert.ToInt32(value)")]
    [InlineData("long:value", "int", "Convert.ToInt32(value)")]
    [InlineData("int:value", "string", "Convert.ToString(value, 16)")]
    [InlineData("bool:value", "string", "Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)")]
    [InlineData("int:value", "string", "Convert.ToBase64String(new byte[]{1, 2})")]
    public void PrimitiveOverload_StaysPure(string parameters, string returnType, string expression)
    {
        var source = PureFunction(parameters, returnType, expression)
            .Replace("    !\n    return Convert.ToBase64String", "    ! alloc\n    return Convert.ToBase64String",
                StringComparison.Ordinal);
        var result = TestHarness.Compile(source);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
        TestHarness.AssertNoDiagnostic(result.Diagnostics, DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void UserReachableOverload_IsAllowedUnderAnUnknownRow()
    {
        var source = PureFunction("object:value", "string?", "Convert.ToString(value)")
            .Replace("    !\n    return", "    ! unknown\n    return", StringComparison.Ordinal);
        var result = TestHarness.Compile(source);
        TestHarness.AssertNoDiagnostic(result.Diagnostics, DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("ToString", new[] { "System.Object" }, true)]
    [InlineData("ToString", new[] { "System.Object", "System.IFormatProvider" }, true)]
    [InlineData("ToString", new[] { "System.Int32", "System.IFormatProvider" }, true)]
    [InlineData("ToString", new[] { "MyNamespace.Box" }, true)]
    [InlineData("ToInt32", new[] { "System.Object" }, true)]
    [InlineData("ChangeType", new[] { "System.Object", "System.Type" }, true)]
    [InlineData("GetTypeCode", new[] { "System.Object" }, true)]
    [InlineData("ToString", new[] { "System.Int32" }, false)]
    [InlineData("ToString", new[] { "System.Int32", "System.Int32" }, false)]
    [InlineData("ToString", new[] { "System.Boolean", "System.IFormatProvider" }, false)]
    [InlineData("ToString", new[] { "System.String", "System.IFormatProvider" }, false)]
    [InlineData("ToInt32", new[] { "System.String" }, false)]
    [InlineData("ToBase64String", new[] { "System.Byte[]" }, false)]
    public void Resolver_ExactPrimitiveEntryBeatsUnknownNameEntry_WhichBeatsWildcard(
        string method, string[] parameterTypes, bool unknown)
    {
        var resolver = new EffectResolver();
        resolver.Initialize();
        var resolution = resolver.Resolve(EffectResolverKey.FromStrings("System.Convert", method, parameterTypes));

        // A manifest "unknown" is a definitive answer (Resolved), not a missing entry.
        Assert.Equal(unknown ? EffectResolutionStatus.Resolved : EffectResolutionStatus.PureExplicit,
            resolution.Status);
        Assert.Equal(unknown, resolution.Effects.IsUnknown);
        Assert.Equal(!unknown, resolution.Effects.IsEmpty);
    }
}
