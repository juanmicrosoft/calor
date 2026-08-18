using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis.CSharp;
using SyntaxCapabilityClassifier =
    Calor.Compiler.Migration.FeatureSupport.SyntaxCapabilityClassifier;
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
    [InlineData("async-await", SupportLevel.Full)]
    [InlineData("lambda", SupportLevel.Full)]
    [InlineData("generics", SupportLevel.Full)]
    [InlineData("operator-overload", SupportLevel.Full)]
    [InlineData("implicit-conversion", SupportLevel.Full)]
    [InlineData("explicit-conversion", SupportLevel.Full)]
    [InlineData("equals-operator", SupportLevel.Full)]
    [InlineData("linq-method", SupportLevel.Full)]
    [InlineData("linq-query", SupportLevel.Full)]
    [InlineData("goto", SupportLevel.Full)]
    [InlineData("labeled-statement", SupportLevel.Full)]
    [InlineData("postfix-operator", SupportLevel.Full)]
    [InlineData("is-type-pattern", SupportLevel.Full)]
    [InlineData("declaration-pattern", SupportLevel.Full)]
    [InlineData("throw-expression", SupportLevel.Full)]
    [InlineData("nested-generic-type", SupportLevel.Full)]
    [InlineData("binary pattern (and/or)", SupportLevel.Full)]
    [InlineData("unary pattern (not)", SupportLevel.Full)]
    public void FeatureCheck_FullySupported_ReturnsFullLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.True(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("ref-parameter", SupportLevel.Partial)]
    [InlineData("dynamic", SupportLevel.Partial)]
    [InlineData("interface", SupportLevel.Partial)]
    public void FeatureCheck_PartiallySupported_ReturnsPartialLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("await-foreach", SupportLevel.NotSupported)]
    [InlineData("file-scoped-type", SupportLevel.NotSupported)]
    [InlineData("record", SupportLevel.NotSupported)]
    [InlineData("local-function", SupportLevel.NotSupported)]
    [InlineData("scoped-parameter", SupportLevel.NotSupported)]
    [InlineData("using-declaration", SupportLevel.NotSupported)]
    public void FeatureCheck_NotSupported_ReturnsNotSupportedLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.False(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("yield-return", SupportLevel.Full)]
    [InlineData("extension-method", SupportLevel.Full)]
    public void FeatureCheck_NewlyFullySupported_ReturnsFullLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.True(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
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
    public void FeatureCheck_UnknownFeature_DefaultsToNotSupported()
    {
        // Unknown features default to NotSupported to prevent silent suppression of blockers
        var level = FeatureSupport.GetSupportLevel("unknown-feature");

        Assert.Equal(SupportLevel.NotSupported, level);
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
    [InlineData("await-foreach")]
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
        var workaround = FeatureSupport.GetWorkaround("await-foreach");

        Assert.NotNull(workaround);
        Assert.NotEmpty(workaround);
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
        // At least the main levels (Full, Partial, NotSupported) should have features
        var fullFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Full).ToList();
        var partialFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Partial).ToList();
        var notSupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported).ToList();

        Assert.NotEmpty(fullFeatures);
        Assert.NotEmpty(partialFeatures);
        Assert.NotEmpty(notSupportedFeatures);
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
    [InlineData("using-declaration")]
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

    [Theory]
    [InlineData("await foreach (var item in items) { }", "await-foreach")]
    [InlineData("await using (resource) { }", "await-using")]
    [InlineData("await using var resource = value;", "await-using")]
    [InlineData("using var resource = value;", "using-declaration")]
    [InlineData("public ref struct Buffer { }", "ref-struct")]
    [InlineData("file class LocalType { }", "file-scoped-type")]
    [InlineData("public void M(scoped ref int value) { }", "scoped-parameter")]
    [InlineData("public interface I { void M() { } }", "interface-method-semantics")]
    [InlineData("public interface I { int P { get => 1; } }", "interface-property-semantics")]
    [InlineData("public interface I { int this[int i] { get => i; } }", "interface-indexer-semantics")]
    [InlineData("public interface I { event System.Action Changed; }", "interface-member")]
    [InlineData("internal interface I { void M(); }", "interface-semantics")]
    [InlineData("public delegate T Factory<T>(T value);", "delegate-semantics")]
    [InlineData("[System.Obsolete] public delegate void Legacy();", "delegate-semantics")]
    public void SyntaxCapabilityClassifier_DetectsRequiredUnsupportedFeature(
        string source,
        string expectedFeature)
    {
        var root = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();

        var detection = Assert.Single(
            SyntaxCapabilityClassifier.Detect(root)
                .Where(item => item.FeatureName == expectedFeature));

        Assert.Equal(expectedFeature, detection.FeatureName);
        Assert.True(detection.Line > 0);
        Assert.True(detection.Column > 0);
        Assert.True(detection.SpanLength > 0);
        Assert.Contains(expectedFeature, SyntaxCapabilityClassifier.RequiredUnsupportedFeatures);
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
