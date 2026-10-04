using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// A solver whose every <see cref="Check"/> runs in a fresh Z3 context, so that its verdict
/// depends only on the asserted formulas and not on the history of the context that built them
/// (#1135).
/// </summary>
/// <remarks>
/// <para>
/// Why: the .NET Z3 binding releases a native AST when the garbage collector finalizes its
/// wrapper, and Z3 recycles the ids of released ASTs. In a long-lived context the ids of the
/// terms a query creates, and so the order Z3's heuristics visit them in, therefore depend on
/// when the GC ran. On the same tree the solver's work (rlimit count) for one query varied up to
/// threefold between runs, and was identical when no GC ran. The F-4 oracle's one observed flip
/// on an identical tree was such a query, a 64-bit array-element precondition, timing out at the
/// 5 s per-check limit (#1135, run 33139062112, <c>case-000307</c>); its siblings take 30 to 65 ms.
/// </para>
/// <para>
/// With this class and <see cref="Simplify"/>, two runs of the oracle, one under GC stress, one
/// with the server GC and no tiered compilation, issued byte-identical queries with identical
/// rlimit counts (2,382 checks).
/// </para>
/// <para>
/// How: assertions are recorded in their push scopes in the building context. <see cref="Check"/>
/// translates them into a new context (a deterministic traversal; nothing else lives there),
/// creates a solver with the same timeout, and checks. Z3's default solver picks its engine by
/// whether it has seen a push or an assertion after a check (its incremental mode); that choice
/// is replayed, so each check uses the engine the shared-context solver used. The query, the
/// timeout, and the engine are unchanged; only the context's history is removed.
/// </para>
/// <para>
/// The checked solver and its context stay alive until the next <see cref="Check"/> or
/// <see cref="Dispose"/>, so the model and the unknown-reason can be read through
/// <see cref="CheckedSolver"/> with variables mapped by <see cref="TranslateVariables"/>.
/// </para>
/// </remarks>
public sealed class IsolatedSolver : IDisposable
{
    private readonly List<List<BoolExpr>> _scopes = [[]];
    private readonly uint _timeoutMs;
    private bool _incremental;
    private bool _checked;
    private Context? _checkContext;
    private Solver? _checkSolver;

    public IsolatedSolver(uint timeoutMs)
    {
        _timeoutMs = timeoutMs;
    }

    /// <summary>Adds constraints to the current scope.</summary>
    public void Assert(params BoolExpr[] constraints)
    {
        // Z3's default solver switches to incremental mode on an assertion after a check.
        if (_checked)
            _incremental = true;
        _scopes[^1].AddRange(constraints);
    }

    /// <summary>Opens a scope. Like Z3's default solver, this switches to incremental mode.</summary>
    public void Push()
    {
        _incremental = true;
        _scopes.Add([]);
    }

    /// <summary>Discards the innermost scope and its constraints.</summary>
    public void Pop()
    {
        if (_scopes.Count == 1)
            throw new InvalidOperationException("Pop without a matching Push.");
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    /// <summary>
    /// Checks the current constraints in a fresh context. Releases the previous check's
    /// solver and context.
    /// </summary>
    public Status Check()
    {
        ReleaseCheck();
        _checked = true;

        var context = Z3ContextFactory.Create();
        _checkContext = context;
        var solver = context.MkSolver();
        _checkSolver = solver;
        solver.Set("timeout", _timeoutMs);
        if (_incremental)
        {
            // A push switches Z3's default solver to incremental mode for good; popping it
            // again keeps the base-level assertions at the base level, as in the source solver.
            solver.Push();
            solver.Pop();
        }

        for (var level = 0; level < _scopes.Count; level++)
        {
            if (level > 0)
                solver.Push();
            foreach (var constraint in _scopes[level])
                solver.Assert((BoolExpr)constraint.Translate(context));
        }

        return solver.Check();
    }

    /// <summary>
    /// <see cref="Expr.Simplify"/> run in a fresh context, with the result translated back into
    /// <paramref name="context"/>. Z3's rewriter orders the arguments of commutative operators by
    /// term id, so simplifying in a long-lived context gives an argument order that depends on
    /// when the GC released earlier terms (#1135), and the order reaches the solver.
    /// </summary>
    public static BoolExpr Simplify(Context context, BoolExpr expr)
    {
        using var isolated = Z3ContextFactory.Create();
        var simplified = (BoolExpr)expr.Translate(isolated).Simplify();
        return (BoolExpr)simplified.Translate(context);
    }

    /// <summary>The solver of the last <see cref="Check"/>, for its model and unknown-reason.</summary>
    public Solver CheckedSolver =>
        _checkSolver ?? throw new InvalidOperationException("Check has not been called.");

    /// <summary>
    /// Maps translator variables into the last check's context, so the model of
    /// <see cref="CheckedSolver"/> can evaluate them.
    /// </summary>
    public IReadOnlyDictionary<string, (Expr Expr, string Type)> TranslateVariables(
        IReadOnlyDictionary<string, (Expr Expr, string Type)> variables)
    {
        var context = _checkContext
            ?? throw new InvalidOperationException("Check has not been called.");
        var translated = new Dictionary<string, (Expr Expr, string Type)>(variables.Count);
        foreach (var (name, (expr, type)) in variables)
        {
            try
            {
                translated[name] = (expr.Translate(context), type);
            }
            catch (Z3Exception)
            {
                // Keep the original: evaluating it fails, and Counterexample.FromModel records
                // that binding as "<eval failed>" instead of losing the whole verdict.
                translated[name] = (expr, type);
            }
        }
        return translated;
    }

    public void Dispose() => ReleaseCheck();

    private void ReleaseCheck()
    {
        _checkSolver?.Dispose();
        _checkSolver = null;
        _checkContext?.Dispose();
        _checkContext = null;
    }
}
