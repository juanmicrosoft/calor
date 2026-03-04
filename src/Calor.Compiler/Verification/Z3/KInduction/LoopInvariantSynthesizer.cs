using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Verification.Z3.KInduction;

/// <summary>
/// Result of loop invariant synthesis.
/// </summary>
public sealed class LoopInvariantResult
{
    public bool Success { get; }
    public string? Invariant { get; }
    public KInductionResult? ProofResult { get; }
    public string? Message { get; }

    private LoopInvariantResult(bool success, string? invariant, KInductionResult? proofResult, string? message)
    {
        Success = success;
        Invariant = invariant;
        ProofResult = proofResult;
        Message = message;
    }

    public static LoopInvariantResult Synthesized(string invariant, KInductionResult proofResult)
        => new(true, invariant, proofResult, $"Invariant synthesized and proven: {invariant}");

    public static LoopInvariantResult Failed(string message)
        => new(false, null, null, message);

    public static LoopInvariantResult Unsupported(string message)
        => new(false, null, null, message);
}

/// <summary>
/// Synthesizes and verifies loop invariants using templates and k-induction.
/// </summary>
public sealed class LoopInvariantSynthesizer : IDisposable
{
    private readonly KInductionProver _prover;
    private readonly KInductionOptions _options;
    private bool _disposed;

    public LoopInvariantSynthesizer(KInductionOptions? options = null)
    {
        _options = options ?? KInductionOptions.Default;
        _prover = new KInductionProver(_options);
    }

    /// <summary>
    /// Synthesizes and verifies a loop invariant for a for-loop.
    /// </summary>
    public LoopInvariantResult SynthesizeForLoop(BoundForStatement loop, BoundFunction function)
    {
        // Extract loop context
        var context = ExtractLoopContext(loop);

        // Try templates in order of specificity
        var templates = GetOrderedTemplates(context);

        foreach (var template in templates)
        {
            var invariant = template.Generate(context);
            if (invariant == null)
                continue;

            var result = _prover.ProveInvariant(loop, invariant, function);

            if (result.Status == KInductionStatus.Proven)
            {
                return LoopInvariantResult.Synthesized(invariant, result);
            }
        }

        // Try combined invariant as last resort
        var combined = InvariantTemplates.SynthesizeStrongestInvariant(context);
        if (combined != null)
        {
            var result = _prover.ProveInvariant(loop, combined, function);
            if (result.Status == KInductionStatus.Proven)
            {
                return LoopInvariantResult.Synthesized(combined, result);
            }
        }

        return LoopInvariantResult.Failed(
            $"Could not synthesize invariant for loop variable '{loop.LoopVariable.Name}'");
    }

    /// <summary>
    /// Synthesizes and verifies a loop invariant for a while-loop.
    /// </summary>
    public LoopInvariantResult SynthesizeWhileLoop(BoundWhileStatement loop, BoundFunction function)
    {
        // Step 1: Analyze the while loop condition
        var loopInfo = WhileConditionAnalyzer.Analyze(loop.Condition);
        if (loopInfo == null || !loopInfo.IsAnalyzable)
        {
            return LoopInvariantResult.Unsupported(
                "While loop condition could not be analyzed");
        }

        // Step 2: Analyze transition patterns in the body
        var transition = WhileConditionAnalyzer.AnalyzeTransition(loop.Body, loopInfo.LoopVariable!);

        // Step 3: Create loop context for template generation
        var context = WhileConditionAnalyzer.CreateLoopContext(loop, loopInfo, transition);

        // Step 4: Try templates in order of specificity
        var templates = GetOrderedTemplates(context);

        foreach (var template in templates)
        {
            var invariant = template.Generate(context);
            if (invariant == null)
                continue;

            var result = _prover.ProveInvariant(loop, invariant, function);

            if (result.Status == KInductionStatus.Proven)
            {
                return LoopInvariantResult.Synthesized(invariant, result);
            }
        }

        // Step 5: Try combined invariant as last resort
        var combined = InvariantTemplates.SynthesizeStrongestInvariant(context);
        if (combined != null)
        {
            var result = _prover.ProveInvariant(loop, combined, function);
            if (result.Status == KInductionStatus.Proven)
            {
                return LoopInvariantResult.Synthesized(combined, result);
            }
        }

        return LoopInvariantResult.Failed(
            $"Could not synthesize invariant for while loop with variable '{loopInfo.LoopVariable}'");
    }

