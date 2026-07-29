using System.Diagnostics;
using System.Text;
using Calor.Compiler.Ast;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Core Z3 verification logic for Calor contracts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread Safety:</b> This class is NOT thread-safe. Each verification operation creates
/// and uses a new <see cref="ContractTranslator"/> instance internally, but the Z3
/// <see cref="Microsoft.Z3.Context"/> is shared and Z3 contexts are not thread-safe by default.
/// For concurrent verification, either create separate Z3Verifier instances with separate
/// Z3 contexts, or synchronize access externally.
/// </para>
/// </remarks>
public sealed class Z3Verifier : IDisposable
{
    private readonly Context _ctx;
    private readonly uint _timeoutMs;
    private bool _disposed;

    public Z3Verifier(Context ctx, uint timeoutMs = VerificationOptions.DefaultTimeoutMs)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Verifies a precondition contract.
    /// For preconditions, we check if the precondition itself is satisfiable.
    /// If it's never satisfiable, the function can never be called correctly.
    /// </summary>
    public ContractVerificationResult VerifyPrecondition(
        IReadOnlyList<(string Name, string Type)> parameters,
        RequiresNode precondition)
    {
        var sw = Stopwatch.StartNew();

        var translator = new ContractTranslator(_ctx);

        // Declare all parameters
        foreach (var (name, type) in parameters)
        {
            if (!translator.DeclareVariable(name, type))
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        ContractTranslator.DiagnoseUnsupportedType(type))),
                    Duration: sw.Elapsed);
            }
        }

        // Translate the precondition
        var preconditionExpr = translator.TranslateBoolExpr(precondition.Condition);
        if (preconditionExpr == null)
        {
            var diagnostic = translator.DiagnoseBoolExprFailure(precondition.Condition)
                ?? translator.DiagnoseTranslationFailure(precondition.Condition)
                ?? "Unknown translation failure in precondition";
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(diagnostic)),
                Duration: sw.Elapsed);
        }

        // For preconditions, we just check if they're satisfiable
        // (i.e., there exists some input that satisfies them)
        // This is informational - preconditions are always kept as runtime checks
        try
        {
            var solver = _ctx.MkSolver();
            solver.Set("timeout", _timeoutMs);
            solver.Assert(preconditionExpr);

            var status = solver.Check();
            var warnings = translator.Warnings.Count > 0 ? translator.Warnings.ToList() : null;

            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.SolverVerdict(
                    status, solver, translator.Variables, SatPolarity.SatIsProof,
                    unsatNote: "Precondition is never satisfiable - function can never be called correctly")),
                warnings,
                sw.Elapsed);
        }
        catch (Z3Exception ex)
        {
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.SolverError(ex)),
                Duration: sw.Elapsed);
        }
    }

    /// <summary>
    /// Verifies a postcondition contract given preconditions.
    /// </summary>
    /// <remarks>
    /// The verification logic:
    /// 1. Assume all preconditions hold (after a vacuity pre-check: an unsatisfiable
    ///    precondition set yields Proven(vacuous) — never a proof that elides checks).
    /// 2. When the postcondition references <c>result</c>, bind it to the function body:
    ///    assert <c>result == encode(body)</c> for bodies inside the encodable surface
    ///    (return-expression and if/else-return compositions). Bodies outside it make the
    ///    obligation Unsupported — never Refuted against an unconstrained result (#807).
    /// 3. Check if (preconditions && result-binding && !postcondition) is satisfiable
    ///    - UNSAT → Proven (no counterexample exists, postcondition always holds when preconditions hold)
    ///    - SAT → Disproven (found a counterexample)
    ///    - UNKNOWN → Unproven (timeout or too complex)
    /// </remarks>
    public ContractVerificationResult VerifyPostcondition(
        IReadOnlyList<(string Name, string Type)> parameters,
        string? outputType,
        IReadOnlyList<RequiresNode> preconditions,
        EnsuresNode postcondition,
        IReadOnlyList<StatementNode>? body = null)
    {
        var sw = Stopwatch.StartNew();

        var translator = new ContractTranslator(_ctx);

        // Declare all parameters
        foreach (var (name, type) in parameters)
        {
            if (!translator.DeclareVariable(name, type))
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        ContractTranslator.DiagnoseUnsupportedType(type))),
                    Duration: sw.Elapsed);
            }
        }

        // Declare 'result' variable for postconditions if there's an output type
        if (!string.IsNullOrEmpty(outputType))
        {
            if (!translator.DeclareVariable("result", outputType))
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        ContractTranslator.DiagnoseUnsupportedType(outputType))),
                    Duration: sw.Elapsed);
            }
        }

        // Translate preconditions
        var preconditionExprs = new List<BoolExpr>();
        foreach (var pre in preconditions)
        {
            var preExpr = translator.TranslateBoolExpr(pre.Condition);
            if (preExpr == null)
            {
                var diagnostic = translator.DiagnoseBoolExprFailure(pre.Condition)
                    ?? translator.DiagnoseTranslationFailure(pre.Condition)
                    ?? "Unknown translation failure in precondition";
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(diagnostic)),
                    Duration: sw.Elapsed);
            }
            preconditionExprs.Add(preExpr);
        }

        // Translate the postcondition
        var postconditionExpr = translator.TranslateBoolExpr(postcondition.Condition);
        if (postconditionExpr == null)
        {
            var diagnostic = translator.DiagnoseBoolExprFailure(postcondition.Condition)
                ?? translator.DiagnoseTranslationFailure(postcondition.Condition)
                ?? "Unknown translation failure in postcondition";
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(diagnostic)),
                Duration: sw.Elapsed);
        }

        // Bind `result` to the function body (guarantees plan D-G1.1, #807). Without this
        // binding a result-referencing postcondition is checked against a free variable and
        // "refuted" with a fabricated model. Bodies outside the encodable surface make the
        // obligation honestly Unsupported instead.
        BoolExpr? resultBinding = null;
        if (!string.IsNullOrEmpty(outputType)
            && FunctionBodyEncoder.ReferencesResult(postcondition.Condition))
        {
            var resultVar = translator.Variables["result"].Expr;
            var (encoded, reason) = FunctionBodyEncoder.TryEncodeResult(translator, _ctx, body);
            if (encoded == null || !encoded.Sort.Equals(resultVar.Sort))
            {
                var why = encoded == null
                    ? reason ?? "the function body is outside the modeled surface"
                    : "the encoded body's solver sort does not match the declared result type";
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        $"Postcondition references 'result' but {why}. Runtime check kept.")),
                    Duration: sw.Elapsed);
            }
            resultBinding = _ctx.MkEq(resultVar, encoded);
        }

        // Create solver and perform verification
        try
        {
            // Vacuity pre-check (guarantees plan D-G1.3): an unsatisfiable precondition set
            // would make the main check UNSAT — a "proof" that holds only because no valid
            // call exists. Detect it and report Proven(vacuous), which never elides checks.
            if (preconditionExprs.Count > 0)
            {
                var preSolver = _ctx.MkSolver();
                preSolver.Set("timeout", _timeoutMs);
                foreach (var preExpr in preconditionExprs)
                {
                    preSolver.Assert(preExpr);
                }
                if (preSolver.Check() == Status.UNSATISFIABLE)
                {
                    return ContractVerificationResult.FromOutcome(
                        ProofOutcome.Assign(ProofEvidence.VacuousProof(
                            "Precondition set is unsatisfiable: the postcondition holds vacuously because no valid call exists. Runtime check kept.")),
                        translator.Warnings.Count > 0 ? translator.Warnings.ToList() : null,
                        sw.Elapsed);
                }
                // SAT or UNKNOWN: proceed with the main check.
            }

            var solver = _ctx.MkSolver();
            solver.Set("timeout", _timeoutMs);

            // Assert all preconditions
            foreach (var preExpr in preconditionExprs)
            {
                solver.Assert(preExpr);
            }

            // Constrain result by the function body
            if (resultBinding != null)
            {
                solver.Assert(resultBinding);
            }

            // Assert the negation of the postcondition
            // If this is UNSAT, the postcondition always holds when preconditions hold
            solver.Assert(_ctx.MkNot(postconditionExpr));

            var status = solver.Check();
            var warnings = translator.Warnings.Count > 0 ? translator.Warnings.ToList() : null;

            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.SolverVerdict(
                    status, solver, translator.Variables, SatPolarity.SatIsRefutation)),
                warnings,
                sw.Elapsed);
        }
        catch (Z3Exception ex)
        {
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.SolverError(ex)),
                Duration: sw.Elapsed);
        }
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

