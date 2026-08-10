using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
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
public sealed partial class CalorTelemetry : IDisposable
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

    internal sealed record TelemetryEventDefinition(
        IReadOnlySet<string> PropertyFields,
        IReadOnlySet<string> MetricFields);

    internal sealed record TelemetryPayload(
        string SchemaVersion,
        string EventName,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyDictionary<string, double> Metrics,
        IReadOnlyDictionary<string, string> Context);

    internal static partial class TelemetrySchema
    {
        internal const string Version = "1.0";

        internal static readonly IReadOnlySet<string> GlobalContextFields =
            Set("schemaVersion", "os", "architecture", "dotnetVersion", "calorVersion",
                "semanticsVersion", "operationId", "codingAgent");

        internal static readonly IReadOnlyDictionary<string, TelemetryEventDefinition> Events =
            new Dictionary<string, TelemetryEventDefinition>(StringComparer.Ordinal)
            {
                ["CommandSucceeded"] = Event(
                    ["command", "exitCode", "error", "verbose", "list"],
                    ["durationMs", "fileCount", "issueCount", "errorCount", "blockerCount",
                     "totalContracts", "provenContracts", "verifyContracts", "verifyProven",
                     "verifyDisproven", "verifyDurationMs"]),
                ["CommandFailed"] = Event(
                    ["command", "exitCode", "error", "verbose", "list"],
                    ["durationMs", "fileCount", "issueCount", "errorCount", "blockerCount",
                     "totalContracts", "provenContracts", "verifyContracts", "verifyProven",
                     "verifyDisproven", "verifyDurationMs"]),
                ["CompilationPhase"] = Event(
                    ["command", "phase", "success"],
                    ["durationMs", "tokenCount", "functionsAnalyzed", "bugPatternsFound",
                     "taintVulnerabilities"]),
                ["DiagnosticOccurrence"] = Event(
                    ["command", "code", "severity", "category"],
                    []),
                ["DiagnosticCoOccurrence"] = Event(
                    ["command", "codeA", "codeB"],
                    ["count"]),
                ["Exception"] = Event(
                    ["command", "exceptionCategory", "phase"],
                    []),
                ["CompileOptions"] = Event(
                    ["command", "strictApi", "requireDocs", "enforceEffects", "strictEffects",
                     "permissiveEffects", "contractMode", "verify", "noCache", "analyze",
                     "strictBindInference"],
                    ["verificationTimeout", "experimentalFlagCount"]),
                ["UnsupportedFeatures"] = Event(
                    ["command"],
                    ["totalUnsupportedCount", "distinctFeatureCount"]),
                ["UnsupportedFeature"] = Event(
                    ["command", "feature"],
                    ["count"]),
                ["InputProfile"] = Event(
                    ["command", "hasContracts", "hasEffects", "hasModules", "sizeCategory"],
                    ["lineCount", "estimatedTokenCount"]),
                ["SessionStarted"] = Event([], []),
                ["SessionEnded"] = Event(
                    ["commandSequence"],
                    ["sessionDurationMs", "commandCount"]),
                ["ConversionAttempted"] = Event(
                    ["command", "success"],
                    ["inputLines", "durationMs", "issueCount", "unsupportedCount"]),
                ["ConversionGap"] = Event(
                    ["command", "feature"],
                    ["line"]),
                ["SyntaxHelpQuery"] = Event(
                    ["command", "resolvedCategory", "isHit"],
                    ["featureLength", "resultCount", "matchedSectionCount"]),
                ["CompilationOutcome"] = Event(
                    ["command", "success"],
                    ["errorCount", "warningCount"]),
                ["HookAllow"] = Event(
                    ["command", "hook", "decision", "fileExtension", "agent"],
                    []),
                ["HookBlock"] = Event(
                    ["command", "hook", "decision", "fileExtension", "agent"],
                    [])
            };

        private static readonly IReadOnlySet<string> Commands = Set(
            "compile", "convert", "migrate", "benchmark", "init", "format", "lint",
            "assess", "analyze-convertibility", "hook", "ids", "fix", "effects",
            "import", "verify", "review-packet", "lsp", "mcp", "feature-check",
            "coverage", "self-test", "self-check", "evaluation", "run", "test", "watch");

        private static readonly IReadOnlySet<string> Phases = Set(
            "Lexer", "Parser", "TypeChecker", "PatternChecker", "BindValidation",
            "ReturnValidation", "ApiStrictness", "EffectEnforcement", "ContractInheritance",
            "ContractVerifier", "ContractSimplification", "ObligationVerification",
            "Z3Verification", "VerificationAnalyses", "CodeGen", "command_execution");

        private static readonly IReadOnlySet<string> DiagnosticSeverities =
            Set("Error", "Warning", "Info", "Hidden");

        private static readonly IReadOnlySet<string> DiagnosticCategories =
            Set("Lexer", "Parser", "Semantic", "Contract", "Effect", "Pattern",
                "ApiStrictness", "Verification", "Import", "Conversion", "CodeGen",
                "Experimental", "Cli", "Other", "Unknown");

        private static readonly IReadOnlySet<string> HelpCategories =
            HelpTool.TelemetryCategories.Append("none").ToHashSet(StringComparer.Ordinal);

        private static readonly IReadOnlySet<string> KnownFeatures =
            FeatureSupport.GetAllFeatures().Select(f => f.Name)
                .Concat(["unsupported-member", "preprocessor-disabled", "post-validation-fallback"])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlySet<string> Architectures =
            Enum.GetNames<Architecture>().ToHashSet(StringComparer.Ordinal);

        internal static bool TryCreatePayload(
            string eventName,
            IReadOnlyDictionary<string, string>? properties,
            IReadOnlyDictionary<string, double>? metrics,
            IReadOnlyDictionary<string, string> context,
            out TelemetryPayload? payload)
        {
            payload = null;
            try
            {
                if (!Events.TryGetValue(eventName, out var definition)
                    || !ValidateFields(properties, definition.PropertyFields)
                    || !ValidateFields(metrics, definition.MetricFields)
                    || !ValidateFields(context, GlobalContextFields)
                    || context.Count != GlobalContextFields.Count
                    || !context.TryGetValue("schemaVersion", out var schemaVersion)
                    || schemaVersion != Version)
                {
                    return false;
                }

                var sanitizedProperties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var (name, value) in properties ?? EmptyProperties)
                {
                    if (!TrySanitizeProperty(name, value, out var sanitized))
                    {
                        return false;
                    }
                    sanitizedProperties[name] = sanitized;
                }

                var sanitizedMetrics = new SortedDictionary<string, double>(StringComparer.Ordinal);
                foreach (var (name, value) in metrics ?? EmptyMetrics)
                {
                    if (!double.IsFinite(value) || value < 0)
                    {
                        return false;
                    }
                    sanitizedMetrics[name] = value;
                }

                var sanitizedContext = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var (name, value) in context)
                {
                    if (!TrySanitizeContext(name, value, out var sanitized))
                    {
                        return false;
                    }
                    sanitizedContext[name] = sanitized;
                }

                payload = new TelemetryPayload(
                    Version, eventName, sanitizedProperties, sanitizedMetrics, sanitizedContext);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string Serialize(TelemetryPayload payload) =>
            System.Text.Json.JsonSerializer.Serialize(payload, SerializerOptions);

        internal static bool IsKnownCommand(string command) => Commands.Contains(command);

        internal static bool IsKnownFeature(string feature) => KnownFeatures.Contains(feature);

        internal static bool IsKnownHelpCategory(string category) => HelpCategories.Contains(category);

        internal static bool IsKnownArchitecture(string architecture) =>
            Architectures.Contains(architecture);

        internal static string GetDiagnosticCategory(string code)
        {
            if (code.Length > 5 && int.TryParse(code.AsSpan(5), out var number))
            {
                return number switch
                {
                    < 100 => "Lexer",
                    < 200 => "Parser",
                    < 300 => "Semantic",
                    < 400 => "Contract",
                    < 500 => "Effect",
                    < 600 => "Pattern",
                    < 700 => "ApiStrictness",
                    < 800 => "Verification",
                    < 900 => "Import",
                    < 1000 => "Conversion",
                    < 1100 => "CodeGen",
                    < 1200 => "Verification",
                    < 1300 => "Experimental",
                    < 1400 => "Cli",
                    _ => "Other"
                };
            }
            return "Unknown";
        }

        internal static string ExportSnapshot()
        {
            var snapshot = new
            {
                schemaVersion = Version,
                globalContextFields = GlobalContextFields.OrderBy(x => x, StringComparer.Ordinal),
                events = Events.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new
                {
                    eventName = x.Key,
                    propertyFields = x.Value.PropertyFields.OrderBy(f => f, StringComparer.Ordinal),
                    metricFields = x.Value.MetricFields.OrderBy(f => f, StringComparer.Ordinal)
                })
            };
            return System.Text.Json.JsonSerializer.Serialize(
                snapshot, SnapshotSerializerOptions) + Environment.NewLine;
        }

        private static bool ValidateFields<T>(
            IReadOnlyDictionary<string, T>? values,
            IReadOnlySet<string> allowed) =>
            values == null || values.Keys.All(allowed.Contains);

        private static bool TrySanitizeProperty(string name, string value, out string sanitized)
        {
            sanitized = value;
            return name switch
            {
                "command" => Commands.Contains(value),
                "exitCode" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode)
                              && exitCode is >= -1 and <= 255,
                "error" => value == "file_not_found",
                "verbose" or "list" or "success" or "strictApi" or "requireDocs"
                    or "enforceEffects" or "strictEffects" or "permissiveEffects" or "verify"
                    or "noCache" or "analyze" or "strictBindInference" or "hasContracts"
                    or "hasEffects" or "hasModules" or "isHit" => NormalizeBoolean(value, out sanitized),
                "contractMode" => value is "off" or "debug" or "release",
                "phase" => Phases.Contains(value),
                "code" or "codeA" or "codeB" => DiagnosticCodeRegex().IsMatch(value),
                "severity" => DiagnosticSeverities.Contains(value),
                "category" => DiagnosticCategories.Contains(value),
                "exceptionCategory" => value is "argument" or "io" or "unauthorized"
                    or "invalid-operation" or "timeout" or "serialization" or "other",
                "feature" => KnownFeatures.Contains(value),
                "sizeCategory" => value is "small" or "medium" or "large" or "xlarge",
                "commandSequence" => ValidateCommandSequence(value),
                "resolvedCategory" => HelpCategories.Contains(value),
                "hook" => value is "validate-write" or "validate-edit",
                "decision" => value is "allow" or "block",
                "fileExtension" => value is ".calr" or ".cs",
                "agent" => value is "claude" or "gemini",
                _ => false
            };
        }

        private static bool TrySanitizeContext(string name, string value, out string sanitized)
        {
            sanitized = value;
            return name switch
            {
                "schemaVersion" => value == Version,
                "os" => value is "windows" or "macos" or "linux" or "other",
                "architecture" => Architectures.Contains(value),
                "dotnetVersion" or "calorVersion" or "semanticsVersion" =>
                    VersionRegex().IsMatch(value),
                "operationId" => OperationIdRegex().IsMatch(value),
                "codingAgent" => value is "none" or "claude-code" or "github-copilot"
                    or "gemini-cli" or "codex",
                _ => false
            };
        }

        private static bool NormalizeBoolean(string value, out string sanitized)
        {
            if (bool.TryParse(value, out var parsed))
            {
                sanitized = parsed ? "true" : "false";
                return true;
            }
            sanitized = "";
            return false;
        }

        private static bool ValidateCommandSequence(string value)
        {
            var commands = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return commands.Length <= 32 && commands.All(Commands.Contains);
        }

        private static TelemetryEventDefinition Event(string[] properties, string[] metrics) =>
            new(Set(properties), Set(metrics));

        private static HashSet<string> Set(params string[] values) =>
            new(values, StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
            new Dictionary<string, string>();
        private static readonly IReadOnlyDictionary<string, double> EmptyMetrics =
            new Dictionary<string, double>();

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        [GeneratedRegex(@"^Calor\d{4}$", RegexOptions.CultureInvariant)]
        private static partial Regex DiagnosticCodeRegex();

        [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$", RegexOptions.CultureInvariant)]
        private static partial Regex VersionRegex();

        [GeneratedRegex(@"^[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
        private static partial Regex OperationIdRegex();
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
        if (featureCounts.Any(kv => kv.Value < 0))
        {
            return;
        }

        Emit("UnsupportedFeatures", eventProperties, new Dictionary<string, double>
        {
            ["totalUnsupportedCount"] = totalCount,
            ["distinctFeatureCount"] = featureCounts.Count
        });

        foreach (var (feature, count) in featureCounts
                     .Where(kv => TelemetrySchema.IsKnownFeature(kv.Key))
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
