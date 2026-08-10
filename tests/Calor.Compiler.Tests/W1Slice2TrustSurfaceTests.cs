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

    /// <summary>
    /// #834 review M4: Initialize replaces the process-wide singleton, and the
    /// opt-in test would otherwise leave a LIVE production-endpoint client
    /// installed for the rest of the test run — any later Track* from any test
    /// could buffer real events to production App Insights. Restore a disabled
    /// singleton on dispose.
    /// </summary>
    private static IDisposable WithDisabledSingletonAfter() => new SingletonReset();

    private sealed class SingletonReset : IDisposable
    {
        public void Dispose() => CalorTelemetry.Initialize(noTelemetryFlag: true);
    }

    [Fact]
    public void Telemetry_DefaultInvocation_IsDisabled()
    {
        // #792: the old posture was default-ON with an embedded ingestion key,
        // sending raw diagnostics from every CLI run. A default invocation must
        // now send nothing.
        using var reset = WithDisabledSingletonAfter();
        using var optIn = WithEnv("CALOR_TELEMETRY", null);
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", null);

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: false);

        Assert.False(telemetry.IsEnabled);
    }

    [Fact]
    public void Telemetry_OptInWithoutEndpointConfiguration_RemainsDisabled()
    {
        using var reset = WithDisabledSingletonAfter();
        using var optIn = WithEnv("CALOR_TELEMETRY", "1");
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", null);

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: false);

        Assert.False(telemetry.IsEnabled);
    }

    [Fact]
    public void Telemetry_OptOut_OverridesOptIn()
    {
        using var reset = WithDisabledSingletonAfter();
        using var optIn = WithEnv("CALOR_TELEMETRY", "1");
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", "1");

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: false);

        Assert.False(telemetry.IsEnabled);
    }

    [Fact]
    public void Telemetry_NoTelemetryFlag_OverridesOptIn()
    {
        using var reset = WithDisabledSingletonAfter();
        using var optIn = WithEnv("CALOR_TELEMETRY", "1");
        using var optOut = WithEnv("CALOR_TELEMETRY_OPTOUT", null);

        var telemetry = CalorTelemetry.Initialize(noTelemetryFlag: true);

        Assert.False(telemetry.IsEnabled);
    }

    [Fact]
    public void TrackDiagnosticEvent_SendsStructuredCodeOnly()
    {
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackDiagnosticEvent("Calor0410", "Error", "Effect");

        var evt = Assert.IsType<EventTelemetry>(Assert.Single(channel.Items));
        Assert.Equal("DiagnosticOccurrence", evt.Name);
        Assert.Equal("Calor0410", evt.Properties["code"]);
        Assert.All(evt.Properties.Values, value =>
        {
            Assert.DoesNotContain("WriteSecrets", value);
            Assert.DoesNotContain("/Users/", value);
        });
    }

    [Fact]
    public void TrackException_SendsCategoryOnly_NeverTypeMessageOrStack()
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
        Assert.Equal("invalid-operation", evt.Properties["exceptionCategory"]);
        Assert.DoesNotContain(evt.Properties.Values,
            value => value.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
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

    [Fact]
    public async Task LintFix_WithoutExperimentalAcknowledgment_IsRefused()
    {
        // #834 review C1: `lint --fix` writes through the SAME CalorFormatter
        // machinery `format --write` gates — an ungated --fix was a one-command
        // bypass of the #793 containment.
        using var env = WithEnv("CALOR_EXPERIMENTAL_FORMAT_WRITE", null);
        var file = Path.Combine(Path.GetTempPath(), $"calor-w1s2-{Guid.NewGuid():N}.calr");
        await File.WriteAllTextAsync(file, "§M{m001:T}\n");
        try
        {
            var original = await File.ReadAllTextAsync(file);
            var command = Calor.Compiler.Commands.LintCommand.Create();

            var exit = await command.InvokeAsync(["--fix", file]);

            Assert.Equal(1, exit);
            Assert.Equal(original, await File.ReadAllTextAsync(file)); // untouched
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task LintFix_WithExperimentalFlag_Proceeds()
    {
        using var env = WithEnv("CALOR_EXPERIMENTAL_FORMAT_WRITE", null);
        var file = Path.Combine(Path.GetTempPath(), $"calor-w1s2-{Guid.NewGuid():N}.calr");
        await File.WriteAllTextAsync(file, "§M{m001:T}\n");
        try
        {
            var command = Calor.Compiler.Commands.LintCommand.Create();

            var exit = await command.InvokeAsync(["--fix", "--experimental", file]);

            Assert.Equal(0, exit);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TelemetryPayload_SerializedBytes_CarryNoHostname()
    {
        // #834 review M1 + verification round: the SDK stamps the machine
        // hostname into cloud.roleInstance AND — once that is pinned — into
        // ai.internal.nodeName. An object-level assertion passed while the
        // wire payload still leaked, so this pin works at SERIALIZATION level:
        // the exact bytes the channel would transmit must not contain the
        // machine's name in any tag.
        var channel = new StubTelemetryChannel();
        var config = new TelemetryConfiguration
        {
            TelemetryChannel = channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        config.TelemetryInitializers.Add(new AnonymizingTelemetryInitializer());
        var client = new TelemetryClient(config);

        client.TrackEvent("probe");

        var item = Assert.Single(channel.Items);
        var json = System.Text.Encoding.UTF8.GetString(
            Microsoft.ApplicationInsights.Extensibility.Implementation.JsonSerializer.Serialize(
                new[] { item }, compress: false));

        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(System.Net.Dns.GetHostName(), json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("calor-cli", json);
    }

    [Fact]
    public void CompilationOutcome_SendsNoContentHashes()
    {
        // #834 review M2: input/output SHA hashes are derived from file content
        // and enable exact-file identification — retired to a no-op.
        var (telemetry, channel) = CreateTestTelemetry();

        telemetry.TrackCompilationOutcome(success: true, errorCount: 0, warningCount: 0);

        Assert.DoesNotContain(channel.Items, i =>
            i is EventTelemetry e && (e.Properties.ContainsKey("inputHash") || e.Properties.ContainsKey("outputHash")));
        Assert.DoesNotContain(channel.Items, i => i is EventTelemetry e && e.Name == "CompilationDeterminism");
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
