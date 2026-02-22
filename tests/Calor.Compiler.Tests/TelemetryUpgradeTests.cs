using Calor.Compiler.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the telemetry upgrade: NaN fix, enriched events, session tracking,
/// converter telemetry, and version regression detection.
/// </summary>
/// <remarks>
/// Integration tests that use SetInstanceForTesting must not run in parallel
/// because they share the CalorTelemetry singleton.
/// </remarks>
[Collection("TelemetrySingleton")]
public class TelemetryUpgradeTests
{
    #region Phase 1: NaN Fix Tests

    [Fact]
    public void TrackCommand_EmitsDurationAsMetric()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        telemetry.SetCommand("compile");

        telemetry.TrackCommand("compile", 0);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.True(evt.Metrics.ContainsKey("durationMs"), "durationMs should be in Metrics");
        Assert.IsType<double>(evt.Metrics["durationMs"]);
        Assert.True(evt.Metrics["durationMs"] >= 0, "durationMs should be non-negative");
    }

    [Fact]
    public void TrackCommand_EmitsDurationInBothPropertiesAndMetrics()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        telemetry.SetCommand("compile");

        telemetry.TrackCommand("compile", 0);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.True(evt.Properties.ContainsKey("durationMs"), "durationMs should still be in Properties for backward compat");
        Assert.True(evt.Metrics.ContainsKey("durationMs"), "durationMs should also be in Metrics for KQL");
    }

    [Fact]
    public void TrackUnsupportedFeatures_EmitsCountsAsMetrics()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var features = new Dictionary<string, int>
        {
            ["goto"] = 5,
            ["unsafe"] = 2
        };

        telemetry.TrackUnsupportedFeatures(features, 7);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal(7.0, evt.Metrics["totalUnsupportedCount"]);
        Assert.Equal(2.0, evt.Metrics["distinctFeatureCount"]);
        // Backward compat: still in Properties
        Assert.Equal("7", evt.Properties["totalUnsupportedCount"]);
        Assert.Equal("2", evt.Properties["distinctFeatureCount"]);
    }

    #endregion

    #region Phase 2: Enriched Event Schema Tests

    [Fact]
    public void InputProfile_CountsLines()
    {
        var source = "line1\nline2\nline3\n";
        var profile = TelemetryEnricher.AnalyzeInput(source);

        Assert.Equal(3, profile.LineCount);
    }

    [Fact]
    public void InputProfile_DetectsFeatures()
    {
        var source = "module MyModule\n  contract requires x > 0\n  effect IO\n";
        var profile = TelemetryEnricher.AnalyzeInput(source);

        Assert.True(profile.HasModules);
        Assert.True(profile.HasContracts);
        Assert.True(profile.HasEffects);
    }

    [Fact]
    public void CoOccurrence_PairsTopCodes()
    {
        // Calor0001 appears 2x, Calor0002 1x, Calor0003 1x
        var diagnostics = new List<string> { "Calor0001", "Calor0002", "Calor0001", "Calor0003" };
        var pairs = TelemetryEnricher.AnalyzeCoOccurrence(diagnostics);

        Assert.Equal(3, pairs.Count); // 3 unique pairs
        // Pair weight = min(count_a, count_b)
        Assert.Equal(1, pairs["Calor0001+Calor0002"]); // min(2,1) = 1
        Assert.Equal(1, pairs["Calor0001+Calor0003"]); // min(2,1) = 1
        Assert.Equal(1, pairs["Calor0002+Calor0003"]); // min(1,1) = 1
    }

    [Fact]
    public void CoOccurrence_WeightsReflectFrequency()
    {
        // Calor0001 appears 3x, Calor0002 appears 2x → pair weight = min(3,2) = 2
        var diagnostics = new List<string>
        {
            "Calor0001", "Calor0001", "Calor0001",
            "Calor0002", "Calor0002"
        };
        var pairs = TelemetryEnricher.AnalyzeCoOccurrence(diagnostics);

        Assert.Single(pairs);
        Assert.Equal(2, pairs["Calor0001+Calor0002"]);
    }

    [Fact]
    public void CoOccurrence_CapsAtTenPairs()
    {
        var codes = Enumerable.Range(1, 20).Select(i => $"Calor{i:D4}").ToList();
        var pairs = TelemetryEnricher.AnalyzeCoOccurrence(codes);

        Assert.Equal(10, pairs.Count);
    }

    [Fact]
    public void InputProfile_NoSourceCodeInProperties()
    {
        var source = "fn secret() -> int\n  return 42\n";
        var (telemetry, channel) = CreateTestTelemetry();

        var profile = TelemetryEnricher.AnalyzeInput(source);
        telemetry.TrackInputProfile(profile);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        foreach (var value in evt.Properties.Values)
        {
            Assert.DoesNotContain("secret", value);
            Assert.DoesNotContain("return 42", value);
        }
    }

    [Fact]
    public void TrackInputProfile_EmitsEvent()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var profile = TelemetryEnricher.AnalyzeInput("line1\nline2\n");

        telemetry.TrackInputProfile(profile);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("InputProfile", evt.Name);
        Assert.True(evt.Metrics.ContainsKey("lineCount"));
    }

    [Fact]
    public void TrackDiagnosticEvent_EmitsStructuredEvent()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackDiagnosticEvent("Calor0001", "Error", "Lexer");

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("DiagnosticOccurrence", evt.Name);
        Assert.Equal("Calor0001", evt.Properties["code"]);
        Assert.Equal("Error", evt.Properties["severity"]);
        Assert.Equal("Lexer", evt.Properties["category"]);
    }

    [Fact]
    public void TrackDiagnosticCoOccurrence_EmitsPairs()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        var pairs = new Dictionary<string, int> { ["Calor0001+Calor0002"] = 3 };

        telemetry.TrackDiagnosticCoOccurrence(pairs);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("DiagnosticCoOccurrence", evt.Name);
        Assert.Equal("3", evt.Properties["pair:Calor0001+Calor0002"]);
    }

    #endregion

    #region Phase 3: Session Journey Tests

    [Fact]
    public void SessionStarted_EmitsEvent()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackSessionStarted();

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("SessionStarted", evt.Name);
        Assert.True(evt.Properties.ContainsKey("version"));
    }

    [Fact]
    public void SessionEnded_IncludesDurationMetric()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackSessionStarted();
        telemetry.SetCommand("compile");
        telemetry.TrackSessionEnded();

        var endEvt = Assert.Single(channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "SessionEnded"));
        Assert.True(endEvt.Metrics.ContainsKey("sessionDurationMs"));
        Assert.True(endEvt.Metrics["sessionDurationMs"] >= 0);
        Assert.True(endEvt.Metrics.ContainsKey("commandCount"));
    }

    [Fact]
    public void SessionStarted_EmitsOnlyOnce()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackSessionStarted();
        telemetry.TrackSessionStarted(); // second call should be no-op

        var sessionEvents = channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "SessionStarted").ToList();
        Assert.Single(sessionEvents);
    }

    [Fact]
    public void CommandSequence_TracksNames()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackSessionStarted();
        telemetry.SetCommand("compile");
        telemetry.SetCommand("convert");
        telemetry.TrackSessionEnded();

        var endEvt = Assert.Single(channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "SessionEnded"));
        Assert.True(endEvt.Properties.ContainsKey("commandSequence"));
        Assert.Contains("compile", endEvt.Properties["commandSequence"]);
        Assert.Contains("convert", endEvt.Properties["commandSequence"]);
    }

    #endregion

    #region Phase 4: Converter-Specific Telemetry Tests

    [Fact]
    public void ConversionAttempted_EmitsMetrics()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackConversionAttempted(100, true, 250, 2, 1);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("ConversionAttempted", evt.Name);
        Assert.Equal(100.0, evt.Metrics["inputLines"]);
        Assert.Equal(250.0, evt.Metrics["durationMs"]);
        Assert.Equal(2.0, evt.Metrics["issueCount"]);
        Assert.Equal(1.0, evt.Metrics["unsupportedCount"]);
        Assert.Equal("True", evt.Properties["success"]);
    }

    [Fact]
    public void ConversionGap_EmitsGapName()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackConversionGap("goto", 42);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("ConversionGap", evt.Name);
        Assert.Equal("goto", evt.Properties["gapName"]);
        Assert.Equal("42", evt.Properties["line"]);
    }

    [Fact]
    public void ConversionAttempted_NoSourceCode()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackConversionAttempted(50, false, 100, 3, 2);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        foreach (var value in evt.Properties.Values)
        {
            // Ensure no source code leaks through - just metadata
            Assert.DoesNotContain("class ", value);
            Assert.DoesNotContain("public void", value);
        }
    }

    #endregion

    #region Phase 5: Version Regression Detection Tests

    [Fact]
    public void CompilationOutcome_EmitsHash_NotSource()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackCompilationOutcome("abc123def456", true, 0, 2);

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("CompilationOutcome", evt.Name);
        Assert.Equal("abc123def456", evt.Properties["inputHash"]);
        Assert.Equal("True", evt.Properties["success"]);
        Assert.Equal(0.0, evt.Metrics["errorCount"]);
        Assert.Equal(2.0, evt.Metrics["warningCount"]);
    }

    [Fact]
    public void CompilationDeterminism_SameInput_SameHash()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackCompilationDeterminism("inputhash123456", "outputhash654321");

        var evt = Assert.Single(channel.Items.OfType<EventTelemetry>());
        Assert.Equal("CompilationDeterminism", evt.Name);
        Assert.Equal("inputhash123456", evt.Properties["inputHash"]);
        Assert.Equal("outputhash654321", evt.Properties["outputHash"]);
    }

    #endregion

    #region Integration: Program.Compile Emits Telemetry Events

    private const string ValidCalorSource = @"
