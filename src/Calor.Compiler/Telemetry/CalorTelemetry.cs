using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Calor.Compiler.Telemetry;

/// <summary>
/// Anonymous telemetry service for the Calor CLI.
/// Tracks command usage, compilation performance, and failures.
/// Opt-out via --no-telemetry flag or CALOR_TELEMETRY_OPTOUT=1 environment variable.
/// </summary>
public sealed class CalorTelemetry : IDisposable
{
    private const string ConnectionString =
        "InstrumentationKey=2d27d2aa-0260-4c57-a193-26fd2c8ae17d;" +
        "IngestionEndpoint=https://eastus-8.in.applicationinsights.azure.com/;" +
        "LiveEndpoint=https://eastus.livediagnostics.monitor.azure.com/;" +
        "ApplicationId=d4520bf2-af61-4e59-a022-bd13bf5a0886";

    private static CalorTelemetry? _instance;
    private readonly TelemetryClient? _client;
    private readonly string _operationId;
    private readonly Stopwatch _sessionTimer;
    private string? _currentCommand;
    private readonly Dictionary<string, string> _commandProperties = new();
    private readonly DateTime _sessionStartTime = DateTime.UtcNow;
    private readonly List<string> _commandSequence = new();
    private bool _sessionStarted;
    private bool _disposed;

    /// <summary>
    /// Whether telemetry is enabled for this session.
    /// </summary>
    public bool IsEnabled => _client != null;

    /// <summary>
    /// Unique operation ID for this CLI invocation. Used for log correlation and issue reporting.
    /// </summary>
    public string OperationId => _operationId;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static CalorTelemetry Instance => _instance ?? throw new InvalidOperationException("Telemetry not initialized. Call Initialize() first.");

    /// <summary>
    /// Whether telemetry has been initialized.
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Sets the singleton instance for testing. Restores previous instance via returned disposable.
    /// </summary>
    internal static IDisposable SetInstanceForTesting(CalorTelemetry instance)
    {
        var previous = _instance;
        _instance = instance;
        return new InstanceRestorer(previous);
    }

    private sealed class InstanceRestorer(CalorTelemetry? previous) : IDisposable
    {
        public void Dispose() => _instance = previous;
    }

    /// <summary>
    /// Internal constructor for unit testing with a custom TelemetryClient.
    /// </summary>
    internal CalorTelemetry(TelemetryClient client)
    {
        _operationId = Guid.NewGuid().ToString("N")[..12];
        _sessionTimer = Stopwatch.StartNew();
        _client = client;
    }

    private CalorTelemetry(bool enabled)
    {
        _operationId = Guid.NewGuid().ToString("N")[..12];
        _sessionTimer = Stopwatch.StartNew();

        if (!enabled)
        {
            _client = null;
            return;
        }

        try
        {
            var config = TelemetryConfiguration.CreateDefault();
            config.ConnectionString = ConnectionString;
            _client = new TelemetryClient(config);

            // Set anonymous context
            _client.Context.Session.Id = _operationId;
            _client.Context.Component.Version = GetCalorVersion();
            _client.Context.Device.OperatingSystem = RuntimeInformation.OSDescription;

            // Set global properties
            _client.Context.GlobalProperties["os"] = GetOsPlatform();
            _client.Context.GlobalProperties["arch"] = RuntimeInformation.ProcessArchitecture.ToString();
            _client.Context.GlobalProperties["dotnet"] = Environment.Version.ToString();
            _client.Context.GlobalProperties["calorVersion"] = GetCalorVersion();
            _client.Context.GlobalProperties["semanticsVersion"] = SemanticsVersion.VersionString;
            _client.Context.GlobalProperties["operationId"] = _operationId;
        }
        catch
        {
            // Telemetry must never crash the CLI
            _client = null;
        }
    }

    /// <summary>
    /// Initializes telemetry. Call once at startup.
    /// </summary>
    public static CalorTelemetry Initialize(bool noTelemetryFlag)
    {
        var optOut = noTelemetryFlag
            || Environment.GetEnvironmentVariable("CALOR_TELEMETRY_OPTOUT") == "1"
            || Environment.GetEnvironmentVariable("CALOR_TELEMETRY_OPTOUT") == "true";

        _instance = new CalorTelemetry(!optOut);
        return _instance;
    }

