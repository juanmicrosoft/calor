using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Microsoft.Z3;

namespace Calor.Compiler.Verification;

/// <summary>
/// Closed proof-status vocabulary for every verification outcome the toolchain reports.
/// This is the envelope-facing enum (envelope schema v1): timeouts are distinguished from
/// genuine unknowns, and unsupported constructs are never silently conflated with either.
/// </summary>
public enum ProofStatus
{
    /// <summary>The obligation was proven to hold.</summary>
    Proven,

    /// <summary>The obligation was proven violable; a counterexample is attached when the solver produced a model.</summary>
    Refuted,

    /// <summary>The solver returned an inconclusive verdict that was not a timeout (too complex, incomplete theory, solver error, or solver unavailable).</summary>
    Unknown,

    /// <summary>The solver hit the configured time budget before reaching a verdict.</summary>
    Timeout,

    /// <summary>The obligation could not be translated to the solver (unsupported type or construct).</summary>
    Unsupported
}

/// <summary>A single variable assignment inside a counterexample model.</summary>
public sealed record CounterexampleBinding(string Name, string Value);

/// <summary>
/// A concrete Z3 model captured at refutation time, kept structured so envelopes can
/// carry machine-readable bindings rather than a pre-rendered string.
/// </summary>
public sealed record Counterexample(IReadOnlyList<CounterexampleBinding> Bindings)
{
    /// <summary>Renders the model in the legacy "Counterexample: a=1, b=2" form.</summary>
    public string Render()
    {
        if (Bindings.Count == 0)
            return "Counterexample found (values unavailable)";
        return "Counterexample: " + string.Join(", ", Bindings.Select(b => $"{b.Name}={b.Value}"));
    }

    /// <summary>
    /// Evaluates every user-visible variable against a Z3 model. Must be called while the
    /// model is still live (before the solver is disposed or re-checked).
    /// </summary>
    public static Counterexample FromModel(
        Model model,
        IReadOnlyDictionary<string, (Expr Expr, string Type)> variables)
    {
        var bindings = new List<CounterexampleBinding>();
        foreach (var (name, (expr, _)) in variables)
        {
            // Internal solver variables carry no meaning for the user-facing model
            if (name.Contains('$') || name.StartsWith("__"))
                continue;

            try
            {
                var value = model.Evaluate(expr, true);
                bindings.Add(new CounterexampleBinding(name, value.ToString()));
            }
            catch (Exception ex)
            {
                bindings.Add(new CounterexampleBinding(name, $"<eval failed: {ex.GetType().Name}>"));
            }
        }
        return new Counterexample(bindings);
    }
}

/// <summary>Whether a SATISFIABLE solver verdict proves or refutes the obligation under test.</summary>
public enum SatPolarity
{
    /// <summary>The solver was asked for a counterexample (negated goal): SAT refutes, UNSAT proves.</summary>
    SatIsRefutation,

    /// <summary>The solver was asked for satisfiability of the goal itself: SAT proves, UNSAT refutes.</summary>
    SatIsProof
}

