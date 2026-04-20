using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.ContractInference;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Dataflow.Analyses;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3.KInduction;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Options for verification analyses.
/// </summary>
public sealed class VerificationAnalysisOptions
{
    /// <summary>
    /// Enable dataflow analyses (uninitialized variables, dead code).
    /// </summary>
    public bool EnableDataflow { get; init; } = true;

    /// <summary>
    /// Enable bug pattern detection (div by zero, null deref, etc.).
    /// </summary>
    public bool EnableBugPatterns { get; init; } = true;

    /// <summary>
    /// Enable security taint analysis.
    /// </summary>
    public bool EnableTaintAnalysis { get; init; } = true;

    /// <summary>
    /// Enable contract inference for functions without contracts.
    /// </summary>
    public bool EnableContractInference { get; init; } = false; // Off by default - opt-in

    /// <summary>
    /// Enable loop invariant synthesis with k-induction.
    /// </summary>
    public bool EnableKInduction { get; init; } = false; // Off by default - expensive

    /// <summary>
    /// Use Z3 SMT solver for precise analysis (slower but more accurate).
    /// </summary>
    public bool UseZ3Verification { get; init; } = true;

    /// <summary>
    /// Z3 solver timeout in milliseconds.
    /// </summary>
    public uint Z3TimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Bug pattern detection options.
    /// </summary>
    public BugPatternOptions? BugPatternOptions { get; init; }

    /// <summary>
    /// Taint analysis options.
    /// </summary>
    public TaintAnalysisOptions? TaintOptions { get; init; }

    /// <summary>
    /// K-induction options.
    /// </summary>
    public KInductionOptions? KInductionOptions { get; init; }

    public static VerificationAnalysisOptions Default => new();

    public static VerificationAnalysisOptions Fast => new()
    {
        UseZ3Verification = false,
        EnableKInduction = false
    };

    public static VerificationAnalysisOptions Thorough => new()
    {
        EnableDataflow = true,
        EnableBugPatterns = true,
        EnableTaintAnalysis = true,
        EnableKInduction = true,
        UseZ3Verification = true,
        Z3TimeoutMs = 10000
    };
}

/// <summary>
/// Results of verification analyses.
/// </summary>
public sealed class VerificationAnalysisResult
{
    /// <summary>
    /// Number of functions analyzed.
    /// </summary>
    public int FunctionsAnalyzed { get; init; }

    /// <summary>
    /// Number of dataflow issues found.
    /// </summary>
    public int DataflowIssues { get; init; }

    /// <summary>
    /// Number of bug patterns found.
    /// </summary>
    public int BugPatternsFound { get; init; }

    /// <summary>
    /// Number of taint vulnerabilities found.
    /// </summary>
    public int TaintVulnerabilities { get; init; }

    /// <summary>
    /// Number of loop invariants synthesized.
    /// </summary>
    public int LoopInvariantsSynthesized { get; init; }

    /// <summary>
    /// Number of contracts inferred.
    /// </summary>
    public int ContractsInferred { get; init; }

    /// <summary>
    /// Analysis duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Comprehensive verification analysis pass that combines dataflow,
/// bug patterns, taint tracking, and loop analysis.
/// </summary>
public sealed class VerificationAnalysisPass
{
    private readonly DiagnosticBag _diagnostics;
    private readonly VerificationAnalysisOptions _options;

    public VerificationAnalysisPass(DiagnosticBag diagnostics, VerificationAnalysisOptions? options = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? VerificationAnalysisOptions.Default;
    }

    /// <summary>
    /// Runs verification analyses on an AST module by first binding it.
    /// </summary>
    public VerificationAnalysisResult Analyze(ModuleNode module)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Extract precondition-guarded parameters from AST before binding
        var guardedParams = ExtractPreconditionGuardedParams(module);

        // Bind the module to get bound nodes
        var bindingDiagnostics = new DiagnosticBag();
        var binder = new Binder(bindingDiagnostics);
        var boundModule = binder.Bind(module);

        // Run analyses on the bound module with contract info
        var result = AnalyzeBound(boundModule, guardedParams);

        // Run contract inference if enabled
        var contractsInferred = 0;
        if (_options.EnableContractInference)
        {
            try
            {
                var inferencePass = new ContractInferencePass(_diagnostics);
                contractsInferred = inferencePass.Infer(module, boundModule);
            }
            catch
            {
                // Contract inference failures are non-fatal
            }
        }

