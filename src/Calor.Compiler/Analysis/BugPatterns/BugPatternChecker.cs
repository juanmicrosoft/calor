using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Analysis.BugPatterns;

/// <summary>
/// Base interface for bug pattern checkers.
/// </summary>
public interface IBugPatternChecker
{
    /// <summary>
    /// The name of this bug pattern checker.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Checks a function for this bug pattern.
    /// </summary>
    /// <param name="function">The bound function to check.</param>
    /// <param name="diagnostics">Diagnostic bag to report issues.</param>
    void Check(BoundFunction function, DiagnosticBag diagnostics);
}

/// <summary>
/// Represents a detected bug pattern.
/// </summary>
public sealed class BugPatternResult
{
    public string PatternName { get; }
    public string DiagnosticCode { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticSeverity Severity { get; }
    public string? Explanation { get; }
    public SuggestedFix? Fix { get; }

    public BugPatternResult(
        string patternName,
        string diagnosticCode,
        string message,
        TextSpan span,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string? explanation = null,
        SuggestedFix? fix = null)
    {
        PatternName = patternName;
        DiagnosticCode = diagnosticCode;
        Message = message;
        Span = span;
        Severity = severity;
        Explanation = explanation;
        Fix = fix;
    }
}

/// <summary>
/// Options for bug pattern checking.
/// </summary>
public sealed class BugPatternOptions
{
    /// <summary>
    /// Enable division by zero checking.
    /// </summary>
    public bool CheckDivisionByZero { get; init; } = true;

    /// <summary>
    /// Enable array bounds checking.
    /// </summary>
    public bool CheckIndexOutOfBounds { get; init; } = true;

    /// <summary>
    /// Enable null/None dereference checking.
    /// </summary>
    public bool CheckNullDereference { get; init; } = true;

    /// <summary>
    /// Enable integer overflow checking.
    /// </summary>
    public bool CheckOverflow { get; init; } = true;

    /// <summary>
    /// Enable missing precondition suggestion.
    /// </summary>
    public bool CheckMissingPreconditions { get; init; } = true;

    /// <summary>
    /// Enable off-by-one error detection.
    /// </summary>
    public bool CheckOffByOne { get; init; } = true;

    /// <summary>
    /// When true, only report findings that are definitively proven (by Z3 or constant analysis).
    /// Suppresses Info-level "inconclusive" findings and precondition suggestions.
    /// Default: false (backward compatible). The CLI sets this to true unless --all-findings is passed.
    /// </summary>
    public bool ReportOnlyVerified { get; init; } = false;

    /// <summary>
    /// Use Z3 SMT solver for verification (more precise but slower).
    /// </summary>
    public bool UseZ3Verification { get; init; } = true;

    /// <summary>
    /// Z3 solver timeout in milliseconds.
    /// </summary>
    public uint Z3TimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Parameter names guarded by existing preconditions, keyed by function name.
    /// </summary>
    public Dictionary<string, HashSet<string>>? PreconditionGuardedParams { get; init; }

    public static BugPatternOptions Default => new();

    public static BugPatternOptions Fast => new()
    {
        UseZ3Verification = false
    };
}

/// <summary>
/// Orchestrates all bug pattern checkers.
/// </summary>
public sealed class BugPatternRunner
{
    private readonly DiagnosticBag _diagnostics;
    private readonly BugPatternOptions _options;
    private readonly List<IBugPatternChecker> _checkers;

    public BugPatternRunner(DiagnosticBag diagnostics, BugPatternOptions? options = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? BugPatternOptions.Default;
        _checkers = CreateCheckers();
    }

    private List<IBugPatternChecker> CreateCheckers()
    {
        var checkers = new List<IBugPatternChecker>();

        if (_options.CheckDivisionByZero)
            checkers.Add(new Patterns.DivisionByZeroChecker(_options));

        if (_options.CheckIndexOutOfBounds)
            checkers.Add(new Patterns.IndexOutOfBoundsChecker(_options));

        if (_options.CheckNullDereference)
            checkers.Add(new Patterns.NullDereferenceChecker(_options));

        if (_options.CheckOverflow)
            checkers.Add(new Patterns.OverflowChecker(_options));

        if (_options.CheckMissingPreconditions)
            checkers.Add(new Patterns.PreconditionSuggester(_options));

        if (_options.CheckOffByOne)
            checkers.Add(new Patterns.OffByOneChecker(_options));

        return checkers;
    }

    /// <summary>
    /// Runs all bug pattern checkers on a bound module.
    /// </summary>
    public void Check(BoundModule module)
    {
        foreach (var function in module.Functions)
        {
            CheckFunction(function);
        }
    }

    /// <summary>
    /// Runs all bug pattern checkers on a single function.
    /// </summary>
    public void CheckFunction(BoundFunction function)
    {
        foreach (var checker in _checkers)
        {
            checker.Check(function, _diagnostics);
        }
    }

    /// <summary>
    /// Gets the names of all enabled checkers.
    /// </summary>
    public IEnumerable<string> EnabledCheckers => _checkers.Select(c => c.Name);
}
