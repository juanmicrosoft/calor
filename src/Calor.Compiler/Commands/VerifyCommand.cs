using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;
using Calor.Compiler.Telemetry;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for verifying contracts with Z3 SMT solver.
/// JSON output is the shared diagnostic envelope (schema v1.1,
/// docs/cli/envelope-schema.md): compiler diagnostics aggregated at the top
/// level with declarationId + verification payloads, and the verify-specific
/// per-file/per-contract report under <c>data</c> using the closed five-status
/// vocabulary (proven|refuted|unknown|timeout|unsupported).
/// </summary>
public static class VerifyCommand
{
    public static Command Create()
    {
        var inputArgument = new Argument<FileInfo[]>(
            name: "files",
            description: "The Calor source file(s) to verify")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Output format: text or json");

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Output file (stdout if not specified)");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output with detailed verification info");

        var timeoutOption = new Option<int>(
            aliases: ["--timeout", "-t"],
            getDefaultValue: () => (int)VerificationOptions.DefaultTimeoutMs,
            description: "Z3 solver timeout per contract in milliseconds (default: 5000)");
        timeoutOption.AddValidator(result =>
        {
            var value = result.GetValueOrDefault<int>();
            if (value <= 0)
            {
                result.ErrorMessage = "Timeout must be a positive integer";
            }
        });

        var noCacheOption = new Option<bool>(
            aliases: ["--no-cache"],
            description: "Disable verification result caching");

        var clearCacheOption = new Option<bool>(
            aliases: ["--clear-cache"],
            description: "Clear verification cache before verifying");

        var command = new Command("verify", "Verify contracts in Calor files using Z3 SMT solver")
        {
            inputArgument,
            formatOption,
            outputOption,
            verboseOption,
            timeoutOption,
            noCacheOption,
            clearCacheOption
        };

        command.SetHandler(ExecuteAsync, inputArgument, formatOption, outputOption, verboseOption, timeoutOption, noCacheOption, clearCacheOption);

