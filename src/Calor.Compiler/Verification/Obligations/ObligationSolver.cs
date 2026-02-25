using System.Diagnostics;
using Calor.Compiler.Ast;
using Calor.Compiler.Verification.Z3;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// Solves obligations using Z3 with the assume-negate-check pattern.
/// Follows the same pattern as Z3Verifier.VerifyPostcondition().
/// </summary>
public sealed class ObligationSolver : IDisposable
{
    private readonly Context _ctx;
    private readonly uint _timeoutMs;
    private bool _disposed;

    public ObligationSolver(Context ctx, uint timeoutMs = VerificationOptions.DefaultTimeoutMs)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Solve all pending obligations in the tracker.
    /// </summary>
    public void SolveAll(
        ObligationTracker tracker,
        ModuleNode module)
    {
        // Build a lookup of function info for parameter declarations
        var functionInfo = BuildFunctionInfo(module);

        foreach (var obligation in tracker.Obligations)
        {
            if (obligation.Status != ObligationStatus.Pending)
                continue;

            if (functionInfo.TryGetValue(obligation.FunctionId, out var info))
            {
                SolveObligation(obligation, info);
            }
            else
            {
                obligation.Status = ObligationStatus.Unsupported;
                obligation.CounterexampleDescription = $"Function '{obligation.FunctionId}' not found";
            }
        }
    }

    private void SolveObligation(Obligation obligation, FunctionInfo info)
    {
        var sw = Stopwatch.StartNew();

        var translator = new ContractTranslator(_ctx);

        // Declare all function parameters
        foreach (var (name, type) in info.Parameters)
        {
            if (!translator.DeclareVariable(name, type))
            {
                obligation.Status = ObligationStatus.Unsupported;
                obligation.CounterexampleDescription =
                    ContractTranslator.DiagnoseUnsupportedType(type);
                obligation.SolverDuration = sw.Elapsed;
                return;
            }
        }

        // For RefinementEntry obligations, declare the self-variable
        // so # in the predicate resolves to the parameter being checked
        if (obligation.Kind == ObligationKind.RefinementEntry)
        {
            // The self variable is the parameter itself — it's already declared above.
            // We push it as the self-ref target for predicate translation.
            var paramName = ExtractParameterName(obligation.Description);
            if (paramName != null)
            {
                translator.PushSelfVariable(paramName);
            }
        }

        // Assert preconditions as assumptions
        foreach (var pre in info.Preconditions)
        {
            var preExpr = translator.TranslateBoolExpr(pre.Condition);
            if (preExpr != null)
            {
                // Preconditions are assumed true
            }
            else
            {
                // Can't translate precondition — continue without it
            }
        }

        // Translate the obligation condition
        var conditionExpr = translator.TranslateBoolExpr(obligation.Condition);
        if (conditionExpr == null)
        {
            obligation.Status = ObligationStatus.Unsupported;
            obligation.CounterexampleDescription =
                translator.DiagnoseBoolExprFailure(obligation.Condition)
                ?? translator.DiagnoseTranslationFailure(obligation.Condition)
                ?? "Obligation condition could not be translated to Z3";
            obligation.SolverDuration = sw.Elapsed;

            if (obligation.Kind == ObligationKind.RefinementEntry)
                translator.PopSelfVariable();
            return;
        }

        try
        {
            var solver = _ctx.MkSolver();
            solver.Set("timeout", _timeoutMs);

            // ASSUME: Assert all translatable preconditions
            foreach (var pre in info.Preconditions)
            {
                var preExpr = translator.TranslateBoolExpr(pre.Condition);
                if (preExpr != null)
                {
                    solver.Assert(preExpr);
                }
            }

            // NEGATE: Assert NOT(obligation condition)
            // If UNSAT -> obligation always holds under preconditions -> Discharged
            solver.Assert(_ctx.MkNot(conditionExpr));

            // CHECK
            var status = solver.Check();

            obligation.SolverDuration = sw.Elapsed;

            switch (status)
            {
                case Status.UNSATISFIABLE:
                    obligation.Status = ObligationStatus.Discharged;
                    break;

                case Status.SATISFIABLE:
                    obligation.Status = ObligationStatus.Failed;
                    obligation.CounterexampleDescription =
                        ExtractCounterexample(solver.Model, translator.Variables);
                    break;

                default:
                    obligation.Status = ObligationStatus.Timeout;
                    break;
            }
        }
        catch (Z3Exception ex)
        {
            obligation.Status = ObligationStatus.Timeout;
            obligation.CounterexampleDescription = $"Z3 solver error: {ex.Message}";
            obligation.SolverDuration = sw.Elapsed;
        }
        finally
        {
            if (obligation.Kind == ObligationKind.RefinementEntry)
                translator.PopSelfVariable();
        }
    }

    /// <summary>
    /// Extract a human-readable counterexample from a Z3 model.
    /// </summary>
    private static string ExtractCounterexample(
        Model model,
        IReadOnlyDictionary<string, (Expr Expr, string Type)> variables)
    {
        var values = new List<string>();

        foreach (var (name, (expr, _)) in variables)
        {
            // Skip internal variables
            if (name.Contains("$") || name.StartsWith("__"))
                continue;

            try
            {
                var value = model.Evaluate(expr, true);
                values.Add($"{name}={value}");
            }
            catch
            {
                values.Add($"{name}=<unavailable>");
            }
        }

        if (values.Count == 0)
            return "Counterexample found (values unavailable)";

        return $"Counterexample: {string.Join(", ", values)}";
    }

    /// <summary>
    /// Extract the parameter name from an obligation description like
    /// "Parameter 'x' must satisfy inline refinement".
    /// </summary>
    private static string? ExtractParameterName(string description)
    {
        var start = description.IndexOf('\'');
        var end = description.IndexOf('\'', start + 1);
        if (start >= 0 && end > start)
            return description[(start + 1)..end];
        return null;
    }

    private static Dictionary<string, FunctionInfo> BuildFunctionInfo(ModuleNode module)
    {
        var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);

        foreach (var func in module.Functions)
        {
            var parameters = func.Parameters
                .Select(p => (p.Name, p.TypeName))
                .ToList();

            result[func.Id] = new FunctionInfo(
                parameters,
                func.Preconditions,
                func.Output?.TypeName);
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var parameters = method.Parameters
                    .Select(p => (p.Name, p.TypeName))
                    .ToList();

                result[method.Id] = new FunctionInfo(
                    parameters,
                    method.Preconditions,
                    method.Output?.TypeName);
            }
        }

        return result;
    }

    private sealed record FunctionInfo(
        List<(string Name, string TypeName)> Parameters,
        IReadOnlyList<RequiresNode> Preconditions,
        string? OutputType);

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ctx.Dispose();
        }
    }
}
