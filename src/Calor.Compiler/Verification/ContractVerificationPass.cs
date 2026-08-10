using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3.Cache;
using System.Runtime.CompilerServices;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Compiler pass that performs static contract verification using Z3.
/// </summary>
public sealed class ContractVerificationPass
{
    private readonly DiagnosticBag _diagnostics;
    private readonly VerificationOptions _options;

    public ContractVerificationPass(DiagnosticBag diagnostics)
        : this(diagnostics, VerificationOptions.Default)
    {
    }

    public ContractVerificationPass(DiagnosticBag diagnostics, VerificationOptions options)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Verifies all contracts in the given module.
    /// </summary>
    public ModuleVerificationResult Verify(ModuleNode module)
    {
        // Check if Z3 is available
        if (!Z3ContextFactory.IsAvailable)
        {
            _diagnostics.ReportInfo(
                module.Span,
                DiagnosticCode.Z3Unavailable,
                "Static contract verification skipped: Z3 SMT solver is not available. " +
                "Install the Z3 native library for static verification support.");

            return CreateSkippedResult(module);
        }

        // Call the core verification method which uses Z3 types
        // This is in a separate method to prevent JIT from loading Z3 types when Z3 is unavailable
        return VerifyCore(module);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ModuleVerificationResult VerifyCore(ModuleNode module)
    {
        var results = new List<FunctionVerificationResult>();

        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx, _options.TimeoutMs);
        using var cache = new VerificationCache(_options.CacheOptions);

        foreach (var function in EnumerateContractBearers(module))
        {
            _options.CancellationToken.ThrowIfCancellationRequested();

            if (!function.HasContracts)
                continue;

            var result = VerifyFunction(verifier, cache, function);
            results.Add(result);
            ReportDiagnostics(function, result);
        }

        // Report summary if any contracts were verified
        if (results.Count > 0)
        {
            var summary = new ModuleVerificationResult(results).GetSummary();
            ReportSummary(module, summary);

            // Report cache statistics in verbose mode
            if (_options.Verbose && cache.IsEnabled)
            {
                var stats = cache.GetStatistics();
                if (stats.TotalLookups > 0)
                {
                    _diagnostics.ReportInfo(
                        module.Span,
                        DiagnosticCode.VerificationCacheStats,
                        $"Verification cache: {stats.Hits} hits, {stats.Misses} misses ({stats.HitRate:F1}% hit rate)");
                }
            }
        }

        return new ModuleVerificationResult(results);
    }

    private FunctionVerificationResult VerifyFunction(
        Z3Verifier verifier,
        VerificationCache cache,
        FunctionNode function)
    {
        // Extract parameter information
        var parameters = function.Parameters
            .Select(p => (p.Name, p.TypeName))
            .ToList();

        var outputType = function.Output?.TypeName;

        // Verify preconditions with cache
        var preconditionResults = new List<ContractVerificationResult>();
        foreach (var pre in function.Preconditions)
        {
            // Try cache first
            if (cache.TryGetPreconditionResult(parameters, pre, out var cached))
            {
                preconditionResults.Add(cached!);
                continue;
            }

            // Cache miss - run Z3 verification
            var result = verifier.VerifyPrecondition(parameters, pre);
            cache.CachePreconditionResult(parameters, pre, result);
            preconditionResults.Add(result);
        }

        // Verify postconditions with cache
        var postconditionResults = new List<ContractVerificationResult>();
        foreach (var post in function.Postconditions)
        {
            // Try cache first (the key covers the body when the postcondition references
            // `result` — a body edit must never reuse a stale result-bound proof)
            if (cache.TryGetPostconditionResult(parameters, outputType, function.Preconditions, post, function.Body, out var cached))
            {
                postconditionResults.Add(cached!);
                continue;
            }

            // Cache miss - run Z3 verification
            var result = verifier.VerifyPostcondition(
                parameters,
                outputType,
                function.Preconditions,
                post,
                function.Body);
            cache.CachePostconditionResult(parameters, outputType, function.Preconditions, post, function.Body, result);
            postconditionResults.Add(result);
        }

        return new FunctionVerificationResult(
            function.Id,
            function.Name,
            preconditionResults,
            postconditionResults);
    }

    private void ReportDiagnostics(FunctionNode function, FunctionVerificationResult result)
    {
        for (int i = 0; i < result.PreconditionResults.Count && i < function.Preconditions.Count; i++)
        {
            ReportContractDiagnostic(
                function, result.PreconditionResults[i], function.Preconditions[i].Span, isPrecondition: true);
        }

        for (int i = 0; i < result.PostconditionResults.Count && i < function.Postconditions.Count; i++)
        {
            ReportContractDiagnostic(
                function, result.PostconditionResults[i], function.Postconditions[i].Span, isPrecondition: false);
        }
    }

