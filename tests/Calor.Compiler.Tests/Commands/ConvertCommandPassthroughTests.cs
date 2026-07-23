using System.Text.RegularExpressions;
using Calor.Compiler.Commands;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #736: the CLI <c>calor convert --passthrough</c> flag must reach
/// <see cref="ConversionOptions.PassthroughOnError"/>, so C#→Calor conversion from the
/// CLI gets the same #717 §CSHARP post-validation fallback the MCP <c>calor_convert</c>
/// tool already had — an unconvertible member is preserved verbatim instead of writing
/// invalid Calor.
///
/// <para>The flag→option mapping is asserted directly on the extracted
/// <see cref="ConvertCommand.BuildCSharpToCalorOptions"/>; the fallback mechanics it
/// unlocks are then exercised end-to-end through the same options object using the #717
/// <see cref="CSharpToCalorConverter.ParseValidatorOverride"/> seam (natural inputs that
/// emit unparseable Calor are effectively unreachable — see PostValidationFallbackTests).</para>
/// </summary>
public class ConvertCommandPassthroughTests
{
    private const string TwoClasses =
        "namespace N { public class Keepme { public int A() => 1; } " +
        "public class Breakme { public int B() => 2; } }";

    private static Func<string, bool> CondemnOutsideCSharp(string marker) =>
        calor => !Regex.Replace(calor, @"§CSHARP\{.*?\}§/CSHARP", "", RegexOptions.Singleline).Contains(marker);

    [Fact]
    public void PassthroughFlag_SetsPassthroughOnError()
    {
        var on = ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false, verbose: false, explain: false, noFallback: false,
            passthrough: true, explicitCallClosers: false);
        Assert.True(on.PassthroughOnError);

        var off = ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false, verbose: false, explain: false, noFallback: false,
            passthrough: false, explicitCallClosers: false);
        Assert.False(off.PassthroughOnError);
    }

    [Fact]
    public void DefaultFlags_MapToExpectedOptions()
    {
        // Default CLI behavior unchanged: passthrough off, graceful fallback on,
        // implicit call closers on.
        var opts = ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false, verbose: false, explain: false, noFallback: false,
            passthrough: false, explicitCallClosers: false);

        Assert.False(opts.PassthroughOnError);
        Assert.True(opts.GracefulFallback);
        Assert.True(opts.UseImplicitCallCloser);
    }

    [Fact]
    public void NoFallbackAndExplicitClosers_AreInverted()
    {
        var opts = ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: true, verbose: true, explain: true, noFallback: true,
            passthrough: true, explicitCallClosers: true);

        Assert.False(opts.GracefulFallback);       // --no-fallback → GracefulFallback = false
        Assert.False(opts.UseImplicitCallCloser);  // --explicit-call-closers → false
        Assert.True(opts.IncludeBenchmark);
        Assert.True(opts.Verbose);
        Assert.True(opts.Explain);
    }

    [Fact]
    public void PassthroughOptions_EnableTheCSharpFallback()
    {
        // The end-to-end acceptance: a member whose emission would not parse is preserved
        // as a §CSHARP block, so the written Calor is always valid — reached through the
        // exact ConversionOptions the CLI builds for `--passthrough`.
        var converter = new CSharpToCalorConverter(
            ConvertCommand.BuildCSharpToCalorOptions(
                benchmark: false, verbose: false, explain: false, noFallback: false,
                passthrough: true, explicitCallClosers: false))
        {
            ParseValidatorOverride = CondemnOutsideCSharp("Breakme"),
        };

        var result = converter.Convert(TwoClasses);

        Assert.True(result.Success);
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("class Breakme", result.CalorSource);
        Assert.Equal(1, result.Context!.Stats.InteropBlocksEmitted);
        // The rescued block is attributed to the fallback specifically (#745 review
        // finding 1), so the CLI can report "N (M via --passthrough fallback)".
        Assert.Equal(1, result.Context!.Stats.FallbackInteropBlocksEmitted);
    }

    [Fact]
    public void PassthroughGiveUp_FailsLoudly_NotSilentSuccess()
    {
        // #745 review finding 2: pin the "never ship broken output" contract the CLI's
        // fail-loudly branch surfaces. Top-level statements become a synthetic Main with
        // no single C# type source, so they cannot be rewrapped; with everything condemned
        // the output stays unparseable — and the converter must return failure WITH a
        // post-validation-fallback warning (which the CLI then prints), never Success=true.
        var converter = new CSharpToCalorConverter(
            ConvertCommand.BuildCSharpToCalorOptions(
                benchmark: false, verbose: false, explain: false, noFallback: false,
                passthrough: true, explicitCallClosers: false))
        {
            ParseValidatorOverride = _ => false,
        };

        var result = converter.Convert("System.Console.WriteLine(\"hi\");");

        Assert.False(result.Success);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Issues, i =>
            i.Severity == ConversionIssueSeverity.Warning && i.Feature == "post-validation-fallback");
        Assert.Equal(0, result.Context!.Stats.FallbackInteropBlocksEmitted);
    }

    [Fact]
    public void WithoutPassthrough_TheFallbackStaysOff()
    {
        // Same forced-unparseable seam, but default (no --passthrough): the fallback is
        // gated off, so no §CSHARP is emitted — proving the flag is what unlocks it.
        var converter = new CSharpToCalorConverter(
            ConvertCommand.BuildCSharpToCalorOptions(
                benchmark: false, verbose: false, explain: false, noFallback: false,
                passthrough: false, explicitCallClosers: false))
        {
            ParseValidatorOverride = CondemnOutsideCSharp("Breakme"),
        };

        var result = converter.Convert(TwoClasses);

        Assert.Equal(0, result.Context!.Stats.InteropBlocksEmitted);
        Assert.DoesNotContain("§CSHARP", result.CalorSource!);
    }
}
