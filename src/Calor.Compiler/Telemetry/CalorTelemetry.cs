using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;

namespace Calor.Compiler.Telemetry;

public sealed class AnonymizingTelemetryInitializer : ITelemetryInitializer
{
    public void Initialize(Microsoft.ApplicationInsights.Channel.ITelemetry telemetry)
    {
        telemetry.Context.Cloud.RoleInstance = "calor-cli";
        telemetry.Context.Cloud.RoleName = "calor-cli";
        telemetry.Context.GetInternalContext().NodeName = "calor-cli";
        telemetry.Context.User.Id = null;
        telemetry.Context.User.AuthenticatedUserId = null;
        telemetry.Context.User.AccountId = null;
        telemetry.Context.Device.Id = null;
        telemetry.Context.Device.Model = null;
        telemetry.Context.Device.OemName = null;
        telemetry.Context.Device.Type = null;
        telemetry.Context.Device.OperatingSystem = null;
        telemetry.Context.Location.Ip = null;
    }
}

/// <summary>
/// Opt-in, schema-enforced anonymous telemetry for public Calor surfaces.
/// </summary>
public sealed class CalorTelemetry : IDisposable
{
    internal const string ConnectionStringEnvironmentVariable =
        "CALOR_TELEMETRY_CONNECTION_STRING";

    private static CalorTelemetry? _instance;
    private readonly TelemetryClient? _client;
    private readonly TextWriter? _previewWriter;
    private readonly object _previewLock = new();
    private readonly string _operationId = Guid.NewGuid().ToString("N")[..12];
    private readonly Stopwatch _sessionTimer = Stopwatch.StartNew();
    private readonly DateTime _sessionStartTime = DateTime.UtcNow;
    private readonly List<string> _commandSequence = [];
    private readonly Dictionary<string, string> _context;
    private string? _currentCommand;
    private bool _commandContextValid = true;
    private bool _sessionStarted;
    private bool _failed;
    private bool _disposed;

    public bool IsEnabled => _client != null && !_failed;
    public bool IsPreview => _previewWriter != null && !_failed;
    public string OperationId => _operationId;
    public static CalorTelemetry Instance =>
        _instance ?? throw new InvalidOperationException(
            "Telemetry not initialized. Call Initialize() first.");
    public static bool IsInitialized => _instance != null;

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

    internal CalorTelemetry(TelemetryClient client)
        : this(client, previewWriter: null)
    {
    }

    internal CalorTelemetry(TextWriter previewWriter)
        : this(client: null, previewWriter)
    {
    }

    private CalorTelemetry(TelemetryClient? client, TextWriter? previewWriter)
    {
        _client = client;
        _previewWriter = previewWriter;
        _context = CreateContext();
        if (!TelemetrySchema.TryCreatePayload(
                "SessionStarted", null, null, _context, out _))
        {
            _failed = true;
        }
        ApplyContext(client);
    }

    private CalorTelemetry()
        : this(client: null, previewWriter: null)
    {
    }

    public static CalorTelemetry Initialize(
        bool noTelemetryFlag,
        bool telemetryPreview = false,
        TextWriter? previewWriter = null)
    {
        var optIn = IsTrue(Environment.GetEnvironmentVariable("CALOR_TELEMETRY"));
        var optOut = noTelemetryFlag
            || IsTrue(Environment.GetEnvironmentVariable("CALOR_TELEMETRY_OPTOUT"));

        if (optOut)
        {
            return _instance = new CalorTelemetry();
        }

        if (telemetryPreview)
        {
            return _instance = new CalorTelemetry(previewWriter ?? Console.Error);
        }

        if (!optIn
            || !TryValidateConnectionString(
                Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable),
                out var connectionString))
        {
            return _instance = new CalorTelemetry();
        }

