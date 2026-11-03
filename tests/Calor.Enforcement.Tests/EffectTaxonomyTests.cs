using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for the effect taxonomy (new granular effect codes and backward compatibility).
/// </summary>
public class EffectTaxonomyTests
{
    [Theory]
    [InlineData("http")]
    [InlineData("HTTP")]
    [InlineData(" http ")]
    public void RetiredHttpCode_IsRejectedBySourceAndManifests(string code)
    {
        Assert.False(EffectCodes.IsKnownCompactCode(code));
        Assert.False(EffectCodes.TryParseCompact(code, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => EffectSet.From(code));

        var result = TestHarness.Compile(
            $$"""
            §M{Test}
              §F{Run:pub} () -> void
                §E{{{code}}}
            """);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode);

        var loader = new ManifestLoader();
        loader.LoadFromJson(
            $$"""
            {
              "version": "1.0",
              "mappings": [{
                "type": "Example.Type",
                "methods": { "Run": ["{{code}}"] }
              }]
            }
            """,
            "retired-http");
        Assert.Contains(loader.ValidateManifests(),
            error => error.Contains("Unknown effect code", StringComparison.Ordinal)
                && error.Contains(code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("fs:r", EffectKind.IO, "filesystem_read")]
    [InlineData("fs:w", EffectKind.IO, "filesystem_write")]
    [InlineData("fs:rw", EffectKind.IO, "filesystem_readwrite")]
    [InlineData("net:r", EffectKind.IO, "network_read")]
    [InlineData("net:w", EffectKind.IO, "network_write")]
    [InlineData("net:rw", EffectKind.IO, "network_readwrite")]
    [InlineData("db:r", EffectKind.IO, "database_read")]
    [InlineData("db:w", EffectKind.IO, "database_write")]
    [InlineData("db:rw", EffectKind.IO, "database_readwrite")]
    [InlineData("env:r", EffectKind.IO, "environment_read")]
    [InlineData("env:w", EffectKind.IO, "environment_write")]
    [InlineData("unsafe", EffectKind.Memory, "unsafe")]
    [InlineData("alloc", EffectKind.Memory, "allocation")]
    [InlineData("cw", EffectKind.IO, "console_write")]
    [InlineData("cr", EffectKind.IO, "console_read")]
    [InlineData("time", EffectKind.Nondeterminism, "time")]
    [InlineData("rand", EffectKind.Nondeterminism, "random")]
    [InlineData("proc", EffectKind.IO, "process")]
    [InlineData("mut", EffectKind.Mutation, "heap_write")]
    [InlineData("throw", EffectKind.Exception, "intentional")]
    public void EffectCodes_ParseCorrectly(string code, EffectKind expectedKind, string expectedValue)
    {
        // Test via AttributeHelper
        var (category, value) = AttributeHelper.ExpandEffectCode(code);
        Assert.Equal(expectedValue, value);

        // Test via EffectSet
        var effectSet = EffectSet.From(code);
        Assert.Single(effectSet.Effects);
        var effect = effectSet.Effects.First();
        Assert.Equal(expectedKind, effect.Kind);
        Assert.Equal(expectedValue, effect.Value);
    }

    [Fact]
    public void EffectSet_ToDisplayString_ShowsHumanReadableCodes()
    {
        var effects = EffectSet.From("cw", "fs:r", "rand");
        var display = effects.ToDisplayString();

        // Should contain surface codes, sorted
        Assert.Contains("cw", display);
        Assert.Contains("fs:r", display);
        Assert.Contains("rand", display);
    }

    [Fact]
    public void EffectSet_Empty_DisplaysAsPure()
    {
        var effects = EffectSet.Empty;
        Assert.Equal("[pure]", effects.ToDisplayString());
    }

    [Fact]
    public void EffectSet_Unknown_DisplaysAsUnknown()
    {
        var effects = EffectSet.Unknown;
        Assert.Equal("[unknown]", effects.ToDisplayString());
    }
}
