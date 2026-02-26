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
                // For IndexBounds obligations, skip undeclarable parameters
                // (e.g., indexed type names like SizedList that aren't Z3-translatable).
                // The obligation condition only references the index and size variables.
                if (obligation.Kind == ObligationKind.IndexBounds)
                    continue;

                obligation.Status = ObligationStatus.Unsupported;
                obligation.CounterexampleDescription =
                    ContractTranslator.DiagnoseUnsupportedType(type);
                obligation.SolverDuration = sw.Elapsed;
                return;
            }
        }

        // Declare extra variables (e.g., indexed type size parameters)
        foreach (var (name, type) in info.ExtraVariables)
        {
            // Only declare if not already declared (could overlap with a parameter name)
            if (!translator.Variables.ContainsKey(name))
            {
                translator.DeclareVariable(name, type);
            }
        }

        // For RefinementEntry obligations, push the self-variable
        // so # in the predicate resolves to the parameter being checked
        if (obligation.Kind == ObligationKind.RefinementEntry && obligation.ParameterName != null)
        {
            translator.PushSelfVariable(obligation.ParameterName);
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

            if (obligation.Kind == ObligationKind.RefinementEntry && obligation.ParameterName != null)
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

            // ASSUME: Assert collected flow-sensitive facts (loop bounds, parameter refinements, etc.)
            // For RefinementEntry obligations, skip collected facts to avoid circular reasoning
            // (the obligation IS the refinement, not an assumption for it).
            if (obligation.Kind != ObligationKind.RefinementEntry)
            {
                foreach (var fact in info.CollectedFacts)
                {
                    var factExpr = translator.TranslateBoolExpr(fact);
                    if (factExpr != null)
                    {
                        solver.Assert(factExpr);
                    }
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
            if (obligation.Kind == ObligationKind.RefinementEntry && obligation.ParameterName != null)
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

    private static Dictionary<string, FunctionInfo> BuildFunctionInfo(ModuleNode module)
    {
        var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);

        // Build indexed type lookup for size parameter injection
        var indexedTypes = new Dictionary<string, IndexedTypeNode>(StringComparer.Ordinal);
        foreach (var itype in module.IndexedTypes)
        {
            indexedTypes[itype.Name] = itype;
        }

        foreach (var func in module.Functions)
        {
            var parameters = func.Parameters
                .Select(p => (p.Name, p.TypeName))
                .ToList();

            // Collect flow-sensitive facts (loop bounds, etc.)
            var factCollector = new FactCollector();
            factCollector.CollectFromFunction(func);

            // Add size parameter variables for indexed-typed parameters
            var extraVars = new List<(string Name, string TypeName)>();
            foreach (var param in func.Parameters)
            {
                var baseTypeName = param.TypeName;
                var genericIdx = baseTypeName.IndexOf('<');
                if (genericIdx > 0)
                    baseTypeName = baseTypeName.Substring(0, genericIdx);

                if (indexedTypes.TryGetValue(baseTypeName, out var itype))
                {
                    // Add the size parameter as an integer variable
                    extraVars.Add((itype.SizeParam, "i32"));

                    // If the indexed type has a constraint, add it as a fact
                    if (itype.Constraint != null)
                    {
                        factCollector.Facts.Add(
                            FactCollector.SubstituteSelfRefStatic(itype.Constraint, itype.SizeParam));
                    }
                }
            }

            result[func.Id] = new FunctionInfo(
                parameters,
                func.Preconditions,
                func.Output?.TypeName,
                factCollector.Facts,
                extraVars);
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
                    method.Output?.TypeName,
                    new List<ExpressionNode>(),
                    new List<(string, string)>());
            }
        }

        return result;
    }

    private sealed record FunctionInfo(
        List<(string Name, string TypeName)> Parameters,
        IReadOnlyList<RequiresNode> Preconditions,
        string? OutputType,
        List<ExpressionNode> CollectedFacts,
        List<(string Name, string TypeName)> ExtraVariables);

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ctx.Dispose();
        }
    }
}
