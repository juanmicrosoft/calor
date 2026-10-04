using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Calor.Compiler.Ast;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Status of an implication proof attempt.
/// </summary>
public enum ImplicationStatus
{
    /// <summary>
    /// Implication is valid (UNSAT - no counterexample exists).
    /// </summary>
    Proven,

    /// <summary>
    /// Implication is invalid (SAT - counterexample found).
    /// </summary>
    Disproven,

    /// <summary>
    /// Could not determine validity (timeout or complexity).
    /// </summary>
    Unknown,

    /// <summary>
    /// Expression contains unsupported constructs (strings, floats, function calls).
    /// </summary>
    Unsupported
}

/// <summary>
/// Result of an implication proof attempt.
/// </summary>
/// <param name="Status">The status of the proof attempt.</param>
/// <param name="CounterexampleDescription">Description of a counterexample if Status is Disproven.</param>
/// <param name="Duration">Time taken to perform the proof.</param>
/// <param name="Outcome">The choke-point outcome (five-status vocabulary + structured counterexample).</param>
public record ImplicationResult(
    ImplicationStatus Status,
    string? CounterexampleDescription = null,
    TimeSpan? Duration = null,
    ProofOutcome? Outcome = null)
{
    /// <summary>Builds a result whose legacy fields are derived from the choke-point outcome.</summary>
    public static ImplicationResult FromOutcome(ProofOutcome outcome, TimeSpan? Duration = null)
        => new(outcome.ToImplicationStatus(), outcome.Describe(), Duration, outcome);
}

/// <summary>
/// Proves contract implications using Z3 SMT solving.
/// Used for LSP enforcement during contract inheritance checking.
/// </summary>
/// <remarks>
/// This prover checks if one contract implies another using the formula:
///   (A AND NOT(C)) is UNSAT
///
/// If UNSAT: A implies C (implication is proven)
/// If SAT: A does not imply C (counterexample found)
/// If UNKNOWN: Cannot determine (timeout or complexity)
///
/// For LSP checking:
/// - Preconditions: P_interface → P_implementer (implementer must accept at least what interface accepts)
/// - Postconditions: Q_implementer → Q_interface (implementer must guarantee at least what interface guarantees)
/// </remarks>
public sealed class Z3ImplicationProver : IDisposable
{
    private readonly Context _ctx;
    private readonly uint _timeoutMs;
    private bool _disposed;

    /// <summary>
    /// Whether the contracts run under checked integer arithmetic (the module default).
    /// When true, checked overflow is a throw: it makes the required contract fail and excludes the
    /// input from the assumed contract. Set it to the module's policy (the inheritance checker and
    /// the weakening command do); the default is the checked module default.
    /// </summary>
    public bool CheckIntegerOverflow { get; set; } = true;

