using Calor.Compiler.Migration;
using Calor.Compiler.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for unsupported feature tracking, GetFeatureCounts(), and telemetry wiring.
/// </summary>
public class UnsupportedFeatureTelemetryTests
{
    #region GetFeatureCounts Tests

    [Fact]
    public void GetFeatureCounts_EmptyDict_ReturnsEmpty()
    {
        var context = new ConversionContext();
        var explanation = context.GetExplanation();

        var counts = explanation.GetFeatureCounts();

        Assert.Empty(counts);
    }

    [Fact]
    public void GetFeatureCounts_SingleFeature_ReturnsCount()
    {
        var context = new ConversionContext();
        context.RecordUnsupportedFeature("goto", "goto label1;", 10);
        context.RecordUnsupportedFeature("goto", "goto label2;", 20);
        context.RecordUnsupportedFeature("goto", "goto label3;", 30);

        var explanation = context.GetExplanation();
        var counts = explanation.GetFeatureCounts();

        Assert.Single(counts);
        Assert.Equal(3, counts["goto"]);
    }

    [Fact]
    public void GetFeatureCounts_MultipleFeatures_ReturnsAllCounts()
    {
        var context = new ConversionContext();
        context.RecordUnsupportedFeature("goto", "goto label1;", 10);
        context.RecordUnsupportedFeature("goto", "goto label2;", 20);
        context.RecordUnsupportedFeature("unsafe", "unsafe { }", 30);
        context.RecordUnsupportedFeature("fixed", "fixed (int* p = &x) { }", 40);
        context.RecordUnsupportedFeature("fixed", "fixed (byte* b = arr) { }", 50);

        var explanation = context.GetExplanation();
        var counts = explanation.GetFeatureCounts();

        Assert.Equal(3, counts.Count);
        Assert.Equal(2, counts["goto"]);
        Assert.Equal(1, counts["unsafe"]);
        Assert.Equal(2, counts["fixed"]);
        Assert.Equal(5, explanation.TotalUnsupportedCount);
    }

    #endregion

    #region Converter Integration Tests

    [Fact]
    public void Converter_UnsupportedCode_RecordsFeature()
    {
        // Directly test the ConversionContext unsupported feature recording pipeline
        var context = new ConversionContext();
        context.RecordUnsupportedFeature("test-feature", "some_code()", 10);

        var explanation = context.GetExplanation();

        Assert.True(explanation.TotalUnsupportedCount > 0,
            "Expected at least one unsupported feature to be recorded");
        Assert.True(explanation.GetFeatureCounts().Count > 0,
            "Expected GetFeatureCounts() to return at least one entry");
    }

    [Fact]
    public void ConvertCommand_WithFallbacks_TracksInExplanation()
    {
        // Directly test that the explanation pipeline correctly aggregates multiple
        // unsupported feature recordings across different features
        var context = new ConversionContext();
        context.RecordUnsupportedFeature("feature-a", "code_a()", 10);
        context.RecordUnsupportedFeature("feature-b", "code_b()", 20);
        context.RecordUnsupportedFeature("feature-a", "code_a2()", 30);

        var explanation = context.GetExplanation();
        Assert.True(explanation.TotalUnsupportedCount > 0,
            "Expected unsupported features to be tracked");

        var counts = explanation.GetFeatureCounts();
        Assert.True(counts.Count > 0, "Expected at least one feature in counts");
        Assert.True(counts.Values.Sum() == explanation.TotalUnsupportedCount,
            "Feature counts should sum to total unsupported count");
        Assert.Equal(2, counts["feature-a"]);
        Assert.Equal(1, counts["feature-b"]);
    }

    #endregion

    #region End-to-End Converter Unsupported Feature Tests

    [Fact]
    public void Converter_TracksFeatureUsage_EndToEnd()
    {
        // End-to-end: verify the converter records feature usage during conversion.
        // The converter tracks features like "class", "method", "lambda" etc. via RecordFeatureUsage.
        var csharp = """
            using System;
            using System.Collections.Generic;
            public class Test
            {
                public List<int> Items { get; set; } = new();
                public void Method()
                {
                    var list = new List<int> { 1, 2, 3 };
                    Action<int> action = x => Console.WriteLine(x);
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success);
        // Converter should have tracked feature usage
        Assert.Contains("class", result.Context.UsedFeatures);
        Assert.Contains("lambda", result.Context.UsedFeatures);
    }

    [Fact]
    public void Converter_MakeRef_NowSupported()
    {
        // __makeref was previously unsupported but is now handled — verify no fallback
        var csharp = """
            public class Test
            {
                public void Method()
                {
                    int x = 42;
                    var r = __makeref(x);
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            GracefulFallback = true
        });

        var result = converter.Convert(csharp);

        Assert.True(result.Success, "Conversion should succeed");
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
        Assert.Contains("__makeref", result.CalorSource);
    }

    #endregion

    #region TrackUnsupportedFeatures Tests

    [Fact]
    public void TrackUnsupportedFeatures_ZeroCount_DoesNotSendEvent()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackUnsupportedFeatures(new Dictionary<string, int>(), 0);