        try
        {
            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.ConnectionString = connectionString;
            configuration.TelemetryInitializers.Add(new AnonymizingTelemetryInitializer());
            return _instance = new CalorTelemetry(new TelemetryClient(configuration), null);
        }
        catch
        {
            return _instance = new CalorTelemetry();
        }
    }

    public void SetCommand(string command, Dictionary<string, string>? properties = null)
    {
        _commandContextValid = properties is null or { Count: 0 };
        _currentCommand = _commandContextValid && TelemetrySchema.IsKnownCommand(command)
            ? command
            : null;
        if (_currentCommand != null)
        {
            _commandSequence.Add(_currentCommand);
        }
    }

    public void TrackCommand(
        string command,
        int exitCode,
        Dictionary<string, string>? properties = null)
    {
        var eventName = exitCode == 0 ? "CommandSucceeded" : "CommandFailed";
        var eventProperties = new Dictionary<string, string>
        {
            ["command"] = command,
            ["exitCode"] = exitCode.ToString(CultureInfo.InvariantCulture)
        };
        var metrics = new Dictionary<string, double>
        {
            ["durationMs"] = _sessionTimer.ElapsedMilliseconds
        };

        if (!TryAddCallerFields(properties, eventProperties, metrics))
        {
            return;
        }

        Emit(eventName, eventProperties, metrics);
    }

    public void TrackPhase(
        string phase,
        long durationMs,
        bool success,
        Dictionary<string, string>? properties = null)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["phase"] = phase;
        eventProperties["success"] = success.ToString();
        var metrics = new Dictionary<string, double>
        {
            ["durationMs"] = durationMs
        };
        if (!TryAddCallerFields(properties, eventProperties, metrics))
        {
            return;
        }
        Emit("CompilationPhase", eventProperties, metrics);
    }

    public void TrackException(
        Exception exception,
        Dictionary<string, string>? properties = null)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["exceptionCategory"] = CategorizeException(exception);
        if (properties != null)
        {
            foreach (var (name, value) in properties)
            {
                if (name != "phase")
                {
                    return;
                }
                eventProperties[name] = value;
            }
        }
        Emit("Exception", eventProperties, null);
    }

    public void TrackEvent(string name, Dictionary<string, string>? properties = null)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        var metrics = new Dictionary<string, double>();
        if (!TryAddCallerFields(properties, eventProperties, metrics))
        {
            return;
        }
        Emit(name, eventProperties, metrics);
    }

    public void TrackUnsupportedFeatures(
        Dictionary<string, int> featureCounts,
        int totalCount)
    {
        if (totalCount <= 0 || !TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        if (featureCounts.Any(kv =>
                !TelemetrySchema.IsKnownFeature(kv.Key) || kv.Value < 0))
        {
            _failed = true;
            return;
        }

        Emit("UnsupportedFeatures", eventProperties, new Dictionary<string, double>
        {
            ["totalUnsupportedCount"] = totalCount,
            ["distinctFeatureCount"] = featureCounts.Count
        });

        foreach (var (feature, count) in featureCounts
                     .OrderByDescending(kv => kv.Value)
                     .Take(50))
        {
            var featureProperties = new Dictionary<string, string>(eventProperties)
            {
                ["feature"] = feature
            };
            Emit("UnsupportedFeature", featureProperties, new Dictionary<string, double>
            {
                ["count"] = count
            });
        }
    }

    public void TrackInputProfile(InputProfile profile)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["hasContracts"] = profile.HasContracts.ToString();
        eventProperties["hasEffects"] = profile.HasEffects.ToString();
        eventProperties["hasModules"] = profile.HasModules.ToString();
        eventProperties["sizeCategory"] = profile.SizeCategory;
        Emit("InputProfile", eventProperties, new Dictionary<string, double>
        {
            ["lineCount"] = profile.LineCount,
            ["estimatedTokenCount"] = profile.EstimatedTokenCount
        });
    }

    public void TrackDiagnosticEvent(string code, string severity, string category)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["code"] = code;
        eventProperties["severity"] = severity;
        eventProperties["category"] = category;
        Emit("DiagnosticOccurrence", eventProperties, null);
    }

    public void TrackDiagnosticCoOccurrence(Dictionary<string, int> codePairs)
    {
        if (!TryGetCommandProperties(out var commandProperties))
        {
            return;
        }
        foreach (var (pair, count) in codePairs)
        {
            var parts = pair.Split('+');
            if (parts.Length != 2)
            {
                continue;
            }
            var eventProperties = new Dictionary<string, string>(commandProperties)
            {
                ["codeA"] = parts[0],
                ["codeB"] = parts[1]
            };
            Emit("DiagnosticCoOccurrence", eventProperties, new Dictionary<string, double>
            {
                ["count"] = count
            });
        }
    }

    public void TrackSessionStarted()
    {
        if (_sessionStarted)
        {
            return;
        }
        _sessionStarted = true;
        Emit("SessionStarted", null, null);
    }

    public void TrackSessionEnded()
    {
        var properties = new Dictionary<string, string>();
        if (_commandSequence.Count > 0)
        {
            properties["commandSequence"] = string.Join(",", _commandSequence.Take(32));
        }
        Emit("SessionEnded", properties, new Dictionary<string, double>
        {
            ["sessionDurationMs"] = (DateTime.UtcNow - _sessionStartTime).TotalMilliseconds,
            ["commandCount"] = _commandSequence.Count
        });
    }

    public void TrackConversionAttempted(
        int inputLines,
        bool success,
        long durationMs,
        int issueCount,
        int unsupportedCount)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["success"] = success.ToString();
        Emit("ConversionAttempted", eventProperties, new Dictionary<string, double>
        {
            ["inputLines"] = inputLines,
            ["durationMs"] = durationMs,
            ["issueCount"] = issueCount,
            ["unsupportedCount"] = unsupportedCount
        });
    }

    public void TrackConversionGap(string gapName, int? line)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["feature"] = gapName;
        var metrics = new Dictionary<string, double>();
        if (line.HasValue)
        {
            metrics["line"] = line.Value;
        }
        Emit("ConversionGap", eventProperties, metrics);
    }

    public void TrackSyntaxHelpQuery(
        string feature,
        string? resolvedCategory,
        int resultCount,
        string? matchedSections)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["resolvedCategory"] = resolvedCategory ?? "none";
        eventProperties["isHit"] = (resultCount > 0).ToString();
        Emit("SyntaxHelpQuery", eventProperties, new Dictionary<string, double>
        {
            ["featureLength"] = feature.Length,
            ["resultCount"] = resultCount,
            ["matchedSectionCount"] = string.IsNullOrEmpty(matchedSections)
                ? 0
                : matchedSections.Split(';', StringSplitOptions.RemoveEmptyEntries).Length
        });
    }

    public void TrackCompilationOutcome(
        bool success,
        int errorCount,
        int warningCount)
    {
        if (!TryGetCommandProperties(out var eventProperties))
        {
            return;
        }
        eventProperties["success"] = success.ToString();
        Emit("CompilationOutcome", eventProperties, new Dictionary<string, double>
        {
            ["errorCount"] = errorCount,
            ["warningCount"] = warningCount
        });
    }

    public void Flush()
    {
        if (_client == null || _failed)
        {
            return;
        }
        try
        {
            _client.Flush();
        }
        catch
        {
            _failed = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Flush();
    }

    public void SetAgents(string agents)
    {
        _context["codingAgent"] = NormalizeCodingAgent(agents);
        ApplyContext(_client);
    }

    private void Emit(
        string eventName,
        IReadOnlyDictionary<string, string>? properties,
        IReadOnlyDictionary<string, double>? metrics)
    {
        if (_failed || (_client == null && _previewWriter == null))
        {
            return;
        }
        try
        {
            if (!TelemetrySchema.TryCreatePayload(
                    eventName, properties, metrics, _context, out var payload)
                || payload == null)
            {
                _failed = true;
                return;
            }

            if (_previewWriter != null)
            {
                lock (_previewLock)
                {
                    _previewWriter.WriteLine(TelemetrySchema.Serialize(payload));
                    _previewWriter.Flush();
                }
                return;
            }

            var telemetry = new EventTelemetry(payload.EventName);
            foreach (var (name, value) in payload.Properties)
            {
                telemetry.Properties[name] = value;
            }
            foreach (var (name, value) in payload.Metrics)
            {
                telemetry.Metrics[name] = value;
            }
            _client!.TrackEvent(telemetry);
        }
        catch
        {
            _failed = true;
        }
    }

    private bool TryGetCommandProperties(out Dictionary<string, string> properties)
    {
        properties = new Dictionary<string, string>();
        if (!_commandContextValid)
        {
            return false;
        }
        if (_currentCommand != null)
        {
            properties["command"] = _currentCommand;
        }
        return true;
    }

    private static bool TryAddCallerFields(
        IReadOnlyDictionary<string, string>? callerFields,
        Dictionary<string, string> properties,
        Dictionary<string, double> metrics)
    {
        if (callerFields == null)
        {
            return true;
        }
        foreach (var (name, value) in callerFields)
        {
            if (IsMetricField(name))
            {
                if (!double.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var metric))
                {
                    return false;
                }
                metrics[name] = metric;
            }
            else
            {
                properties[name] = value;
            }
        }
        return true;
    }

    private static bool IsMetricField(string name) =>
        name.EndsWith("Count", StringComparison.Ordinal)
        || name.EndsWith("Ms", StringComparison.Ordinal)
        || name is "fileCount" or "issueCount" or "errorCount" or "blockerCount"
            or "totalContracts" or "provenContracts" or "verifyContracts"
            or "verifyProven" or "verifyDisproven" or "verificationTimeout"
            or "experimentalFlagCount" or "tokenCount" or "functionsAnalyzed"
            or "bugPatternsFound" or "taintVulnerabilities";

    private Dictionary<string, string> CreateContext() => new(StringComparer.Ordinal)
    {
        ["schemaVersion"] = TelemetrySchema.Version,
        ["os"] = GetOsPlatform(),
        ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
        ["dotnetVersion"] = Environment.Version.ToString(),
        ["calorVersion"] = GetCalorVersion(),
        ["semanticsVersion"] = SemanticsVersion.VersionString,
        ["operationId"] = _operationId,
        ["codingAgent"] = "none"
    };

    private void ApplyContext(TelemetryClient? client)
    {
        if (client == null)
        {
            return;
        }
        client.Context.GlobalProperties.Clear();
        foreach (var (name, value) in _context)
        {
            client.Context.GlobalProperties[name] = value;
        }
    }

    private static bool TryValidateConnectionString(
        string? candidate,
        out string connectionString)
    {
        connectionString = "";
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }
        try
        {
            var parts = candidate.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var separator = part.IndexOf('=');
                if (separator <= 0 || separator == part.Length - 1)
                {
                    return false;
                }
                values[part[..separator]] = part[(separator + 1)..];
            }

            if (!values.TryGetValue("InstrumentationKey", out var key)
                || !Guid.TryParse(key, out _))
            {
                return false;
            }
            if (values.TryGetValue("IngestionEndpoint", out var endpoint)
                && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }
            connectionString = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string CategorizeException(Exception exception) => exception switch
    {
        ArgumentException => "argument",
        IOException => "io",
        UnauthorizedAccessException => "unauthorized",
        InvalidOperationException => "invalid-operation",
        TimeoutException => "timeout",
        JsonException => "serialization",
        _ => "other"
    };

    private static string NormalizeCodingAgent(string agents)
    {
        var values = agents.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 1)
        {
            return "none";
        }
        return values[0].ToLowerInvariant() switch
        {
            "claude" or "claude-code" => "claude-code",
            "copilot" or "github-copilot" => "github-copilot",
            "gemini" or "gemini-cli" => "gemini-cli",
            "codex" => "codex",
            _ => "none"
        };
    }

    private static bool IsTrue(string? value) =>
        value is not null
        && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase));

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
}
