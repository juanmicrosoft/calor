namespace Calor.Compiler.Migration;

/// <summary>
/// Severity level for conversion issues.
/// </summary>
public enum ConversionIssueSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Represents a single instance of an unsupported feature usage.
/// </summary>
public sealed class UnsupportedFeatureInstance
{
    public required string Code { get; init; }
    public required int Line { get; init; }
    public string? Suggestion { get; init; }
}

/// <summary>
/// Detailed explanation of conversion issues and unsupported features.
/// </summary>
public sealed class ConversionExplanation
{
    /// <summary>
    /// Dictionary of feature name → list of instances where that feature was used.
    /// </summary>
    public required Dictionary<string, List<UnsupportedFeatureInstance>> UnsupportedFeatures { get; init; }

    /// <summary>
    /// Total count of unsupported feature instances.
    /// </summary>
    public int TotalUnsupportedCount { get; init; }

    /// <summary>
    /// List of partially supported features that were used (may need review).
    /// </summary>
    public required List<string> PartialFeatures { get; init; }

    /// <summary>
    /// List of features that require manual conversion.
    /// </summary>
    public required List<string> ManualRequiredFeatures { get; init; }

    /// <summary>
    /// Returns feature names and occurrence counts only (no source code).
    /// </summary>
    public Dictionary<string, int> GetFeatureCounts()
    {
        return UnsupportedFeatures.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count);
    }

    /// <summary>
    /// Formats the explanation as a human-readable string for CLI output.
    /// </summary>
    public string FormatForCli()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Conversion Explanation ===");
        sb.AppendLine();

        if (UnsupportedFeatures.Count > 0)
        {
            sb.AppendLine($"Unsupported Features ({TotalUnsupportedCount} instance(s)):");
            foreach (var (feature, instances) in UnsupportedFeatures)
            {
                sb.AppendLine($"  [{feature}] - {instances.Count} occurrence(s)");
                foreach (var instance in instances.Take(3))
                {
                    sb.AppendLine($"    (line {instance.Line}): {instance.Code}");
                    if (!string.IsNullOrEmpty(instance.Suggestion))
                    {
                        sb.AppendLine($"      Suggestion: {instance.Suggestion}");
                    }
                }
                if (instances.Count > 3)
                {
                    sb.AppendLine($"    ... and {instances.Count - 3} more");
                }
            }
            sb.AppendLine();
        }

        if (PartialFeatures.Count > 0)
        {
            sb.AppendLine("Partially Supported Features (may need review):");
            foreach (var feature in PartialFeatures)
            {
                var workaround = FeatureSupport.GetWorkaround(feature);
                var hint = workaround != null ? $": {workaround}" : "";
                sb.AppendLine($"  - {feature}{hint}");
            }
            sb.AppendLine();
        }

        if (ManualRequiredFeatures.Count > 0)
        {
            sb.AppendLine("Features Requiring Manual Conversion:");
            foreach (var feature in ManualRequiredFeatures)
            {
                var workaround = FeatureSupport.GetWorkaround(feature);
                var hint = workaround != null ? $": {workaround}" : "";
                sb.AppendLine($"  - {feature}{hint}");
            }
            sb.AppendLine();
        }

        if (UnsupportedFeatures.Count == 0 && PartialFeatures.Count == 0 && ManualRequiredFeatures.Count == 0)
        {
            sb.AppendLine("All features fully supported. No manual conversion needed.");
        }

        return sb.ToString();
    }
}

/// <summary>
/// The kind of semantic loss recorded during a C# → Calor conversion (#770).
/// </summary>
public enum ConversionLossKind
{
    /// <summary>A member/type/statement was preserved verbatim as §CSHARP interop instead of native Calor.</summary>
    InteropPreserved,

    /// <summary>A construct was replaced by a TODO fallback comment (its behavior is lost).</summary>
    FallbackTodo,

    /// <summary>A construct was dropped entirely from the output.</summary>
    Dropped,

