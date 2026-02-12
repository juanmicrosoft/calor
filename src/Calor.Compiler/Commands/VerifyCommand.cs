using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Init;
using Calor.Compiler.Telemetry;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for verifying contracts with Z3 SMT solver.
/// Outputs verification results in structured formats for AI agents and CI/CD pipelines.
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
            getDefaultValue: () => 5000,
            description: "Z3 solver timeout per contract in milliseconds");

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
        var hasErrors = false;

        foreach (var file in files)
        {
            if (!file.Exists)
            {
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
                var result = await VerifyFileAsync(file, verbose, timeout, noCache, clearCache);
                results.Add(result);

                if (result.HasDisproven)
                {
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing {file.Name}: {ex.Message}");
                Environment.ExitCode = 2;
                hasErrors = true;
            }
        }

        // Format output
        var formatted = FormatOutput(results, format);

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
        bool clearCache)
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
            VerificationCacheOptions = cacheOptions
        };

        var result = Program.Compile(source, file.FullName, options);

        var moduleResult = options.VerificationResults;
        var summary = moduleResult?.GetSummary() ?? new VerificationSummary(0, 0, 0, 0, 0);

        var functions = new List<FunctionVerificationOutput>();
        if (moduleResult != null)
        {
            foreach (var funcResult in moduleResult.Functions)
            {
                var preconditions = funcResult.PreconditionResults
                    .Select((r, i) => new ContractOutput(i, "precondition", r.Status.ToString(), r.CounterexampleDescription))
                    .ToList();

                var postconditions = funcResult.PostconditionResults
                    .Select((r, i) => new ContractOutput(i, "postcondition", r.Status.ToString(), r.CounterexampleDescription))
                    .ToList();

                functions.Add(new FunctionVerificationOutput(
                    funcResult.FunctionId,
                    funcResult.FunctionName,
                    preconditions,
                    postconditions));
            }
        }

        return new FileVerificationResult(
            file.Name,
            file.FullName,
            new SummaryOutput(summary.Proven, summary.Unproven, summary.Disproven, summary.Unsupported, summary.Skipped),
            functions,
            result.Diagnostics.Errors.Select(d => d.Message).ToList(),
            result.Diagnostics.Warnings.Select(d => d.Message).ToList());
    }

    private static string FormatOutput(List<FileVerificationResult> results, string format)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => FormatJson(results),
            _ => FormatText(results)
        };
    }

    private static string FormatJson(List<FileVerificationResult> results)
    {
        var output = new JsonOutput
        {
            Version = "1.0",
            VerifiedAt = DateTime.UtcNow,
            Files = results,
            Summary = new SummaryOutput(
                results.Sum(r => r.Summary.Proven),
                results.Sum(r => r.Summary.Unproven),
                results.Sum(r => r.Summary.Disproven),
                results.Sum(r => r.Summary.Unsupported),
                results.Sum(r => r.Summary.Skipped))
        };

        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

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

                    foreach (var pre in func.Preconditions)
                    {
                        var status = pre.Status;
                        var marker = status == "Proven" ? "[OK]" : status == "Disproven" ? "[!!]" : "[??]";
                        sb.AppendLine($"    {marker} Precondition {pre.Index}: {status}");
                        if (!string.IsNullOrEmpty(pre.CounterExample))
                        {
                            sb.AppendLine($"        Counterexample: {pre.CounterExample}");
                        }
                    }

                    foreach (var post in func.Postconditions)
                    {
                        var status = post.Status;
                        var marker = status == "Proven" ? "[OK]" : status == "Disproven" ? "[!!]" : "[??]";
                        sb.AppendLine($"    {marker} Postcondition {post.Index}: {status}");
                        if (!string.IsNullOrEmpty(post.CounterExample))
                        {
                            sb.AppendLine($"        Counterexample: {post.CounterExample}");
                        }
                    }
                }
            }

            if (file.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Errors:");
                foreach (var error in file.Errors)
                {
                    sb.AppendLine($"    - {error}");
                }
            }

            if (file.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Warnings:");
                foreach (var warning in file.Warnings)
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

    // JSON output types
    private sealed class JsonOutput
    {
        public required string Version { get; init; }
        public DateTime VerifiedAt { get; init; }
        public required List<FileVerificationResult> Files { get; init; }
        public required SummaryOutput Summary { get; init; }
    }

    private sealed record FileVerificationResult(
        string FileName,
        string FilePath,
        SummaryOutput Summary,
        List<FunctionVerificationOutput> Functions,
        List<string> Errors,
        List<string> Warnings)
    {
        public bool HasDisproven => Summary.Disproven > 0;
    }

    private sealed record SummaryOutput(
        int Proven,
        int Unproven,
        int Disproven,
        int Unsupported,
        int Skipped)
    {
        public int Total => Proven + Unproven + Disproven + Unsupported + Skipped;
    }

    private sealed record FunctionVerificationOutput(
        string FunctionId,
        string FunctionName,
        List<ContractOutput> Preconditions,
        List<ContractOutput> Postconditions);

    private sealed record ContractOutput(
        int Index,
        string Type,
        string Status,
        string? CounterExample);
}
