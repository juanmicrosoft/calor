using Calor.Compiler.Effects.Manifests;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for the effect manifest loader.
/// </summary>
public class ManifestLoaderTests
{
    [Fact]
    public void LoadFromJson_ParsesValidManifest()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""description"": ""Test manifest"",
            ""mappings"": [
                {
                    ""type"": ""TestNamespace.TestType"",
                    ""defaultEffects"": [""cw""],
                    ""methods"": {
                        ""DoWork"": [""fs:w""],
                        ""GetData"": [""fs:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        var manifest = loader.LoadFromJson(json, "test");

        Assert.NotNull(manifest);
        Assert.Equal("1.0", manifest.Version);
        Assert.Equal("Test manifest", manifest.Description);
        Assert.Single(manifest.Mappings);

        var mapping = manifest.Mappings[0];
        Assert.Equal("TestNamespace.TestType", mapping.Type);
        Assert.Equal(new[] { "cw" }, mapping.DefaultEffects);
        Assert.Equal(2, mapping.Methods?.Count);
        Assert.Equal(new[] { "fs:w" }, mapping.Methods?["DoWork"]);
        Assert.Equal(new[] { "fs:r" }, mapping.Methods?["GetData"]);
    }

    [Fact]
    public void LoadFromJson_ParsesNamespaceDefaults()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [],
            ""namespaceDefaults"": {
                ""System.IO"": [""fs:rw""],
                ""System.Net"": [""net:rw""]
            }
        }";

        var loader = new ManifestLoader();
        var manifest = loader.LoadFromJson(json, "test");

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.NamespaceDefaults.Count);
        Assert.Equal(new[] { "fs:rw" }, manifest.NamespaceDefaults["System.IO"]);
        Assert.Equal(new[] { "net:rw" }, manifest.NamespaceDefaults["System.Net"]);
    }

    [Fact]
    public void LoadFromJson_ParsesGettersAndSetters()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""System.Console"",
                    ""getters"": {
                        ""ForegroundColor"": [""cr""]
                    },
                    ""setters"": {
                        ""ForegroundColor"": [""cw""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        var manifest = loader.LoadFromJson(json, "test");

        Assert.NotNull(manifest);
        var mapping = manifest.Mappings[0];
        Assert.NotNull(mapping.Getters);
        Assert.NotNull(mapping.Setters);
        Assert.Single(mapping.Getters);
        Assert.Single(mapping.Setters);
        Assert.Equal(new[] { "cr" }, mapping.Getters["ForegroundColor"]);
        Assert.Equal(new[] { "cw" }, mapping.Setters["ForegroundColor"]);
    }

    [Fact]
    public void LoadFromJson_ParsesConstructors()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""System.IO.StreamReader"",
                    ""constructors"": {
                        ""(String)"": [""fs:r""],
                        ""(Stream)"": []
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        var manifest = loader.LoadFromJson(json, "test");

        Assert.NotNull(manifest);
        var mapping = manifest.Mappings[0];
        Assert.Equal(2, mapping.Constructors?.Count);
        Assert.Equal(new[] { "fs:r" }, mapping.Constructors?["(String)"]);
        Assert.Empty(mapping.Constructors?["(Stream)"] ?? new List<string>());
    }

    [Fact]
    public void LoadFromJson_ReportsInvalidJson()
    {
        var json = "{ invalid json }";

        var loader = new ManifestLoader();
        var manifest = loader.LoadFromJson(json, "test");

        Assert.Null(manifest);
        Assert.Single(loader.LoadErrors);
        Assert.Contains("Failed to parse manifest from JSON", loader.LoadErrors[0]);
    }

    [Fact]
    public void ValidateManifests_ReportsMissingVersion()
    {
        var json = @"{
            ""version"": """",
            ""mappings"": []
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");
        var errors = loader.ValidateManifests();

        Assert.Single(errors);
        Assert.Contains("Missing 'version' field", errors[0]);
    }

    [Fact]
    public void ValidateManifests_ReportsUnsupportedVersion()
    {
        var json = @"{
            ""version"": ""2.0"",
            ""mappings"": []
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");
        var errors = loader.ValidateManifests();

        Assert.Single(errors);
        Assert.Contains("Unsupported version", errors[0]);
    }

    [Fact]
    public void ValidateManifests_ReportsEmptyTypeName()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": """",
                    ""methods"": {}
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");
        var errors = loader.ValidateManifests();

        Assert.Single(errors);
        Assert.Contains("empty 'type' field", errors[0]);
    }

    [Fact]
    public void ValidateManifests_ReportsUnknownEffectCodes()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""TestType"",
                    ""methods"": {
                        ""DoWork"": [""unknown_effect""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");
        var errors = loader.ValidateManifests();

        Assert.Single(errors);
        Assert.Contains("Unknown effect code 'unknown_effect'", errors[0]);
    }

    [Fact]
    public void ValidateManifests_AcceptsValidManifest()
    {
        var json = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""TestType"",
                    ""methods"": {
                        ""DoWork"": [""cw"", ""fs:r""]
                    }
                }
            ]
        }";

        var loader = new ManifestLoader();
        loader.LoadFromJson(json, "test");
        var errors = loader.ValidateManifests();

        Assert.Empty(errors);
    }

    [Fact]
    public void LoadAll_LoadsBuiltInManifests()
    {
        var loader = new ManifestLoader();
        loader.LoadAll();

        // Should have loaded embedded BCL manifests
        Assert.True(loader.LoadedManifests.Count > 0);

        // All should be built-in priority
        Assert.All(loader.LoadedManifests, m =>
            Assert.Equal(ManifestPriority.BuiltIn, m.Source.Priority));
    }

    [Fact]
    public void ManifestSource_PriorityOrdering()
    {
        // Verify priority ordering is correct
        Assert.True(ManifestPriority.UserLevel > ManifestPriority.BuiltIn);
        Assert.True(ManifestPriority.SolutionLevel > ManifestPriority.UserLevel);
        Assert.True(ManifestPriority.ProjectLocal > ManifestPriority.SolutionLevel);
    }
}
