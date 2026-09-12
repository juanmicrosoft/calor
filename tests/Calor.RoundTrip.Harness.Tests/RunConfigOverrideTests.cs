using System.Reflection;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// The <c>run</c> command applies CLI overrides (bisect, build timeout, coverage
/// floors) by deriving a new config from the project's canonical one. Any field
/// that derivation drops silently changes gate behaviour — this is exactly how
/// the MediatR upstream-flake allowlist went missing in CI (jobs 97677469389,
/// 97668600448, 97670944824: the allowlisted test still failed the gate).
/// </summary>
public class RunConfigOverrideTests
{
    private const string MediatRFlake =
        "MediatR.Tests.GenericRequestHandlerTests.ShouldThrowExceptionWhenTimeoutOccurs";

    [Fact]
    public void MediatR_SerializesTestCollectionsWithoutChangingSelectionOrAttempts()
    {
        var canonical = ProjectConfigs.Get("MediatR", "/corpus", "dotnet")!;
        var effective = canonical.WithRunOverrides(
            enableBisect: true, buildTimeout: TimeSpan.FromMinutes(5),
            minimumCoverage: null, minimumNative: null);

        foreach (var config in new[] { canonical, effective })
        {
            Assert.Contains("-p:VSTestCLIRunSettings=xUnit.ParallelizeTestCollections=false",
                config.ExtraBuildProperties, StringComparison.Ordinal);
            Assert.Null(config.TestFilter);
            Assert.Equal(1, config.TestAttemptsPerLeg);
            Assert.Equal("net8.0", config.TargetFramework);
        }
    }

    [Fact]
    public void OtherProjects_DoNotApplyMediatRTestIsolation()
    {
        foreach (var project in new[] { "Synthetic", "Synthetic2", "Serilog", "FluentValidation" })
        {
            var config = ProjectConfigs.Get(project, "/corpus", "dotnet")!;
            Assert.DoesNotContain("VSTestCLIRunSettings", config.ExtraBuildProperties,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WithRunOverrides_PreservesMediatRFlakeAllowlist()
    {
        var canonical = ProjectConfigs.Get("MediatR", "/corpus", "dotnet");
        Assert.NotNull(canonical);
        Assert.Contains(MediatRFlake, canonical!.ExpectedFlakyTestFullyQualifiedNames);

        var effective = canonical.WithRunOverrides(
            enableBisect: false, buildTimeout: null, minimumCoverage: null, minimumNative: null);

        Assert.Contains(MediatRFlake, effective.ExpectedFlakyTestFullyQualifiedNames);
    }

    [Fact]
    public void WithRunOverrides_AppliesOverridesAndKeepsEveryOtherProperty()
    {
        var canonical = ProjectConfigs.Get("MediatR", "/corpus", "dotnet")!;
        var overridden = new HashSet<string>
        {
            nameof(RoundTripConfig.EnableBisect),
            nameof(RoundTripConfig.BuildTimeout),
            nameof(RoundTripConfig.MinimumCoverageFraction),
            nameof(RoundTripConfig.MinimumNativeFraction),
        };

        var effective = canonical.WithRunOverrides(
            enableBisect: true,
            buildTimeout: TimeSpan.FromMinutes(42),
            minimumCoverage: 0.11,
            minimumNative: 0.22);

        Assert.True(effective.EnableBisect);
        Assert.Equal(TimeSpan.FromMinutes(42), effective.BuildTimeout);
        Assert.Equal(0.11, effective.MinimumCoverageFraction);
        Assert.Equal(0.22, effective.MinimumNativeFraction);

        // Every other public property must carry over unchanged, so a field added
        // to RoundTripConfig later cannot silently vanish on the CI path again.
        var properties = typeof(RoundTripConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !overridden.Contains(p.Name) && p.Name != "EqualityContract");
        foreach (var property in properties)
        {
            Assert.True(
                Equals(property.GetValue(canonical), property.GetValue(effective)),
                $"{property.Name} was not preserved by WithRunOverrides");
        }
    }
}