    /// <summary>
    /// Sets the current command being executed.
    /// </summary>
    public void SetCommand(string command, Dictionary<string, string>? properties = null)
    {
        _currentCommand = command;
        _commandSequence.Add(command);
        _commandProperties.Clear();
        _commandProperties["command"] = command;
        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                _commandProperties[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Tracks a command invocation event.
    /// </summary>
    public void TrackCommand(string command, int exitCode, Dictionary<string, string>? properties = null)
    {
        if (_client == null) return;

        try
        {
            var eventName = exitCode == 0 ? "CommandSucceeded" : "CommandFailed";
            var telemetry = new EventTelemetry(eventName);
            telemetry.Properties["command"] = command;
            telemetry.Properties["exitCode"] = exitCode.ToString();
            telemetry.Properties["durationMs"] = _sessionTimer.ElapsedMilliseconds.ToString();
            telemetry.Metrics["durationMs"] = _sessionTimer.ElapsedMilliseconds;

            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    telemetry.Properties[kvp.Key] = kvp.Value;
                }
            }

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks a compilation phase with timing.
    /// </summary>
    public void TrackPhase(string phase, long durationMs, bool success, Dictionary<string, string>? properties = null)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new DependencyTelemetry
            {
                Name = phase,
                Type = "CompilationPhase",
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Success = success,
                Data = _currentCommand ?? "compile"
            };

            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    telemetry.Properties[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in _commandProperties)
            {
                telemetry.Properties[kvp.Key] = kvp.Value;
            }

            _client.TrackDependency(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks a diagnostic message (error/warning from compilation).
    /// </summary>
    public void TrackDiagnostic(string code, string message, SeverityLevel severity)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new TraceTelemetry($"[{code}] {message}", severity);
            telemetry.Properties["diagnosticCode"] = code;

            foreach (var kvp in _commandProperties)
            {
                telemetry.Properties[kvp.Key] = kvp.Value;
            }

            _client.TrackTrace(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks an exception with full context.
    /// </summary>
    public void TrackException(Exception exception, Dictionary<string, string>? properties = null)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new ExceptionTelemetry(exception);

            foreach (var kvp in _commandProperties)
            {
                telemetry.Properties[kvp.Key] = kvp.Value;
            }

            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    telemetry.Properties[kvp.Key] = kvp.Value;
                }
            }

            _client.TrackException(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks a custom event.
    /// </summary>
    public void TrackEvent(string name, Dictionary<string, string>? properties = null)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new EventTelemetry(name);

            foreach (var kvp in _commandProperties)
            {
                telemetry.Properties[kvp.Key] = kvp.Value;
            }

            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    telemetry.Properties[kvp.Key] = kvp.Value;
                }
            }

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks unsupported C# features encountered during conversion.
    /// Sends one event per conversion with feature names and counts (no user source code).
    /// </summary>
    public void TrackUnsupportedFeatures(Dictionary<string, int> featureCounts, int totalCount)
    {
        if (_client == null || totalCount == 0) return;

        try
        {
            var telemetry = new EventTelemetry("UnsupportedFeatures");
            telemetry.Properties["totalUnsupportedCount"] = totalCount.ToString();
            telemetry.Properties["distinctFeatureCount"] = featureCounts.Count.ToString();
            telemetry.Metrics["totalUnsupportedCount"] = totalCount;
            telemetry.Metrics["distinctFeatureCount"] = featureCounts.Count;

            var i = 0;
            foreach (var (feature, count) in featureCounts.OrderByDescending(kv => kv.Value))
            {
                if (i >= 50) break;
                telemetry.Properties[$"feature:{feature}"] = count.ToString();
                i++;
            }

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    // ── Phase 2: Enriched Event Schema ──────────────────────────────────

    /// <summary>
    /// Tracks an input profile (metadata-only, no source code).
    /// </summary>
    public void TrackInputProfile(InputProfile profile)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new EventTelemetry("InputProfile");
            telemetry.Metrics["lineCount"] = profile.LineCount;
            telemetry.Metrics["estimatedTokenCount"] = profile.EstimatedTokenCount;
            telemetry.Properties["hasContracts"] = profile.HasContracts.ToString();
            telemetry.Properties["hasEffects"] = profile.HasEffects.ToString();
            telemetry.Properties["hasModules"] = profile.HasModules.ToString();
            telemetry.Properties["sizeCategory"] = profile.SizeCategory;

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks a structured diagnostic occurrence event.
    /// </summary>
    public void TrackDiagnosticEvent(string code, string severity, string category)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new EventTelemetry("DiagnosticOccurrence");
            telemetry.Properties["code"] = code;
            telemetry.Properties["severity"] = severity;
            telemetry.Properties["category"] = category;

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks diagnostic co-occurrence pairs.
    /// </summary>
    public void TrackDiagnosticCoOccurrence(Dictionary<string, int> codePairs)
    {
        if (_client == null || codePairs.Count == 0) return;

        try
        {
            var telemetry = new EventTelemetry("DiagnosticCoOccurrence");

            foreach (var (pair, count) in codePairs)
            {
                telemetry.Properties[$"pair:{pair}"] = count.ToString();
            }

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    // ── Phase 3: Session Journey Tracking ─────────────────────────────

    /// <summary>
    /// Tracks the start of a CLI session.
    /// </summary>
    public void TrackSessionStarted()
    {
        if (_client == null || _sessionStarted) return;
        _sessionStarted = true;

        try
        {
            var telemetry = new EventTelemetry("SessionStarted");
            telemetry.Properties["version"] = GetCalorVersion();
            telemetry.Properties["timestamp"] = _sessionStartTime.ToString("O");

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks the end of a CLI session with duration and command sequence.
    /// </summary>
    public void TrackSessionEnded()
    {
        if (_client == null) return;

        try
        {
            var sessionDuration = (DateTime.UtcNow - _sessionStartTime).TotalMilliseconds;
            var telemetry = new EventTelemetry("SessionEnded");
            telemetry.Metrics["sessionDurationMs"] = sessionDuration;
            telemetry.Metrics["commandCount"] = _commandSequence.Count;
            telemetry.Properties["commandSequence"] = string.Join(",", _commandSequence);

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    // ── Phase 4: Converter-Specific Telemetry ─────────────────────────

    /// <summary>
    /// Tracks a conversion attempt with metrics (no source code).
    /// </summary>
    public void TrackConversionAttempted(int inputLines, bool success, long durationMs, int issueCount, int unsupportedCount)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new EventTelemetry("ConversionAttempted");
            telemetry.Properties["success"] = success.ToString();
            telemetry.Metrics["inputLines"] = inputLines;
            telemetry.Metrics["durationMs"] = durationMs;
            telemetry.Metrics["issueCount"] = issueCount;
            telemetry.Metrics["unsupportedCount"] = unsupportedCount;

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks a specific conversion gap (unsupported feature).
    /// </summary>
    public void TrackConversionGap(string gapName, int? line)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new EventTelemetry("ConversionGap");
            telemetry.Properties["gapName"] = gapName;
            if (line.HasValue)
                telemetry.Properties["line"] = line.Value.ToString();

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    // ── Phase 5: Version Regression Detection ─────────────────────────

    /// <summary>
    /// Tracks a compilation outcome with input hash for regression detection.
    /// </summary>
    public void TrackCompilationOutcome(string inputHash, bool success, int errorCount, int warningCount)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new EventTelemetry("CompilationOutcome");
            telemetry.Properties["inputHash"] = inputHash;
            telemetry.Properties["success"] = success.ToString();
            telemetry.Properties["version"] = GetCalorVersion();
            telemetry.Metrics["errorCount"] = errorCount;
            telemetry.Metrics["warningCount"] = warningCount;

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    /// <summary>
    /// Tracks compilation determinism (same input should produce same output).
    /// </summary>
    public void TrackCompilationDeterminism(string inputHash, string outputHash)
    {
        if (_client == null) return;

        try
        {
            var telemetry = new EventTelemetry("CompilationDeterminism");
            telemetry.Properties["inputHash"] = inputHash;
            telemetry.Properties["outputHash"] = outputHash;
            telemetry.Properties["version"] = GetCalorVersion();

            foreach (var kvp in _commandProperties)
                telemetry.Properties[kvp.Key] = kvp.Value;

            _client.TrackEvent(telemetry);
        }
        catch
        {
            // Never crash the CLI
        }
    }

    // ── Flush & Dispose ───────────────────────────────────────────────

    /// <summary>
    /// Flushes all pending telemetry. Call before process exit.
    /// </summary>
    public void Flush()
    {
        if (_client == null) return;

        try
        {
            _client.Flush();
            // Give the channel a moment to send
            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Never crash the CLI
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
    }

    private static string GetCalorVersion()
    {
        try
        {
            return Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string GetOsPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "other";
    }

    /// <summary>
    /// Sets the coding agent(s) from .calor/config.json discovery.
    /// Call after determining the working directory for the current command.
    /// </summary>
    public void SetAgents(string agents)
    {
        if (_client == null) return;
        _client.Context.GlobalProperties["codingAgent"] = agents;
    }
}
