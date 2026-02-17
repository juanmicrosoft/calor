using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration tests for the feature-check command functionality.
/// Tests the FeatureSupport registry for completeness and correctness.
/// </summary>
public class FeatureCheckCommandTests
{
    #region Feature Lookup

    [Theory]
    [InlineData("class", SupportLevel.Full)]
    [InlineData("interface", SupportLevel.Full)]
    [InlineData("async-await", SupportLevel.Full)]
    [InlineData("lambda", SupportLevel.Full)]
    [InlineData("generics", SupportLevel.Full)]
    public void FeatureCheck_FullySupported_ReturnsFullLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.True(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("linq-method", SupportLevel.Partial)]
    [InlineData("linq-query", SupportLevel.Partial)]
    [InlineData("ref-parameter", SupportLevel.Partial)]
    [InlineData("dynamic", SupportLevel.Partial)]
    public void FeatureCheck_PartiallySupported_ReturnsPartialLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("yield-return", SupportLevel.NotSupported)]
    [InlineData("goto", SupportLevel.NotSupported)]
    [InlineData("unsafe", SupportLevel.NotSupported)]
    [InlineData("primary-constructor", SupportLevel.NotSupported)]
    [InlineData("lock-statement", SupportLevel.NotSupported)]
    [InlineData("await-foreach", SupportLevel.NotSupported)]
    [InlineData("collection-expression", SupportLevel.NotSupported)]
    [InlineData("file-scoped-type", SupportLevel.NotSupported)]
    [InlineData("utf8-string-literal", SupportLevel.NotSupported)]
    public void FeatureCheck_NotSupported_ReturnsNotSupportedLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.False(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("extension-method", SupportLevel.ManualRequired)]
    [InlineData("operator-overload", SupportLevel.ManualRequired)]
    [InlineData("implicit-conversion", SupportLevel.ManualRequired)]
    public void FeatureCheck_ManualRequired_ReturnsManualLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.False(FeatureSupport.IsSupported(feature));
    }

    #endregion

    #region Unknown Features

    [Fact]
    public void FeatureCheck_UnknownFeature_ReturnsNull()
    {
        var info = FeatureSupport.GetFeatureInfo("some-made-up-feature");

        Assert.Null(info);
    }

    [Fact]
    public void FeatureCheck_UnknownFeature_DefaultsToFullSupport()
    {
        // Unknown features default to Full support (assume basic C# works)
        var level = FeatureSupport.GetSupportLevel("unknown-feature");

        Assert.Equal(SupportLevel.Full, level);
    }

    #endregion

    #region Case Insensitivity

    [Theory]
    [InlineData("ASYNC-AWAIT")]
    [InlineData("Async-Await")]
    [InlineData("async-AWAIT")]
    public void FeatureCheck_CaseInsensitive_FindsFeature(string feature)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal("async-await", info.Name);
    }

    #endregion

    #region Workarounds

    [Theory]
    [InlineData("yield-return")]
    [InlineData("goto")]
    [InlineData("primary-constructor")]
    [InlineData("lock-statement")]
    [InlineData("collection-expression")]
    public void FeatureCheck_UnsupportedFeature_HasWorkaround(string feature)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.NotNull(info.Workaround);
        Assert.NotEmpty(info.Workaround);
    }

    [Fact]
    public void FeatureCheck_GetWorkaround_ReturnsWorkaroundText()
    {
        var workaround = FeatureSupport.GetWorkaround("yield-return");

        Assert.NotNull(workaround);
        Assert.Contains("List", workaround);
    }

    #endregion

    #region Feature Listing

    [Fact]
    public void FeatureCheck_GetAllFeatures_ReturnsNonEmpty()
    {
        var features = FeatureSupport.GetAllFeatures().ToList();

        Assert.NotEmpty(features);
        Assert.True(features.Count > 50, $"Expected > 50 features, got {features.Count}");
    }

    [Fact]
    public void FeatureCheck_GetFeaturesBySupport_FiltersCorrectly()
    {
        var fullFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Full).ToList();
        var notSupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported).ToList();

        Assert.NotEmpty(fullFeatures);
        Assert.NotEmpty(notSupportedFeatures);
        Assert.All(fullFeatures, f => Assert.Equal(SupportLevel.Full, f.Support));
        Assert.All(notSupportedFeatures, f => Assert.Equal(SupportLevel.NotSupported, f.Support));
    }

    [Fact]
    public void FeatureCheck_AllLevelsHaveFeatures()
    {
        var levels = Enum.GetValues<SupportLevel>();

        foreach (var level in levels)
        {
            var features = FeatureSupport.GetFeaturesBySupport(level).ToList();
            Assert.NotEmpty(features);
        }
    }

    #endregion

    #region Blocker Name Consistency

    [Theory]
    [InlineData("relational-pattern")]
    [InlineData("compound-pattern")]
    [InlineData("range-expression")]
    [InlineData("index-from-end")]
    [InlineData("target-typed-new")]
    [InlineData("null-conditional-method")]
    [InlineData("named-argument")]
    [InlineData("declaration-pattern")]
    [InlineData("throw-expression")]
    [InlineData("nested-generic-type")]
    [InlineData("out-var")]
    [InlineData("in-parameter")]
    [InlineData("checked-block")]
    [InlineData("with-expression")]
    [InlineData("init-accessor")]
    [InlineData("required-member")]
    [InlineData("list-pattern")]
    [InlineData("static-abstract-member")]
    [InlineData("ref-struct")]
    [InlineData("lock-statement")]
    [InlineData("await-foreach")]
    [InlineData("await-using")]
    [InlineData("scoped-parameter")]
    [InlineData("collection-expression")]
    [InlineData("readonly-struct")]
    [InlineData("default-lambda-parameter")]
    [InlineData("file-scoped-type")]
    [InlineData("utf8-string-literal")]
    [InlineData("generic-attribute")]
    [InlineData("using-type-alias")]
    public void FeatureCheck_BlockerName_ExistsInRegistry(string blockerName)
    {
        // All blocker names from MigrationAnalyzer should exist in FeatureSupport
        var info = FeatureSupport.GetFeatureInfo(blockerName);

        Assert.NotNull(info);
        Assert.Equal(blockerName, info.Name);
    }

    [Fact]
    public void FeatureCheck_AllBlockerNames_UseKebabCase()
    {
        var features = FeatureSupport.GetAllFeatures();

        foreach (var feature in features)
        {
            // All names should be lowercase kebab-case
            Assert.Equal(feature.Name.ToLowerInvariant(), feature.Name);
            Assert.DoesNotContain("_", feature.Name);
            Assert.DoesNotMatch(@"[A-Z]", feature.Name);
        }
    }

    #endregion

    #region Description Quality

    [Fact]
    public void FeatureCheck_AllFeatures_HaveDescription()
    {
        var features = FeatureSupport.GetAllFeatures();

        foreach (var feature in features)
        {
            Assert.NotNull(feature.Description);
            Assert.NotEmpty(feature.Description);
        }
    }

    [Fact]
    public void FeatureCheck_UnsupportedFeatures_HaveWorkaround()
    {
        var unsupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported);

        foreach (var feature in unsupportedFeatures)
        {
            Assert.NotNull(feature.Workaround);
            Assert.NotEmpty(feature.Workaround);
        }
    }

    [Fact]
    public void FeatureCheck_ManualRequiredFeatures_HaveWorkaround()
    {
        var manualFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.ManualRequired);

        foreach (var feature in manualFeatures)
        {
            Assert.NotNull(feature.Workaround);
            Assert.NotEmpty(feature.Workaround);
        }
    }

    #endregion
}