    /// <summary>A preprocessor directive/branch was stripped before conversion (branch content or condition lost).</summary>
    PreprocessorStripped,

    /// <summary>An inactive nonconditional compiler directive was removed by selected-branch mode.</summary>
    DirectiveRemoved,

    /// <summary>
    /// A raw C# fallback (§CS{…}/§RAW/§CSHARP) found in the emitted Calor with no
    /// matching ledger entry — produced by the Calor emitter's internal fallback
    /// paths, which do not (yet) thread through the loss ledger (#836 M2). Counted
    /// by post-emission reconciliation so raw C# in the output can never coexist
    /// with a zero-loss "fully native" claim.
    /// </summary>
    EmitterFallback
}

/// <summary>
/// One structured, located semantic loss recorded during conversion (#770 item 8):
/// every interop preservation, fallback, and dropped construct is counted with a
/// file:line location so callers can distinguish faithful conversion from loss.
/// </summary>
public sealed class ConversionLoss
{
    public required ConversionLossKind Kind { get; init; }
    public required string Feature { get; init; }
    public required string Description { get; init; }
    public int? Line { get; init; }
    public string? File { get; init; }
    public bool IsSemanticLoss => Kind is ConversionLossKind.FallbackTodo
        or ConversionLossKind.Dropped
        or ConversionLossKind.PreprocessorStripped
        or ConversionLossKind.DirectiveRemoved;

    public override string ToString()
    {
        var location = File != null
            ? $"{File}:{Line?.ToString() ?? "?"}"
            : Line.HasValue ? $"line {Line}" : "unknown location";
        return $"[{Kind}] {location} [{Feature}] {Description}";
    }
}

/// <summary>
/// Represents an issue encountered during conversion.
/// </summary>
public sealed class ConversionIssue
{
    public required ConversionIssueSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? Feature { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? Suggestion { get; init; }

    public override string ToString()
    {
        var location = Line.HasValue ? $" (line {Line}" + (Column.HasValue ? $", col {Column})" : ")") : "";
        var feature = Feature != null ? $" [{Feature}]" : "";
        return $"{Severity}{feature}{location}: {Message}";
    }
}

/// <summary>
/// Statistics about the conversion process.
/// </summary>
public sealed class ConversionStats
{
    public int TotalNodes { get; set; }
    public int ConvertedNodes { get; set; }
    public int SkippedNodes { get; set; }
    public int ClassesConverted { get; set; }
    public int InterfacesConverted { get; set; }
    public int EnumsConverted { get; set; }
    public int MethodsConverted { get; set; }
    public int PropertiesConverted { get; set; }
    public int FieldsConverted { get; set; }
    public int StatementsConverted { get; set; }
    public int ExpressionsConverted { get; set; }
    public int InteropBlocksEmitted { get; set; }

    /// <summary>
    /// The subset of <see cref="InteropBlocksEmitted"/> produced by the #717
    /// post-validation fallback specifically — a member whose emitted Calor did not
    /// parse, re-preserved as §CSHARP (as opposed to the visitor wrapping a known-
    /// unsupported feature). Lets a caller attribute how many blocks a C#-preserving
    /// mode (e.g. <c>--passthrough</c>) actually rescued.
    /// </summary>
    public int FallbackInteropBlocksEmitted { get; set; }

    public int MembersDropped { get; set; }