    /// <summary>
    /// Verifies a user-provided invariant.
    /// </summary>
    public LoopInvariantResult VerifyInvariant(
        BoundForStatement loop,
        string invariant,
        BoundFunction function)
    {
        var result = _prover.ProveInvariant(loop, invariant, function);

        return result.Status switch
        {
            KInductionStatus.Proven => LoopInvariantResult.Synthesized(invariant, result),
            KInductionStatus.Disproven => LoopInvariantResult.Failed(
                $"Invariant '{invariant}' is invalid: {result.CounterexampleDescription}"),
            KInductionStatus.Unknown => LoopInvariantResult.Failed(
                $"Could not verify invariant '{invariant}' within k={_options.MaxK}"),
            _ => LoopInvariantResult.Unsupported(
                $"Invariant '{invariant}' involves unsupported constructs")
        };
    }

    private InvariantTemplates.LoopContext ExtractLoopContext(BoundForStatement loop)
    {
        var modifiedVars = new List<string>();
        var readVars = new List<string>();
        var arrayVars = new List<string>();

        foreach (var stmt in loop.Body)
        {
            CollectVariables(stmt, modifiedVars, readVars, arrayVars);
        }

        // Get bounds
        long? lower = loop.From is BoundIntLiteral fromLit ? fromLit.Value : null;
        long? upper = loop.To is BoundIntLiteral toLit ? toLit.Value : null;

        return new InvariantTemplates.LoopContext
        {
            LoopVariable = loop.LoopVariable.Name,
            LowerBound = lower,
            UpperBound = upper,
            ModifiedVariables = modifiedVars.Distinct().ToList(),
            ReadVariables = readVars.Distinct().ToList(),
            ArrayVariables = arrayVars.Distinct().ToList()
        };
    }

    private void CollectVariables(BoundStatement stmt, List<string> modified, List<string> read, List<string> arrays)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                modified.Add(bind.Variable.Name);
                if (bind.Initializer != null)
                    CollectReadVariables(bind.Initializer, read, arrays);
                break;

            case BoundIfStatement ifStmt:
                CollectReadVariables(ifStmt.Condition, read, arrays);
                foreach (var s in ifStmt.ThenBody)
                    CollectVariables(s, modified, read, arrays);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                {
                    CollectReadVariables(elseIf.Condition, read, arrays);
                    foreach (var s in elseIf.Body)
                        CollectVariables(s, modified, read, arrays);
                }
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        CollectVariables(s, modified, read, arrays);
                break;

            case BoundWhileStatement whileStmt:
                CollectReadVariables(whileStmt.Condition, read, arrays);
                foreach (var s in whileStmt.Body)
                    CollectVariables(s, modified, read, arrays);
                break;

            case BoundForStatement forStmt:
                modified.Add(forStmt.LoopVariable.Name);
                CollectReadVariables(forStmt.From, read, arrays);
                CollectReadVariables(forStmt.To, read, arrays);
                if (forStmt.Step != null)
                    CollectReadVariables(forStmt.Step, read, arrays);
                foreach (var s in forStmt.Body)
                    CollectVariables(s, modified, read, arrays);
                break;

            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                    CollectReadVariables(arg, read, arrays);
                break;