        return command;
    }

    private static async Task ExecuteAsync(
        FileInfo[] files,
        string format,
        FileInfo? output,
        bool verbose,
        int timeout,
        bool noCache,
        bool clearCache)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("verify");
        if (telemetry != null && files.Length > 0)
        {
            var discovered = CalorConfigManager.Discover(files[0].FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();

        var results = new List<FileVerificationResult>();
        // Compiler diagnostics aggregated across all files for the envelope's
        // top-level diagnostics[]; the resolver maps each diagnostic to its
        // enclosing declaration ID from the file's parsed AST.
        var aggregatedDiagnostics = new DiagnosticBag();
        var declarationIds = new DeclarationIdResolver();
        var hasErrors = false;

        foreach (var file in files)
        {
            if (!file.Exists)
            {
                aggregatedDiagnostics.Add(new Diagnostic(
                    DiagnosticCode.CliInputNotFound,
                    $"File not found: {file.FullName}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error,
                    file.FullName));
                Console.Error.WriteLine($"Error: File not found: {file.FullName}");
                Environment.ExitCode = 1;
                hasErrors = true;
                continue;
            }

            if (!file.Extension.Equals(".calr", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Warning: Skipping non-Calor file: {file.Name}");
                continue;
            }

            try
            {
                var result = await VerifyFileAsync(file, verbose, timeout, noCache, clearCache, declarationIds);
                results.Add(result);
                aggregatedDiagnostics.AddRange(result.Diagnostics);

                if (result.HasDisproven)
                {
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                aggregatedDiagnostics.Add(new Diagnostic(
                    DiagnosticCode.CliInternalError,
                    $"Error processing {file.Name}: {ex.Message}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error,
                    file.FullName));
                Console.Error.WriteLine($"Error processing {file.Name}: {ex.Message}");
                Environment.ExitCode = 2;
                hasErrors = true;
            }
        }

        // Format output
        var formatted = FormatOutput(results, format, aggregatedDiagnostics, declarationIds);

        // Write output
        if (output != null)
        {
            await File.WriteAllTextAsync(output.FullName, formatted);
            Console.Error.WriteLine($"Verification results written to: {output.FullName}");
        }
        else
        {
            Console.WriteLine(formatted);
        }

        // Set exit code based on results
        if (hasErrors)
        {
            Environment.ExitCode = 1;
        }

        sw.Stop();
        var totalContracts = results.Sum(r => r.Summary.Total);
        var totalProven = results.Sum(r => r.Summary.Proven);
        telemetry?.TrackCommand("verify", Environment.ExitCode, new Dictionary<string, string>
        {
            ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
            ["fileCount"] = files.Length.ToString(),
            ["totalContracts"] = totalContracts.ToString(),
            ["provenContracts"] = totalProven.ToString()
        });

        if (Environment.ExitCode != 0)
        {
            IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "verify", "Contract verification found issues");
        }
    }

    private static async Task<FileVerificationResult> VerifyFileAsync(
        FileInfo file,
        bool verbose,
        int timeout,
        bool noCache,
        bool clearCache,
        DeclarationIdResolver declarationIds)
    {
        var source = await File.ReadAllTextAsync(file.FullName);
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(file.FullName);

        var cacheOptions = new VerificationCacheOptions
        {
            Enabled = !noCache,
            ClearBeforeVerification = clearCache,
            ProjectDirectory = Path.GetDirectoryName(file.FullName)
        };

        var options = new CompilationOptions
        {
            Verbose = verbose,
            VerifyContracts = true,
            ProjectDirectory = Path.GetDirectoryName(file.FullName),
            VerificationCacheOptions = cacheOptions,
            VerificationTimeoutMs = (uint)timeout
        };

        var result = Program.Compile(source, file.FullName, options);

        // Feed the declaration-ID resolver whenever parsing got far enough to
        // produce an AST — even for error-bearing files, so their diagnostics
        // still resolve to enclosing declarations.
        if (result.Ast != null)
        {
            declarationIds.AddFile(file.FullName, source, result.Ast);
        }

        var moduleResult = options.VerificationResults;
        var summary = moduleResult?.GetSummary() ?? new VerificationSummary(0, 0, 0, 0, 0);

        var functions = new List<FunctionVerificationOutput>();
        if (moduleResult != null)
        {
            foreach (var funcResult in moduleResult.Functions)
            {
                var contracts = new List<ContractOutput>();
                contracts.AddRange(funcResult.PreconditionResults
                    .Select((r, i) => BuildContractOutput("precondition", i, r)));
                contracts.AddRange(funcResult.PostconditionResults
                    .Select((r, i) => BuildContractOutput("postcondition", i, r)));

                functions.Add(new FunctionVerificationOutput(
                    funcResult.FunctionId,
                    funcResult.FunctionName,
                    contracts));
            }
        }

        return new FileVerificationResult(
            file.Name,
            file.FullName,
            summary,
            functions,
            result.Diagnostics);
    }

    private static ContractOutput BuildContractOutput(string type, int index, Verification.Z3.ContractVerificationResult result)
    {
        var outcome = result.EffectiveOutcome;
        return new ContractOutput(
            type,
            index,
            outcome,
            LegacyStatus: result.Status.ToString(),
            CounterexampleDescription: result.CounterexampleDescription);
    }

    private static string FormatOutput(
        List<FileVerificationResult> results,
        string format,
        DiagnosticBag aggregatedDiagnostics,
        DeclarationIdResolver declarationIds)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => FormatJson(results, aggregatedDiagnostics, declarationIds),
            _ => FormatText(results)
        };
    }

    // ------------------------------------------------------------------
    // JSON: the one envelope (docs/cli/envelope-schema.md)
    // ------------------------------------------------------------------

    private static string FormatJson(
        List<FileVerificationResult> results,
        DiagnosticBag aggregatedDiagnostics,
        DeclarationIdResolver declarationIds)
    {
        var fileOutputs = results.Select(BuildFileJson).ToList();

        var envelope = new EnvelopeOutput
        {
            Version = JsonDiagnosticFormatter.SchemaVersion,
            Command = "verify",
            Diagnostics = DiagnosticEnvelope.Build(aggregatedDiagnostics, declarationIds),
            Summary = DiagnosticEnvelope.Summarize(aggregatedDiagnostics),
            Data = new VerifyData
            {
                VerifiedAt = DateTime.UtcNow,
                Files = fileOutputs,
                Summary = new FiveStatusSummary
                {
                    Proven = fileOutputs.Sum(f => f.Summary.Proven),
                    Refuted = fileOutputs.Sum(f => f.Summary.Refuted),
                    Unknown = fileOutputs.Sum(f => f.Summary.Unknown),
                    Timeout = fileOutputs.Sum(f => f.Summary.Timeout),
                    Unsupported = fileOutputs.Sum(f => f.Summary.Unsupported)
                }
            }
        };

        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static FileJson BuildFileJson(FileVerificationResult file)
    {
        var outcomes = file.Functions
            .SelectMany(f => f.Contracts)
            .Select(c => c.Outcome)
            .ToList();

        return new FileJson
        {
            FileName = file.FileName,
            FilePath = file.FilePath,
            Summary = new FileSummaryJson
            {
                // Legacy enum counts (one release of back-compat).
                Proven = file.Summary.Proven,
                Unproven = file.Summary.Unproven,
                Disproven = file.Summary.Disproven,
                Unsupported = file.Summary.Unsupported,
                Skipped = file.Summary.Skipped,
                // Five-status counts from the choke-point outcome. proven and
                // unsupported coincide with the legacy columns; refuted/unknown/
                // timeout replace disproven and the unproven/skipped conflation.
                Refuted = outcomes.Count(o => o.Status == ProofStatus.Refuted),
                Unknown = outcomes.Count(o => o.Status == ProofStatus.Unknown),
                Timeout = outcomes.Count(o => o.Status == ProofStatus.Timeout)
            },
            Functions = file.Functions.Select(f => new FunctionJson
            {
                FunctionId = f.FunctionId,
                FunctionName = f.FunctionName,
                Contracts = f.Contracts.Select(c => new ContractJson
                {
                    Type = c.Type,
                    Index = c.Index,
                    Status = c.Outcome.StatusName,
                    LegacyStatus = c.LegacyStatus,
                    Reason = c.Outcome.Reason,
                    Counterexample = c.Outcome.Counterexample == null
                        ? null
                        : new EnvelopeCounterexample
                        {
                            Rendered = c.Outcome.Counterexample.Render(),
                            Bindings = c.Outcome.Counterexample.Bindings
                                .Select(b => new EnvelopeBinding { Name = b.Name, Value = b.Value })
                                .ToList()
                        }
                }).ToList()
            }).ToList()
        };
    }

    // ------------------------------------------------------------------
    // Text output (unchanged)
    // ------------------------------------------------------------------

    private static string FormatText(List<FileVerificationResult> results)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Calor Contract Verification Report");
        sb.AppendLine("===================================");
        sb.AppendLine();

        foreach (var file in results)
        {
            sb.AppendLine($"File: {file.FileName}");
            sb.AppendLine($"  Proven:      {file.Summary.Proven}");
            sb.AppendLine($"  Unproven:    {file.Summary.Unproven}");
            sb.AppendLine($"  Disproven:   {file.Summary.Disproven}");
            sb.AppendLine($"  Unsupported: {file.Summary.Unsupported}");
            sb.AppendLine($"  Skipped:     {file.Summary.Skipped}");

            if (file.Functions.Count > 0)
            {
                sb.AppendLine();
                foreach (var func in file.Functions)
                {
                    sb.AppendLine($"  Function: {func.FunctionName} ({func.FunctionId})");

                    foreach (var contract in func.Contracts)
                    {
                        var status = contract.LegacyStatus;
                        var marker = status == "Proven" ? "[OK]" : status == "Disproven" ? "[!!]" : "[??]";
                        var label = contract.Type == "precondition" ? "Precondition" : "Postcondition";
                        sb.AppendLine($"    {marker} {label} {contract.Index}: {status}");
                        if (!string.IsNullOrEmpty(contract.CounterexampleDescription))
                        {
                            sb.AppendLine($"        Counterexample: {contract.CounterexampleDescription}");
                        }
                    }
                }
            }

            var errors = file.Diagnostics.Errors.Select(d => d.Message).ToList();
            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Errors:");
                foreach (var error in errors)
                {
                    sb.AppendLine($"    - {error}");
                }
            }

            var warnings = file.Diagnostics.Warnings.Select(d => d.Message).ToList();
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Warnings:");
                foreach (var warning in warnings)
                {
                    sb.AppendLine($"    - {warning}");
                }
            }

            sb.AppendLine();
        }

        // Overall summary
        var totalProven = results.Sum(r => r.Summary.Proven);
        var totalUnproven = results.Sum(r => r.Summary.Unproven);
        var totalDisproven = results.Sum(r => r.Summary.Disproven);
        var totalUnsupported = results.Sum(r => r.Summary.Unsupported);
        var totalSkipped = results.Sum(r => r.Summary.Skipped);
        var total = totalProven + totalUnproven + totalDisproven + totalUnsupported + totalSkipped;

        sb.AppendLine("===================================");
        sb.AppendLine("Overall Summary");
        sb.AppendLine("===================================");
        sb.AppendLine($"Total Contracts: {total}");
        sb.AppendLine($"  Proven:      {totalProven}");
        sb.AppendLine($"  Unproven:    {totalUnproven}");
        sb.AppendLine($"  Disproven:   {totalDisproven}");
        sb.AppendLine($"  Unsupported: {totalUnsupported}");
        sb.AppendLine($"  Skipped:     {totalSkipped}");

        if (total > 0)
        {
            var provenRate = (double)totalProven / total * 100;
            sb.AppendLine($"  Proven Rate: {provenRate:F1}%");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Internal per-file model (feeds both text and JSON shapes)
    // ------------------------------------------------------------------

    private sealed record FileVerificationResult(
        string FileName,
        string FilePath,
        VerificationSummary Summary,
        List<FunctionVerificationOutput> Functions,
        DiagnosticBag Diagnostics)
    {
        public bool HasDisproven => Summary.Disproven > 0;
    }

    private sealed record FunctionVerificationOutput(
        string FunctionId,
        string FunctionName,
        List<ContractOutput> Contracts);

    private sealed record ContractOutput(
        string Type,
        int Index,
        ProofOutcome Outcome,
        string LegacyStatus,
        string? CounterexampleDescription);

    // ------------------------------------------------------------------
    // JSON DTOs (envelope schema v1.1)
    // ------------------------------------------------------------------

    private sealed class EnvelopeOutput
    {
        public required string Version { get; init; }
        public required string Command { get; init; }
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }
        public required EnvelopeSummary Summary { get; init; }
        public required VerifyData Data { get; init; }
    }

    private sealed class VerifyData
    {
        public DateTime VerifiedAt { get; init; }
        public required List<FileJson> Files { get; init; }
        public required FiveStatusSummary Summary { get; init; }
    }

    private sealed class FileJson
    {
        public required string FileName { get; init; }
        public required string FilePath { get; init; }
        public required FileSummaryJson Summary { get; init; }
        public required List<FunctionJson> Functions { get; init; }
    }

    private sealed class FileSummaryJson
    {
        // Legacy enum counts (Proven/Unproven/Disproven/Unsupported/Skipped) —
        // kept for one release of back-compat.
        public int Proven { get; init; }
        public int Unproven { get; init; }
        public int Disproven { get; init; }
        public int Unsupported { get; init; }
        public int Skipped { get; init; }

        // Five-status counts (envelope vocabulary); proven and unsupported
        // are shared with the legacy columns above.
        public int Refuted { get; init; }
        public int Unknown { get; init; }
        public int Timeout { get; init; }
    }

    private sealed class FunctionJson
    {
        public required string FunctionId { get; init; }
        public required string FunctionName { get; init; }
        public required List<ContractJson> Contracts { get; init; }
    }

    private sealed class ContractJson
    {
        public required string Type { get; init; }
        public int Index { get; init; }

        /// <summary>Five-status wire name: proven|refuted|unknown|timeout|unsupported.</summary>
        public required string Status { get; init; }

        /// <summary>Legacy enum name (Proven/Unproven/Disproven/Unsupported/Skipped); kept for one release.</summary>
        public required string LegacyStatus { get; init; }

        public string? Reason { get; init; }
        public EnvelopeCounterexample? Counterexample { get; init; }
    }

    private sealed class FiveStatusSummary
    {
        public int Proven { get; init; }
        public int Refuted { get; init; }
        public int Unknown { get; init; }
        public int Timeout { get; init; }
        public int Unsupported { get; init; }
    }
}
