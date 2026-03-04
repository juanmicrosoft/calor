using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Microsoft.Z3;

namespace Calor.Compiler.Verification.Z3.KInduction;

/// <summary>
/// Result of a k-induction proof attempt.
/// </summary>
public enum KInductionStatus
{
    /// <summary>Property proven to hold.</summary>
    Proven,
    /// <summary>Property proven to NOT hold (counterexample found).</summary>
    Disproven,
    /// <summary>Could not prove or disprove within the given k bound.</summary>
    Unknown,
    /// <summary>Property involves unsupported constructs.</summary>
    Unsupported
}

/// <summary>
/// Result of a k-induction verification.
/// </summary>
public sealed class KInductionResult
{
    public KInductionStatus Status { get; }
    public int K { get; }
    public string? Invariant { get; }
    public string? CounterexampleDescription { get; }
    public TimeSpan Duration { get; }

    public KInductionResult(
        KInductionStatus status,
        int k,
        string? invariant = null,
        string? counterexampleDescription = null,
        TimeSpan duration = default)
    {
        Status = status;
        K = k;
        Invariant = invariant;
        CounterexampleDescription = counterexampleDescription;
        Duration = duration;
    }
}

/// <summary>
/// Options for k-induction proving.
/// </summary>
public sealed class KInductionOptions
{
    /// <summary>
    /// Maximum k value to try before giving up.
    /// </summary>
    public int MaxK { get; init; } = 10;

    /// <summary>
    /// Z3 solver timeout in milliseconds.
    /// </summary>
    public uint TimeoutMs { get; init; } = 10000;

    /// <summary>
    /// Whether to use invariant templates for strengthening.
    /// </summary>
    public bool UseInvariantTemplates { get; init; } = true;

    public static KInductionOptions Default => new();
}

/// <summary>
/// K-induction prover for loop properties.
///
/// K-induction is a technique to prove loop invariants:
/// 1. Base case: Invariant holds at loop entry
/// 2. Inductive case: If invariant holds for k iterations, it holds for iteration k+1
/// </summary>
public sealed class KInductionProver : IDisposable
{
    private readonly Context _ctx;
    private readonly KInductionOptions _options;
    private bool _disposed;

    public KInductionProver(KInductionOptions? options = null)
    {
        _ctx = new Context(new Dictionary<string, string>
        {
            { "model", "true" },
            { "proof", "false" }
        });
        _options = options ?? KInductionOptions.Default;
    }

    /// <summary>
    /// Proves a loop invariant using k-induction.
    /// </summary>
    /// <param name="loop">The bound for statement to verify.</param>
    /// <param name="invariantExpression">The invariant to prove (as expression string).</param>
    /// <param name="function">The containing function for context.</param>
    /// <returns>The verification result.</returns>
    public KInductionResult ProveInvariant(
        BoundForStatement loop,
        string invariantExpression,
        BoundFunction function)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Extract loop context
        var context = ExtractLoopContext(loop);

        // Try k-induction with increasing k
        for (var k = 1; k <= _options.MaxK; k++)
        {
            var result = TryKInduction(loop, invariantExpression, function, k);

            if (result.Status == KInductionStatus.Proven)
            {
                return new KInductionResult(
                    KInductionStatus.Proven,
                    k,
                    invariantExpression,
                    duration: sw.Elapsed);
            }

            if (result.Status == KInductionStatus.Disproven)
            {
                return new KInductionResult(
                    KInductionStatus.Disproven,
                    k,
                    invariantExpression,
                    result.CounterexampleDescription,
                    sw.Elapsed);
            }
        }

