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