    /// <summary>
    /// Creates a new implication prover with the given Z3 context.
    /// </summary>
    /// <param name="ctx">The Z3 context to use for proving.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 5000ms).</param>
    public Z3ImplicationProver(Context ctx, uint timeoutMs = VerificationOptions.DefaultTimeoutMs)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Proves whether the antecedent implies the consequent.
    /// Uses the formula: (A AND NOT(C)) is UNSAT means A → C.
    /// </summary>
    /// <param name="parameters">The parameters with their names and types.</param>
    /// <param name="antecedent">The antecedent expression (A in A → C).</param>
    /// <param name="consequent">The consequent expression (C in A → C).</param>
    /// <returns>The result of the proof attempt.</returns>
    public ImplicationResult ProveImplication(
        IReadOnlyList<(string Name, string Type)> parameters,
        ExpressionNode antecedent,
        ExpressionNode consequent)
    {
        var sw = Stopwatch.StartNew();

        var translator = new ContractTranslator(_ctx);

        // Declare the parameters the contracts read (#1413: an unused string, array, or
        // user-type parameter must not put a reference sort into an integer-only query).
        var referenced = ReferencedRoots(antecedent, consequent);
        foreach (var (name, type) in parameters)
        {
            if (!referenced.Contains(name))
                continue;
            if (!translator.DeclareVariable(name, type))
            {
                // Unsupported parameter type (strings, floats, etc.)
                return ImplicationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        ContractTranslator.DiagnoseUnsupportedType(type))),
                    Duration: sw.Elapsed);
            }
        }

        // Also declare 'result' in case contracts reference it — but only when
        // the caller has not already declared it with the real output type
        // (#826 review C4: the unconditional i32 default silently overwrote a
        // caller-supplied i64/bool result, producing false DETERMINATE
        // weakening verdicts for non-i32 outputs).
        if (!translator.Variables.ContainsKey("result"))
        {
            translator.DeclareVariable("result", "i32");
        }

        // Translate antecedent → Z3 BoolExpr A
        var antecedentExpr = translator.TranslateBoolExpr(antecedent);
        if (antecedentExpr == null)
        {
            return ImplicationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    translator.DiagnoseTranslationFailure(antecedent)
                    ?? "Antecedent could not be translated to Z3")),
                Duration: sw.Elapsed);
        }

        // Translate consequent → Z3 BoolExpr C
        var consequentExpr = translator.TranslateBoolExpr(consequent);
        if (consequentExpr == null)
        {
            return ImplicationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    translator.DiagnoseTranslationFailure(consequent)
                    ?? "Consequent could not be translated to Z3")),
                Duration: sw.Elapsed);
        }

        return Solve(translator, antecedent, antecedentExpr, consequent, consequentExpr, sw);
    }

    /// <summary>
    /// Decides A → C, where A (an assumed contract) and C (a contract that must then hold) are
    /// runtime-evaluated C# expressions. The solver's terms are total — x/0 has a value,
    /// arithmetic wraps, strings and arrays are never null — while at runtime a contract can
    /// throw (a zero or MinValue÷−1 divisor, checked overflow, a null receiver). #1413 (S1 rows
    /// IMPL-ASSUMPTION-FORMS, IMPL-DIVISION-TOTALIZED): the question is asked with definedness
    /// made explicit. An input matters only when A completes and is true; there C must
    /// complete and be true. So the query is <c>A ∧ D(A) ∧ ¬(D(C) ∧ C)</c>, where D(e) is e's
    /// divisor and (checked module) overflow side conditions:
    /// <list type="bullet">
    /// <item>UNSAT: proven — unless the query touched a string, array, or user-type sort,
    /// whose null the solver cannot represent (then Assumed), or D(C) could not be modeled
    /// (then Assumed: C may throw where the solver cannot see it).</item>
    /// <item>SAT: refuted, and the model is a real input where A completes true and C throws
    /// or is false — unless D(A) or D(C) could not be modeled, in which case the model may
    /// describe an input where A throws: Unsupported, no counterexample claimed.</item>
    /// </list>
    /// When D(A) cannot be modeled it is omitted; A alone over-approximates "A completes and
    /// is true", so an UNSAT verdict stays sound.
    /// </summary>
    private ImplicationResult Solve(
        ContractTranslator translator,
        ExpressionNode antecedent,
        BoolExpr antecedentExpr,
        ExpressionNode consequent,
        BoolExpr consequentExpr,
        Stopwatch sw)
    {
        try
        {
            // D() is exact only for quantifier-free contracts; under a quantifier it is a sufficient
            // condition (safety for every bound value), which must never restrict the antecedent and
            // never back a refutation (#1413 review round 2).
            var antecedentExact = !ContainsQuantifier(antecedent);
            var consequentExact = !ContainsQuantifier(consequent);
            string? antecedentFailure = "the assumed contract quantifies, so its definedness is not modeled exactly";
            var antecedentDefined = antecedentExact ? Definedness(translator, antecedent, out antecedentFailure) : null;
            var consequentDefined = Definedness(translator, consequent, out var consequentFailure);

            using var solver = new IsolatedSolver(_ctx, _timeoutMs);
            solver.Assert(antecedentExpr);
            if (antecedentDefined != null)
                solver.Assert(antecedentDefined);
            solver.Assert(_ctx.MkNot(consequentDefined != null
                ? _ctx.MkAnd(consequentDefined, consequentExpr)
                : consequentExpr));

            var status = solver.Check();
            // #1413 review round 3: string ranges, array bounds, and null receivers are total in the
            // solver, so a model over them may be an input where a contract throws.
            if (status == Status.SATISFIABLE && (translator.TouchedStringTheory || translator.TouchedNullableReferenceSort))
                antecedentFailure = "a contract reads a string, array, or user-type value whose throwing operations (index bounds, substring ranges, null receivers) are not modeled";
            if (status == Status.SATISFIABLE && (antecedentDefined == null || consequentDefined == null || !consequentExact
                || translator.TouchedStringTheory || translator.TouchedNullableReferenceSort))
            {
                return ImplicationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        $"No counterexample established: {antecedentFailure ?? consequentFailure ?? "the required contract quantifies, so its definedness is not modeled exactly"}, so the "
                        + "solver's model may be an input where a contract throws. Implication not established.")),
                    Duration: sw.Elapsed);
            }
            if (status != Status.UNSATISFIABLE)
            {
                return ImplicationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.SolverVerdict(
                        status, solver.CheckedSolver, solver.TranslateVariables(translator.Variables),
                        SatPolarity.SatIsRefutation)),
                    Duration: sw.Elapsed);
            }

            var assumptions = new List<string>();
            var reasons = new List<string>();
            if (consequentDefined == null)
            {
                assumptions.Add(consequentFailure!.Contains("overflow", StringComparison.Ordinal)
                    ? Z3Verifier.CheckedArithmeticAssumption
                    : Z3Verifier.ContractExpressionDivisionAssumption);
                reasons.Add($"the required contract does not throw ({consequentFailure})");
            }
            if (translator.TouchedStringTheory)
            {
                assumptions.Add(Z3Verifier.StringModelAssumption);
                reasons.Add("no string is null or non-ASCII (the solver's strings are non-null and differently counted, D3/D12)");
            }
            if (translator.TouchedNullableReferenceSort)
            {
                assumptions.Add(Z3Verifier.NullableReferenceModelAssumption);
                reasons.Add("no array or user-type reference is null (the solver's sorts are non-null, D14)");
            }
            // A literal `true` requirement (e.g. no contract on that side) cannot throw and
            // holds for every input, including those the solver cannot represent.
            if (assumptions.Count > 0 && consequent is not BoolLiteralNode { Value: true })
            {
                return ImplicationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.AssumedProof(
                        $"The implication holds only if {string.Join("; and ", reasons)}, which the contracts do not guarantee. Not established.",
                        assumptions)),
                    Duration: sw.Elapsed);
            }

            return ImplicationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.SolverVerdict(
                    status, solver.CheckedSolver, solver.TranslateVariables(translator.Variables),
                    SatPolarity.SatIsRefutation)),
                Duration: sw.Elapsed);
        }
        catch (Z3Exception ex)
        {
            return ImplicationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.SolverError(ex)),
                Duration: sw.Elapsed);
        }
    }

    /// <summary>
    /// The condition under which <paramref name="contract"/> completes without throwing:
    /// divisor side conditions and, in a checked module, overflow safety. Null (with
    /// <paramref name="failure"/>) when a condition cannot be modeled, e.g. a divisor in a
    /// conditionally evaluated position.
    /// </summary>
    private BoolExpr? Definedness(ContractTranslator translator, ExpressionNode contract, out string? failure)
    {
        var (divisors, divisorFailure) =
            FunctionBodyEncoder.CollectDivisorNonZeroConstraintsFromExpression(translator, _ctx, contract);
        if (divisorFailure != null)
        {
            failure = divisorFailure;
            return null;
        }
        var overflow = CheckIntegerOverflow ? translator.GetCheckedArithmeticSafety(contract) : _ctx.MkTrue();
        if (overflow == null)
        {
            failure = "the contract's checked overflow could not be modeled";
            return null;
        }
        failure = null;
        return _ctx.MkAnd(divisors.Append(overflow).ToArray());
    }

    private static bool ContainsQuantifier(AstNode node)
        => node is ForallExpressionNode or ExistsExpressionNode
           || Analysis.RecursiveAstWalker.GetAllChildren(node).Any(ContainsQuantifier);

    /// <summary>The variable names (dotted paths by their root) two contracts read.</summary>
    private static HashSet<string> ReferencedRoots(params ExpressionNode[] contracts)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Walk(AstNode node)
        {
            if (node is ReferenceNode reference)
            {
                names.Add(reference.Name);
                names.Add(reference.Name.Split('.')[0]);
            }
            foreach (var child in Analysis.RecursiveAstWalker.GetAllChildren(node))
                Walk(child);
        }
        foreach (var contract in contracts)
            Walk(contract);
        return names;
    }

    /// <summary>
    /// Checks if a precondition weakening is valid for LSP.
    /// The implementer's precondition must be weaker or equal to the interface's precondition.
    /// This means: P_interface → P_implementer (interface precondition implies implementer precondition).
    /// </summary>
    /// <param name="parameters">The method parameters.</param>
    /// <param name="interfacePrecondition">The interface's precondition (must be implied by).</param>
    /// <param name="implementerPrecondition">The implementer's precondition (must be implied).</param>
    /// <returns>The result of the LSP check.</returns>
    public ImplicationResult CheckPreconditionWeakening(
        IReadOnlyList<(string Name, string Type)> parameters,
        ExpressionNode interfacePrecondition,
        ExpressionNode implementerPrecondition)
    {
        // For LSP: interface precondition must imply implementer precondition
        // i.e., anything that satisfies the interface precondition should also satisfy the implementer's
        // This means the implementer can only accept MORE inputs (weaker precondition)
        return ProveImplication(parameters, interfacePrecondition, implementerPrecondition);
    }

    /// <summary>
    /// Checks if a postcondition strengthening is valid for LSP.
    /// The implementer's postcondition must be stronger or equal to the interface's postcondition.
    /// This means: Q_implementer → Q_interface (implementer postcondition implies interface postcondition).
    /// </summary>
    /// <param name="parameters">The method parameters.</param>
    /// <param name="outputType">The return type of the method.</param>
    /// <param name="interfacePostcondition">The interface's postcondition (must be implied).</param>
    /// <param name="implementerPostcondition">The implementer's postcondition (must imply).</param>
    /// <returns>The result of the LSP check.</returns>
    public ImplicationResult CheckPostconditionStrengthening(
        IReadOnlyList<(string Name, string Type)> parameters,
        string? outputType,
        ExpressionNode interfacePostcondition,
        ExpressionNode implementerPostcondition,
        ExpressionNode? interfacePrecondition = null)
    {
        var sw = Stopwatch.StartNew();

        // #1413 review round 2: the interface guarantees its postcondition only on inputs its
        // precondition accepts (completes true), so the precondition joins the assumptions; as
        // part of the required contract its throws would read as broken guarantees.
        if (interfacePrecondition is not null and not BoolLiteralNode { Value: true })
        {
            implementerPostcondition = new BinaryOperationNode(
                implementerPostcondition.Span, BinaryOperator.And, interfacePrecondition, implementerPostcondition);
        }

        var translator = new ContractTranslator(_ctx);

        // Declare the parameters the contracts read (see ProveImplication).
        var referenced = ReferencedRoots(interfacePostcondition, implementerPostcondition);
        foreach (var (name, type) in parameters)
        {
            if (!referenced.Contains(name))
                continue;
            if (!translator.DeclareVariable(name, type))
            {
                return ImplicationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        ContractTranslator.DiagnoseUnsupportedType(type))),
                    Duration: sw.Elapsed);
            }
        }

        // Declare 'result' variable for postconditions
        if (!referenced.Contains("result"))
        {
            // Neither contract reads the result: no result variable.
        }
        else if (!string.IsNullOrEmpty(outputType))
        {
            if (!translator.DeclareVariable("result", outputType))
            {
                return ImplicationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        ContractTranslator.DiagnoseUnsupportedType(outputType))),
                    Duration: sw.Elapsed);
            }
        }
        else
        {
            // Default to i32 if no output type specified
            translator.DeclareVariable("result", "i32");
        }

        // Translate implementer postcondition → Z3 BoolExpr (the antecedent)
        var implementerExpr = translator.TranslateBoolExpr(implementerPostcondition);
        if (implementerExpr == null)
        {
            return ImplicationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    translator.DiagnoseTranslationFailure(implementerPostcondition)
                    ?? "Implementer postcondition could not be translated to Z3")),
                Duration: sw.Elapsed);
        }

        // Translate interface postcondition → Z3 BoolExpr (the consequent)
        var interfaceExpr = translator.TranslateBoolExpr(interfacePostcondition);
        if (interfaceExpr == null)
        {
            return ImplicationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    translator.DiagnoseTranslationFailure(interfacePostcondition)
                    ?? "Interface postcondition could not be translated to Z3")),
                Duration: sw.Elapsed);
        }

        // For LSP: implementer postcondition must imply interface postcondition
        // i.e., anything the implementer guarantees should also satisfy what the interface guarantees
        // This means the implementer can only guarantee MORE (stronger postcondition)
        return Solve(translator, implementerPostcondition, implementerExpr, interfacePostcondition, interfaceExpr, sw);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Context is managed externally, don't dispose it here
            _disposed = true;
        }
    }
}