/// <summary>
/// Encodes a function body into a solver expression for the value of <c>result</c>
/// (guarantees plan D-G1.1). The encodable surface is deliberately small and fully sound:
/// bodies composed exclusively of value-carrying <c>§R</c> returns and <c>§IF</c>/<c>§EI</c>/<c>§EL</c>
/// branching (including guard-clause fall-through), over expressions the
/// <see cref="ContractTranslator"/> models. Everything else — loops, bindings, calls,
/// effects — reports a reason and the caller maps the obligation to Unsupported; a body
/// outside this surface must never be verified against an unconstrained <c>result</c>.
/// </summary>
public static class FunctionBodyEncoder
{
    /// <summary>
    /// Attempts to encode the body as a single solver expression for the returned value.
    /// Returns the expression, or null with a human-readable reason.
    /// </summary>
    public static (Expr? Result, string? Reason) TryEncodeResult(
        ContractTranslator translator,
        Context ctx,
        IReadOnlyList<StatementNode>? body)
    {
        if (body == null || body.Count == 0)
            return (null, "the function body is empty or unavailable");

        return EncodeSequence(translator, ctx, body, 0, continuation: null);
    }

    /// <summary>
    /// Encodes a statement list starting at <paramref name="index"/>. When control can fall
    /// past the end of the list, <paramref name="continuation"/> encodes the statements that
    /// follow (guard-clause style: an if without an else falls through to the next statement).
    /// </summary>
    private static (Expr? Result, string? Reason) EncodeSequence(
        ContractTranslator translator,
        Context ctx,
        IReadOnlyList<StatementNode> statements,
        int index,
        Func<(Expr?, string?)>? continuation)
    {
        if (index >= statements.Count)
        {
            return continuation != null
                ? continuation()
                : (null, "not all paths through the body end in a value-carrying return");
        }

        switch (statements[index])
        {
            case ReturnStatementNode ret when ret.Expression != null:
                var value = translator.Translate(ret.Expression);
                return value != null
                    ? (value, null)
                    : (null, translator.DiagnoseTranslationFailure(ret.Expression)
                        ?? "a returned expression is outside the modeled surface");

            case ReturnStatementNode:
                return (null, "a return statement carries no value");

            case IfStatementNode ifStmt:
            {
                // Whatever follows this if-statement is the fall-through continuation for
                // every branch that does not return on all paths.
                Func<(Expr?, string?)> fallThrough = () =>
                    EncodeSequence(translator, ctx, statements, index + 1, continuation);

                var (elseValue, elseReason) = ifStmt.ElseBody != null
                    ? EncodeSequence(translator, ctx, ifStmt.ElseBody, 0, fallThrough)
                    : fallThrough();
                if (elseValue == null)
                    return (null, elseReason);

                for (int i = ifStmt.ElseIfClauses.Count - 1; i >= 0; i--)
                {
                    var clause = ifStmt.ElseIfClauses[i];
                    var clauseCond = translator.TranslateBoolExpr(clause.Condition);
                    if (clauseCond == null)
                        return (null, "an elseif condition is outside the modeled surface");
                    var (clauseValue, clauseReason) = EncodeSequence(translator, ctx, clause.Body, 0, fallThrough);
                    if (clauseValue == null)
                        return (null, clauseReason);
                    if (!clauseValue.Sort.Equals(elseValue.Sort))
                        return (null, "branches return values of different solver sorts");
                    elseValue = ctx.MkITE(clauseCond, clauseValue, elseValue);
                }

                var cond = translator.TranslateBoolExpr(ifStmt.Condition);
                if (cond == null)
                    return (null, "an if condition is outside the modeled surface");
                var (thenValue, thenReason) = EncodeSequence(translator, ctx, ifStmt.ThenBody, 0, fallThrough);
                if (thenValue == null)
                    return (null, thenReason);
                if (!thenValue.Sort.Equals(elseValue.Sort))
                    return (null, "branches return values of different solver sorts");

                return (ctx.MkITE(cond, thenValue, elseValue), null);
            }

            default:
                return (null, $"the body contains a statement outside the modeled surface ({statements[index].GetType().Name})");
        }
    }

