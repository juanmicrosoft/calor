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

    /// <summary>
    /// The canonical assumption-set entry for proofs conditional on the contract
    /// expressions' own evaluability (W1 Slice 1, D8): §Q/§S contain division/modulo,
    /// and a zero divisor would make the runtime contract check itself throw rather
    /// than pass or fail. Content-stable: envelopes carry this exact string.
    /// </summary>
    public const string ContractExpressionDivisionAssumption =
        "exceptional-paths:contract-division — every division/modulo divisor inside §Q/§S on a verified state is nonzero; a zero divisor makes the runtime contract check itself throw";

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

        // Positive-whitelist gate (guarantees plan D-G2.3): unsupported is decided by
        // ModeledForms, not by whichever translator branch happens to return null.
        if (!ModeledForms.TryValidate(precondition.Condition, out var preOffending))
        {
            return GateReject(translator, precondition.Condition, preOffending!, sw);
        }

        // Translate the precondition (a raised solver exception is unsupported,
        // never a crash — #822 review M3)
        BoolExpr? preconditionExpr;
        try
        {
            preconditionExpr = translator.TranslateBoolExpr(precondition.Condition);
        }
        catch (Z3Exception ex)
        {
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    $"contract translation raised a solver exception: {ex.Message}. Runtime check kept.")),
                Duration: sw.Elapsed);
        }
        if (preconditionExpr == null)
        {
            return AcceptedButUntranslatable(translator, precondition.Condition, "precondition", sw);
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

        // Positive-whitelist gate over every contract expression in this obligation
        // (guarantees plan D-G2.3).
        foreach (var contractExpr in preconditions.Select(p => p.Condition).Append(postcondition.Condition))
        {
            if (!ModeledForms.TryValidate(contractExpr, out var gateOffending))
            {
                return GateReject(translator, contractExpr, gateOffending!, sw);
            }
        }

        // Translate preconditions
        var preconditionExprs = new List<BoolExpr>();
        foreach (var pre in preconditions)
        {
            BoolExpr? preExpr;
            try
            {
                preExpr = translator.TranslateBoolExpr(pre.Condition);
            }
            catch (Z3Exception ex)
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        $"contract translation raised a solver exception: {ex.Message}. Runtime check kept.")),
                    Duration: sw.Elapsed);
            }
            if (preExpr == null)
            {
                return AcceptedButUntranslatable(translator, pre.Condition, "precondition", sw);
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
        BoolExpr? postconditionExpr;
        try
        {
            postconditionExpr = translator.TranslateBoolExpr(postcondition.Condition);
        }
        catch (Z3Exception ex)
        {
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    $"contract translation raised a solver exception: {ex.Message}. Runtime check kept.")),
                Duration: sw.Elapsed);
        }
        if (postconditionExpr == null)
        {
            return AcceptedButUntranslatable(translator, postcondition.Condition, "postcondition", sw);
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

        // W1 Slice 1 (D8): division/modulo INSIDE the contract expressions themselves
        // was totalized (bvsdiv/bvsrem are total) with no side conditions — a proof
        // could rely on x/0 = -1 while runtime evaluation of the same §Q/§S throws.
        // Collect divisor-nonzero side conditions from every contract expression with
        // the same position rules as the body collector; a proof reached under them
        // is demoted to Assumed below (never elides), and a refutation model becomes
        // runtime-genuine (its divisors are nonzero).
        var contractDivisorConditions = new List<BoolExpr>();
        foreach (var contractExpr in preconditions.Select(p => p.Condition).Append(postcondition.Condition))
        {
            var (contractConstraints, contractDivFailure) =
                FunctionBodyEncoder.CollectDivisorNonZeroConstraintsFromExpression(translator, _ctx, contractExpr);
            if (contractDivFailure != null)
            {
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.Unsupported(
                        $"{contractDivFailure}. Runtime check kept.")),
                    Duration: sw.Elapsed);
            }
            contractDivisorConditions.AddRange(contractConstraints);
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

            // Divisor-nonzero side conditions for the contract expressions (D8).
            // Entailment refinement: when §Q (+ the result binding) already entails
            // every side condition — the guard idiom `§Q (!= y 0)`, or an equality
            // pinning the divisor — the runtime check cannot throw on any valid
            // call, so the proof needs no Assumed demotion. Only side conditions
            // that genuinely restrict the verified state force the demotion.
            var contractDivisionAssumed = false;
            if (contractDivisorConditions.Count > 0)
            {
                solver.Push();
                solver.Assert(_ctx.MkNot(_ctx.MkAnd(contractDivisorConditions.ToArray())));
                var entailed = solver.Check() == Status.UNSATISFIABLE;
                solver.Pop();
                contractDivisionAssumed = !entailed;

                foreach (var contractCondition in contractDivisorConditions)
                {
                    solver.Assert(contractCondition);
                }
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
            if (status == Status.UNSATISFIABLE && (pathConditions.Count > 0 || contractDivisionAssumed))
            {
                var assumptions = new List<string>();
                var reasons = new List<string>();
                if (pathConditions.Count > 0)
                {
                    assumptions.Add(ExceptionalPathDivisionAssumption);
                    reasons.Add("the body divides, and paths with a zero divisor throw before the postcondition is evaluated");
                }
                if (contractDivisionAssumed)
                {
                    assumptions.Add(ContractExpressionDivisionAssumption);
                    reasons.Add("the contract expressions divide, and a zero divisor (or MinValue ÷ -1 overflow) would make the runtime contract check itself throw (W1 Slice 1, D8)");
                }
                return ContractVerificationResult.FromOutcome(
                    ProofOutcome.Assign(ProofEvidence.AssumedProof(
                        $"Proof is conditional on §S normal-return semantics: {string.Join("; ", reasons)}. Runtime check kept.",
                        assumptions)),
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

    /// <summary>
    /// Outcome for a contract expression the whitelist REJECTS (guarantees plan
    /// D-G2.3; labeling per #822 review C1/M2): if the translator nevertheless
    /// supports the form, that IS whitelist drift (the whitelist is too narrow) —
    /// labeled as such; otherwise the legacy diagnosis is composed into the
    /// whitelist framing without duplication.
    /// </summary>
    private static ContractVerificationResult GateReject(
        ContractTranslator translator,
        ExpressionNode expr,
        string offending,
        Stopwatch sw)
    {
        bool translates;
        try
        {
            translates = translator.TranslateBoolExpr(expr) != null;
        }
        catch (Z3Exception)
        {
            translates = false;
        }

        if (translates)
        {
            return ContractVerificationResult.FromOutcome(
                ProofOutcome.Assign(ProofEvidence.Unsupported(
                    $"whitelist drift — the whitelist rejects a form the translator supports ({offending}); please report. Runtime check kept.")),
                Duration: sw.Elapsed);
        }

        // A deliberate refusal (W1 Slice 1: narrow-int arithmetic, mixed-signedness
        // comparison, unknown field/array widths, string.Replace) carries its own
        // reason — prefer it over the generic diagnosis walk.
        string? detail = translator.LastRefusalReason;
        if (detail == null)
        {
            try
            {
                detail = translator.DiagnoseBoolExprFailure(expr) ?? translator.DiagnoseTranslationFailure(expr);
            }
            catch (Z3Exception)
            {
            }
        }

        return ContractVerificationResult.FromOutcome(
            ProofOutcome.Assign(ProofEvidence.Unsupported(
                $"Contract uses a form outside the modeled whitelist ({offending})"
                + (detail != null ? $": {detail}" : ". Runtime check kept."))),
            Duration: sw.Elapsed);
    }

    /// <summary>
    /// Outcome for a whitelist-ACCEPTED contract expression that failed to
    /// translate: a typed diagnosis means unmodeled operand typing (routine,
    /// not drift); no diagnosis at all is genuine whitelist/translator drift
    /// (#822 review C1 — the drift bucket must not be noise).
    /// </summary>
    private static ContractVerificationResult AcceptedButUntranslatable(
        ContractTranslator translator,
        ExpressionNode expr,
        string where,
        Stopwatch sw)
    {
        // A deliberate refusal (W1 Slice 1: narrow-int arithmetic, mixed-signedness
        // comparison, unknown field/array widths, string.Replace) carries its own
        // reason — prefer it over the generic diagnosis walk.
        string? detail = translator.LastRefusalReason;
        if (detail == null)
        {
            try
            {
                detail = translator.DiagnoseBoolExprFailure(expr) ?? translator.DiagnoseTranslationFailure(expr);
            }
            catch (Z3Exception)
            {
            }
        }

        return ContractVerificationResult.FromOutcome(
            ProofOutcome.Assign(ProofEvidence.Unsupported(
                detail != null
                    ? $"Contract form is in the modeled whitelist but its operand typing is not modeled: {detail}. Runtime check kept."
                    : $"whitelist drift — whitelist-accepted form failed to translate with no diagnosis (in {where}). Runtime check kept.")),
            Duration: sw.Elapsed);
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

        // Immutable §B bindings encode by SSA-style AST substitution (guarantees
        // plan D-G3.1): the env maps a bound name to its (already-substituted)
        // initializer tree, applied at each use site before translation. The env
        // is copy-on-extend and captured by branch continuations at creation, so
        // branch-local bindings never leak into fall-through code and the
        // memoized continuation is scope-safe by construction. Semantics note:
        // an initializer whose value is unused is dropped from the encoding —
        // sound under §S normal-return semantics because encodable initializers
        // are pure except division, whose throw-before-return case is covered by
        // the divisor side conditions collected at the BINDING site.
        // M1 (#824 review): a binding whose name collides with a parameter makes
        // the divisor collector (which walks UNSUBSTITUTED trees) resolve the
        // name to the parameter while the encoder sees the binding — a wrong
        // Assumed on API-driven ASTs. Calor0255 forbids the shape in legal
        // source; refuse it here for unchecked-AST callers.
        var shadowing = FindParameterShadowingBinding(body, declaredParameters);
        if (shadowing != null)
            return (null, $"the body binds '{shadowing}', which shadows a parameter of the same name");

        var undeclared = FindUndeclaredReference(
            body, declaredParameters, boundNames: new HashSet<string>(StringComparer.Ordinal));
        if (undeclared != null)
            return (null, $"the body references '{undeclared}', which is not a declared parameter");

        return EncodeSequence(
            translator, ctx, body, 0,
            env: new Dictionary<string, ExpressionNode>(StringComparer.Ordinal),
            depth: 0,
            continuation: null);
    }

    /// <summary>
    /// Binding type annotations the encoder can preserve under substitution:
    /// the translator's default 32-bit integer family, bool, and string. Any
    /// other width changes runtime arithmetic semantics the substituted tree
    /// would not reflect (#824 review C1).
    /// </summary>
    private static bool IsWidthNeutralBindingType(string? typeName) =>
        // Two spelling families: compact/source ("i32") for direct-AST callers,
        // and the parser's expanded forms ("INT"; width-annotated variants like
        // "INT[bits=64][signed=true]" are deliberately NOT listed — width or
        // signedness changes are exactly what substitution cannot preserve).
        typeName is null
            or "i32" or "int" or "Int32" or "System.Int32" or "INT"
            or "bool" or "BOOL"
            or "str" or "string" or "STRING";

    /// <summary>Maximum branch-nesting depth the encoder follows (D-G3.1 "bounded").</summary>
    private const int MaxEncodeDepth = 32;

    /// <summary>Maximum node count of a substituted expression (guards SSA blowup on chains).</summary>
    private const int MaxSubstitutedNodes = 10_000;

    private static string? FindParameterShadowingBinding(
        IReadOnlyList<StatementNode> statements,
        IReadOnlyCollection<string> declaredParameters)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case BindStatementNode bind when declaredParameters.Contains(bind.Name):
                    return bind.Name;
                case IfStatementNode ifStmt:
                    var hit = FindParameterShadowingBinding(ifStmt.ThenBody, declaredParameters)
                        ?? ifStmt.ElseIfClauses
                            .Select(c => FindParameterShadowingBinding(c.Body, declaredParameters))
                            .FirstOrDefault(h => h != null)
                        ?? (ifStmt.ElseBody != null
                            ? FindParameterShadowingBinding(ifStmt.ElseBody, declaredParameters)
                            : null);
                    if (hit != null)
                        return hit;
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// Walks the encodable statement surface (returns, bindings, if/elseif/else) and
    /// reports the first reference whose base identifier is neither a declared
    /// parameter nor a §B name bound EARLIER in flow order — or a marker for an
    /// expression kind the walker does not model (conservative: such bodies fail
    /// encoding rather than risk translating against an auto-declared free
    /// variable). Branch-local bindings do not escape their branch.
    /// </summary>
    private static string? FindUndeclaredReference(
        IReadOnlyList<StatementNode> statements,
        IReadOnlyCollection<string> declaredParameters,
        HashSet<string> boundNames)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case ReturnStatementNode ret:
                    if (ret.Expression != null)
                    {
                        var hit = FindUndeclaredInExpression(ret.Expression, declaredParameters, boundNames);
                        if (hit != null)
                            return hit;
                    }
                    break;

                case BindStatementNode bind:
                    if (bind.Initializer != null)
                    {
                        var initHit = FindUndeclaredInExpression(bind.Initializer, declaredParameters, boundNames);
                        if (initHit != null)
                            return initHit;
                    }
                    boundNames = new HashSet<string>(boundNames, StringComparer.Ordinal) { bind.Name };
                    break;

                case IfStatementNode ifStmt:
                    var condHit = FindUndeclaredInExpression(ifStmt.Condition, declaredParameters, boundNames);
                    if (condHit != null)
                        return condHit;
                    var thenHit = FindUndeclaredReference(
                        ifStmt.ThenBody, declaredParameters, new HashSet<string>(boundNames, StringComparer.Ordinal));
                    if (thenHit != null)
                        return thenHit;
                    foreach (var clause in ifStmt.ElseIfClauses)
                    {
                        var clauseCondHit = FindUndeclaredInExpression(clause.Condition, declaredParameters, boundNames);
                        if (clauseCondHit != null)
                            return clauseCondHit;
                        var clauseHit = FindUndeclaredReference(
                            clause.Body, declaredParameters, new HashSet<string>(boundNames, StringComparer.Ordinal));
                        if (clauseHit != null)
                            return clauseHit;
                    }
                    if (ifStmt.ElseBody != null)
                    {
                        var elseHit = FindUndeclaredReference(
                            ifStmt.ElseBody, declaredParameters, new HashSet<string>(boundNames, StringComparer.Ordinal));
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
        IReadOnlyCollection<string> declaredParameters,
        IReadOnlyCollection<string> boundNames)
    {
        switch (expr)
        {
            case IntLiteralNode or FloatLiteralNode or BoolLiteralNode or StringLiteralNode:
                return null;
            case ReferenceNode r:
            {
                var baseName = r.Name.Split('.')[0];
                return declaredParameters.Contains(baseName) || boundNames.Contains(baseName) ? null : r.Name;
            }
            case BinaryOperationNode b:
                return FindUndeclaredInExpression(b.Left, declaredParameters, boundNames)
                    ?? FindUndeclaredInExpression(b.Right, declaredParameters, boundNames);
            case UnaryOperationNode u:
                return FindUndeclaredInExpression(u.Operand, declaredParameters, boundNames);
            case ConditionalExpressionNode c:
                return FindUndeclaredInExpression(c.Condition, declaredParameters, boundNames)
                    ?? FindUndeclaredInExpression(c.WhenTrue, declaredParameters, boundNames)
                    ?? FindUndeclaredInExpression(c.WhenFalse, declaredParameters, boundNames);
            case ArrayAccessNode a:
                return FindUndeclaredInExpression(a.Array, declaredParameters, boundNames)
                    ?? FindUndeclaredInExpression(a.Index, declaredParameters, boundNames);
            case ArrayLengthNode al:
                return FindUndeclaredInExpression(al.Array, declaredParameters, boundNames);
            case FieldAccessNode fa:
                return FindUndeclaredInExpression(fa.Target, declaredParameters, boundNames);
            case StringOperationNode sop:
            {
                foreach (var arg in sop.Arguments)
                {
                    var hit = FindUndeclaredInExpression(arg, declaredParameters, boundNames);
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
    /// <summary>
    /// W1 Slice 1 (D8): collects divisor-nonzero side conditions for division/modulo
    /// inside a CONTRACT expression (§Q/§S), which the translator otherwise totalizes
    /// (bvsdiv/bvsrem are total; runtime evaluation of the same expression throws on a
    /// zero divisor). Same position rules as the body collector: a divisor in a
    /// conditionally-evaluated position (short-circuit RHS, ?: arm) reports a failure
    /// and the obligation becomes Unsupported.
    /// </summary>
    public static (IReadOnlyList<BoolExpr> Constraints, string? Failure) CollectDivisorNonZeroConstraintsFromExpression(
        ContractTranslator translator,
        Context ctx,
        ExpressionNode expr)
    {
        var constraints = new List<BoolExpr>();
        var failure = CollectDivisorsFromExpression(translator, ctx, expr, constraints, conditional: false);
        if (failure != null)
        {
            failure = failure.Replace("the body contains", "the contract expression contains");
        }
        return (constraints, failure);
    }

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

                case BindStatementNode bind when bind.Initializer != null:
                    // A binding's initializer evaluates AT THE BINDING SITE (eagerly,
                    // once) — divisors inside it take the binding's own position
                    // conditionality, not the use sites' (D-G3.1: SSA substitution
                    // moves the expression to use sites for the ENCODING, but runtime
                    // evaluation order stays here). An unused dividing initializer
                    // still throws at runtime on a zero divisor, so its side
                    // condition genuinely holds on every normal-return path.
                    var bindFailure = CollectDivisorsFromExpression(translator, ctx, bind.Initializer, constraints, conditional);
                    if (bindFailure != null)
                        return bindFailure;
                    break;

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
                    // A literal divisor that is neither 0 nor −1 can never throw
                    // (no DivideByZeroException, no MinValue÷−1 OverflowException):
                    // no side condition, no demotion (W1 Slice 1).
                    var literalDivisor = b.Right as IntLiteralNode;
                    var isSafeLiteral = literalDivisor != null
                        && (literalDivisor.IsUnsigned
                            ? literalDivisor.UnsignedValue != 0
                            : literalDivisor.Value != 0 && literalDivisor.Value != -1);
                    if (isSafeLiteral)
                    {
                        return null;
                    }
                    if (conditional)
                    {
                        return "the body contains division/modulo in a conditionally-evaluated position, "
                            + "which is not yet modeled for exception-path soundness (a path-guarded encoding is planned)";
                    }
                    var operands = translator.GetDivModOperands(b.Left, b.Right);
                    if (operands == null)
                        return "a division/modulo divisor could not be modeled for the non-zero side condition";
                    var (dividend, divisor, signedDivision) = operands.Value;

                    // A literal −1 divisor cannot be zero — skip the zero condition.
                    var isNegOneLiteral = literalDivisor != null
                        && !literalDivisor.IsUnsigned && literalDivisor.Value == -1;
                    if (!isNegOneLiteral)
                    {
                        constraints.Add(ctx.MkNot(ctx.MkEq(divisor, ctx.MkBV(0, divisor.SortSize))));
                    }

                    // Signed division: MinValue ÷ −1 throws OverflowException in C#
                    // (checked AND unchecked) while bvsdiv wraps to MinValue — a
                    // proof relying on that state would elide a check that throws
                    // (review #833 C4). Same demote-unless-entailed machinery.
                    if (signedDivision)
                    {
                        var w = dividend.SortSize;
                        var minValue = ctx.MkBVSHL(ctx.MkBV(1, w), ctx.MkBV(w - 1, w));
                        var negOne = ctx.MkBVNeg(ctx.MkBV(1, w));
                        constraints.Add(ctx.MkNot(ctx.MkAnd(
                            ctx.MkEq(dividend, minValue),
                            ctx.MkEq(divisor, negOne))));
                    }
                }
                return null;
            }
            case UnaryOperationNode u:
                return CollectDivisorsFromExpression(translator, ctx, u.Operand, constraints, conditional);
            case ConditionalExpressionNode c:
                return CollectDivisorsFromExpression(translator, ctx, c.Condition, constraints, conditional)
                    ?? CollectDivisorsFromExpression(translator, ctx, c.WhenTrue, constraints, conditional: true)
                    ?? CollectDivisorsFromExpression(translator, ctx, c.WhenFalse, constraints, conditional: true);
            // Review #833 C5: these previously fell to the default (no constraints,
            // no failure) — a division inside `(-> p q)`'s consequent or a
            // quantifier body was silently uncovered, and the emitted short-circuit
            // check could throw where the proof said nothing. The consequent of an
            // implication is conditionally evaluated (`!p || q`); quantifier bodies
            // are evaluated per-element with varying bound values — both take the
            // conditional-position rule (division there → Unsupported).
            case ImplicationExpressionNode imp:
                return CollectDivisorsFromExpression(translator, ctx, imp.Antecedent, constraints, conditional)
                    ?? CollectDivisorsFromExpression(translator, ctx, imp.Consequent, constraints, conditional: true);
            case ForallExpressionNode forall:
                return CollectDivisorsFromExpression(translator, ctx, forall.Body, constraints, conditional: true);
            case ExistsExpressionNode exists:
                return CollectDivisorsFromExpression(translator, ctx, exists.Body, constraints, conditional: true);
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
    /// <paramref name="env"/> carries the SSA substitution for immutable §B bindings in
    /// scope; it is never mutated — extensions copy — so continuations capture the correct
    /// pre-branch environment.
    /// </summary>
    private static (Expr? Result, string? Reason) EncodeSequence(
        ContractTranslator translator,
        Context ctx,
        IReadOnlyList<StatementNode> statements,
        int index,
        Dictionary<string, ExpressionNode> env,
        int depth,
        Func<(Expr?, string?)>? continuation)
    {
        if (depth > MaxEncodeDepth)
            return (null, $"branch nesting exceeds the encoder's bound ({MaxEncodeDepth})");

        if (index >= statements.Count)
        {
            return continuation != null
                ? continuation()
                : (null, "not all paths through the body end in a value-carrying return");
        }

        switch (statements[index])
        {
            case ReturnStatementNode ret when ret.Expression != null:
            {
                var (substituted, substReason) = SubstituteBindings(ret.Expression, env);
                if (substituted == null)
                    return (null, substReason);
                var value = translator.Translate(substituted);
                return value != null
                    ? (value, null)
                    : (null, translator.DiagnoseTranslationFailure(substituted)
                        ?? "a returned expression is outside the modeled surface");
            }

            case ReturnStatementNode:
                return (null, "a return statement carries no value");

            case BindStatementNode bind:
            {
                if (bind.IsMutable)
                    return (null, "the body contains a mutable (§B{~}) binding, which is outside the encodable surface");
                if (bind.Initializer == null)
                    return (null, "the body contains a binding without an initializer");
                if (bind.Name == "result")
                    return (null, "the body binds the name 'result', which collides with the postcondition result variable");
                // C1 (#824 review): substitution ERASES the binding's type
                // annotation, but the annotation changes runtime arithmetic width
                // at every use site (§B{t:i64} INT:2147483647 → t+1 is 64-bit at
                // runtime, 32-bit-wrapped in the encoding → false Proven, check
                // deleted). Only width-neutral annotations are encodable;
                // widening/narrowing bindings refuse. Conservative loss recorded:
                // an i64 binding over i64-typed operands would be consistent but
                // is refused too — width-matching inference is future work.
                if (!IsWidthNeutralBindingType(bind.TypeName))
                    return (null, $"the body binds '{bind.Name}' with type '{bind.TypeName}', whose width semantics substitution cannot preserve");

                var (substInit, initReason) = SubstituteBindings(bind.Initializer, env);
                if (substInit == null)
                    return (null, initReason);

                var extended = new Dictionary<string, ExpressionNode>(env, StringComparer.Ordinal)
                {
                    [bind.Name] = substInit
                };
                return EncodeSequence(translator, ctx, statements, index + 1, extended, depth, continuation);
            }

            case IfStatementNode ifStmt:
            {
                // Whatever follows this if-statement is the fall-through continuation for
                // every branch that does not return on all paths. Memoized: each branch
                // that falls through re-invokes it, and un-memoized the tail would be
                // re-encoded 2^depth times on nested guard-clause chains (G1 review m3).
                // The captured env is the PRE-branch environment by construction.
                (Expr?, string?)? fallThroughMemo = null;
                Func<(Expr?, string?)> fallThrough = () =>
                    fallThroughMemo ??= EncodeSequence(translator, ctx, statements, index + 1, env, depth, continuation);

                var (elseValue, elseReason) = ifStmt.ElseBody != null
                    ? EncodeSequence(translator, ctx, ifStmt.ElseBody, 0, env, depth + 1, fallThrough)
                    : fallThrough();
                if (elseValue == null)
                    return (null, elseReason);

                for (int i = ifStmt.ElseIfClauses.Count - 1; i >= 0; i--)
                {
                    var clause = ifStmt.ElseIfClauses[i];
                    var (clauseCondSubst, clauseCondReason) = SubstituteBindings(clause.Condition, env);
                    if (clauseCondSubst == null)
                        return (null, clauseCondReason);
                    var clauseCond = translator.TranslateBoolExpr(clauseCondSubst);
                    if (clauseCond == null)
                        return (null, "an elseif condition is outside the modeled surface");
                    var (clauseValue, clauseReason) = EncodeSequence(translator, ctx, clause.Body, 0, env, depth + 1, fallThrough);
                    if (clauseValue == null)
                        return (null, clauseReason);
                    if (!clauseValue.Sort.Equals(elseValue.Sort))
                        return (null, "branches return values of different solver sorts");
                    elseValue = ctx.MkITE(clauseCond, clauseValue, elseValue);
                }

                var (condSubst, condReason) = SubstituteBindings(ifStmt.Condition, env);
                if (condSubst == null)
                    return (null, condReason);
                var cond = translator.TranslateBoolExpr(condSubst);
                if (cond == null)
                    return (null, "an if condition is outside the modeled surface");
                var (thenValue, thenReason) = EncodeSequence(translator, ctx, ifStmt.ThenBody, 0, env, depth + 1, fallThrough);
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
    /// Applies the SSA env to an expression: every reference to a bound name is replaced
    /// by its (already-substituted) initializer tree. Returns null with a reason when the
    /// expression contains a kind substitution does not model (conservative: such
    /// expressions must not slip through with a bound reference intact) or when the
    /// result exceeds the size bound.
    /// </summary>
    private static (ExpressionNode? Result, string? Reason) SubstituteBindings(
        ExpressionNode expr,
        Dictionary<string, ExpressionNode> env)
    {
        if (env.Count == 0)
            return (expr, null);

        var substituted = SubstituteCore(expr, env, out var failure);
        if (substituted == null)
            return (null, failure ?? "substitution failed");

        if (CountNodes(substituted) > MaxSubstitutedNodes)
            return (null, $"the binding chain expands past the encoder's size bound ({MaxSubstitutedNodes} nodes)");

        return (substituted, null);
    }

    private static ExpressionNode? SubstituteCore(
        ExpressionNode expr,
        Dictionary<string, ExpressionNode> env,
        out string? failure)
    {
        failure = null;
        switch (expr)
        {
            case IntLiteralNode or BoolLiteralNode or StringLiteralNode or FloatLiteralNode or SelfRefNode:
                return expr;

            case ReferenceNode r:
                return env.TryGetValue(r.Name, out var replacement) ? replacement : expr;

            case BinaryOperationNode b:
            {
                var left = SubstituteCore(b.Left, env, out failure);
                if (left == null) return null;
                var right = SubstituteCore(b.Right, env, out failure);
                if (right == null) return null;
                return ReferenceEquals(left, b.Left) && ReferenceEquals(right, b.Right)
                    ? b
                    : new BinaryOperationNode(b.Span, b.Operator, left, right);
            }

            case UnaryOperationNode u:
            {
                var operand = SubstituteCore(u.Operand, env, out failure);
                if (operand == null) return null;
                return ReferenceEquals(operand, u.Operand) ? u : new UnaryOperationNode(u.Span, u.Operator, operand);
            }

            case ConditionalExpressionNode c:
            {
                var condition = SubstituteCore(c.Condition, env, out failure);
                if (condition == null) return null;
                var whenTrue = SubstituteCore(c.WhenTrue, env, out failure);
                if (whenTrue == null) return null;
                var whenFalse = SubstituteCore(c.WhenFalse, env, out failure);
                if (whenFalse == null) return null;
                return ReferenceEquals(condition, c.Condition)
                        && ReferenceEquals(whenTrue, c.WhenTrue)
                        && ReferenceEquals(whenFalse, c.WhenFalse)
                    ? c
                    : new ConditionalExpressionNode(c.Span, condition, whenTrue, whenFalse);
            }

            case ArrayAccessNode a:
            {
                var array = SubstituteCore(a.Array, env, out failure);
                if (array == null) return null;
                var indexExpr = SubstituteCore(a.Index, env, out failure);
                if (indexExpr == null) return null;
                return ReferenceEquals(array, a.Array) && ReferenceEquals(indexExpr, a.Index)
                    ? a
                    : new ArrayAccessNode(a.Span, array, indexExpr);
            }

            case ArrayLengthNode al:
            {
                var array = SubstituteCore(al.Array, env, out failure);
                if (array == null) return null;
                return ReferenceEquals(array, al.Array) ? al : new ArrayLengthNode(al.Span, array);
            }

            case FieldAccessNode fa:
            {
                var target = SubstituteCore(fa.Target, env, out failure);
                if (target == null) return null;
                return ReferenceEquals(target, fa.Target) ? fa : new FieldAccessNode(fa.Span, target, fa.FieldName);
            }

            case StringOperationNode sop:
            {
                var args = new List<ExpressionNode>(sop.Arguments.Count);
                var changed = false;
                foreach (var arg in sop.Arguments)
                {
                    var substArg = SubstituteCore(arg, env, out failure);
                    if (substArg == null) return null;
                    changed |= !ReferenceEquals(substArg, arg);
                    args.Add(substArg);
                }
                return changed ? new StringOperationNode(sop.Span, sop.Operation, args, sop.ComparisonMode) : sop;
            }

            default:
                // Conservative: an unmodeled expression kind could carry a bound
                // reference we cannot rewrite — refuse rather than translate a
                // stale name (the translator would auto-declare arrays, G1 M3).
                failure = $"the body contains an expression kind substitution does not model ({expr.GetType().Name})";
                return null;
        }
    }

    private static int CountNodes(ExpressionNode expr) => expr switch
    {
        BinaryOperationNode b => 1 + CountNodes(b.Left) + CountNodes(b.Right),
        UnaryOperationNode u => 1 + CountNodes(u.Operand),
        ConditionalExpressionNode c => 1 + CountNodes(c.Condition) + CountNodes(c.WhenTrue) + CountNodes(c.WhenFalse),
        _ => 1
    };

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
