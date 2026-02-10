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
public record ImplicationResult(
    ImplicationStatus Status,
    string? CounterexampleDescription = null,
    TimeSpan? Duration = null);

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

        // Declare all parameters
        foreach (var (name, type) in parameters)
        {
            if (!translator.DeclareVariable(name, type))
            {
                // Unsupported parameter type (strings, floats, etc.)
                return new ImplicationResult(
                    ImplicationStatus.Unsupported,
                    Duration: sw.Elapsed);
            }
        }

        // Also declare 'result' variable in case contracts reference it
        // Use i32 as the default type for result
        translator.DeclareVariable("result", "i32");

        // Translate antecedent → Z3 BoolExpr A
        var antecedentExpr = translator.TranslateBoolExpr(antecedent);
        if (antecedentExpr == null)
        {
            return new ImplicationResult(
                ImplicationStatus.Unsupported,
                Duration: sw.Elapsed);
        }

        // Translate consequent → Z3 BoolExpr C
        var consequentExpr = translator.TranslateBoolExpr(consequent);
        if (consequentExpr == null)
        {
            return new ImplicationResult(
                ImplicationStatus.Unsupported,
                Duration: sw.Elapsed);
        }

        // Create solver and add constraint: A AND NOT(C)
        var solver = _ctx.MkSolver();
        solver.Set("timeout", _timeoutMs);
        solver.Assert(antecedentExpr);
        solver.Assert(_ctx.MkNot(consequentExpr));

        // Check satisfiability
        var status = solver.Check();

        return status switch
        {
            // UNSAT means no counterexample exists → A always implies C
            Status.UNSATISFIABLE => new ImplicationResult(
                ImplicationStatus.Proven,
                Duration: sw.Elapsed),

            // SAT means counterexample found → A does not imply C
            Status.SATISFIABLE => new ImplicationResult(
                ImplicationStatus.Disproven,
                CounterexampleDescription: ExtractCounterexample(solver.Model, translator.Variables),
                Duration: sw.Elapsed),

            // UNKNOWN → timeout or too complex
            _ => new ImplicationResult(
                ImplicationStatus.Unknown,
                Duration: sw.Elapsed)
        };
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
        ExpressionNode implementerPostcondition)
    {
        var sw = Stopwatch.StartNew();

        var translator = new ContractTranslator(_ctx);

        // Declare all parameters
        foreach (var (name, type) in parameters)
        {
            if (!translator.DeclareVariable(name, type))
            {
                return new ImplicationResult(
                    ImplicationStatus.Unsupported,
                    Duration: sw.Elapsed);
            }
        }

        // Declare 'result' variable for postconditions
        if (!string.IsNullOrEmpty(outputType))
        {
            if (!translator.DeclareVariable("result", outputType))
            {
                return new ImplicationResult(
                    ImplicationStatus.Unsupported,
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
            return new ImplicationResult(
                ImplicationStatus.Unsupported,
                Duration: sw.Elapsed);
        }

        // Translate interface postcondition → Z3 BoolExpr (the consequent)
        var interfaceExpr = translator.TranslateBoolExpr(interfacePostcondition);
        if (interfaceExpr == null)
        {
            return new ImplicationResult(
                ImplicationStatus.Unsupported,
                Duration: sw.Elapsed);
        }

        // For LSP: implementer postcondition must imply interface postcondition
        // i.e., anything the implementer guarantees should also satisfy what the interface guarantees
        // This means the implementer can only guarantee MORE (stronger postcondition)
        var solver = _ctx.MkSolver();
        solver.Set("timeout", _timeoutMs);
        solver.Assert(implementerExpr);
        solver.Assert(_ctx.MkNot(interfaceExpr));

        var status = solver.Check();

        return status switch
        {
            Status.UNSATISFIABLE => new ImplicationResult(
                ImplicationStatus.Proven,
                Duration: sw.Elapsed),
            Status.SATISFIABLE => new ImplicationResult(
                ImplicationStatus.Disproven,
                CounterexampleDescription: ExtractCounterexample(solver.Model, translator.Variables),
                Duration: sw.Elapsed),
            _ => new ImplicationResult(
                ImplicationStatus.Unknown,
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
            catch (Exception ex)
            {
                values.Add($"{name}=<eval failed: {ex.GetType().Name}>");
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
