using System.CommandLine;
using System.CommandLine.Parsing;
using Calor.Compiler.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// W1 Slice 2 pins (wedge-w1-prereqs.md §4 Slice 2 — the trust surface, #792):
/// telemetry is opt-in (a default invocation sends nothing) and payloads are
/// metadata-only — diagnostic CODES not messages, exception TYPE NAMES not
/// messages/stacks. Joins the TelemetrySingleton collection because Initialize
/// mutates the process-wide singleton.
/// </summary>
[Collection("TelemetrySingleton")]
public class W1Slice2TrustSurfaceTests
{
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

    [Fact]
    public void Telemetry_DefaultInvocation_IsDisabled()
    {
        // #792: the old posture was default-ON with an embedded ingestion key,
        // sending raw diagnostics from every CLI run. A default invocation must
        // now send nothing.
        using var optIn = WithEnv("CALOR_TELEMETRY", null);
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", null);

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: false);

        Assert.False(telemetry.IsEnabled);
    }

    [Fact]
    public void Telemetry_OptInEnvironmentVariable_Enables()
    {
        using var optIn = WithEnv("CALOR_TELEMETRY", "1");
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", null);

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: false);

        Assert.True(telemetry.IsEnabled);
    }

    [Fact]
    public void Telemetry_OptOut_OverridesOptIn()
    {
        using var optIn = WithEnv("CALOR_TELEMETRY", "1");
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", "1");

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: false);

        Assert.False(telemetry.IsEnabled);
    }

    [Fact]
    public void Telemetry_NoTelemetryFlag_OverridesOptIn()
    {
        using var optIn = WithEnv("CALOR_TELEMETRY", "1");
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", null);

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: true);

        Assert.False(telemetry.IsEnabled);
    }

    [Fact]
    public void TrackDiagnostic_SendsCodeOnly_NeverMessageText()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackDiagnostic("Calor0410",
            "Undeclared effect 'fs:w' in function 'WriteSecrets' at /Users/someone/private/file.calr:12",
            SeverityLevel.Error);

        var trace = Assert.IsType<TraceTelemetry>(Assert.Single(channel.Items));
        Assert.DoesNotContain("WriteSecrets", trace.Message);
        Assert.DoesNotContain("/Users/", trace.Message);
        Assert.Contains("Calor0410", trace.Message);
        Assert.Equal("Calor0410", trace.Properties["diagnosticCode"]);
    }

    [Fact]
    public void TrackException_SendsTypeNameOnly_NeverMessageOrStack()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        Exception thrown;
        try
        {
            throw new InvalidOperationException("secret path /Users/someone/private/file.calr");
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        telemetry.TrackException(thrown);

        var item = Assert.Single(channel.Items);
        // Never an ExceptionTelemetry (which carries message + parsed stack).
        var evt = Assert.IsType<EventTelemetry>(item);
        Assert.Equal("System.InvalidOperationException", evt.Properties["exceptionType"]);
        Assert.All(evt.Properties.Values, v => Assert.DoesNotContain("/Users/", v));
    }

    [Fact]
    public async Task FormatWrite_WithoutExperimentalAcknowledgment_IsRefused()
    {
        // T3 containment (kickoff §1.4): the #793 release policy held the
        // formatter write path "disabled" in prose only — the gate makes it
        // code. Read-only modes stay available.
        using var env = WithEnv("CALOR_EXPERIMENTAL_FORMAT_WRITE", null);
        var file = Path.Combine(Path.GetTempPath(), $"calor-w1s2-{Guid.NewGuid():N}.calr");
        await File.WriteAllTextAsync(file, "§M{m001:T}\n");
        try
        {
            var original = await File.ReadAllTextAsync(file);
            var command = Calor.Compiler.Commands.FormatCommand.Create();

            var exit = await command.InvokeAsync(["--write", file]);

            Assert.Equal(1, exit);
            Assert.Equal(original, await File.ReadAllTextAsync(file)); // untouched
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task FormatWrite_WithExperimentalFlag_Proceeds()
    {
        using var env = WithEnv("CALOR_EXPERIMENTAL_FORMAT_WRITE", null);
        var file = Path.Combine(Path.GetTempPath(), $"calor-w1s2-{Guid.NewGuid():N}.calr");
        await File.WriteAllTextAsync(file, "§M{m001:T}\n");
        try
        {
            var command = Calor.Compiler.Commands.FormatCommand.Create();

            var exit = await command.InvokeAsync(["--write", "--experimental", file]);

            Assert.Equal(0, exit);
        }
        finally
        {
            File.Delete(file);
        }
    }

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
}