    /// <summary>
    /// Reports one diagnostic per contract from the choke-point outcome. Every
    /// non-proven status is surfaced — refuted as a warning, timeout / unknown /
    /// unsupported as info — so no verification cliff is silent (loop plan D1.2).
    /// Each diagnostic carries the outcome as its envelope verification payload.
    /// </summary>
    private void ReportContractDiagnostic(
        FunctionNode function,
        ContractVerificationResult contractResult,
        Parsing.TextSpan span,
        bool isPrecondition)
    {
        // Z3-unavailable runs are reported once per module via Calor0710
        if (contractResult.Status == ContractVerificationStatus.Skipped)
            return;

        var outcome = contractResult.EffectiveOutcome;
        var kind = isPrecondition ? "Precondition" : "Postcondition";

        switch (outcome.Status)
        {
            case ProofStatus.Refuted:
                _diagnostics.ReportVerification(
                    span,
                    isPrecondition
                        ? DiagnosticCode.PreconditionMayBeViolated
                        : DiagnosticCode.PostconditionMayBeViolated,
                    $"{kind} may be violated in function '{function.Name}'. {contractResult.CounterexampleDescription}",
                    DiagnosticSeverity.Warning,
                    outcome);
                break;

            case ProofStatus.Proven:
                if (outcome.IsVacuous)
                {
                    // Vacuous proof: the precondition set is unsatisfiable, so the
                    // postcondition holds only because no valid call exists. Loud by
                    // design (guarantees plan D-G1.3) — and never treated as elidable.
                    _diagnostics.ReportVerification(
                        span,
                        DiagnosticCode.VacuousPrecondition,
                        $"{kind} in function '{function.Name}' holds vacuously: the precondition set is unsatisfiable, so no valid call exists. Runtime check kept.",
                        DiagnosticSeverity.Warning,
                        outcome);
                }
                else if (!isPrecondition && _options.Verbose)
                {
                    // v0.13: elision is opt-in, so the message must state what actually
                    // happens to the check — claiming "elided" when the guard stays
                    // would misreport the emitted code.
                    var checkDisposition = _options.ElideProvenGuards
                        ? "Runtime check elided."
                        : "Runtime check kept (elision opt-in not set).";
                    _diagnostics.ReportVerification(
                        span,
                        DiagnosticCode.PostconditionProven,
                        $"Postcondition statically verified in function '{function.Name}'. {checkDisposition}",
                        DiagnosticSeverity.Info,
                        outcome);
                }
                break;

            case ProofStatus.Assumed:
                _diagnostics.ReportVerification(
                    span,
                    DiagnosticCode.ContractVerificationAssumed,
                    $"{kind} in function '{function.Name}' holds conditionally on assumptions: " +
                        string.Join("; ", outcome.Assumptions) +
                        ". Runtime check kept.",
                    DiagnosticSeverity.Info,
                    outcome);
                break;

            // NOTE (G2 review M1): this arm is currently unreachable on the compile
            // path — the no-solver flow produces legacy Skipped results, and the
            // Skipped early-return above fires first (module-level Calor0710 covers
            // it once per module). The arm is kept for when a per-contract
            // Unavailable producer appears (evidence-level SolverUnavailable).
            case ProofStatus.Unavailable:
                _diagnostics.ReportVerification(
                    span,
                    DiagnosticCode.ContractVerificationUnavailable,
                    $"{kind} verification unavailable in function '{function.Name}'" +
                        (outcome.Reason is { Length: > 0 } unavailWhy ? $" ({unavailWhy})" : "") +
                        ". Runtime check kept.",
                    DiagnosticSeverity.Info,
                    outcome);
                break;

            case ProofStatus.Timeout:
                _diagnostics.ReportVerification(
                    span,
                    DiagnosticCode.ContractVerificationTimeout,
                    $"{kind} verification timed out in function '{function.Name}'. Runtime check kept.",
                    DiagnosticSeverity.Info,
                    outcome);
                break;

            case ProofStatus.Unsupported:
                _diagnostics.ReportVerification(
                    span,
                    DiagnosticCode.ContractVerificationUnsupported,
                    $"{kind} could not be translated for verification in function '{function.Name}'" +
                        (outcome.Reason is { Length: > 0 } reason
                            // Several producers (Z3Verifier's solver-exception, name-collision and
                            // vacuity reasons) already end with this sentence; appending it blindly
                            // printed it twice. And a reason that does not end in punctuation ran
                            // straight into it — "...the program needs Runtime check kept."
                            ? $": {reason}{(reason.EndsWith('.') || reason.EndsWith('!') ? "" : ".")}"
                            : ".") +
                        (outcome.Reason?.EndsWith("Runtime check kept.", StringComparison.Ordinal) == true
                            ? ""
                            : " Runtime check kept."),
                    DiagnosticSeverity.Info,
                    outcome);
                break;

            default:
                _diagnostics.ReportVerification(
                    span,
                    DiagnosticCode.ContractVerificationInconclusive,
                    $"{kind} verification was inconclusive in function '{function.Name}'" +
                        (outcome.Reason is { Length: > 0 } why ? $" ({why})" : "") +
                        ". Runtime check kept.",
                    DiagnosticSeverity.Info,
                    outcome);
                break;
        }
    }