        sw.Stop();
        return new VerificationAnalysisResult
        {
            FunctionsAnalyzed = result.FunctionsAnalyzed,
            DataflowIssues = result.DataflowIssues,
            BugPatternsFound = result.BugPatternsFound,
            TaintVulnerabilities = result.TaintVulnerabilities,
            LoopInvariantsSynthesized = result.LoopInvariantsSynthesized,
            ContractsInferred = contractsInferred,
            Duration = sw.Elapsed
        };
    }

    /// <summary>
    /// Extracts parameter names referenced in preconditions, keyed by function name.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ExtractPreconditionGuardedParams(ModuleNode module)
    {
        var result = new Dictionary<string, HashSet<string>>();

        foreach (var func in module.Functions)
        {
            if (func.Preconditions.Count == 0)
                continue;

            var paramNames = func.Parameters.Select(p => p.Name).ToHashSet();
            var guardedNames = new HashSet<string>();

            foreach (var pre in func.Preconditions)
            {
                CollectReferencedNames(pre.Condition, paramNames, guardedNames);
            }

            if (guardedNames.Count > 0)
                result[func.Name] = guardedNames;
        }

        // Also extract from class members with preconditions
        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                if (method.Preconditions.Count == 0) continue;
                var paramNames = method.Parameters.Select(p => p.Name).ToHashSet();
                var guardedNames = new HashSet<string>();
                foreach (var pre in method.Preconditions)
                    CollectReferencedNames(pre.Condition, paramNames, guardedNames);
                if (guardedNames.Count > 0)
                    result[$"{cls.Name}.{method.Name}"] = guardedNames;
            }

            foreach (var ctor in cls.Constructors)
            {
                if (ctor.Preconditions.Count == 0) continue;
                var paramNames = ctor.Parameters.Select(p => p.Name).ToHashSet();
                var guardedNames = new HashSet<string>();
                foreach (var pre in ctor.Preconditions)
                    CollectReferencedNames(pre.Condition, paramNames, guardedNames);
                if (guardedNames.Count > 0)
                    result[$"{cls.Name}..ctor"] = guardedNames;
            }

            foreach (var op in cls.OperatorOverloads)
            {
                if (op.Preconditions.Count == 0) continue;
                var paramNames = op.Parameters.Select(p => p.Name).ToHashSet();
                var guardedNames = new HashSet<string>();
                foreach (var pre in op.Preconditions)
                    CollectReferencedNames(pre.Condition, paramNames, guardedNames);
                if (guardedNames.Count > 0)
                    result[$"{cls.Name}.op_{op.Kind}"] = guardedNames;
            }

            // Property/indexer accessor preconditions
            foreach (var prop in cls.Properties)
            {
                if (prop.Setter?.Preconditions.Count > 0)
                {
                    var setterParams = new HashSet<string> { "value" };
                    var guardedNames = new HashSet<string>();
                    foreach (var pre in prop.Setter.Preconditions)
                        CollectReferencedNames(pre.Condition, setterParams, guardedNames);
                    if (guardedNames.Count > 0)
                        result[$"{cls.Name}.{prop.Name}.set"] = guardedNames;
                }
            }

