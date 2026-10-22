using System.CommandLine;
using Calor.Compiler.Commands;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// #1586: the default-mode harness leg must convert with exactly the options
/// <c>calor convert</c> builds when the user passes no flags. These tests parse a
/// flagless <c>convert</c> command line through the real CLI definition, map the
/// parsed values the way the command handler does, and fail if the result differs
/// from what the harness uses — so the CLI and the harness cannot drift apart.
/// </summary>
public class DefaultModeOptionsParityTests
{
    /// <summary>
    /// Every option the <c>convert</c> command defines today. If a flag is added or
    /// removed, this test fails so whoever changes the CLI also decides how the
    /// harness's default-mode leg should treat it.
    /// </summary>
    private static readonly string[] KnownConvertOptions =
    [
        "--benchmark", "--configuration", "--define", "--documentation-mode",
        "--explain", "--explicit-call-closers", "--feature", "--format",
        "--framework", "--language-version", "--lossy", "--no-fallback",
        "--output", "--passthrough", "--reference",
        "--select-active-preprocessor-branch-lossy", "--source-kind",
        "--timeout", "--validate", "--verbose",
    ];

    /// <summary>
    /// Runs the real <c>convert</c> command (parse, handler and all) on a small C#
    /// file and returns the exact options it handed to the converter, captured by
    /// <see cref="ConvertCommand.ConversionOptionsObserver"/>. Anything the handler
    /// adds or changes after <c>BuildCSharpToCalorOptions</c> shows up here.
    /// </summary>
    private static async Task<ConversionOptions> CaptureCliOptionsAsync(params string[] flags)
    {
        var dir = Directory.CreateTempSubdirectory("calor-1586-parity-");
        try
        {
            var input = Path.Combine(dir.FullName, "Sample.cs");
            await File.WriteAllTextAsync(input,
                "namespace Demo;\npublic static class Sample\n{\n    public static int Add(int a, int b) => a + b;\n}\n");
            ConversionOptions? captured = null;
            ConvertCommand.ConversionOptionsObserver.Value = options => captured = options;
            try
            {
                await ConvertCommand.Create().InvokeAsync([input, .. flags]);
            }
            finally
            {
                ConvertCommand.ConversionOptionsObserver.Value = null;
            }
            Assert.NotNull(captured);
            return captured!;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static Dictionary<string, string> Snapshot(ConversionOptions options)
        => ReportGenerator.SnapshotOptions(options)
            .ToDictionary(pair => pair.Key, pair => pair.Value.GetRawText());

    [Fact]
    public void ConvertCommand_DefinesOnlyTheReviewedOptions()
    {
        var actual = ConvertCommand.Create().Options
            .Select(option => option.Aliases.Where(alias => alias.StartsWith("--", StringComparison.Ordinal))
                .OrderBy(alias => alias, StringComparer.Ordinal).First())
            .OrderBy(alias => alias, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(KnownConvertOptions.OrderBy(a => a, StringComparer.Ordinal), actual);
    }

    [Fact]
    public async Task DefaultModeOptions_EqualFlaglessCalorConvert()
    {
        var cli = Snapshot(await CaptureCliOptionsAsync());
        var harness = Snapshot(RoundTripPipeline.CreateDefaultModeConversionOptions());

        Assert.Equal(cli.Keys.OrderBy(k => k), harness.Keys.OrderBy(k => k));
        foreach (var (name, value) in cli)
            Assert.True(value == harness[name], $"ConversionOptions.{name}: calor convert={value}, harness={harness[name]}");
    }

    /// <summary>
    /// Negative control: the comparison above is sensitive to what the handler does.
    /// A CLI run with a flag that changes the options must NOT match the harness.
    /// </summary>
    [Theory]
    [InlineData("--lossy", "Fidelity")]
    [InlineData("--passthrough", "PassthroughOnError")]
    [InlineData("--no-fallback", "GracefulFallback")]
    [InlineData("--explicit-call-closers", "UseImplicitCallCloser")]
    public async Task CliRunWithAFlag_DoesNotMatchDefaultMode(string flag, string changedOption)
    {
        var cli = Snapshot(await CaptureCliOptionsAsync(flag));
        var harness = Snapshot(RoundTripPipeline.CreateDefaultModeConversionOptions());

        Assert.NotEqual(harness[changedOption], cli[changedOption]);
        Assert.NotEqual(harness, cli);
    }

    [Fact]
    public void DefaultModeOptions_AreTheLosslessCliDefaults()
    {
        var options = RoundTripPipeline.CreateDefaultModeConversionOptions();

        Assert.Equal(ConversionFidelity.Lossless, options.Fidelity);
        Assert.Equal(PreprocessorConversionMode.PreserveAllBranches, options.PreprocessorMode);
        Assert.True(options.GracefulFallback);
        Assert.False(options.PassthroughOnError);
        Assert.True(options.ValidateRoundTripCSharp);
        Assert.Empty(options.DefinedSymbols);
        Assert.Empty(options.References);
    }

    [Fact]
    public void HarnessConversionOptions_InDefaultMode_IgnoreHarnessOnlySettings()
    {
        var config = ProjectConfigs.Get("Serilog", "/corpus", "dotnet")!
            .ForMode(HarnessConversionMode.Default);

        foreach (var mode in Enum.GetValues<PreprocessorConversionMode>())
        {
            var options = RoundTripPipeline.CreateHarnessConversionOptions(config, parseContext: null, mode);
            Assert.Equal(Snapshot(RoundTripPipeline.CreateDefaultModeConversionOptions()), Snapshot(options));
        }
    }

    [Fact]
    public void HarnessConversionOptions_InLossyMode_AreUnchanged()
    {
        var config = ProjectConfigs.Get("Serilog", "/corpus", "dotnet")!;
        Assert.Equal(HarnessConversionMode.Lossy, config.ConversionMode);

        var selected = RoundTripPipeline.CreateHarnessConversionOptions(
            config, parseContext: null, PreprocessorConversionMode.SelectActiveBranchLossy);
        Assert.Equal(ConversionFidelity.Lossy, selected.Fidelity);
        Assert.True(selected.GracefulFallback);
        Assert.True(selected.PassthroughOnError);
        Assert.False(selected.ValidateRoundTripCSharp);

        var divergent = RoundTripPipeline.CreateHarnessConversionOptions(
            config, parseContext: null, PreprocessorConversionMode.PreserveAllBranches);
        Assert.Equal(ConversionFidelity.Lossless, divergent.Fidelity);
        Assert.True(divergent.PassthroughOnError);
    }

    [Fact]
    public void ForMode_Default_SwapsInDefaultModeFloors_AndCliOverridesStillWin()
    {
        foreach (var name in ProjectConfigs.KnownProjects)
        {
            var canonical = ProjectConfigs.Get(name, "/corpus", "dotnet")!;
            var lossy = canonical.ForMode(HarnessConversionMode.Lossy);
            var defaults = canonical.ForMode(HarnessConversionMode.Default);

            Assert.Equal(canonical.MinimumCoverageFraction, lossy.MinimumCoverageFraction);
            Assert.Equal(canonical.MinimumNativeFraction, lossy.MinimumNativeFraction);
            Assert.Equal(HarnessConversionMode.Default, defaults.ConversionMode);
            Assert.Equal(canonical.DefaultModeMinimumCoverageFraction, defaults.MinimumCoverageFraction);
            Assert.Equal(canonical.DefaultModeMinimumNativeFraction, defaults.MinimumNativeFraction);
            // The leg keeps every other gate input, e.g. the upstream-flake allowlist.
            Assert.Equal(canonical.ExpectedFlakyTestFullyQualifiedNames, defaults.ExpectedFlakyTestFullyQualifiedNames);

            var overridden = defaults.WithRunOverrides(false, null, minimumCoverage: 0.9, minimumNative: 0.8);
            Assert.Equal(0.9, overridden.MinimumCoverageFraction);
            Assert.Equal(0.8, overridden.MinimumNativeFraction);
        }
    }

    [Fact]
    public void ForMode_RefusesToTurnADefaultModeConfigBackIntoLossy()
    {
        // A default-mode config no longer carries the Lossy floors, so switching it
        // back would gate the Lossy leg on the wrong numbers.
        var defaults = ProjectConfigs.Get("MediatR", "/corpus", "dotnet")!
            .ForMode(HarnessConversionMode.Default);

        Assert.Throws<InvalidOperationException>(() => defaults.ForMode(HarnessConversionMode.Lossy));
        var again = defaults.ForMode(HarnessConversionMode.Default);
        Assert.Equal(defaults.MinimumCoverageFraction, again.MinimumCoverageFraction);
        Assert.Equal(defaults.MinimumNativeFraction, again.MinimumNativeFraction);
    }

    [Fact]
    public void EveryKnownProject_DeclaresDefaultModeFloors()
    {
        foreach (var name in ProjectConfigs.KnownProjects)
        {
            var config = ProjectConfigs.Get(name, "/corpus", "dotnet")!;
            Assert.InRange(config.DefaultModeMinimumCoverageFraction, 0.0, 1.0);
            Assert.InRange(config.DefaultModeMinimumNativeFraction, 0.0, config.DefaultModeMinimumCoverageFraction);
            // A floor of zero would make the leg non-blocking on coverage.
            Assert.True(config.DefaultModeMinimumCoverageFraction > 0, $"{name} has no default-mode coverage floor");
        }
    }
}