        return new KInductionResult(
            KInductionStatus.Unknown,
            _options.MaxK,
            invariantExpression,
            duration: sw.Elapsed);
    }

    /// <summary>
    /// Proves a while loop invariant using k-induction.
    /// </summary>
    public KInductionResult ProveInvariant(
        BoundWhileStatement loop,
        string invariantExpression,
        BoundFunction function)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var k = 1; k <= _options.MaxK; k++)
        {
            var result = TryKInductionWhile(loop, invariantExpression, function, k);

            if (result.Status == KInductionStatus.Proven)
            {
                return new KInductionResult(
                    KInductionStatus.Proven,
                    k,
                    invariantExpression,
                    duration: sw.Elapsed);
            }

            if (result.Status == KInductionStatus.Disproven)
            {
                return new KInductionResult(
                    KInductionStatus.Disproven,
                    k,
                    invariantExpression,
                    result.CounterexampleDescription,
                    sw.Elapsed);
            }
        }

        return new KInductionResult(
            KInductionStatus.Unknown,
            _options.MaxK,
            invariantExpression,
            duration: sw.Elapsed);
    }

    /// <summary>
    /// Synthesizes and verifies a loop invariant.
    /// </summary>
    public KInductionResult SynthesizeAndProve(BoundForStatement loop, BoundFunction function)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Extract loop context for template instantiation
        var context = ExtractLoopContext(loop);

        // Try each template
        foreach (var template in InvariantTemplates.All)
        {
            var invariant = template.Generate(context);
            if (invariant == null)
                continue;

            var result = ProveInvariant(loop, invariant, function);
            if (result.Status == KInductionStatus.Proven)
            {
                return result;
            }
        }

        // Try combined invariant
        var combined = InvariantTemplates.SynthesizeStrongestInvariant(context);
        if (combined != null)
        {
            var result = ProveInvariant(loop, combined, function);
            if (result.Status == KInductionStatus.Proven)
            {
                return result;
            }
        }

        return new KInductionResult(
            KInductionStatus.Unknown,
            _options.MaxK,
            duration: sw.Elapsed);
    }

    private KInductionResult TryKInduction(
        BoundForStatement loop,
        string invariantExpression,
        BoundFunction function,
        int k)
    {
        try
        {
            var solver = _ctx.MkSolver();
            solver.Set("timeout", _options.TimeoutMs);

            // Create variables
            var loopVar = _ctx.MkBVConst(loop.LoopVariable.Name, 32);

            // Get bounds
            var fromValue = GetIntValue(loop.From);
            var toValue = GetIntValue(loop.To);

            if (fromValue == null || toValue == null)
            {
                return new KInductionResult(KInductionStatus.Unsupported, k);
            }

            var lower = _ctx.MkBV(fromValue.Value, 32);
            var upper = _ctx.MkBV(toValue.Value, 32);

            // Base case: invariant holds at loop entry
            // Assert: loopVar == lower && !invariant
            var baseSolver = _ctx.MkSolver();
            baseSolver.Set("timeout", _options.TimeoutMs);
            baseSolver.Assert(_ctx.MkEq(loopVar, lower));

            var baseInvariant = ParseInvariant(invariantExpression, loop.LoopVariable.Name, loopVar);
            if (baseInvariant == null)
            {
                return new KInductionResult(KInductionStatus.Unsupported, k);
            }

            baseSolver.Assert(_ctx.MkNot(baseInvariant));

            var baseStatus = baseSolver.Check();
            if (baseStatus == Status.SATISFIABLE)
            {
                // Base case fails - invariant doesn't hold at entry
                return new KInductionResult(
                    KInductionStatus.Disproven,
                    k,
                    counterexampleDescription: $"Invariant fails at loop entry: {ExtractCounterexample(baseSolver.Model, loopVar, loop.LoopVariable.Name)}");
            }

            // Inductive case: if invariant holds for k steps, it holds for k+1
            var inductiveSolver = _ctx.MkSolver();
            inductiveSolver.Set("timeout", _options.TimeoutMs);

            // Create k copies of loop variable (for k iterations)
            var iterations = new List<BitVecExpr> { loopVar };
            for (var i = 1; i <= k; i++)
            {
                iterations.Add(_ctx.MkBVConst($"{loop.LoopVariable.Name}_{i}", 32));
            }

            // Assert invariant holds for first k iterations
            for (var i = 0; i < k; i++)
            {
                var inv = ParseInvariant(invariantExpression, loop.LoopVariable.Name, iterations[i]);
                if (inv != null)
                {
                    inductiveSolver.Assert(inv);
                }

                // Assert transition: i+1 = i + step
                var stepValue = loop.Step != null ? GetIntValue(loop.Step) ?? 1 : 1;
                inductiveSolver.Assert(_ctx.MkEq(
                    iterations[i + 1],
                    _ctx.MkBVAdd(iterations[i], _ctx.MkBV(stepValue, 32))));

                // Assert still in bounds
                inductiveSolver.Assert(_ctx.MkBVSLE(iterations[i], upper));
            }

            // Assert invariant does NOT hold for iteration k+1
            var nextInvariant = ParseInvariant(invariantExpression, loop.LoopVariable.Name, iterations[k]);
            if (nextInvariant != null)
            {
                inductiveSolver.Assert(_ctx.MkNot(nextInvariant));
            }

            var inductiveStatus = inductiveSolver.Check();
            if (inductiveStatus == Status.UNSATISFIABLE)
            {
                // Inductive case holds - invariant is proven!
                return new KInductionResult(KInductionStatus.Proven, k, invariantExpression);
            }

            // Could not prove at this k - try higher
            return new KInductionResult(KInductionStatus.Unknown, k);
        }
        catch
        {
            return new KInductionResult(KInductionStatus.Unsupported, k);
        }
    }

    private KInductionResult TryKInductionWhile(
        BoundWhileStatement loop,
        string invariantExpression,
        BoundFunction function,
        int k)
    {
        try
        {
            // Step 1: Analyze the while loop condition
            var loopInfo = WhileConditionAnalyzer.Analyze(loop.Condition);
            if (loopInfo == null || !loopInfo.IsAnalyzable)
            {
                // Can't analyze - return unknown
                return new KInductionResult(KInductionStatus.Unknown, k);
            }

            var loopVarName = loopInfo.LoopVariable!;

            // Step 2: Analyze body for transition patterns
            var transition = WhileConditionAnalyzer.AnalyzeTransition(loop.Body, loopVarName);
            if (transition == null || !transition.IsWellFormed)
            {
                // Can't determine how loop variable changes
                return new KInductionResult(KInductionStatus.Unknown, k);
            }

            // Step 3: Create Z3 solver and variables
            var solver = _ctx.MkSolver();
            solver.Set("timeout", _options.TimeoutMs);

            var loopVar = _ctx.MkBVConst(loopVarName, 32);

            // Step 4: Base case - invariant holds at loop entry
            // For while loops, we assume the loop variable starts at some value
            // that satisfies the preconditions
            var baseSolver = _ctx.MkSolver();
            baseSolver.Set("timeout", _options.TimeoutMs);

            // Assert initial conditions based on loop info
            if (loopInfo.LowerBound.HasValue)
            {
                baseSolver.Assert(_ctx.MkBVSGE(loopVar, _ctx.MkBV(loopInfo.LowerBound.Value, 32)));
            }
            if (loopInfo.UpperBound.HasValue)
            {
                baseSolver.Assert(_ctx.MkBVSLE(loopVar, _ctx.MkBV(loopInfo.UpperBound.Value, 32)));
            }

            var baseInvariant = ParseInvariant(invariantExpression, loopVarName, loopVar);
            if (baseInvariant == null)
            {
                return new KInductionResult(KInductionStatus.Unsupported, k);
            }

            // Check if invariant fails at entry
            baseSolver.Assert(_ctx.MkNot(baseInvariant));
            var baseStatus = baseSolver.Check();
            if (baseStatus == Status.SATISFIABLE)
            {
                return new KInductionResult(
                    KInductionStatus.Disproven,
                    k,
                    counterexampleDescription: $"Invariant fails at loop entry: {ExtractCounterexample(baseSolver.Model, loopVar, loopVarName)}");
            }

            // Step 5: Inductive case - if invariant holds for k iterations, it holds for k+1
            var inductiveSolver = _ctx.MkSolver();
            inductiveSolver.Set("timeout", _options.TimeoutMs);

            // Create k copies of loop variable (for k iterations)
            var iterations = new List<BitVecExpr> { loopVar };
            for (var i = 1; i <= k; i++)
            {
                iterations.Add(_ctx.MkBVConst($"{loopVarName}_{i}", 32));
            }

            // Assert loop condition holds for all k iterations
            for (var i = 0; i < k; i++)
            {
                // Build loop condition for iteration i
                var iterCondition = BuildLoopCondition(loopInfo, iterations[i]);
                if (iterCondition != null)
                {
                    inductiveSolver.Assert(iterCondition);
                }

                // Assert invariant holds for iteration i
                var inv = ParseInvariant(invariantExpression, loopVarName, iterations[i]);
                if (inv != null)
                {
                    inductiveSolver.Assert(inv);
                }

                // Assert transition from i to i+1
                var stepValue = transition.Delta ?? 1;
                if (transition.Kind == WhileConditionAnalyzer.TransitionKind.SubConstant ||
                    transition.Kind == WhileConditionAnalyzer.TransitionKind.Decrement)
                    stepValue = -stepValue;

                inductiveSolver.Assert(_ctx.MkEq(
                    iterations[i + 1],
                    _ctx.MkBVAdd(iterations[i], _ctx.MkBV(stepValue, 32))));
            }

            // Assert loop condition holds for iteration k
            var lastCondition = BuildLoopCondition(loopInfo, iterations[k]);
            if (lastCondition != null)
            {
                inductiveSolver.Assert(lastCondition);
            }

            // Assert invariant does NOT hold for iteration k+1
            var nextInvariant = ParseInvariant(invariantExpression, loopVarName, iterations[k]);
            if (nextInvariant != null)
            {
                inductiveSolver.Assert(_ctx.MkNot(nextInvariant));
            }

            var inductiveStatus = inductiveSolver.Check();
            if (inductiveStatus == Status.UNSATISFIABLE)
            {
                // Inductive case holds - invariant is proven!
                return new KInductionResult(KInductionStatus.Proven, k, invariantExpression);
            }

            // Could not prove at this k - try higher
            return new KInductionResult(KInductionStatus.Unknown, k);
        }
        catch
        {
            return new KInductionResult(KInductionStatus.Unsupported, k);
        }
    }

    private BoolExpr? BuildLoopCondition(WhileConditionAnalyzer.WhileLoopInfo loopInfo, BitVecExpr varExpr)
    {
        // Build Z3 representation of the loop condition
        return loopInfo.ConditionOperator switch
        {
            "<" when loopInfo.UpperBound.HasValue =>
                _ctx.MkBVSLT(varExpr, _ctx.MkBV(loopInfo.UpperBound.Value, 32)),
            "<=" when loopInfo.UpperBound.HasValue =>
                _ctx.MkBVSLE(varExpr, _ctx.MkBV(loopInfo.UpperBound.Value, 32)),
            ">" when loopInfo.LowerBound.HasValue =>
                _ctx.MkBVSGT(varExpr, _ctx.MkBV(loopInfo.LowerBound.Value - 1, 32)),
            ">=" when loopInfo.LowerBound.HasValue =>
                _ctx.MkBVSGE(varExpr, _ctx.MkBV(loopInfo.LowerBound.Value, 32)),
            "!=" when loopInfo.UpperBound.HasValue =>
                _ctx.MkNot(_ctx.MkEq(varExpr, _ctx.MkBV(loopInfo.UpperBound.Value, 32))),
            _ => null
        };
    }

    private BoolExpr? ParseInvariant(string expression, string varName, BitVecExpr varExpr)
    {
        // Parse simple invariant expressions
        // Format: "lower <= var && var <= upper"
        try
        {
            expression = expression.Trim();

            // Handle conjunction
            if (expression.Contains("&&"))
            {
                var parts = expression.Split("&&").Select(p => p.Trim()).ToArray();
                var conjuncts = new List<BoolExpr>();
                foreach (var part in parts)
                {
                    var parsed = ParseSimpleInvariant(part, varName, varExpr);
                    if (parsed != null)
                        conjuncts.Add(parsed);
                }
                if (conjuncts.Count > 0)
                    return _ctx.MkAnd(conjuncts.ToArray());
            }

            return ParseSimpleInvariant(expression, varName, varExpr);
        }
        catch
        {
            return null;
        }
    }

    private BoolExpr? ParseSimpleInvariant(string expression, string varName, BitVecExpr varExpr)
    {
        // Parse: "num <= var" or "var <= num" or "var >= num" etc.
        expression = expression.Replace(" ", "");

        if (expression.Contains("<="))
        {
            var parts = expression.Split("<=");
            if (parts.Length == 2)
            {
                if (parts[0] == varName && int.TryParse(parts[1], out var upper))
                    return _ctx.MkBVSLE(varExpr, _ctx.MkBV(upper, 32));
                if (parts[1] == varName && int.TryParse(parts[0], out var lower))
                    return _ctx.MkBVSLE(_ctx.MkBV(lower, 32), varExpr);
            }
        }

        if (expression.Contains(">="))
        {
            var parts = expression.Split(">=");
            if (parts.Length == 2)
            {
                if (parts[0] == varName && int.TryParse(parts[1], out var lower))
                    return _ctx.MkBVSGE(varExpr, _ctx.MkBV(lower, 32));
                if (parts[1] == varName && int.TryParse(parts[0], out var upper))
                    return _ctx.MkBVSGE(_ctx.MkBV(upper, 32), varExpr);
            }
        }

        if (expression.Contains("<") && !expression.Contains("<="))
        {
            var parts = expression.Split("<");
            if (parts.Length == 2)
            {
                if (parts[0] == varName && int.TryParse(parts[1], out var upper))
                    return _ctx.MkBVSLT(varExpr, _ctx.MkBV(upper, 32));
                if (parts[1] == varName && int.TryParse(parts[0], out var lower))
                    return _ctx.MkBVSLT(_ctx.MkBV(lower, 32), varExpr);
            }
        }

        if (expression.Contains(">") && !expression.Contains(">="))
        {
            var parts = expression.Split(">");
            if (parts.Length == 2)
            {
                if (parts[0] == varName && int.TryParse(parts[1], out var lower))
                    return _ctx.MkBVSGT(varExpr, _ctx.MkBV(lower, 32));
                if (parts[1] == varName && int.TryParse(parts[0], out var upper))
                    return _ctx.MkBVSGT(_ctx.MkBV(upper, 32), varExpr);
            }
        }

        return null;
    }

    private static long? GetIntValue(BoundExpression expr)
    {
        return expr switch
        {
            BoundIntLiteral intLit => intLit.Value,
            _ => null
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

        return new InvariantTemplates.LoopContext
        {
            LoopVariable = loop.LoopVariable.Name,
            LowerBound = GetIntValue(loop.From),
            UpperBound = GetIntValue(loop.To),
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

    private static string ExtractCounterexample(Model model, BitVecExpr var, string varName)
    {
        try
        {
            var value = model.Evaluate(var, true);
            return $"{varName}={value}";
        }
        catch
        {
            return $"{varName}=<unknown>";
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _ctx.Dispose();
            _disposed = true;
        }
    }
}