            case BoundReturnStatement ret:
                if (ret.Expression != null)
                    CollectReadVariables(ret.Expression, read, arrays);
                break;
        }
    }

    private void CollectReadVariables(BoundExpression expr, List<string> read, List<string> arrays)
    {
        switch (expr)
        {
            case BoundVariableExpression varExpr:
                read.Add(varExpr.Variable.Name);
                break;

            case BoundBinaryExpression binExpr:
                CollectReadVariables(binExpr.Left, read, arrays);
                CollectReadVariables(binExpr.Right, read, arrays);
                break;

            case BoundUnaryExpression unaryExpr:
                CollectReadVariables(unaryExpr.Operand, read, arrays);
                break;

            case BoundCallExpression callExpr:
                // Heuristic: calls with [] or .get might be array access
                if (callExpr.Target.Contains("[]") || callExpr.Target.Contains(".get"))
                {
                    var arrayName = callExpr.Target.Split(new[] { '[', '.' })[0];
                    if (!string.IsNullOrEmpty(arrayName))
                        arrays.Add(arrayName);
                }
                foreach (var arg in callExpr.Arguments)
                    CollectReadVariables(arg, read, arrays);
                break;
        }
    }

    private static IEnumerable<InvariantTemplates.InvariantTemplate> GetOrderedTemplates(
        InvariantTemplates.LoopContext context)
    {
        // Order templates by likelihood of success for this context

        // For loops with known bounds, bounded loop variable is most likely
        if (context.HasKnownBounds)
        {
            yield return InvariantTemplates.BoundedLoopVariable;
            yield return InvariantTemplates.TerminationDecreasing;
        }

        // If there are array accesses, try array bounds
        if (context.ArrayVariables.Count > 0)
        {
            yield return InvariantTemplates.ArrayIndexBounds;
        }

        // If there are accumulator-like variables, try those
        if (context.ModifiedVariables.Any(v =>
            v.Contains("sum", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("count", StringComparison.OrdinalIgnoreCase)))
        {
            yield return InvariantTemplates.AccumulatorNonNegative;
            yield return InvariantTemplates.MonotonicallyIncreasing;
        }

        // Return remaining templates
        foreach (var template in InvariantTemplates.All)
        {
            // Skip already yielded ones (they might still generate different invariants)
            yield return template;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _prover.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Runner for loop analysis on a module.
/// </summary>
public sealed class LoopAnalysisRunner
{
    private readonly DiagnosticBag _diagnostics;
    private readonly KInductionOptions _options;

    public LoopAnalysisRunner(DiagnosticBag diagnostics, KInductionOptions? options = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? KInductionOptions.Default;
    }

    /// <summary>
    /// Analyzes all loops in a bound module.
    /// </summary>
    public void Analyze(BoundModule module)
    {
        foreach (var function in module.Functions)
        {
            AnalyzeFunction(function);
        }
    }

    /// <summary>
    /// Analyzes loops in a single function.
    /// </summary>
    public void AnalyzeFunction(BoundFunction function)
    {
        using var synthesizer = new LoopInvariantSynthesizer(_options);

        foreach (var stmt in function.Body)
        {
            AnalyzeStatement(stmt, function, synthesizer);
        }
    }

    private void AnalyzeStatement(
        BoundStatement stmt,
        BoundFunction function,
        LoopInvariantSynthesizer synthesizer)
    {
        switch (stmt)
        {
            case BoundForStatement forStmt:
                var forResult = synthesizer.SynthesizeForLoop(forStmt, function);
                ReportLoopResult(forStmt.Span, forResult);

                // Analyze nested statements
                foreach (var s in forStmt.Body)
                    AnalyzeStatement(s, function, synthesizer);
                break;

            case BoundWhileStatement whileStmt:
                var whileResult = synthesizer.SynthesizeWhileLoop(whileStmt, function);
                ReportLoopResult(whileStmt.Span, whileResult);

                foreach (var s in whileStmt.Body)
                    AnalyzeStatement(s, function, synthesizer);
                break;

            case BoundIfStatement ifStmt:
                foreach (var s in ifStmt.ThenBody)
                    AnalyzeStatement(s, function, synthesizer);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                    foreach (var s in elseIf.Body)
                        AnalyzeStatement(s, function, synthesizer);
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        AnalyzeStatement(s, function, synthesizer);
                break;
        }
    }

    private void ReportLoopResult(Parsing.TextSpan span, LoopInvariantResult result)
    {
        if (result.Success)
        {
            _diagnostics.ReportInfo(
                span,
                DiagnosticCode.LoopInvariantSynthesized,
                result.Message ?? $"Loop invariant proven: {result.Invariant}");
        }
        else if (result.Message?.Contains("unsupported", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Don't report unsupported loops - too noisy
        }
        else
        {
            _diagnostics.ReportWarning(
                span,
                DiagnosticCode.LoopInvariantUnknown,
                result.Message ?? "Could not synthesize loop invariant");
        }
    }
}
