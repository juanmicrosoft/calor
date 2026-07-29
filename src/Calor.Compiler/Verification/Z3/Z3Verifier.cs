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
    /// <summary>
    /// The canonical assumption-set entry for proofs conditional on exceptional-path
    /// division semantics (guarantees plan D-G2.5; strategy 2b item 6). Content-stable:
    /// this exact string is what envelopes carry and assumption-set hashing keys on.
    /// </summary>
    public const string ExceptionalPathDivisionAssumption =
        "exceptional-paths:division — every division/modulo divisor on a verified path is nonzero; a zero divisor throws before §S is evaluated (normal-return semantics)";

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

        // A parameter named `result` collides with the postcondition result variable:
        // DeclareVariable("result") would silently overwrite it, aliasing the two into one
        // solver constant — a contradictory body binding (e.g. `result == result + 1`) then
        // makes the whole query UNSAT and mints a FALSE Proven that elides the runtime
        // check (G1 review C1). Refuse the shape outright.
        if (!string.IsNullOrEmpty(outputType) && parameters.Any(p => p.Name == "result"))
        {
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    "a parameter is named 'result', which collides with the postcondition result variable; rename the parameter. Runtime check kept.")),
                Duration: sw.Elapsed);
        }

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

        // Vacuity pre-check (guarantees plan D-G1.3), run BEFORE postcondition translation
        // and body encoding so a vacuous precondition set reports Calor0719 even when the
        // rest of the obligation is unencodable (G1 review m2). An unsatisfiable
        // precondition set would make the main check UNSAT — a "proof" that holds only
        // because no valid call exists. NOTE: an UNKNOWN pre-check verdict falls through
        // to the main check, so a vacuous-but-hard precondition set can still surface as
        // plain Proven and elide the postcondition check; that elision is runtime-safe
        // ONLY because D-G1.2 always emits precondition guards (an unsatisfiable set makes
        // the body unreachable through them) — the two invariants are load-bearing
        // together (G1 review m1).
        if (preconditionExprs.Count > 0)
        {
            try
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
            catch (Z3Exception ex)
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.SolverError(ex)),
                    Duration: sw.Elapsed);
            }
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
        IReadOnlyList<BoolExpr> pathConditions = Array.Empty<BoolExpr>();
        if (!string.IsNullOrEmpty(outputType)
            && FunctionBodyEncoder.ReferencesResult(postcondition.Condition))
        {
            // Array-returning bodies: the binding `result == xs` does not link the
            // synthetic `result$length`/`xs$length` variables, so a §LEN-carrying
            // postcondition would still see a free length — the #807 hole through a side
            // door (G1 review M2). Unsupported until length linkage is modeled.
            if (outputType.Contains('['))
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        "Postcondition references 'result' but array-returning bodies are not yet modeled (result length linkage). Runtime check kept.")),
                    Duration: sw.Elapsed);
            }

            var resultVar = translator.Variables["result"].Expr;
            var parameterNames = parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var (encoded, reason) = FunctionBodyEncoder.TryEncodeResult(translator, _ctx, body, parameterNames);
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

            // Division/modulo in the body: Z3 totalizes x/0 (bvsdiv(x,0) = -1), so a model
            // exercising a zero divisor is not runtime-reproducible — at runtime that path
            // throws and the postcondition is never evaluated (G1 review M4). Conjoin
            // divisor-nonzero side conditions: refutations become runtime-genuine, and
            // proofs remain elision-safe under §S's normal-return semantics (a throwing
            // path never reaches the check). WS-G2's D-G2.5 will surface this as a named
            // `assumed` condition instead of silently strengthening the query.
            var (divisorConstraints, divisorFailure) =
                FunctionBodyEncoder.CollectDivisorNonZeroConstraints(translator, _ctx, body!);
            if (divisorFailure != null)
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        $"Postcondition references 'result' but {divisorFailure}. Runtime check kept.")),
                    Duration: sw.Elapsed);
            }
            pathConditions = divisorConstraints;
        }

        // Create solver and perform verification
        try
        {
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

            // Divisor-nonzero path conditions (normal-return semantics; see above)
            foreach (var pathCondition in pathConditions)
            {
                solver.Assert(pathCondition);
            }

            // Assert the negation of the postcondition
            // If this is UNSAT, the postcondition always holds when preconditions hold
            solver.Assert(_ctx.MkNot(postconditionExpr));

            var status = solver.Check();
            var warnings = translator.Warnings.Count > 0 ? translator.Warnings.ToList() : null;

            // D-G2.5 (Assumed's first producer): a proof reached under divisor-nonzero
            // side conditions is conditional on §S's normal-return semantics — paths
            // where a divisor is zero throw before the postcondition is evaluated and
            // are unverified. Surface that as `assumed` with the named assumption
            // instead of a silent strengthening: Assumed never elides the runtime
            // check and never aggregates into proven. A refutation under the same
            // side conditions needs no assumption — its model is a genuine
            // non-throwing execution.
            if (status == Status.UNSATISFIABLE && pathConditions.Count > 0)
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.AssumedProof(
                        "Proof is conditional on §S normal-return semantics: the body divides, and paths with a zero divisor throw before the postcondition is evaluated. Runtime check kept.",
                        [ExceptionalPathDivisionAssumption])),
                    warnings,
                    sw.Elapsed);
            }

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
    /// Every reference in the body's expressions must be a declared parameter
    /// (<paramref name="declaredParameters"/>): the translator auto-declares unknown
    /// ARRAY references as fresh unconstrained variables (G1 review M3), which would
    /// reopen the #807 free-variable hole through the body side.
    /// </summary>
    public static (Expr? Result, string? Reason) TryEncodeResult(
        ContractTranslator translator,
        Context ctx,
        IReadOnlyList<StatementNode>? body,
        IReadOnlyCollection<string> declaredParameters)
    {
        if (body == null || body.Count == 0)
            return (null, "the function body is empty or unavailable");

        var undeclared = FindUndeclaredReference(body, declaredParameters);
        if (undeclared != null)
            return (null, $"the body references '{undeclared}', which is not a declared parameter");

        return EncodeSequence(translator, ctx, body, 0, continuation: null);
    }

    /// <summary>
    /// Walks the encodable statement surface (returns, if/elseif/else) and reports the
    /// first reference whose base identifier is not a declared parameter — or a marker
    /// for an expression kind the walker does not model (conservative: such bodies fail
    /// encoding rather than risk translating against an auto-declared free variable).
    /// </summary>
    private static string? FindUndeclaredReference(
        IReadOnlyList<StatementNode> statements,
        IReadOnlyCollection<string> declaredParameters)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case ReturnStatementNode ret:
                    if (ret.Expression != null)
                    {
                        var hit = FindUndeclaredInExpression(ret.Expression, declaredParameters);
                        if (hit != null)
                            return hit;
                    }
                    break;

                case IfStatementNode ifStmt:
                    var condHit = FindUndeclaredInExpression(ifStmt.Condition, declaredParameters);
                    if (condHit != null)
                        return condHit;
                    var thenHit = FindUndeclaredReference(ifStmt.ThenBody, declaredParameters);
                    if (thenHit != null)
                        return thenHit;
                    foreach (var clause in ifStmt.ElseIfClauses)
                    {
                        var clauseCondHit = FindUndeclaredInExpression(clause.Condition, declaredParameters);
                        if (clauseCondHit != null)
                            return clauseCondHit;
                        var clauseHit = FindUndeclaredReference(clause.Body, declaredParameters);
                        if (clauseHit != null)
                            return clauseHit;
                    }
                    if (ifStmt.ElseBody != null)
                    {
                        var elseHit = FindUndeclaredReference(ifStmt.ElseBody, declaredParameters);
                        if (elseHit != null)
                            return elseHit;
                    }
                    break;

                // Other statement kinds make EncodeSequence bail before translating,
                // so their expressions are never handed to the translator.
            }
        }
        return null;
    }

    private static string? FindUndeclaredInExpression(
        ExpressionNode expr,
        IReadOnlyCollection<string> declaredParameters)
    {
        switch (expr)
        {
            case IntLiteralNode or FloatLiteralNode or BoolLiteralNode or StringLiteralNode:
                return null;
            case ReferenceNode r:
            {
                var baseName = r.Name.Split('.')[0];
                return declaredParameters.Contains(baseName) ? null : r.Name;
            }
            case BinaryOperationNode b:
                return FindUndeclaredInExpression(b.Left, declaredParameters)
                    ?? FindUndeclaredInExpression(b.Right, declaredParameters);
            case UnaryOperationNode u:
                return FindUndeclaredInExpression(u.Operand, declaredParameters);
            case ConditionalExpressionNode c:
                return FindUndeclaredInExpression(c.Condition, declaredParameters)
                    ?? FindUndeclaredInExpression(c.WhenTrue, declaredParameters)
                    ?? FindUndeclaredInExpression(c.WhenFalse, declaredParameters);
            case ArrayAccessNode a:
                return FindUndeclaredInExpression(a.Array, declaredParameters)
                    ?? FindUndeclaredInExpression(a.Index, declaredParameters);
            case ArrayLengthNode al:
                return FindUndeclaredInExpression(al.Array, declaredParameters);
            case FieldAccessNode fa:
                return FindUndeclaredInExpression(fa.Target, declaredParameters);
            case StringOperationNode sop:
            {
                foreach (var arg in sop.Arguments)
                {
                    var hit = FindUndeclaredInExpression(arg, declaredParameters);
                    if (hit != null)
                        return hit;
                }
                return null;
            }
            default:
                // Unknown expression kind: refuse rather than risk the translator
                // auto-declaring something inside it.
                return $"<{expr.GetType().Name}>";
        }
    }

    /// <summary>
    /// Collects divisor-nonzero side conditions for division/modulo in the body's
    /// encodable expressions (G1 review M4): Z3 totalizes x/0, so without these a
    /// refutation model may exercise a divisor of zero — a path that throws at runtime
    /// and never evaluates the postcondition. SOUNDNESS RULE (G1 re-verification
    /// C1-new): a bare `divisor != 0` assumption is only valid for a divisor that is
    /// evaluated on EVERY normal-return execution — a divisor inside a branch body,
    /// an elseif condition, a statement after branching, the right operand of a
    /// short-circuiting &amp;&amp;/||, or a conditional-expression arm is evaluated only on
    /// some paths, and asserting it globally excludes violating inputs on the OTHER
    /// paths (a false Proven that deletes the runtime check). Conditionally-evaluated
    /// division therefore reports a failure and the obligation becomes Unsupported —
    /// until a path-guarded encoding (guard =&gt; divisor != 0) lands with D-G2.5.
    /// Collection stops at the first return in a statement list: dead code must not
    /// constrain the query either.
    /// </summary>
    public static (IReadOnlyList<BoolExpr> Constraints, string? Failure) CollectDivisorNonZeroConstraints(
        ContractTranslator translator,
        Context ctx,
        IReadOnlyList<StatementNode> statements)
    {
        var constraints = new List<BoolExpr>();
        var failure = CollectDivisorsFromStatements(translator, ctx, statements, constraints, conditional: false);
        return (constraints, failure);
    }

    private static string? CollectDivisorsFromStatements(
        ContractTranslator translator,
        Context ctx,
        IReadOnlyList<StatementNode> statements,
        List<BoolExpr> constraints,
        bool conditional)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case ReturnStatementNode ret:
                    if (ret.Expression != null)
                    {
                        var retFailure = CollectDivisorsFromExpression(translator, ctx, ret.Expression, constraints, conditional);
                        if (retFailure != null)
                            return retFailure;
                    }
                    // Everything after a return is dead code — it is never evaluated
                    // and must not contribute constraints.
                    return null;

                case IfStatementNode ifStmt:
                {
                    // The if's own condition is evaluated whenever this statement is
                    // reached; branch bodies, elseif conditions (evaluated only when
                    // prior conditions were false), the else body, and every statement
                    // AFTER the if (reached only via fall-through) are conditional.
                    var condFailure = CollectDivisorsFromExpression(translator, ctx, ifStmt.Condition, constraints, conditional);
                    if (condFailure != null)
                        return condFailure;
                    var thenFailure = CollectDivisorsFromStatements(translator, ctx, ifStmt.ThenBody, constraints, conditional: true);
                    if (thenFailure != null)
                        return thenFailure;
                    foreach (var clause in ifStmt.ElseIfClauses)
                    {
                        var clauseCondFailure = CollectDivisorsFromExpression(translator, ctx, clause.Condition, constraints, conditional: true);
                        if (clauseCondFailure != null)
                            return clauseCondFailure;
                        var clauseFailure = CollectDivisorsFromStatements(translator, ctx, clause.Body, constraints, conditional: true);
                        if (clauseFailure != null)
                            return clauseFailure;
                    }
                    if (ifStmt.ElseBody != null)
                    {
                        var elseFailure = CollectDivisorsFromStatements(translator, ctx, ifStmt.ElseBody, constraints, conditional: true);
                        if (elseFailure != null)
                            return elseFailure;
                    }
                    conditional = true;
                    break;
                }
            }
        }
        return null;
    }

    private static string? CollectDivisorsFromExpression(
        ContractTranslator translator,
        Context ctx,
        ExpressionNode expr,
        List<BoolExpr> constraints,
        bool conditional)
    {
        switch (expr)
        {
            case BinaryOperationNode b:
            {
                // Short-circuit operators: the right operand is evaluated only when
                // the left didn't decide the result — conditional territory.
                var rightConditional = conditional
                    || b.Operator is BinaryOperator.And or BinaryOperator.Or;

                var leftFailure = CollectDivisorsFromExpression(translator, ctx, b.Left, constraints, conditional);
                if (leftFailure != null)
                    return leftFailure;
                var rightFailure = CollectDivisorsFromExpression(translator, ctx, b.Right, constraints, rightConditional);
                if (rightFailure != null)
                    return rightFailure;
                if (b.Operator is BinaryOperator.Divide or BinaryOperator.Modulo)
                {
                    if (conditional)
                    {
                        return "the body contains division/modulo in a conditionally-evaluated position, "
                            + "which is not yet modeled for exception-path soundness (a path-guarded encoding is planned)";
                    }
                    if (translator.Translate(b.Right) is not BitVecExpr divisor)
                        return "a division/modulo divisor could not be modeled for the non-zero side condition";
                    constraints.Add(ctx.MkNot(ctx.MkEq(divisor, ctx.MkBV(0, divisor.SortSize))));
                }
                return null;
            }
            case UnaryOperationNode u:
                return CollectDivisorsFromExpression(translator, ctx, u.Operand, constraints, conditional);
            case ConditionalExpressionNode c:
                return CollectDivisorsFromExpression(translator, ctx, c.Condition, constraints, conditional)
                    ?? CollectDivisorsFromExpression(translator, ctx, c.WhenTrue, constraints, conditional: true)
                    ?? CollectDivisorsFromExpression(translator, ctx, c.WhenFalse, constraints, conditional: true);
            case ArrayAccessNode a:
                return CollectDivisorsFromExpression(translator, ctx, a.Array, constraints, conditional)
                    ?? CollectDivisorsFromExpression(translator, ctx, a.Index, constraints, conditional);
            case ArrayLengthNode al:
                return CollectDivisorsFromExpression(translator, ctx, al.Array, constraints, conditional);
            case FieldAccessNode fa:
                return CollectDivisorsFromExpression(translator, ctx, fa.Target, constraints, conditional);
            case StringOperationNode sop:
            {
                foreach (var arg in sop.Arguments)
                {
                    var failure = CollectDivisorsFromExpression(translator, ctx, arg, constraints, conditional);
                    if (failure != null)
                        return failure;
                }
                return null;
            }
            default:
                return null;
        }
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
                // every branch that does not return on all paths. Memoized: each branch
                // that falls through re-invokes it, and un-memoized the tail would be
                // re-encoded 2^depth times on nested guard-clause chains (G1 review m3).
                (Expr?, string?)? fallThroughMemo = null;
                Func<(Expr?, string?)> fallThrough = () =>
                    fallThroughMemo ??= EncodeSequence(translator, ctx, statements, index + 1, continuation);

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
    /// True when the expression references the special <c>result</c> identifier — bare or
    /// as the base of a dotted reference (<c>result.Field</c> lexes as a single dotted
    /// ReferenceNode, G1 review M1) — ignoring occurrences shadowed by a quantifier bound
    /// variable of the same name.
    /// </summary>
    public static bool ReferencesResult(ExpressionNode expr) => ReferencesName(expr, "result");

    private static bool ReferencesName(ExpressionNode expr, string name)
    {
        switch (expr)
        {
            case ReferenceNode r:
                return r.Name == name || r.Name.StartsWith(name + ".", StringComparison.Ordinal);
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
            case ArrayLengthNode al:
                return ReferencesName(al.Array, name);
            case FieldAccessNode fa:
                return ReferencesName(fa.Target, name);
            case StringOperationNode s:
                return s.Arguments.Any(arg => ReferencesName(arg, name));
            default:
                return false;
        }
    }
}