    public double ConversionRate => TotalNodes > 0 ? (double)ConvertedNodes / TotalNodes * 100 : 0;
}

/// <summary>
/// Records the C# parse and conditional-compilation configuration used for a conversion.
/// </summary>
public sealed record ConversionMetadata
{
    public PreprocessorConversionMode PreprocessorMode { get; init; } =
        PreprocessorConversionMode.PreserveAllBranches;
    public string? Configuration { get; init; }
    public string? TargetFramework { get; init; }
    public string LanguageVersion { get; init; } = "";
    public string DocumentationMode { get; init; } = "";
    public string SourceCodeKind { get; init; } = "";
    public string OutputKind { get; init; } = "";
    public IReadOnlyList<string> DefinedSymbols { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Features { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<ConversionReference> References { get; init; } =
        Array.Empty<ConversionReference>();
}

public sealed record ConversionReference(
    string Path,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Shared context for tracking state during C# to Calor conversion.
/// </summary>
public sealed class ConversionContext
{
    private readonly List<ConversionIssue> _issues = new();
    private readonly List<ConversionLoss> _losses = new();
    private readonly HashSet<string> _usedFeatures = new();
    private readonly Stack<string> _scopeStack = new();
    private readonly Stack<(string FullName, string ScopeId)> _namespaceStack = new();
    private readonly Dictionary<string, List<UnsupportedFeatureInstance>> _unsupportedFeatures = new();
    private int _idCounter = 0;

    /// <summary>
    /// The source file being converted.
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>
    /// Whether to use verbose output.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Whether to include benchmark metrics.
    /// </summary>
    public bool IncludeBenchmark { get; set; }

    /// <summary>
    /// Whether to preserve XML documentation comments.
    /// </summary>
    public bool PreserveDocumentationComments { get; set; } = true;

    /// <summary>
    /// Compatibility alias for <see cref="PreserveDocumentationComments"/>.
    /// It does not preserve ordinary source comments.
    /// </summary>
    public bool PreserveComments
    {
        get => PreserveDocumentationComments;
        set => PreserveDocumentationComments = value;
    }

    /// <summary>
    /// Whether to generate module IDs automatically.
    /// </summary>
    public bool AutoGenerateIds { get; set; } = true;

    /// <summary>
    /// Whether to emit graceful fallback for unsupported constructs.
    /// When true (default), unsupported code emits TODO comments and conversion succeeds.
    /// When false, unsupported code causes conversion to fail with errors.
    /// </summary>
    public bool GracefulFallback { get; set; } = true;

    /// <summary>The fidelity contract applied to this conversion.</summary>
    public ConversionFidelity Fidelity { get; set; } = ConversionFidelity.Lossless;

    /// <summary>The parse and conditional-compilation configuration used for this conversion.</summary>
    public ConversionMetadata Metadata { get; set; } = new();

    /// <summary>
    /// The module name to use (derived from file name if not set).
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// Conversion mode controlling how unsupported constructs are handled.
    /// </summary>
    public ConversionMode Mode { get; set; } = ConversionMode.Standard;

    /// <summary>When true, wraps unsupported constructs in §CSHARP blocks instead of emitting broken Calor.</summary>
    public bool PassthroughOnError { get; set; }

    /// <summary>
    /// When true, the emitter elides the trailing <c>§/C</c> on zero-arg
    /// expression-context <c>§C</c> calls. RFC <c>v0.6-call-closer-elision</c>
    /// Phase 2. Default <c>true</c> as of v0.6.1; the parser accepts both
    /// forms regardless, so consumers that want the explicit closer back can
    /// set this to <c>false</c>. One-arg elision remains gated off pending
    /// context-aware Lisp-arg-list safety analysis (tracked for a later v0.6.x).
    /// </summary>
    public bool UseImplicitCallCloser { get; set; } = true;

    /// <summary>
    /// Whether unsupported constructs should be preserved as C# passthrough blocks.
    /// True when Mode is Interop or PassthroughOnError is enabled.
    /// </summary>
    public bool ShouldPreserveCSharp =>
        Fidelity == ConversionFidelity.Lossless ||
        Mode == ConversionMode.Interop ||
        PassthroughOnError;

    /// <summary>
    /// Current namespace being processed.
    /// </summary>
    public string? CurrentNamespace { get; private set; }

    /// <summary>
    /// Current lexical namespace declaration identity.
    /// </summary>
    public string? CurrentNamespaceScopeId { get; private set; }

    /// <summary>
    /// Current type being processed.
    /// </summary>
    public string? CurrentTypeName { get; private set; }

    /// <summary>
    /// Current method being processed.
    /// </summary>
    public string? CurrentMethodName { get; private set; }

    /// <summary>
    /// All issues encountered during conversion.
    /// </summary>
    public IReadOnlyList<ConversionIssue> Issues => _issues;

    /// <summary>
    /// All features used in the source code.
    /// </summary>
    public IReadOnlySet<string> UsedFeatures => _usedFeatures;

    /// <summary>
    /// Conversion statistics.
    /// </summary>
    public ConversionStats Stats { get; } = new();

    /// <summary>
    /// Structured semantic-loss accounting (#770): every interop preservation,
    /// fallback, dropped construct, and stripped preprocessor branch, each with a
    /// file:line location. Empty means the output is fully native with no known loss.
    /// </summary>
    public IReadOnlyList<ConversionLoss> Losses => _losses;

    /// <summary>
    /// Records a structured semantic loss with its location.
    /// </summary>
    public void RecordLoss(ConversionLossKind kind, string feature, string description, int? line = null)
    {
        _losses.Add(new ConversionLoss
        {
            Kind = kind,
            Feature = feature,
            Description = description.Length > 120 ? description[..117] + "..." : description,
            Line = line,
            File = SourceFile
        });
    }

    /// <summary>
    /// Original C# source code.
    /// </summary>
    public string? OriginalSource { get; set; }

    /// <summary>
    /// Checks if any errors were encountered.
    /// </summary>
    public bool HasErrors => _issues.Any(i => i.Severity == ConversionIssueSeverity.Error);

    /// <summary>
    /// Checks if any warnings were encountered.
    /// </summary>
    public bool HasWarnings => _issues.Any(i => i.Severity == ConversionIssueSeverity.Warning);

    /// <summary>
    /// Generates a unique ID for Calor elements.
    /// When a hint is provided and valid, it is appended to the prefix for readability
    /// (e.g., prefix="_chain", hint="Where" → "_chainWhere001").
    /// </summary>
    public string GenerateId(string prefix = "", string hint = "")
    {
        _idCounter++;
        var sanitized = SanitizeHint(hint);
        var effectivePrefix = string.IsNullOrEmpty(sanitized) ? prefix : $"{prefix}{sanitized}";
        return string.IsNullOrEmpty(effectivePrefix) ? $"id{_idCounter:D3}" : $"{effectivePrefix}{_idCounter:D3}";
    }

    /// <summary>
    /// Sanitizes a hint string to be a valid C# identifier fragment.
    /// Strips invalid characters, capitalizes the first letter, and truncates to 20 chars.
    /// </summary>
    internal static string SanitizeHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return "";

        // Keep only letters and digits
        var chars = hint.Where(c => char.IsLetterOrDigit(c)).ToArray();
        if (chars.Length == 0)
            return "";

        // Capitalize first letter for camelCase readability
        chars[0] = char.ToUpperInvariant(chars[0]);

        // Truncate to 20 characters
        var length = Math.Min(chars.Length, 20);
        return new string(chars, 0, length);
    }

    /// <summary>
    /// Records usage of a feature.
    /// </summary>
    public void RecordFeatureUsage(string feature)
    {
        _usedFeatures.Add(feature);

        var info = FeatureSupport.GetFeatureInfo(feature);
        if (info == null)
            return;

        switch (info.Support)
        {
            case SupportLevel.Partial:
                AddWarning($"Feature '{feature}' is partially supported. {info.Workaround ?? ""}", feature);
                break;
            case SupportLevel.NotSupported:
                AddError($"Feature '{feature}' is not supported. {info.Workaround ?? ""}", feature);
                break;
            case SupportLevel.ManualRequired:
                AddWarning($"Feature '{feature}' requires manual intervention. {info.Workaround ?? ""}", feature);
                break;
        }
    }

    /// <summary>
    /// Enters a new namespace scope.
    /// </summary>
    public void EnterNamespace(string namespaceName, string scopeId)
    {
        var fullName = string.IsNullOrEmpty(CurrentNamespace)
            ? namespaceName
            : $"{CurrentNamespace}.{namespaceName}";
        _namespaceStack.Push((fullName, scopeId));
        CurrentNamespace = fullName;
        CurrentNamespaceScopeId = scopeId;
        _scopeStack.Push($"namespace:{fullName}");
    }

    /// <summary>
    /// Exits the current namespace scope.
    /// </summary>
    public void ExitNamespace()
    {
        if (_scopeStack.Count > 0 && _scopeStack.Peek().StartsWith("namespace:"))
        {
            _scopeStack.Pop();
            if (_namespaceStack.Count > 0)
                _namespaceStack.Pop();
            CurrentNamespace = _namespaceStack.Count > 0
                ? _namespaceStack.Peek().FullName
                : null;
            CurrentNamespaceScopeId = _namespaceStack.Count > 0
                ? _namespaceStack.Peek().ScopeId
                : null;
        }
    }

    /// <summary>
    /// Enters a type scope.
    /// </summary>
    public void EnterType(string typeName)
    {
        CurrentTypeName = typeName;
        _scopeStack.Push($"type:{typeName}");
    }

    /// <summary>
    /// Exits the current type scope.
    /// </summary>
    public void ExitType()
    {
        if (_scopeStack.Count > 0 && _scopeStack.Peek().StartsWith("type:"))
        {
            _scopeStack.Pop();
            CurrentTypeName = _scopeStack
                .Where(s => s.StartsWith("type:"))
                .Select(s => s["type:".Length..])
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Enters a method scope.
    /// </summary>
    public void EnterMethod(string methodName)
    {
        CurrentMethodName = methodName;
        _scopeStack.Push($"method:{methodName}");
    }

    /// <summary>
    /// Exits the current method scope.
    /// </summary>
    public void ExitMethod()
    {
        if (_scopeStack.Count > 0 && _scopeStack.Peek().StartsWith("method:"))
        {
            _scopeStack.Pop();
            CurrentMethodName = _scopeStack
                .Where(s => s.StartsWith("method:"))
                .Select(s => s["method:".Length..])
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Gets the current scope path.
    /// </summary>
    public string GetCurrentScopePath()
    {
        var parts = new List<string>();
        if (CurrentNamespace != null) parts.Add(CurrentNamespace);
        if (CurrentTypeName != null) parts.Add(CurrentTypeName);
        if (CurrentMethodName != null) parts.Add(CurrentMethodName);
        return string.Join(".", parts);
    }

    /// <summary>
    /// Adds an informational message.
    /// </summary>
    public void AddInfo(string message, string? feature = null, int? line = null, int? column = null)
    {
        _issues.Add(new ConversionIssue
        {
            Severity = ConversionIssueSeverity.Info,
            Message = message,
            Feature = feature,
            Line = line,
            Column = column
        });
    }

    /// <summary>
    /// Adds a warning.
    /// </summary>
    public void AddWarning(string message, string? feature = null, int? line = null, int? column = null, string? suggestion = null)
    {
        _issues.Add(new ConversionIssue
        {
            Severity = ConversionIssueSeverity.Warning,
            Message = message,
            Feature = feature,
            Line = line,
            Column = column,
            Suggestion = suggestion
        });
    }

    /// <summary>
    /// Adds an error.
    /// </summary>
    public void AddError(string message, string? feature = null, int? line = null, int? column = null, string? suggestion = null)
    {
        _issues.Add(new ConversionIssue
        {
            Severity = ConversionIssueSeverity.Error,
            Message = message,
            Feature = feature,
            Line = line,
            Column = column,
            Suggestion = suggestion
        });
    }

    /// <summary>
    /// Increments the converted nodes count.
    /// </summary>
    public void IncrementConverted()
    {
        Stats.TotalNodes++;
        Stats.ConvertedNodes++;
    }

    /// <summary>
    /// Increments the skipped nodes count.
    /// </summary>
    public void IncrementSkipped()
    {
        Stats.TotalNodes++;
        Stats.SkippedNodes++;
    }

    /// <summary>
    /// Gets a summary of all issues.
    /// </summary>
    public string GetIssuesSummary()
    {
        var errors = _issues.Count(i => i.Severity == ConversionIssueSeverity.Error);
        var warnings = _issues.Count(i => i.Severity == ConversionIssueSeverity.Warning);
        var infos = _issues.Count(i => i.Severity == ConversionIssueSeverity.Info);

        return $"{errors} error(s), {warnings} warning(s), {infos} info(s)";
    }

    /// <summary>
    /// Gets issues filtered by severity.
    /// </summary>
    public IEnumerable<ConversionIssue> GetIssuesBySeverity(ConversionIssueSeverity severity)
    {
        return _issues.Where(i => i.Severity == severity);
    }

    /// <summary>
    /// Gets features that were used but are not fully supported.
    /// </summary>
    public IEnumerable<string> GetProblematicFeatures()
    {
        return _usedFeatures.Where(f => !FeatureSupport.IsFullySupported(f));
    }

    /// <summary>
    /// Records an instance of an unsupported feature for explanation output.
    /// </summary>
    public void RecordUnsupportedFeature(string featureName, string originalCode, int line, string? suggestion = null)
    {
        if (!_unsupportedFeatures.TryGetValue(featureName, out var instances))
        {
            instances = new List<UnsupportedFeatureInstance>();
            _unsupportedFeatures[featureName] = instances;
        }

        var truncatedCode = originalCode.Length > 100 ? originalCode.Substring(0, 97) + "..." : originalCode;

        instances.Add(new UnsupportedFeatureInstance
        {
            Code = truncatedCode,
            Line = line,
            Suggestion = suggestion
        });

        // When GracefulFallback is disabled, unsupported features cause errors
        if (!GracefulFallback)
        {
            AddError($"Unsupported feature [{featureName}]: {truncatedCode}", feature: featureName, line: line);
        }
    }

    /// <summary>
    /// Gets a detailed explanation of the conversion, including unsupported features.
    /// </summary>
    public ConversionExplanation GetExplanation()
    {
        var partialFeatures = _usedFeatures
            .Where(f => FeatureSupport.GetSupportLevel(f) == SupportLevel.Partial)
            .ToList();

        var manualRequired = _usedFeatures
            .Where(f => FeatureSupport.GetSupportLevel(f) == SupportLevel.ManualRequired)
            .ToList();

        var totalUnsupported = _unsupportedFeatures.Values.Sum(list => list.Count);

        return new ConversionExplanation
        {
            UnsupportedFeatures = new Dictionary<string, List<UnsupportedFeatureInstance>>(_unsupportedFeatures),
            TotalUnsupportedCount = totalUnsupported,
            PartialFeatures = partialFeatures,
            ManualRequiredFeatures = manualRequired
        };
    }

    /// <summary>
    /// Resets the context for a new conversion.
    /// </summary>
    public void Reset()
    {
        _issues.Clear();
        _losses.Clear();
        _usedFeatures.Clear();
        _scopeStack.Clear();
        _unsupportedFeatures.Clear();
        _idCounter = 0;
        CurrentNamespace = null;
        CurrentNamespaceScopeId = null;
        _namespaceStack.Clear();
        CurrentTypeName = null;
        CurrentMethodName = null;
        Stats.TotalNodes = 0;
        Stats.ConvertedNodes = 0;
        Stats.SkippedNodes = 0;
        Stats.ClassesConverted = 0;
        Stats.InterfacesConverted = 0;
        Stats.EnumsConverted = 0;
        Stats.MethodsConverted = 0;
        Stats.PropertiesConverted = 0;
        Stats.FieldsConverted = 0;
        Stats.StatementsConverted = 0;
        Stats.ExpressionsConverted = 0;
        Stats.InteropBlocksEmitted = 0;
        Stats.FallbackInteropBlocksEmitted = 0;
        Stats.MembersDropped = 0;
    }
}
