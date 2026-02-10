using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
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
                "Calor0700",
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

        foreach (var function in module.Functions)
        {
            if (!function.HasContracts)
                continue;

            var result = VerifyFunction(verifier, function);
            results.Add(result);
            ReportDiagnostics(function, result);
        }

        // Report summary if any contracts were verified
        if (results.Count > 0)
        {
            var summary = new ModuleVerificationResult(results).GetSummary();
            ReportSummary(module, summary);
        }

        return new ModuleVerificationResult(results);
    }

    private FunctionVerificationResult VerifyFunction(Z3Verifier verifier, FunctionNode function)
    {
        // Extract parameter information
        var parameters = function.Parameters
            .Select(p => (p.Name, p.TypeName))
            .ToList();

        var outputType = function.Output?.TypeName;

        // Verify preconditions
        var preconditionResults = new List<ContractVerificationResult>();
        foreach (var pre in function.Preconditions)
        {
            var result = verifier.VerifyPrecondition(parameters, pre);
            preconditionResults.Add(result);
        }

        // Verify postconditions
        var postconditionResults = new List<ContractVerificationResult>();
        foreach (var post in function.Postconditions)
        {
            var result = verifier.VerifyPostcondition(
                parameters,
                outputType,
                function.Preconditions,
                post);
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
        // Report disproven preconditions
        for (int i = 0; i < result.PreconditionResults.Count && i < function.Preconditions.Count; i++)
        {
            var preResult = result.PreconditionResults[i];
            var preNode = function.Preconditions[i];

            if (preResult.Status == ContractVerificationStatus.Disproven)
            {
                _diagnostics.ReportWarning(
                    preNode.Span,
                    "Calor0701",
                    $"Precondition may be violated in function '{function.Name}'. {preResult.CounterexampleDescription}");
            }
        }

        // Report disproven postconditions
        for (int i = 0; i < result.PostconditionResults.Count && i < function.Postconditions.Count; i++)
        {
            var postResult = result.PostconditionResults[i];
            var postNode = function.Postconditions[i];

            if (postResult.Status == ContractVerificationStatus.Disproven)
            {
                _diagnostics.ReportWarning(
                    postNode.Span,
                    "Calor0702",
                    $"Postcondition may be violated in function '{function.Name}'. {postResult.CounterexampleDescription}");
            }
        }

        // Report proven postconditions at info level if verbose
        if (_options.Verbose)
        {
            for (int i = 0; i < result.PostconditionResults.Count; i++)
            {
                var postResult = result.PostconditionResults[i];
                var postNode = function.Postconditions[i];

                if (postResult.Status == ContractVerificationStatus.Proven)
                {
                    _diagnostics.ReportInfo(
                        postNode.Span,
                        "Calor0703",
                        $"Postcondition statically verified in function '{function.Name}'. Runtime check elided.");
                }
            }
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
            _diagnostics.ReportInfo(module.Span, "Calor0704", message);
        }
        else if (_options.Verbose)
        {
            _diagnostics.ReportInfo(module.Span, "Calor0704", message);
        }
    }

    private static ModuleVerificationResult CreateSkippedResult(ModuleNode module)
    {
        var results = new List<FunctionVerificationResult>();

        foreach (var function in module.Functions)
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
