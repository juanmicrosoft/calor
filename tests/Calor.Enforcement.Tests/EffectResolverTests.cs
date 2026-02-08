using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for the effect resolver (layered manifest resolution).
/// </summary>
public class EffectResolverTests
{
    [Fact]
    public void Resolve_ReturnsBuiltInEffects_ForKnownBclMethods()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("System.Console", "WriteLine");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "console_write");
        Assert.Equal("built-in", resolution.Source);
    }

    [Fact]
    public void Resolve_ReturnsPureExplicit_ForMathMethods()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("System.Math", "Max");

        // Math.Max should be pure (no effects)
        Assert.True(resolution.Effects.IsEmpty || resolution.Status == EffectResolutionStatus.PureExplicit);
    }

    [Fact]
    public void Resolve_ReturnsUnknown_ForUnknownMethod()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution = resolver.Resolve("UnknownNamespace.UnknownType", "UnknownMethod");

        Assert.Equal(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.True(resolution.Effects.IsUnknown);
    }

    [Fact]
    public void Resolve_UsesManifestForSpecificMethod()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.CustomService"",
                    ""methods"": {
                        ""DoWork"": [""net:w"", ""db:w""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.CustomService", "DoWork");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "network_write");
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_write");
    }

    [Fact]
    public void Resolve_UsesWildcard_WhenNoSpecificMethod()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.PureService"",
                    ""methods"": {
                        ""*"": []
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.PureService", "AnyMethod");

        Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
        Assert.True(resolution.Effects.IsEmpty);
    }

    [Fact]
    public void Resolve_UsesDefaultEffects_WhenNoMethodMatch()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.IoService"",
                    ""defaultEffects"": [""fs:rw""]
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.IoService", "SomeMethod");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "filesystem_readwrite");
    }

    [Fact]
    public void Resolve_UsesNamespaceDefaults_WhenNoTypeMatch()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [],
            ""namespaceDefaults"": {
                ""MyApp.Data"": [""db:rw""]
            }
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.Resolve("MyApp.Data.Repository", "GetAll");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_readwrite");
    }

    [Fact]
    public void Resolve_SpecificMethodOverridesWildcard()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Service"",
                    ""methods"": {
                        ""SpecificMethod"": [""cw""],
                        ""*"": [""fs:rw""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var specificResolution = resolver.Resolve("MyApp.Service", "SpecificMethod");
        var otherResolution = resolver.Resolve("MyApp.Service", "OtherMethod");

        Assert.Contains(specificResolution.Effects.Effects, e => e.Value == "console_write");
        Assert.DoesNotContain(specificResolution.Effects.Effects, e => e.Value == "filesystem_readwrite");

        Assert.Contains(otherResolution.Effects.Effects, e => e.Value == "filesystem_readwrite");
    }

    [Fact]
    public void ResolveGetter_ReturnsPropertyEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Config"",
                    ""getters"": {
                        ""Value"": [""env:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.ResolveGetter("MyApp.Config", "Value");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "environment_read");
    }

    [Fact]
    public void ResolveSetter_ReturnsPropertyEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.Config"",
                    ""setters"": {
                        ""Value"": [""env:w""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.ResolveSetter("MyApp.Config", "Value");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "environment_write");
    }

    [Fact]
    public void ResolveConstructor_ReturnsConstructorEffects()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyApp.FileService"",
                    ""constructors"": {
                        ""(String)"": [""fs:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        var resolution = resolver.ResolveConstructor("MyApp.FileService", "String");

        Assert.Equal(EffectResolutionStatus.Resolved, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "filesystem_read");
    }

    [Fact]
    public void Resolve_CachesResults()
    {
        var resolver = new EffectResolver();
        resolver.Initialize();

        var resolution1 = resolver.Resolve("System.Console", "WriteLine");
        var resolution2 = resolver.Resolve("System.Console", "WriteLine");

        // Should return same reference due to caching
        Assert.Same(resolution1, resolution2);
    }

    [Fact]
    public void LoadErrors_ReportsManifestProblems()
    {
        var loader = new ManifestLoader();
        loader.LoadFromJson("{ invalid }", "bad-manifest");

        var resolver = new EffectResolver(loader);
        resolver.Initialize();

        Assert.NotEmpty(resolver.LoadErrors);
    }
}