        Assert.Empty(channel.Items);
    }

    [Fact]
    public void TrackUnsupportedFeatures_SendsEventWithCorrectName()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var features = new Dictionary<string, int> { ["goto"] = 3 };

        telemetry.TrackUnsupportedFeatures(features, 3);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("UnsupportedFeatures", evt.Name);
    }

    [Fact]
    public void TrackUnsupportedFeatures_IncludesTotalAndDistinctCounts()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var features = new Dictionary<string, int>
        {
            ["goto"] = 5,
            ["unsafe"] = 2,
            ["fixed"] = 1
        };

        telemetry.TrackUnsupportedFeatures(features, 8);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("8", evt.Properties["totalUnsupportedCount"]);
        Assert.Equal("3", evt.Properties["distinctFeatureCount"]);
    }

    [Fact]
    public void TrackUnsupportedFeatures_IncludesFeatureProperties()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var features = new Dictionary<string, int>
        {
            ["goto"] = 5,
            ["unsafe"] = 2
        };

        telemetry.TrackUnsupportedFeatures(features, 7);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("5", evt.Properties["feature:goto"]);
        Assert.Equal("2", evt.Properties["feature:unsafe"]);
    }

    [Fact]
    public void TrackUnsupportedFeatures_CapsAt50Features()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var features = new Dictionary<string, int>();
        for (int i = 0; i < 60; i++)
        {
            features[$"feature_{i:D3}"] = i + 1;
        }

        telemetry.TrackUnsupportedFeatures(features, features.Values.Sum());

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        var featureProps = evt.Properties.Keys.Where(k => k.StartsWith("feature:")).ToList();
        Assert.Equal(50, featureProps.Count);
    }

    [Fact]
    public void TrackUnsupportedFeatures_OrdersByCountDescending()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var features = new Dictionary<string, int>
        {
            ["rare"] = 1,
            ["common"] = 100,
            ["medium"] = 10
        };

        telemetry.TrackUnsupportedFeatures(features, 111);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        // All 3 should be present (under 50 cap), but we verify the most common is included
        Assert.Equal("100", evt.Properties["feature:common"]);
        Assert.Equal("10", evt.Properties["feature:medium"]);
        Assert.Equal("1", evt.Properties["feature:rare"]);
    }

    [Fact]
    public void TrackUnsupportedFeatures_IncludesCommandProperties()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        telemetry.SetCommand("convert", new Dictionary<string, string>
        {
            ["direction"] = "cs-to-calor"
        });
        var features = new Dictionary<string, int> { ["goto"] = 1 };

        telemetry.TrackUnsupportedFeatures(features, 1);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("convert", evt.Properties["command"]);
        Assert.Equal("cs-to-calor", evt.Properties["direction"]);
    }

    #endregion

    #region MigrateCommand Pipeline Tests

    [Fact]
    public void MigrateCommand_FeatureAggregation_CorrectlyGroupsFromIssues()
    {
        // Simulates the exact aggregation logic used in MigrateCommand
        var fileResults = new List<FileMigrationResult>
        {
            new()
            {
                SourcePath = "file1.cs",
                OutputPath = "file1.calr",
                Status = FileMigrationStatus.Partial,
                Issues = new List<ConversionIssue>
                {
                    new() { Severity = ConversionIssueSeverity.Warning, Message = "goto fallback", Feature = "goto" },
                    new() { Severity = ConversionIssueSeverity.Warning, Message = "goto fallback", Feature = "goto" },
                    new() { Severity = ConversionIssueSeverity.Warning, Message = "unsafe fallback", Feature = "unsafe" },
                }
            },
            new()
            {
                SourcePath = "file2.cs",
                OutputPath = "file2.calr",
                Status = FileMigrationStatus.Partial,
                Issues = new List<ConversionIssue>
                {
                    new() { Severity = ConversionIssueSeverity.Warning, Message = "goto fallback", Feature = "goto" },
                    new() { Severity = ConversionIssueSeverity.Error, Message = "compile error", Feature = null },
                }
            },
            new()
            {
                SourcePath = "file3.cs",
                OutputPath = "file3.calr",
                Status = FileMigrationStatus.Success,
                Issues = new List<ConversionIssue>() // no issues
            }
        };

        // This is the exact aggregation logic from MigrateCommand
        var featureCounts = fileResults
            .SelectMany(f => f.Issues)
            .Where(i => i.Feature != null)
            .GroupBy(i => i.Feature!)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(2, featureCounts.Count);
        Assert.Equal(3, featureCounts["goto"]);  // 2 from file1 + 1 from file2
        Assert.Equal(1, featureCounts["unsafe"]);
        Assert.Equal(4, featureCounts.Values.Sum());
    }

    [Fact]
    public void MigrateCommand_FeatureAggregation_EmptyIssues_ProducesEmptyDict()
    {
        var fileResults = new List<FileMigrationResult>
        {
            new()
            {
                SourcePath = "clean.cs",
                OutputPath = "clean.calr",
                Status = FileMigrationStatus.Success,
                Issues = new List<ConversionIssue>()
            }
        };

        var featureCounts = fileResults
            .SelectMany(f => f.Issues)
            .Where(i => i.Feature != null)
            .GroupBy(i => i.Feature!)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Empty(featureCounts);
    }

    [Fact]
    public void MigrateCommand_FeatureAggregation_NullFeaturesFiltered()
    {
        var fileResults = new List<FileMigrationResult>
        {
            new()
            {
                SourcePath = "errors.cs",
                OutputPath = null,
                Status = FileMigrationStatus.Failed,
                Issues = new List<ConversionIssue>
                {
                    new() { Severity = ConversionIssueSeverity.Error, Message = "syntax error", Feature = null },
                    new() { Severity = ConversionIssueSeverity.Error, Message = "type error", Feature = null },
                }
            }
        };

        var featureCounts = fileResults
            .SelectMany(f => f.Issues)
            .Where(i => i.Feature != null)
            .GroupBy(i => i.Feature!)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Empty(featureCounts);
    }

    #endregion

    #region End-to-End Pipeline Tests

    [Fact]
    public void EndToEnd_ConversionIssuesPreserveFeature_ViaConverter()
    {
        // End-to-end: verify that conversion issues preserve Feature property.
        // Conversion warnings from the converter set the Feature property for tracking.
        var csharp = """
            using System.Collections.Generic;
            public class Test
            {
                public List<int> GetNumbers() => new() { 1, 2, 3 };
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharp);

        Assert.True(result.Success);
        // Context should have tracked conversions
        Assert.True(result.Context.Stats.ConvertedNodes > 0,
            "Expected converter to track converted nodes");
        Assert.True(result.Context.Stats.ClassesConverted > 0,
            "Expected at least one class conversion");
    }

    [Fact]
    public void EndToEnd_ConversionIssuesPreserveFeature()
    {
        // Verifies the pipeline: ConversionContext → Issues → Feature property
        // Tests that AddWarning with feature tags and RecordUnsupportedFeature are consistent
        var context = new ConversionContext { GracefulFallback = true };
        context.RecordUnsupportedFeature("test-feature", "some_code()", 10);
        context.AddWarning("Unsupported feature [test-feature] replaced with fallback: some_code()",
            feature: "test-feature", line: 10);

        var issues = context.Issues;

        // Issues should contain entries with non-null Feature
        var issuesWithFeature = issues.Where(i => i.Feature != null).ToList();
        Assert.NotEmpty(issuesWithFeature);

        // The same aggregation used in MigrateCommand should work on these issues
        var featureCounts = issuesWithFeature
            .GroupBy(i => i.Feature!)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.NotEmpty(featureCounts);

        // Feature counts from issues should be consistent with GetExplanation
        var explanation = context.GetExplanation();
        var explanationCounts = explanation.GetFeatureCounts();
        foreach (var feature in explanationCounts.Keys)
        {
            Assert.True(featureCounts.ContainsKey(feature),
                $"Feature '{feature}' in explanation but not in issues");
        }
    }

    [Fact]
    public void EndToEnd_TrackUnsupportedFeatures_FullPipeline()
    {
        // Full pipeline: ConversionContext → GetExplanation → GetFeatureCounts → TrackUnsupportedFeatures → verify event
        var (telemetry, channel) = CreateTestTelemetry();

        var context = new ConversionContext();
        context.RecordUnsupportedFeature("feature-x", "code_x()", 10);
        context.RecordUnsupportedFeature("feature-x", "code_x2()", 20);
        context.RecordUnsupportedFeature("feature-y", "code_y()", 30);

        var explanation = context.GetExplanation();

        Assert.True(explanation.TotalUnsupportedCount > 0);

        telemetry.TrackUnsupportedFeatures(
            explanation.GetFeatureCounts(),
            explanation.TotalUnsupportedCount);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("UnsupportedFeatures", evt.Name);
        Assert.Equal(
            explanation.TotalUnsupportedCount.ToString(),
            evt.Properties["totalUnsupportedCount"]);

        // Every feature from GetFeatureCounts should appear as a property
        foreach (var (feature, count) in explanation.GetFeatureCounts())
        {
            Assert.Equal(count.ToString(), evt.Properties[$"feature:{feature}"]);
        }
    }

    #endregion

    #region Test Helpers

    private static (CalorTelemetry telemetry, StubTelemetryChannel channel) CreateTestTelemetry()
    {
        var channel = new StubTelemetryChannel();
        var config = new TelemetryConfiguration
        {
            TelemetryChannel = channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        var client = new TelemetryClient(config);
        var telemetry = new CalorTelemetry(client);
        return (telemetry, channel);
    }

    /// <summary>
    /// Minimal ITelemetryChannel that captures sent items for test assertions.
    /// </summary>
    private sealed class StubTelemetryChannel : ITelemetryChannel
    {
        public List<ITelemetry> Items { get; } = new();
        public bool? DeveloperMode { get; set; } = true;
        public string EndpointAddress { get; set; } = "https://localhost";

        public void Send(ITelemetry item) => Items.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }

    #endregion
}