/// <summary>
/// Raw evidence from a verification attempt, captured at the solver boundary while Z3
/// objects (model, ReasonUnknown) are still valid. Evidence carries no status — status is
/// assigned exclusively by <see cref="ProofOutcome.Assign"/>.
/// </summary>
public readonly struct ProofEvidence
{
    internal enum EvidenceKind
    {
        SolverVerdict,
        SolverError,
        Unsupported,
        SolverUnavailable
    }

    internal EvidenceKind Kind { get; private init; }
    internal Status Check { get; private init; }
    internal SatPolarity Polarity { get; private init; }
    internal Counterexample? Model { get; private init; }
    internal string? ReasonUnknown { get; private init; }
    internal string? Detail { get; private init; }

    /// <summary>
    /// Captures a completed <c>solver.Check()</c>: the verdict, the model when SATISFIABLE,
    /// and the solver's unknown-reason when UNKNOWN. <paramref name="unsatNote"/> describes a
    /// refutation that has no model (an UNSAT refutation under <see cref="SatPolarity.SatIsProof"/>).
    /// </summary>
    public static ProofEvidence SolverVerdict(
        Status check,
        Solver solver,
        IReadOnlyDictionary<string, (Expr Expr, string Type)> variables,
        SatPolarity polarity,
        string? unsatNote = null)
    {
        return new ProofEvidence
        {
            Kind = EvidenceKind.SolverVerdict,
            Check = check,
            Polarity = polarity,
            Model = check == Status.SATISFIABLE && polarity == SatPolarity.SatIsRefutation
                ? Counterexample.FromModel(solver.Model, variables)
                : null,
            ReasonUnknown = check == Status.UNKNOWN ? SafeReasonUnknown(solver) : null,
            Detail = unsatNote
        };
    }

    /// <summary>Captures a thrown <see cref="Z3Exception"/>.</summary>
    public static ProofEvidence SolverError(Z3Exception ex) => new()
    {
        Kind = EvidenceKind.SolverError,
        Detail = $"Z3 solver error: {ex.Message}"
    };

    /// <summary>Captures a translation failure or undeclarable type.</summary>
    public static ProofEvidence Unsupported(string reason) => new()
    {
        Kind = EvidenceKind.Unsupported,
        Detail = reason
    };

    /// <summary>Captures "no solver available" (Z3 missing or disabled).</summary>
    public static ProofEvidence SolverUnavailable(string reason) => new()
    {
        Kind = EvidenceKind.SolverUnavailable,
        Detail = reason
    };

    private static string? SafeReasonUnknown(Solver solver)
    {
        try
        {
            return solver.ReasonUnknown;
        }
        catch (Z3Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// The single choke point for verification status assignment (loop plan D1.2). Every
/// solver-evidence outcome in the compiler — contracts, obligations, implication proofs —
/// is a <see cref="ProofOutcome"/> produced by <see cref="Assign"/>; the constructor is
/// private and a conformance test enforces that <c>new ProofOutcome</c> appears nowhere
/// outside this file. Precisely stated, this file has three status-producing entry
/// points, not one: <see cref="Assign"/> (the only one that maps solver evidence),
/// plus <see cref="Rehydrate"/> and <see cref="FromLegacyContractStatus"/>, which
/// restore previously-assigned statuses from persistence and carry no evidence of
/// their own. Callers must never route fresh solver results through the latter two.
/// </summary>
public sealed class ProofOutcome
{
    public ProofStatus Status { get; }

    /// <summary>Concrete model; non-null only when <see cref="Status"/> is <see cref="ProofStatus.Refuted"/> and the solver produced one.</summary>
    public Counterexample? Counterexample { get; }

    /// <summary>Human-readable detail: unsupported-construct diagnosis, solver error, unknown-reason, or model-less refutation note.</summary>
    public string? Reason { get; }

    private ProofOutcome(ProofStatus status, Counterexample? counterexample, string? reason)
    {
        Status = status;
        Counterexample = counterexample;
        Reason = reason;
    }

    /// <summary>
    /// The one status-assigning function. Maps raw solver evidence onto the closed
    /// five-status vocabulary; in particular, UNKNOWN verdicts are split into
    /// <see cref="ProofStatus.Timeout"/> vs <see cref="ProofStatus.Unknown"/> using the
    /// solver's own unknown-reason.
    /// </summary>
    public static ProofOutcome Assign(ProofEvidence evidence)
    {
        switch (evidence.Kind)
        {
            case ProofEvidence.EvidenceKind.Unsupported:
                return new ProofOutcome(ProofStatus.Unsupported, null, evidence.Detail);

            case ProofEvidence.EvidenceKind.SolverError:
            case ProofEvidence.EvidenceKind.SolverUnavailable:
                return new ProofOutcome(ProofStatus.Unknown, null, evidence.Detail);

            case ProofEvidence.EvidenceKind.SolverVerdict:
                switch (evidence.Check)
                {
                    case Microsoft.Z3.Status.UNSATISFIABLE:
                        return evidence.Polarity == SatPolarity.SatIsProof
                            ? new ProofOutcome(ProofStatus.Refuted, null, evidence.Detail)
                            : new ProofOutcome(ProofStatus.Proven, null, null);

                    case Microsoft.Z3.Status.SATISFIABLE:
                        return evidence.Polarity == SatPolarity.SatIsRefutation
                            ? new ProofOutcome(ProofStatus.Refuted, evidence.Model, evidence.Detail)
                            : new ProofOutcome(ProofStatus.Proven, null, null);

                    default:
                        return IsTimeoutReason(evidence.ReasonUnknown)
                            ? new ProofOutcome(ProofStatus.Timeout, null, evidence.ReasonUnknown)
                            : new ProofOutcome(ProofStatus.Unknown, null, evidence.ReasonUnknown);
                }

            default:
                return new ProofOutcome(ProofStatus.Unknown, null, evidence.Detail);
        }
    }

    private static bool IsTimeoutReason(string? reasonUnknown)
    {
        if (string.IsNullOrEmpty(reasonUnknown))
            return false;
        return reasonUnknown.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || reasonUnknown.Contains("canceled", StringComparison.OrdinalIgnoreCase)
            || reasonUnknown.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
            || reasonUnknown.Contains("resource limit", StringComparison.OrdinalIgnoreCase)
            || reasonUnknown.Contains("max. resource", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Envelope wire name for the status: proven|refuted|unknown|timeout|unsupported.</summary>
    public string StatusName => Status switch
    {
        ProofStatus.Proven => "proven",
        ProofStatus.Refuted => "refuted",
        ProofStatus.Timeout => "timeout",
        ProofStatus.Unsupported => "unsupported",
        _ => "unknown"
    };

    /// <summary>Legacy description string (counterexample rendering, or the reason detail).</summary>
    public string? Describe() => Counterexample?.Render() ?? Reason;

    /// <summary>Single mapping site onto the legacy contract enum.</summary>
    public ContractVerificationStatus ToContractStatus() => Status switch
    {
        ProofStatus.Proven => ContractVerificationStatus.Proven,
        ProofStatus.Refuted => ContractVerificationStatus.Disproven,
        ProofStatus.Unsupported => ContractVerificationStatus.Unsupported,
        _ => ContractVerificationStatus.Unproven
    };

    /// <summary>Single mapping site onto the legacy obligation enum.</summary>
    public ObligationStatus ToObligationStatus() => Status switch
    {
        ProofStatus.Proven => ObligationStatus.Discharged,
        ProofStatus.Refuted => ObligationStatus.Failed,
        ProofStatus.Unsupported => ObligationStatus.Unsupported,
        _ => ObligationStatus.Timeout
    };

    /// <summary>Single mapping site onto the legacy implication enum.</summary>
    public ImplicationStatus ToImplicationStatus() => Status switch
    {
        ProofStatus.Proven => ImplicationStatus.Proven,
        ProofStatus.Refuted => ImplicationStatus.Disproven,
        ProofStatus.Unsupported => ImplicationStatus.Unsupported,
        _ => ImplicationStatus.Unknown
    };

    /// <summary>
    /// Rehydrates a persisted outcome (verification cache, telemetry replay). This is
    /// deserialization, not status assignment — the status being rehydrated was originally
    /// assigned by <see cref="Assign"/>. Unrecognized status names rehydrate as
    /// <see cref="ProofStatus.Unknown"/> rather than throwing.
    /// </summary>
    public static ProofOutcome Rehydrate(
        string statusName,
        IReadOnlyList<CounterexampleBinding>? counterexampleBindings,
        string? reason)
    {
        var status = statusName?.ToLowerInvariant() switch
        {
            "proven" => ProofStatus.Proven,
            "refuted" => ProofStatus.Refuted,
            "timeout" => ProofStatus.Timeout,
            "unsupported" => ProofStatus.Unsupported,
            _ => ProofStatus.Unknown
        };

        var counterexample = status == ProofStatus.Refuted && counterexampleBindings is { Count: > 0 }
            ? new Counterexample(counterexampleBindings)
            : null;

        return new ProofOutcome(status, counterexample, reason);
    }

    /// <summary>
    /// Reconstructs an outcome from a legacy contract status (verification-cache entries
    /// predating the outcome field). Lossy by construction: legacy Unproven cannot be split
    /// into unknown vs timeout after the fact.
    /// </summary>
    public static ProofOutcome FromLegacyContractStatus(ContractVerificationStatus status, string? description)
    {
        return status switch
        {
            ContractVerificationStatus.Proven => new ProofOutcome(ProofStatus.Proven, null, null),
            ContractVerificationStatus.Disproven => new ProofOutcome(ProofStatus.Refuted, null, description),
            ContractVerificationStatus.Unsupported => new ProofOutcome(ProofStatus.Unsupported, null, description),
            ContractVerificationStatus.Skipped => new ProofOutcome(ProofStatus.Unknown, null, description ?? "Verification skipped (solver unavailable)"),
            _ => new ProofOutcome(ProofStatus.Unknown, null, description)
        };
    }
}
