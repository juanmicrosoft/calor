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

    /// <summary>
    /// The obligation holds conditionally on a named, per-proof assumption set
    /// (exceptional-path totality, callee summaries, aliasing assumptions).
    /// Transitive; listed per-proof; never aggregates into Proven; never elides
    /// runtime checks (strategy §5.1, guarantees plan D-G2.1).
    /// </summary>
    Assumed,

    /// <summary>The solver returned an inconclusive verdict that was not a timeout (too complex, incomplete theory, or solver error).</summary>
    Unknown,

    /// <summary>The solver hit the configured time budget before reaching a verdict.</summary>
    Timeout,

    /// <summary>The obligation could not be translated to the solver (unsupported type or construct).</summary>
    Unsupported,

    /// <summary>
    /// No solver was available to attempt the obligation (Z3 missing or disabled).
    /// Split from Unknown (guarantees plan D-G2.2): "the solver gave up" and
    /// "there was no solver" are different facts with different remedies.
    /// </summary>
    Unavailable
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
        SolverUnavailable,
        VacuousProof,
        AssumedProof
    }

    internal EvidenceKind Kind { get; private init; }
    internal Status Check { get; private init; }
    internal SatPolarity Polarity { get; private init; }
    internal Counterexample? Model { get; private init; }
    internal string? ReasonUnknown { get; private init; }
    internal string? Detail { get; private init; }
    internal IReadOnlyList<string>? AssumptionList { get; private init; }

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

    /// <summary>
    /// Captures a vacuous proof: the assumption set (e.g. the precondition set of a
    /// postcondition obligation) is itself unsatisfiable, so the obligation holds only
    /// because no valid call exists. Maps to Proven with <see cref="ProofOutcome.IsVacuous"/>
    /// set — a vacuous proof never justifies eliding the runtime check.
    /// </summary>
    public static ProofEvidence VacuousProof(string reason) => new()
    {
        Kind = EvidenceKind.VacuousProof,
        Detail = reason
    };

    /// <summary>
    /// Captures a proof that holds conditionally on a named assumption set: the solver
    /// verdict was UNSAT-on-negation, but only under assumptions the solver did not
    /// discharge (e.g. exceptional-path totality). Maps to <see cref="ProofStatus.Assumed"/>;
    /// never elides runtime checks and never aggregates into Proven.
    /// </summary>
    public static ProofEvidence AssumedProof(string reason, IReadOnlyList<string> assumptions) => new()
    {
        Kind = EvidenceKind.AssumedProof,
        Detail = reason,
        AssumptionList = assumptions
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

    /// <summary>
    /// True when <see cref="Status"/> is <see cref="ProofStatus.Proven"/> but the proof is
    /// vacuous — the obligation's assumption set is unsatisfiable, so it holds only because
    /// no valid call exists. A vacuous proof never justifies eliding the runtime check
    /// (guarantees plan D-G1.3; strategy §5.1).
    /// </summary>
    public bool IsVacuous { get; }

    /// <summary>
    /// The named assumption set an <see cref="ProofStatus.Assumed"/> proof is conditional
    /// on. Empty for every other status. Stored sorted so the set has one canonical form
    /// (content-addressing: equal sets hash equal — strategy §5.1 keying).
    /// </summary>
    public IReadOnlyList<string> Assumptions { get; }

    private ProofOutcome(
        ProofStatus status,
        Counterexample? counterexample,
        string? reason,
        bool isVacuous = false,
        IReadOnlyList<string>? assumptions = null)
    {
        Status = status;
        Counterexample = counterexample;
        Reason = reason;
        IsVacuous = isVacuous;
        Assumptions = status == ProofStatus.Assumed && assumptions is { Count: > 0 }
            ? assumptions.OrderBy(a => a, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();
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

            case ProofEvidence.EvidenceKind.VacuousProof:
                return new ProofOutcome(ProofStatus.Proven, null, evidence.Detail, isVacuous: true);

            case ProofEvidence.EvidenceKind.AssumedProof:
                return new ProofOutcome(ProofStatus.Assumed, null, evidence.Detail, assumptions: evidence.AssumptionList);

            case ProofEvidence.EvidenceKind.SolverError:
                return new ProofOutcome(ProofStatus.Unknown, null, evidence.Detail);

            case ProofEvidence.EvidenceKind.SolverUnavailable:
                return new ProofOutcome(ProofStatus.Unavailable, null, evidence.Detail);

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
        ProofStatus.Assumed => "assumed",
        ProofStatus.Timeout => "timeout",
        ProofStatus.Unsupported => "unsupported",
        ProofStatus.Unavailable => "unavailable",
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
        // Frozen rule (guarantees plan D-G2.2): Assumed NEVER maps to a legacy
        // Proven-equivalent in any consumer — a conditional proof must not elide
        // checks or count as proven anywhere downstream.
        ProofStatus.Assumed => ContractVerificationStatus.Unproven,
        ProofStatus.Unavailable => ContractVerificationStatus.Skipped,
        _ => ContractVerificationStatus.Unproven
    };

    /// <summary>Single mapping site onto the legacy obligation enum.</summary>
    public ObligationStatus ToObligationStatus() => Status switch
    {
        ProofStatus.Proven => ObligationStatus.Discharged,
        ProofStatus.Refuted => ObligationStatus.Failed,
        ProofStatus.Unsupported => ObligationStatus.Unsupported,
        // Assumed/Unavailable land in the legacy inconclusive bucket — never
        // Discharged (the D-G2.2 frozen rule).
        _ => ObligationStatus.Timeout
    };

    /// <summary>Single mapping site onto the legacy implication enum.</summary>
    public ImplicationStatus ToImplicationStatus() => Status switch
    {
        ProofStatus.Proven => ImplicationStatus.Proven,
        ProofStatus.Refuted => ImplicationStatus.Disproven,
        ProofStatus.Unsupported => ImplicationStatus.Unsupported,
        // Assumed/Unavailable are Unknown to the legacy implication consumer —
        // never Proven (the D-G2.2 frozen rule).
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
        string? reason,
        bool isVacuous = false,
        IReadOnlyList<string>? assumptions = null)
    {
        var status = statusName?.ToLowerInvariant() switch
        {
            "proven" => ProofStatus.Proven,
            "refuted" => ProofStatus.Refuted,
            "assumed" => ProofStatus.Assumed,
            "timeout" => ProofStatus.Timeout,
            "unsupported" => ProofStatus.Unsupported,
            "unavailable" => ProofStatus.Unavailable,
            _ => ProofStatus.Unknown
        };

        var counterexample = status == ProofStatus.Refuted && counterexampleBindings is { Count: > 0 }
            ? new Counterexample(counterexampleBindings)
            : null;

        return new ProofOutcome(
            status, counterexample, reason,
            isVacuous && status == ProofStatus.Proven,
            assumptions);
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
            ContractVerificationStatus.Skipped => new ProofOutcome(ProofStatus.Unavailable, null, description ?? "Verification skipped (solver unavailable)"),
            _ => new ProofOutcome(ProofStatus.Unknown, null, description)
        };
    }
}