            // Recurse into nested classes
            foreach (var nested in cls.NestedClasses)
                ExtractFromClass(nested, result);
        }

        return result;
    }

    private static void ExtractFromClass(Ast.ClassDefinitionNode cls, Dictionary<string, HashSet<string>> result)
    {
        foreach (var method in cls.Methods)
        {
            if (method.Preconditions.Count == 0) continue;
            var paramNames = method.Parameters.Select(p => p.Name).ToHashSet();
            var guardedNames = new HashSet<string>();
            foreach (var pre in method.Preconditions)
                CollectReferencedNames(pre.Condition, paramNames, guardedNames);
            if (guardedNames.Count > 0)
                result[$"{cls.Name}.{method.Name}"] = guardedNames;
        }

        foreach (var ctor in cls.Constructors)
        {
            if (ctor.Preconditions.Count == 0) continue;
            var paramNames = ctor.Parameters.Select(p => p.Name).ToHashSet();
            var guardedNames = new HashSet<string>();
            foreach (var pre in ctor.Preconditions)
                CollectReferencedNames(pre.Condition, paramNames, guardedNames);
            if (guardedNames.Count > 0)
                result[$"{cls.Name}..ctor"] = guardedNames;
        }

        foreach (var nested in cls.NestedClasses)
            ExtractFromClass(nested, result);
    }

    /// <summary>
    /// Recursively collects variable names from an expression that match parameter names.
    /// </summary>
    private static void CollectReferencedNames(
        Ast.ExpressionNode expr,
        HashSet<string> paramNames,
        HashSet<string> collected)
    {
        switch (expr)
        {
            case Ast.ReferenceNode refNode:
                if (paramNames.Contains(refNode.Name))
                    collected.Add(refNode.Name);
                break;

            case Ast.BinaryOperationNode binOp:
                CollectReferencedNames(binOp.Left, paramNames, collected);
                CollectReferencedNames(binOp.Right, paramNames, collected);
                break;

            case Ast.UnaryOperationNode unary:
                CollectReferencedNames(unary.Operand, paramNames, collected);
                break;

            case Ast.ConditionalExpressionNode cond:
                CollectReferencedNames(cond.Condition, paramNames, collected);
                CollectReferencedNames(cond.WhenTrue, paramNames, collected);
                CollectReferencedNames(cond.WhenFalse, paramNames, collected);
                break;
        }
    }

    /// <summary>
    /// Runs verification analyses on an already-bound module.
    /// </summary>
    public VerificationAnalysisResult AnalyzeBound(BoundModule module,
        Dictionary<string, HashSet<string>>? preconditionGuardedParams = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dataflowIssues = 0;
        var bugPatternsFound = 0;
        var taintVulnerabilities = 0;
        var loopInvariants = 0;

        foreach (var function in module.Functions)
        {
            // Dataflow analysis
            if (_options.EnableDataflow)
            {
                dataflowIssues += RunDataflowAnalysis(function);
            }

            // Bug pattern detection
            if (_options.EnableBugPatterns)
            {
                var bugOptions = _options.BugPatternOptions ?? new BugPatternOptions
                {
                    UseZ3Verification = _options.UseZ3Verification,
                    Z3TimeoutMs = _options.Z3TimeoutMs,
                    PreconditionGuardedParams = preconditionGuardedParams
                };
                var bugRunner = new BugPatternRunner(_diagnostics, bugOptions);
                var beforeCount = _diagnostics.Count;
                bugRunner.CheckFunction(function);
                bugPatternsFound += _diagnostics.Count - beforeCount;
            }

            // Taint analysis
            if (_options.EnableTaintAnalysis)
            {
                var taintOptions = _options.TaintOptions ?? TaintAnalysisOptions.Default;
                var taintAnalysis = new TaintAnalysis(function, taintOptions, function.DeclaredEffects);
                taintVulnerabilities += taintAnalysis.Vulnerabilities.Count;
                taintAnalysis.ReportDiagnostics(_diagnostics);
            }

            // K-induction for loops
            if (_options.EnableKInduction)
            {
                var kOptions = _options.KInductionOptions ?? new KInductionOptions
                {
                    TimeoutMs = _options.Z3TimeoutMs
                };
                var loopRunner = new LoopAnalysisRunner(_diagnostics, kOptions);
                var beforeCount = _diagnostics.Count;
                loopRunner.AnalyzeFunction(function);
                // Count synthesized invariants (info diagnostics)
                loopInvariants = _diagnostics.Skip(beforeCount)
                    .Count(d => d.Code == DiagnosticCode.LoopInvariantSynthesized);
            }
        }

        sw.Stop();
        return new VerificationAnalysisResult
        {
            FunctionsAnalyzed = module.Functions.Count,
            DataflowIssues = dataflowIssues,
            BugPatternsFound = bugPatternsFound,
            TaintVulnerabilities = taintVulnerabilities,
            LoopInvariantsSynthesized = loopInvariants,
            Duration = sw.Elapsed
        };
    }

    private int RunDataflowAnalysis(BoundFunction function)
    {
        var issueCount = 0;

        try
        {
            // Build CFG
            var cfg = ControlFlowGraph.Build(function);

            // Get parameter names for initialization analysis
            var paramNames = function.Symbol.Parameters.Select(p => p.Name);

            // Uninitialized variable analysis
            var uninitAnalysis = new UninitializedVariablesAnalysis(cfg, paramNames);
            uninitAnalysis.ReportDiagnostics(_diagnostics);
            issueCount += uninitAnalysis.UninitializedUses.Count;

            // Live variable analysis for dead store detection
            var liveAnalysis = new LiveVariablesAnalysis(cfg);
            foreach (var (block, stmt, variable) in liveAnalysis.FindDeadAssignments())
            {
                // Skip loop variables and parameters
                if (function.Symbol.Parameters.Any(p => p.Name == variable))
                    continue;

                _diagnostics.ReportWarning(
                    stmt.Span,
                    DiagnosticCode.DeadStore,
                    $"Assignment to '{variable}' is never read (dead store)");
                issueCount++;
            }
        }
        catch
        {
            // Ignore analysis failures - continue with other analyses
        }

        return issueCount;
    }
}
