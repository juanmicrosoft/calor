using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// Status of an obligation after verification.
/// </summary>
public enum ObligationStatus
{
    /// <summary>Not yet checked.</summary>
    Pending,

    /// <summary>Proven correct by the solver (UNSAT on negation).</summary>
    Discharged,

    /// <summary>Disproven — solver found a counterexample (SAT on negation).</summary>
    Failed,

    /// <summary>Solver timed out or returned UNKNOWN.</summary>
    Timeout,

    /// <summary>Boundary obligation — cannot be statically verified (e.g., public API entry).</summary>
    Boundary,

    /// <summary>Contains unsupported constructs for the solver.</summary>
    Unsupported
}

/// <summary>
/// The kind of obligation that was generated.
/// </summary>
public enum ObligationKind
{
    /// <summary>Refinement type constraint on function entry (parameter).</summary>
    RefinementEntry,

    /// <summary>Refinement type constraint on function return.</summary>
    RefinementReturn,

    /// <summary>Explicit proof obligation (§PROOF).</summary>
    ProofObligation,

    /// <summary>Array/collection index bounds check.</summary>
    IndexBounds,

    /// <summary>Assignment from unrefined type to refined type.</summary>
    Subtype
}

/// <summary>
/// A single verification obligation.
/// </summary>
public sealed class Obligation
{
    public string Id { get; }
    public ObligationKind Kind { get; }
    public string FunctionId { get; }
    public string Description { get; }
    public ExpressionNode Condition { get; }
    public TextSpan Span { get; }
    public ObligationStatus Status { get; internal set; }
    public string? CounterexampleDescription { get; internal set; }
    public TimeSpan? SolverDuration { get; internal set; }
    public string? SuggestedFix { get; internal set; }

    public Obligation(
        string id,
        ObligationKind kind,
        string functionId,
        string description,
        ExpressionNode condition,
        TextSpan span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        FunctionId = functionId ?? throw new ArgumentNullException(nameof(functionId));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Span = span;
        Kind = kind;
        Status = ObligationStatus.Pending;
    }
}

/// <summary>
/// Summary of obligation verification results.
/// </summary>
public sealed record ObligationSummary(
    int Total,
    int Discharged,
    int Failed,
    int Timeout,
    int Boundary,
    int Pending,
    int Unsupported);

/// <summary>
/// Tracks all obligations for a compilation unit.
/// </summary>
public sealed class ObligationTracker
{
    private readonly List<Obligation> _obligations = new();
    private int _nextId;

    public IReadOnlyList<Obligation> Obligations => _obligations;

    public Obligation Add(
        ObligationKind kind,
        string functionId,
        string description,
        ExpressionNode condition,
        TextSpan span)
    {
        var id = $"obl_{_nextId++}";
        var obligation = new Obligation(id, kind, functionId, description, condition, span);
        _obligations.Add(obligation);
        return obligation;
    }

    public IReadOnlyList<Obligation> GetByFunction(string functionId)
        => _obligations.Where(o => o.FunctionId == functionId).ToList();

    public IReadOnlyList<Obligation> GetFailed()
        => _obligations.Where(o => o.Status == ObligationStatus.Failed).ToList();

    public IReadOnlyList<Obligation> GetByStatus(ObligationStatus status)
        => _obligations.Where(o => o.Status == status).ToList();

    public ObligationSummary GetSummary()
        => new(
            Total: _obligations.Count,
            Discharged: _obligations.Count(o => o.Status == ObligationStatus.Discharged),
            Failed: _obligations.Count(o => o.Status == ObligationStatus.Failed),
            Timeout: _obligations.Count(o => o.Status == ObligationStatus.Timeout),
            Boundary: _obligations.Count(o => o.Status == ObligationStatus.Boundary),
            Pending: _obligations.Count(o => o.Status == ObligationStatus.Pending),
            Unsupported: _obligations.Count(o => o.Status == ObligationStatus.Unsupported));
}