§M{m001:Calculator}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}
";

    private const string InvalidCalorSource = @"
§M{m001:Test}
§F{f001:Hello:pub}
  §O{void}
    §INVALID_SYNTAX
§/F{f001}
§/M{m001}
";

    [Fact]
    public void ProgramCompile_EmitsInputProfile()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);
        telemetry.SetCommand("compile");

        Program.Compile(ValidCalorSource, "test.calr");

        var inputProfileEvents = channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "InputProfile").ToList();
        Assert.NotEmpty(inputProfileEvents);
        Assert.True(inputProfileEvents[0].Metrics["lineCount"] > 0);
    }

    [Fact]
    public void ProgramCompile_EmitsCompilationOutcome()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);
        telemetry.SetCommand("compile");

        Program.Compile(ValidCalorSource, "calculator.calr");

        var outcomeEvents = channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "CompilationOutcome").ToList();
        Assert.NotEmpty(outcomeEvents);
        var evt = outcomeEvents[0];
        Assert.True(evt.Properties.ContainsKey("inputHash"));
        Assert.Equal(16, evt.Properties["inputHash"].Length); // 16 hex chars
        // Must not contain source code
        foreach (var value in evt.Properties.Values)
        {
            Assert.DoesNotContain("§M{m001", value);
            Assert.DoesNotContain("§R (+ a b)", value);
        }
    }

    [Fact]
    public void ProgramCompile_EmitsCompilationDeterminism()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);
        telemetry.SetCommand("compile");

        Program.Compile(ValidCalorSource, "calculator.calr");

        var deterministicEvents = channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "CompilationDeterminism").ToList();
        Assert.NotEmpty(deterministicEvents);
        Assert.True(deterministicEvents[0].Properties.ContainsKey("inputHash"));
        Assert.True(deterministicEvents[0].Properties.ContainsKey("outputHash"));
    }

    [Fact]
    public void ProgramCompile_SameSource_ProducesSameHashes()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);
        telemetry.SetCommand("compile");

        // Two compilations of the same source within the same test
        Program.Compile(ValidCalorSource, "calculator.calr");
        Program.Compile(ValidCalorSource, "calculator.calr");

        var deterministicEvents = channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "CompilationDeterminism").ToList();
        Assert.True(deterministicEvents.Count >= 2,
            $"Expected at least 2 CompilationDeterminism events, got {deterministicEvents.Count}");

        // Group by inputHash — our two compilations should share the same hash
        var groups = deterministicEvents.GroupBy(e => e.Properties["inputHash"])
            .Where(g => g.Count() >= 2)
            .ToList();
        Assert.NotEmpty(groups); // At least one inputHash appears 2+ times

        // For that group, all outputHashes should be identical (deterministic)
        var matchingGroup = groups.First();
        var outputHashes = matchingGroup.Select(e => e.Properties["outputHash"]).Distinct().ToList();
        Assert.Single(outputHashes);
    }

    [Fact]
    public void ProgramCompile_WithErrors_EmitsDiagnosticOccurrence()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        using var _ = CalorTelemetry.SetInstanceForTesting(telemetry);
        telemetry.SetCommand("compile");

        Program.Compile(InvalidCalorSource, "test.calr");

        var diagEvents = channel.Items.OfType<EventTelemetry>()
            .Where(e => e.Name == "DiagnosticOccurrence").ToList();
        Assert.NotEmpty(diagEvents);
        // Each event should have code, severity, and category
        foreach (var evt in diagEvents)
        {
            Assert.True(evt.Properties.ContainsKey("code"));
            Assert.True(evt.Properties.ContainsKey("severity"));
            Assert.True(evt.Properties.ContainsKey("category"));
        }
    }

    #endregion

    #region All Track Methods Have Try/Catch (Never Crash)

    [Fact]
    public void TrackInputProfile_NeverThrows()
    {
        var (telemetry, _) = CreateTestTelemetry();
        var profile = TelemetryEnricher.AnalyzeInput("test\n");

        // Should not throw
        var exception = Record.Exception(() => telemetry.TrackInputProfile(profile));
        Assert.Null(exception);
    }

    [Fact]
    public void TrackSessionStarted_NeverThrows()
    {
        var (telemetry, _) = CreateTestTelemetry();

        var exception = Record.Exception(() => telemetry.TrackSessionStarted());
        Assert.Null(exception);
    }

    [Fact]
    public void TrackConversionAttempted_NeverThrows()
    {
        var (telemetry, _) = CreateTestTelemetry();

        var exception = Record.Exception(() => telemetry.TrackConversionAttempted(10, true, 100, 0, 0));
        Assert.Null(exception);
    }

    [Fact]
    public void TrackCompilationOutcome_NeverThrows()
    {
        var (telemetry, _) = CreateTestTelemetry();

        var exception = Record.Exception(() => telemetry.TrackCompilationOutcome("hash", true, 0, 0));
        Assert.Null(exception);
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

    private sealed class StubTelemetryChannel : ITelemetryChannel
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<ITelemetry> _items = new();
        public List<ITelemetry> Items => _items.ToList();
        public bool? DeveloperMode { get; set; } = true;
        public string EndpointAddress { get; set; } = "https://localhost";

        public void Send(ITelemetry item) => _items.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }

    #endregion
}
