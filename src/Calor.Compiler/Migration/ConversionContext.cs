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

    public double ConversionRate => TotalNodes > 0 ? (double)ConvertedNodes / TotalNodes * 100 : 0;
}

/// <summary>
/// Shared context for tracking state during C# to Calor conversion.
/// </summary>
public sealed class ConversionContext
{
    private readonly List<ConversionIssue> _issues = new();
    private readonly HashSet<string> _usedFeatures = new();
    private readonly Stack<string> _scopeStack = new();
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
    /// Whether to preserve original comments.
    /// </summary>
    public bool PreserveComments { get; set; } = true;

    /// <summary>
    /// Whether to generate module IDs automatically.
    /// </summary>
    public bool AutoGenerateIds { get; set; } = true;

    /// <summary>
    /// The module name to use (derived from file name if not set).
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// Current namespace being processed.
    /// </summary>
    public string? CurrentNamespace { get; private set; }

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
    /// </summary>
    public string GenerateId(string prefix = "")
    {
        _idCounter++;
        return string.IsNullOrEmpty(prefix) ? $"id{_idCounter:D3}" : $"{prefix}{_idCounter:D3}";
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
    public void EnterNamespace(string namespaceName)
    {
        CurrentNamespace = namespaceName;
        _scopeStack.Push($"namespace:{namespaceName}");
    }

    /// <summary>
    /// Exits the current namespace scope.
    /// </summary>
    public void ExitNamespace()
    {
        if (_scopeStack.Count > 0 && _scopeStack.Peek().StartsWith("namespace:"))
        {
            _scopeStack.Pop();
            CurrentNamespace = _scopeStack
                .Where(s => s.StartsWith("namespace:"))
                .Select(s => s["namespace:".Length..])
                .FirstOrDefault();
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
    /// Resets the context for a new conversion.
    /// </summary>
    public void Reset()
    {
        _issues.Clear();
        _usedFeatures.Clear();
        _scopeStack.Clear();
        _idCounter = 0;
        CurrentNamespace = null;
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
    }
}