    private void ReportSummary(ModuleNode module, VerificationSummary summary)
    {
        if (summary.Total == 0)
            return;

        var message = $"Contract verification complete: " +
            $"{summary.Proven} proven, " +
            $"{summary.Unproven} unproven, " +
            $"{summary.Disproven} potentially violated, " +
            $"{summary.Unsupported} unsupported";

        if (summary.Disproven > 0)
        {
            _diagnostics.ReportInfo(module.Span, DiagnosticCode.VerificationSummary, message);
        }
        else if (_options.Verbose)
        {
            _diagnostics.ReportInfo(module.Span, DiagnosticCode.VerificationSummary, message);
        }
    }

    /// <summary>
    /// Yields all contract-bearing subjects in the module: top-level functions
    /// plus class methods (and methods on nested classes). Each MethodNode is
    /// adapted as a FunctionNode so the verifier sees a uniform shape — methods
    /// are functions with an implicit `this` receiver, but contracts in §Q/§S
    /// reference parameters and `result`, neither of which depends on `this`.
    /// </summary>
    private static IEnumerable<FunctionNode> EnumerateContractBearers(ModuleNode module)
    {
        foreach (var f in module.Functions)
            yield return f;

        foreach (var cls in module.Classes)
        {
            foreach (var fn in EnumerateClassMethodsAsFunctions(cls))
                yield return fn;
        }
    }

    private static IEnumerable<FunctionNode> EnumerateClassMethodsAsFunctions(ClassDefinitionNode cls)
    {
        foreach (var m in cls.Methods)
        {
            if (!m.HasContracts)
                continue;
            yield return AdaptMethodAsFunction(m, cls);
        }

        foreach (var nested in cls.NestedClasses)
        {
            foreach (var fn in EnumerateClassMethodsAsFunctions(nested))
                yield return fn;
        }
    }

    /// <summary>
    /// Wraps a MethodNode in a FunctionNode shape for the contract verifier.
    /// Preserves Id, Name, Parameters, Output, Preconditions, Postconditions, Body
    /// — the verifier touches only these. The method's receiver (`this`) is
    /// modelled implicitly: contracts cannot currently reference `self.X` on
    /// receiver state, only parameters and `result`. That matches what the
    /// existing free-function verifier already supports.
    /// </summary>
    private static FunctionNode AdaptMethodAsFunction(MethodNode m, ClassDefinitionNode owner)
    {
        // Use a synthetic name so cache keys distinguish methods on different classes.
        var qualifiedName = owner.Name + "." + m.Name;
        return new FunctionNode(
            m.Span,
            m.Id,
            qualifiedName,
            m.Visibility,
            m.TypeParameters,
            m.Parameters,
            m.Output,
            m.Effects,
            m.Preconditions,
            m.Postconditions,
            m.Body,
            m.Attributes);
    }

    private static ModuleVerificationResult CreateSkippedResult(ModuleNode module)
    {
        var results = new List<FunctionVerificationResult>();

        foreach (var function in EnumerateContractBearers(module))
        {
            if (!function.HasContracts)
                continue;

            var skippedResult = new ContractVerificationResult(ContractVerificationStatus.Skipped);

            var preResults = function.Preconditions
                .Select(_ => skippedResult)
                .ToList();

            var postResults = function.Postconditions
                .Select(_ => skippedResult)
                .ToList();

            results.Add(new FunctionVerificationResult(
                function.Id,
                function.Name,
                preResults,
                postResults));
        }

        return new ModuleVerificationResult(results);
    }
}
