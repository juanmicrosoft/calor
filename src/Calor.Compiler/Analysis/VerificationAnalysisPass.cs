using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.ContractInference;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Analysis.Dataflow.Analyses;
using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
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
        BindingDiagnosticPolicy.PropagateCompilationErrors(bindingDiagnostics, _diagnostics);
        foreach (var diagnostic in bindingDiagnostics
                     .Where(diagnostic => diagnostic.Code == DiagnosticCode.AnalysisUnsupportedNode))
        {
            if (!_diagnostics.Any(existing =>
                    existing.Code == diagnostic.Code
                    && existing.Span == diagnostic.Span
                    && existing.Message == diagnostic.Message))
            {
                _diagnostics.Add(diagnostic);
            }
        }

        // Analysis instrumentation remains opt-in/noisy and is routed separately
        // from the correctness diagnostics propagated by BindingDiagnosticPolicy.
        foreach (var d in bindingDiagnostics.Where(d => d.Code == DiagnosticCode.AnalysisIncomplete))
        {
            _diagnostics.Report(d.Span, d.Code, d.Message, d.Severity);
        }

        // Run analyses on the bound module with contract info
        var result = AnalyzeBoundCore(
            boundModule,
            guardedParams,
            BuildGuardedParameterIds(module, boundModule));

        // Run contract inference if enabled
        var contractsInferred = 0;
        if (_options.EnableContractInference)
        {
            try
            {
                var inferencePass = new ContractInferencePass(_diagnostics);
                contractsInferred = inferencePass.Infer(module, boundModule);
            }
            catch (Exception ex)
            {
                ReportAnalysisIncomplete(
                    module.Span,
                    $"Contract inference did not complete: {ex.GetType().Name}");
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
                    result[$"{cls.Name}.{(ctor.IsStatic ? ".cctor" : ".ctor")}"] = guardedNames;
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
                result[$"{cls.Name}.{(ctor.IsStatic ? ".cctor" : ".ctor")}"] = guardedNames;
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
        Visit(expr, new HashSet<string>(StringComparer.Ordinal));

        void Visit(Ast.AstNode node, HashSet<string> shadowed)
        {
            switch (node)
            {
                case Ast.ReferenceNode reference:
                    if (paramNames.Contains(reference.Name) && !shadowed.Contains(reference.Name))
                        collected.Add(reference.Name);
                    return;

                case Ast.ForallExpressionNode forall:
                {
                    var nested = new HashSet<string>(shadowed, StringComparer.Ordinal);
                    nested.UnionWith(forall.BoundVariables.Select(variable => variable.Name));
                    Visit(forall.Body, nested);
                    return;
                }

                case Ast.ExistsExpressionNode exists:
                {
                    var nested = new HashSet<string>(shadowed, StringComparer.Ordinal);
                    nested.UnionWith(exists.BoundVariables.Select(variable => variable.Name));
                    Visit(exists.Body, nested);
                    return;
                }

                case Ast.LambdaExpressionNode lambda:
                {
                    var nested = new HashSet<string>(shadowed, StringComparer.Ordinal);
                    nested.UnionWith(lambda.Parameters.Select(parameter => parameter.Name));
                    if (lambda.ExpressionBody != null)
                        Visit(lambda.ExpressionBody, nested);
                    if (lambda.StatementBody != null)
                    {
                        foreach (var statement in lambda.StatementBody)
                            Visit(statement, nested);
                    }
                    return;
                }
            }

            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                Visit(child, shadowed);
        }
    }

    private static IEnumerable<Ast.AstNode> DescendantsAndSelf(Ast.AstNode node)
    {
        yield return node;
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    /// <summary>
    /// Runs verification analyses on an already-bound module.
    /// </summary>
    public VerificationAnalysisResult AnalyzeBound(BoundModule module,
        Dictionary<string, HashSet<string>>? preconditionGuardedParams = null)
        => AnalyzeBoundCore(module, preconditionGuardedParams, null);

    private VerificationAnalysisResult AnalyzeBoundCore(
        BoundModule module,
        Dictionary<string, HashSet<string>>? preconditionGuardedParams,
        IReadOnlyDictionary<SymbolId, IReadOnlySet<SymbolId>>? guardedParameterIds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dataflowIssues = 0;
        var bugPatternsFound = 0;
        var taintVulnerabilities = 0;
        var loopInvariants = 0;

        if (_options.EnableTaintAnalysis)
        {
            var taintRunner = new TaintAnalysisRunner(
                _diagnostics,
                _options.TaintOptions ?? TaintAnalysisOptions.Default);
            taintRunner.Analyze(module);
            taintVulnerabilities = taintRunner.VulnerabilityCount;
        }

        foreach (var function in module.Functions)
        {
            var incomplete = BoundNodeHelpers.GetAnalysisIncompleteNodes(function).FirstOrDefault();
            if (incomplete != null)
            {
                ReportAnalysisIncomplete(
                    incomplete.Span,
                    $"Analysis of '{function.Symbol.DisplaySignature}' is incomplete because " +
                    $"the bound tree contains '{incomplete.GetType().Name}'");
            }

            // Dataflow analysis
            if (_options.EnableDataflow)
            {
                dataflowIssues += RunDataflowAnalysis(function);
            }

            // Bug pattern detection
            if (_options.EnableBugPatterns)
            {
                var bugOptions = CreateBugPatternOptions(
                    preconditionGuardedParams,
                    guardedParameterIds);
                var bugRunner = new BugPatternRunner(_diagnostics, bugOptions);
                var beforeCount = _diagnostics.Count;
                bugRunner.CheckFunction(function);
                bugPatternsFound += _diagnostics.Count - beforeCount;
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

    private BugPatternOptions CreateBugPatternOptions(
        Dictionary<string, HashSet<string>>? guardedNames,
        IReadOnlyDictionary<SymbolId, IReadOnlySet<SymbolId>>? guardedIds)
    {
        var configured = _options.BugPatternOptions;
        return new BugPatternOptions
        {
            CheckDivisionByZero = configured?.CheckDivisionByZero ?? true,
            CheckIndexOutOfBounds = configured?.CheckIndexOutOfBounds ?? true,
            CheckNullDereference = configured?.CheckNullDereference ?? true,
            CheckOverflow = configured?.CheckOverflow ?? true,
            CheckMissingPreconditions = configured?.CheckMissingPreconditions ?? true,
            CheckOffByOne = configured?.CheckOffByOne ?? true,
            ReportOnlyVerified = configured?.ReportOnlyVerified ?? false,
            UseZ3Verification = configured?.UseZ3Verification ?? _options.UseZ3Verification,
            Z3TimeoutMs = configured?.Z3TimeoutMs ?? _options.Z3TimeoutMs,
            PreconditionGuardedParams = configured?.PreconditionGuardedParams ?? guardedNames,
            PreconditionGuardedParameterIds =
                configured?.PreconditionGuardedParameterIds ?? guardedIds,
        };
    }

    private static IReadOnlyDictionary<SymbolId, IReadOnlySet<SymbolId>>
        BuildGuardedParameterIds(ModuleNode module, BoundModule boundModule)
    {
        var namesByDeclaration = new Dictionary<TextSpan, HashSet<string>>();
        foreach (var node in DescendantsAndSelf(module))
        {
            switch (node)
            {
                case FunctionNode function:
                    Register(
                        function.Span,
                        function.Parameters,
                        function.Preconditions);
                    break;
                case MethodNode method:
                    Register(
                        method.Span,
                        method.Parameters,
                        method.Preconditions);
                    break;
                case ConstructorNode constructor:
                    Register(
                        constructor.Span,
                        constructor.Parameters,
                        constructor.Preconditions);
                    break;
                case OperatorOverloadNode operatorOverload:
                    Register(
                        operatorOverload.Span,
                        operatorOverload.Parameters,
                        operatorOverload.Preconditions);
                    break;
                case PropertyAccessorNode accessor
                    when accessor.Kind is PropertyAccessorNode.AccessorKind.Set
                        or PropertyAccessorNode.AccessorKind.Init:
                    Register(
                        accessor.Span,
                        [new ParameterNode(
                            accessor.Span,
                            "value",
                            "OBJECT",
                            new AttributeCollection())],
                        accessor.Preconditions);
                    break;
            }
        }

        var result = new Dictionary<SymbolId, IReadOnlySet<SymbolId>>();
        foreach (var function in boundModule.Functions)
        {
            if (!namesByDeclaration.TryGetValue(function.Symbol.DefinitionSpan, out var guardedNames))
                continue;

            result[function.SymbolId] = function.Symbol.Parameters
                .Where(parameter => guardedNames.Contains(parameter.Name))
                .Select(parameter => parameter.Id)
                .ToHashSet();
        }

        return result;

        void Register(
            TextSpan span,
            IReadOnlyList<ParameterNode> parameters,
            IReadOnlyList<RequiresNode> preconditions)
        {
            if (preconditions.Count == 0)
                return;

            var parameterNames = parameters.Select(parameter => parameter.Name).ToHashSet();
            var guarded = new HashSet<string>();
            foreach (var precondition in preconditions)
                CollectReferencedNames(precondition.Condition, parameterNames, guarded);
            if (guarded.Count > 0)
                namesByDeclaration[span] = guarded;
        }
    }

    private int RunDataflowAnalysis(BoundFunction function)
    {
        var issueCount = 0;

        try
        {
            // Build CFG
            var cfg = ControlFlowGraph.Build(function);

            // Get parameter names for initialization analysis
            // Uninitialized variable analysis
            var uninitAnalysis = new UninitializedVariablesAnalysis(cfg, function.Symbol.Parameters);
            uninitAnalysis.ReportDiagnostics(_diagnostics);
            issueCount += uninitAnalysis.UninitializedUses.Count;

            // Live variable analysis for dead store detection
            var liveAnalysis = new LiveVariablesAnalysis(cfg);
            foreach (var (_, stmt, variable) in liveAnalysis.FindDeadAssignmentsWithSymbols())
            {
                // Skip loop variables and parameters
                if (function.Symbol.Parameters.Any(parameter =>
                        BoundNodeHelpers.SameSymbol(parameter, variable)))
                    continue;

                _diagnostics.ReportWarning(
                    stmt.Span,
                    DiagnosticCode.DeadStore,
                    $"Assignment to '{variable.Name}' is never read (dead store)");
                issueCount++;
            }
        }
        catch (Exception ex)
        {
            _diagnostics.ReportError(
                function.Span,
                DiagnosticCode.AnalysisICE,
                $"Internal dataflow analysis failure for " +
                $"'{function.Symbol.DisplaySignature}': {ex.GetType().Name}: {ex.Message}");
        }

        return issueCount;
    }

    private void ReportAnalysisIncomplete(TextSpan span, string message)
    {
        if (_diagnostics.Any(diagnostic =>
                (diagnostic.Code == DiagnosticCode.AnalysisSkipped
                 || diagnostic.Code == DiagnosticCode.AnalysisUnsupportedNode)
                && diagnostic.Span == span))
        {
            return;
        }

        _diagnostics.ReportInfo(span, DiagnosticCode.AnalysisSkipped, message);
    }
}