    /// <summary>
    /// True when the expression references the special <c>result</c> identifier, ignoring
    /// occurrences shadowed by a quantifier bound variable of the same name.
    /// </summary>
    public static bool ReferencesResult(ExpressionNode expr) => ReferencesName(expr, "result");

    private static bool ReferencesName(ExpressionNode expr, string name)
    {
        switch (expr)
        {
            case ReferenceNode r:
                return r.Name == name;
            case BinaryOperationNode b:
                return ReferencesName(b.Left, name) || ReferencesName(b.Right, name);
            case UnaryOperationNode u:
                return ReferencesName(u.Operand, name);
            case ConditionalExpressionNode c:
                return ReferencesName(c.Condition, name) || ReferencesName(c.WhenTrue, name) || ReferencesName(c.WhenFalse, name);
            case ImplicationExpressionNode i:
                return ReferencesName(i.Antecedent, name) || ReferencesName(i.Consequent, name);
            case ForallExpressionNode f:
                return !f.BoundVariables.Any(v => v.Name == name) && ReferencesName(f.Body, name);
            case ExistsExpressionNode e:
                return !e.BoundVariables.Any(v => v.Name == name) && ReferencesName(e.Body, name);
            case ArrayAccessNode a:
                return ReferencesName(a.Array, name) || ReferencesName(a.Index, name);
            case FieldAccessNode fa:
                return ReferencesName(fa.Target, name);
            case StringOperationNode s:
                return s.Arguments.Any(arg => ReferencesName(arg, name));
            default:
                return false;
        }
    }
}
