using System.Diagnostics;
using System.Text;
using Calor.Compiler.Ast;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Core Z3 verification logic for Calor contracts.
/// </summary>
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
                // Unsupported parameter type
                return new ContractVerificationResult(
                    ContractVerificationStatus.Unsupported,
                    Duration: sw.Elapsed);
            }
        }

        // Translate the precondition
        var preconditionExpr = translator.TranslateBoolExpr(precondition.Condition);
        if (preconditionExpr == null)
        {
            return new ContractVerificationResult(
                ContractVerificationStatus.Unsupported,
                Duration: sw.Elapsed);
        }

        // For preconditions, we just check if they're satisfiable
        // (i.e., there exists some input that satisfies them)
        // This is informational - preconditions are always kept as runtime checks
        var solver = _ctx.MkSolver();
        solver.Set("timeout", _timeoutMs);
        solver.Assert(preconditionExpr);

        var status = solver.Check();

        return status switch
        {
            Status.SATISFIABLE => new ContractVerificationResult(
                ContractVerificationStatus.Proven,
                Duration: sw.Elapsed),
            Status.UNSATISFIABLE => new ContractVerificationResult(
                ContractVerificationStatus.Disproven,
                CounterexampleDescription: "Precondition is never satisfiable - function can never be called correctly",
                Duration: sw.Elapsed),
            _ => new ContractVerificationResult(
                ContractVerificationStatus.Unproven,
                Duration: sw.Elapsed)
        };
    }

    /// <summary>
    /// Verifies a postcondition contract given preconditions.
    /// </summary>
    /// <remarks>
    /// The verification logic:
    /// 1. Assume all preconditions hold
    /// 2. Check if (preconditions && !postcondition) is satisfiable
    ///    - UNSAT → Proven (no counterexample exists, postcondition always holds when preconditions hold)
    ///    - SAT → Disproven (found a counterexample)
    ///    - UNKNOWN → Unproven (timeout or too complex)
    /// </remarks>
    public ContractVerificationResult VerifyPostcondition(
        IReadOnlyList<(string Name, string Type)> parameters,
        string? outputType,
        IReadOnlyList<RequiresNode> preconditions,
        EnsuresNode postcondition)
    {
        var sw = Stopwatch.StartNew();

        var translator = new ContractTranslator(_ctx);

        // Declare all parameters
        foreach (var (name, type) in parameters)
        {
            if (!translator.DeclareVariable(name, type))
            {
                return new ContractVerificationResult(
                    ContractVerificationStatus.Unsupported,
                    Duration: sw.Elapsed);
            }
        }

        // Declare 'result' variable for postconditions if there's an output type
        if (!string.IsNullOrEmpty(outputType))
        {
            if (!translator.DeclareVariable("result", outputType))
            {
                return new ContractVerificationResult(
                    ContractVerificationStatus.Unsupported,
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
                // If we can't translate a precondition, we can't verify the postcondition
                return new ContractVerificationResult(
                    ContractVerificationStatus.Unsupported,
                    Duration: sw.Elapsed);
            }
            preconditionExprs.Add(preExpr);
        }

        // Translate the postcondition
        var postconditionExpr = translator.TranslateBoolExpr(postcondition.Condition);
        if (postconditionExpr == null)
        {
            return new ContractVerificationResult(
                ContractVerificationStatus.Unsupported,
                Duration: sw.Elapsed);
        }

        // Create solver
        var solver = _ctx.MkSolver();
        solver.Set("timeout", _timeoutMs);

        // Assert all preconditions
        foreach (var preExpr in preconditionExprs)
        {
            solver.Assert(preExpr);
        }

        // Assert the negation of the postcondition
        // If this is UNSAT, the postcondition always holds when preconditions hold
        solver.Assert(_ctx.MkNot(postconditionExpr));

        var status = solver.Check();

        return status switch
        {
            Status.UNSATISFIABLE => new ContractVerificationResult(
                ContractVerificationStatus.Proven,
                Duration: sw.Elapsed),
            Status.SATISFIABLE => new ContractVerificationResult(
                ContractVerificationStatus.Disproven,
                CounterexampleDescription: ExtractCounterexample(solver.Model, translator.Variables),
                Duration: sw.Elapsed),
            _ => new ContractVerificationResult(
                ContractVerificationStatus.Unproven,
                Duration: sw.Elapsed)
        };
    }

    private static string ExtractCounterexample(Model model, IReadOnlyDictionary<string, (Expr Expr, string Type)> variables)
    {
        var sb = new StringBuilder("Counterexample: ");
        var values = new List<string>();

        foreach (var (name, (expr, _)) in variables)
        {
            try
            {
                var value = model.Evaluate(expr, true);
                values.Add($"{name}={value}");
            }
            catch
            {
                // If we can't evaluate, skip this variable
            }
        }

        if (values.Count == 0)
            return "Counterexample found (values unavailable)";

        sb.Append(string.Join(", ", values));
        return sb.ToString();
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
