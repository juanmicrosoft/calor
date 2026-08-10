using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Telemetry;

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
        Set("Lexer", "Parser", "Semantic", "Contracts", "Effects", "Patterns",
            "ApiStrictness", "SemanticsVersion", "IdValidation", "Other");

    private static readonly IReadOnlySet<string> HelpCategories = Set(
        "none", "overview", "async", "contracts", "effects", "loops", "conditionals",
        "functions", "classes", "generics", "collections", "patterns", "exceptions",
        "lambdas", "strings", "types", "records", "enums", "constructors",
        "properties", "structs", "operators", "nullable", "linq", "events", "using",
        "modifiers", "indexers", "yield", "tuples", "preprocessor");

    private static readonly IReadOnlySet<string> KnownFeatures =
        FeatureSupport.GetAllFeatures().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

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
        JsonSerializer.Serialize(payload, SerializerOptions);

    internal static bool IsKnownCommand(string command) => Commands.Contains(command);

    internal static bool IsKnownFeature(string feature) => KnownFeatures.Contains(feature);

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
        return JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions) + Environment.NewLine;
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
            "architecture" => value is "X86" or "X64" or "Arm" or "Arm64" or "Wasm"
                or "S390x" or "LoongArch64" or "Armv6" or "Ppc64le",
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
