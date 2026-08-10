using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Xunit;

namespace Calor.Compiler.Tests;

[Collection("TelemetrySingleton")]
public sealed class TelemetryPrivacyTests
{
    private const string Canary = "CALOR_SECRET_CANARY_792";

    [Fact]
    public void OptInRequiresValidExternalConnectionString()
    {
        using var optIn = WithEnv("CALOR_TELEMETRY", "1");
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", null);
        using var absent = WithEnv(CalorTelemetry.ConnectionStringEnvironmentVariable, null);

        Assert.False(CalorTelemetry.Initialize(noTelemetryFlag: false).IsEnabled);

        using var invalid = WithEnv(
            CalorTelemetry.ConnectionStringEnvironmentVariable,
            "InstrumentationKey=not-a-guid;IngestionEndpoint=http://insecure.example");
        Assert.False(CalorTelemetry.Initialize(noTelemetryFlag: false).IsEnabled);

        using var valid = WithEnv(
            CalorTelemetry.ConnectionStringEnvironmentVariable,
            "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=https://localhost/");
        Assert.True(CalorTelemetry.Initialize(noTelemetryFlag: false).IsEnabled);

        CalorTelemetry.Initialize(noTelemetryFlag: true);
    }

    [Fact]
    public void PreviewProducesSanitizedPayloadAndNeverEnablesNetwork()
    {
        var writer = new StringWriter();
        var telemetry = new CalorTelemetry(writer);
        telemetry.SetCommand("compile");
        telemetry.TrackDiagnosticEvent("Calor0410", "Error", "Effect");

        Assert.False(telemetry.IsEnabled);
        Assert.True(telemetry.IsPreview);
        var json = writer.ToString();
        Assert.DoesNotContain(Canary, json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(TelemetrySchema.Version,
            document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("DiagnosticOccurrence",
            document.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("Calor0410",
            document.RootElement.GetProperty("properties").GetProperty("code").GetString());
    }

    [Fact]
    public void InvalidEventsAreDroppedWithoutDisablingLaterValidTelemetry()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        telemetry.SetCommand("compile");

        telemetry.TrackCommand("compile", 0, new Dictionary<string, string>
        {
            ["arguments"] = $"--input /Users/{Canary}/secret.calr"
        });
        telemetry.TrackEvent("CompileOptions", new Dictionary<string, string>
        {
            ["strictApi"] = Canary
        });
        telemetry.TrackEvent($"event-{Canary}", new Dictionary<string, string>());

        Assert.Empty(channel.Items);
        Assert.True(telemetry.IsEnabled);

        telemetry.TrackCommand("compile", 0);

        Assert.Single(channel.Items);
        Assert.True(telemetry.IsEnabled);
    }

    [Fact]
    public void ProducerRegistriesAreAcceptedBySchema()
    {
        var diagnosticCodes = new[]
        {
            "Calor0001", "Calor0100", "Calor0200", "Calor0300", "Calor0400",
            "Calor0500", "Calor0600", "Calor0700", "Calor0800", "Calor0900",
            "Calor1000", "Calor1100", "Calor1200", "Calor1300", "Calor1400",
            "invalid"
        };
        Assert.All(diagnosticCodes, code =>
        {
            var category = TelemetrySchema.GetDiagnosticCategory(code);
            var (telemetry, channel) = CreateTestTelemetry();
            telemetry.SetCommand("compile");
            telemetry.TrackDiagnosticEvent(
                code == "invalid" ? "Calor9999" : code,
                "Error",
                category);
            Assert.Single(channel.Items);
            Assert.True(telemetry.IsEnabled);
        });

        Assert.All(
            HelpTool.TelemetryCategories,
            category => Assert.True(TelemetrySchema.IsKnownHelpCategory(category)));
        Assert.All(
            Enum.GetNames<Architecture>(),
            architecture => Assert.True(TelemetrySchema.IsKnownArchitecture(architecture)));

        foreach (var feature in new[]
                 {
                     "unsupported-member",
                     "preprocessor-disabled",
                     "post-validation-fallback"
                 })
        {
            var (telemetry, channel) = CreateTestTelemetry();
            telemetry.SetCommand("convert");
            telemetry.TrackConversionGap(feature, 1);
            Assert.Single(channel.Items);
            Assert.True(telemetry.IsEnabled);
        }
    }

    [Fact]
    public void UnknownConversionFeaturesDropOnlyPerFeatureEvent()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        telemetry.SetCommand("convert");

        telemetry.TrackUnsupportedFeatures(
            new Dictionary<string, int>
            {
                ["namespace-collision"] = 1,
                ["goto"] = 2
            },
            totalCount: 3);
        telemetry.TrackCommand("convert", 0);

        Assert.True(telemetry.IsEnabled);
        Assert.Contains(
            channel.Items.OfType<EventTelemetry>(),
            item => item.Name == "UnsupportedFeatures");
        Assert.Contains(
            channel.Items.OfType<EventTelemetry>(),
            item => item.Name == "UnsupportedFeature"
                    && item.Properties["feature"] == "goto");
        Assert.DoesNotContain(
            channel.Items.OfType<EventTelemetry>(),
            item => item.Properties.TryGetValue("feature", out var feature)
                    && feature == "namespace-collision");
        Assert.Contains(
            channel.Items.OfType<EventTelemetry>(),
            item => item.Name == "CommandSucceeded");
    }

    [Fact]
    public void SerializedPayloadsContainNoSourceIdentifiersPathsArgumentsDiagnosticsOrExceptions()
    {
        var (telemetry, channel) = CreateTestTelemetry(anonymize: true);
        telemetry.SetCommand("compile");

        var source =
            $"§M{{module_{Canary}:Secret}}\n" +
            $"§B{{identifier_{Canary}:str}} STR:\"{Canary}\"\n";
        telemetry.TrackInputProfile(TelemetryEnricher.AnalyzeInput(source));
        using (CalorTelemetry.SetInstanceForTesting(telemetry))
        {
            Program.Compile(source + "\n§INVALID", $"/Users/{Canary}/secret.calr");
        }
        telemetry.TrackException(new CanarySecretException(
            $"exception {Canary} /Users/{Canary}/secret.calr"));
        telemetry.TrackSyntaxHelpQuery(
            $"query {Canary} --input /Users/{Canary}/secret.calr",
            null,
            0,
            null);
        telemetry.TrackCommand("compile", 1);
        telemetry.SetAgents(Canary);

        var bytes = Microsoft.ApplicationInsights.Extensibility.Implementation.JsonSerializer.Serialize(
            channel.Items, compress: false);
        var json = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain(Canary, json, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", json, StringComparison.Ordinal);
        Assert.DoesNotContain("identifier_", json, StringComparison.Ordinal);
        Assert.DoesNotContain("STR:", json, StringComparison.Ordinal);
        Assert.DoesNotContain("--input", json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(CanarySecretException), json, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryApplicationFieldIsSchemaApproved()
    {
        var (telemetry, channel) = CreateTestTelemetry();
        telemetry.SetCommand("compile");
        telemetry.TrackSessionStarted();
        telemetry.TrackPhase("Lexer", 12, true,
            new Dictionary<string, string> { ["tokenCount"] = "4" });
        telemetry.TrackDiagnosticEvent("Calor0001", "Error", "Lexer");
        telemetry.TrackCompilationOutcome(true, 0, 1);
        telemetry.TrackCommand("compile", 0);
        telemetry.TrackSessionEnded();

        foreach (var item in channel.Items.OfType<EventTelemetry>())
        {
            var definition = TelemetrySchema.Events[item.Name];
            Assert.All(item.Properties.Keys.Where(k => k != "DeveloperMode"),
                key => Assert.True(
                    definition.PropertyFields.Contains(key)
                    || TelemetrySchema.GlobalContextFields.Contains(key),
                    $"Unlisted property '{key}' on event '{item.Name}'"));
            Assert.All(item.Metrics.Keys,
                key => Assert.Contains(key, definition.MetricFields));
        }
    }

    [Fact]
    public void ChannelFailureNeverAffectsCompilerAndDisablesFurtherTelemetry()
    {
        var channel = new ThrowingTelemetryChannel();
        var configuration = new TelemetryConfiguration
        {
            TelemetryChannel = channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        var telemetry = new CalorTelemetry(new TelemetryClient(configuration));

        var exception = Record.Exception(() => telemetry.TrackCommand("compile", 0));

        Assert.Null(exception);
        Assert.False(telemetry.IsEnabled);
        Assert.Equal(1, channel.SendAttempts);
        telemetry.TrackCommand("compile", 0);
        Assert.Equal(1, channel.SendAttempts);
    }

    [Fact]
    public void SchemaSnapshotMatchesCommittedPublicSchema()
    {
        var root = CliTestHarness.FindRepoRoot();
        var path = Path.Combine(root, "docs", "telemetry-schema-v1.json");
        var committed = File.ReadAllText(path).ReplaceLineEndings("\n");
        var actual = TelemetrySchema.ExportSnapshot().ReplaceLineEndings("\n");

        Assert.Equal(committed, actual);
    }

    [Fact]
    public void SchemaVersion1RetainsCompatibilityBaseline()
    {
        var expectedEvents = new[]
        {
            "CommandSucceeded", "CommandFailed", "CompilationPhase",
            "DiagnosticOccurrence", "DiagnosticCoOccurrence", "Exception",
            "CompileOptions", "UnsupportedFeatures", "UnsupportedFeature",
            "InputProfile", "SessionStarted", "SessionEnded",
            "ConversionAttempted", "ConversionGap", "SyntaxHelpQuery",
            "CompilationOutcome", "HookAllow", "HookBlock"
        };

        Assert.Equal("1.0", TelemetrySchema.Version);
        Assert.All(expectedEvents,
            eventName => Assert.True(TelemetrySchema.Events.ContainsKey(eventName)));
        Assert.Equal(
            new[]
            {
                "architecture", "calorVersion", "codingAgent", "dotnetVersion",
                "operationId", "os", "schemaVersion", "semanticsVersion"
            },
            TelemetrySchema.GlobalContextFields.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "category", "code", "command", "severity" },
            TelemetrySchema.Events["DiagnosticOccurrence"].PropertyFields
                .OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "command", "exceptionCategory", "phase" },
            TelemetrySchema.Events["Exception"].PropertyFields
                .OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ProductionTelemetrySourceContainsNoEmbeddedConnectionString()
    {
        var root = CliTestHarness.FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "Calor.Compiler", "Telemetry", "CalorTelemetry.cs"));

        Assert.DoesNotContain("InstrumentationKey=", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".applicationinsights.azure.com", source, StringComparison.Ordinal);
        Assert.Contains("CALOR_TELEMETRY_CONNECTION_STRING", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicCliPreviewWorksAfterSubcommandAndDoesNotExposeArguments()
    {
        var canary = $"{Canary}_ARGUMENT";
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = CliTestHarness.FindRepoRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["CALOR_TELEMETRY"] = "1";
        startInfo.Environment.Remove(CalorTelemetry.ConnectionStringEnvironmentVariable);
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add("feature-check");
        startInfo.ArgumentList.Add("--feature");
        startInfo.ArgumentList.Add(canary);
        startInfo.ArgumentList.Add("--telemetry-preview");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start calor CLI.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000));

        var payloadLines = stderr.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith('{'))
            .ToArray();
        Assert.NotEmpty(payloadLines);
        Assert.All(payloadLines, line =>
        {
            Assert.DoesNotContain(canary, line, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(line);
            Assert.Equal(TelemetrySchema.Version,
                document.RootElement.GetProperty("schemaVersion").GetString());
        });
        Assert.NotNull(stdout);
    }

    [Fact]
    public void ColonDelimitedOptOutOverridesPreview()
    {
        var startInfo = CreateCliStartInfo(
            "feature-check",
            "--list",
            "--no-telemetry:true",
            "--telemetry-preview");
        startInfo.Environment["CALOR_TELEMETRY"] = "1";
        startInfo.Environment["CALOR_TELEMETRY_CONNECTION_STRING"] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000001;" +
            "IngestionEndpoint=https://localhost/";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start calor CLI.");
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000));

        Assert.DoesNotContain("\"eventName\"", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ColonDelimitedPreviewUsesLocalSink()
    {
        var startInfo = CreateCliStartInfo(
            "feature-check",
            "--list",
            "--telemetry-preview:true");
        startInfo.Environment["CALOR_TELEMETRY"] = "1";
        startInfo.Environment.Remove(CalorTelemetry.ConnectionStringEnvironmentVariable);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start calor CLI.");
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000));

        Assert.Contains("\"eventName\"", stderr, StringComparison.Ordinal);
    }

    private static ProcessStartInfo CreateCliStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = CliTestHarness.FindRepoRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static (CalorTelemetry Telemetry, CapturingTelemetryChannel Channel)
        CreateTestTelemetry(bool anonymize = false)
    {
        var channel = new CapturingTelemetryChannel();
        var configuration = new TelemetryConfiguration
        {
            TelemetryChannel = channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        if (anonymize)
        {
            configuration.TelemetryInitializers.Add(new AnonymizingTelemetryInitializer());
        }
        return (new CalorTelemetry(new TelemetryClient(configuration)), channel);
    }

    private static IDisposable WithEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestore(name, previous);
    }

    private sealed class EnvRestore(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    private sealed class CanarySecretException(string message) : Exception(message);

    private sealed class CapturingTelemetryChannel : ITelemetryChannel
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<ITelemetry> _items = [];
        public IReadOnlyCollection<ITelemetry> Items => _items.ToArray();
        public bool? DeveloperMode { get; set; } = false;
        public string EndpointAddress { get; set; } = "https://localhost";
        public void Send(ITelemetry item) => _items.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }

    private sealed class ThrowingTelemetryChannel : ITelemetryChannel
    {
        public int SendAttempts { get; private set; }
        public bool? DeveloperMode { get; set; } = false;
        public string EndpointAddress { get; set; } = "https://localhost";
        public void Send(ITelemetry item)
        {
            SendAttempts++;
            throw new InvalidOperationException("offline endpoint");
        }
        public void Flush() { }
        public void Dispose() { }
    }
}
