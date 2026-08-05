using System.CommandLine;
using System.CommandLine.Invocation;
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

        var weakeningOption = new Option<string?>(
            aliases: ["--weakening-check"],
            description: "M-G4 mode (gates doc Annex A-1.3): with exactly two input files "
                + "(frozen, final), mechanically decide whether the named declaration's "
                + "§Q/§S conjunction was weakened — prints a JSON verdict, exit 0");

        var command = new Command("verify", "Verify contracts in Calor files using Z3 SMT solver")
        {
            inputArgument,
            formatOption,
            outputOption,
            verboseOption,
            timeoutOption,
            noCacheOption,
            clearCacheOption,
            weakeningOption
        };

        // Exit code returned through ctx.ExitCode: a code parked only on
        // Environment.ExitCode is overwritten by Main's InvokeAsync return.
        command.SetHandler(async (InvocationContext ctx) =>
        {
            var weakeningDeclaration = ctx.ParseResult.GetValueForOption(weakeningOption);
            if (weakeningDeclaration != null)
            {
                ctx.ExitCode = ExecuteWeakeningCheck(
                    ctx.ParseResult.GetValueForArgument(inputArgument),
                    weakeningDeclaration,
                    (uint)ctx.ParseResult.GetValueForOption(timeoutOption));
                return;
            }

            ctx.ExitCode = await ExecuteAsync(
                ctx.ParseResult.GetValueForArgument(inputArgument),
                ctx.ParseResult.GetValueForOption(formatOption) ?? "text",
                ctx.ParseResult.GetValueForOption(outputOption),
                ctx.ParseResult.GetValueForOption(verboseOption),
                ctx.ParseResult.GetValueForOption(timeoutOption),
                ctx.ParseResult.GetValueForOption(noCacheOption),
                ctx.ParseResult.GetValueForOption(clearCacheOption));
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
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
        // Exit-code semantics (review of #754): 1 when any file is missing, any
        // compile error occurs, or any contract is REFUTED (disproven); 0 when
        // every contract is proven or merely unknown/timeout/unsupported —
        // inconclusive is not failure (runtime checks are kept). Applies to
        // both text and JSON modes.
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

                if (result.HasRefuted || result.Diagnostics.HasErrors)
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

        // Exit code based on results (returned through ctx.ExitCode).
        var exitCode = hasErrors ? 1 : 0;

        sw.Stop();
        var totalContracts = results.Sum(r => r.Summary.Total);
        var totalProven = results.Sum(r => r.Summary.Proven);
        telemetry?.TrackCommand("verify", exitCode, new Dictionary<string, string>
        {
            ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
            ["fileCount"] = files.Length.ToString(),
            ["totalContracts"] = totalContracts.ToString(),
            ["provenContracts"] = totalProven.ToString()
        });

        if (exitCode != 0)
        {
            IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "verify", "Contract verification found issues");
        }

        return exitCode;
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
                    Assumed = fileOutputs.Sum(f => f.Summary.Assumed),
                    Unknown = fileOutputs.Sum(f => f.Summary.Unknown),
                    Timeout = fileOutputs.Sum(f => f.Summary.Timeout),
                    Unsupported = fileOutputs.Sum(f => f.Summary.Unsupported),
                    Unavailable = fileOutputs.Sum(f => f.Summary.Unavailable)
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
                // Choke-point status counts (schema 2.0 seven-status vocabulary).
                // proven and unsupported coincide with the legacy columns;
                // refuted/assumed/unknown/timeout/unavailable replace disproven and
                // the legacy unproven/skipped conflation — the columns sum to the
                // total contract count (G2 review M3).
                Refuted = outcomes.Count(o => o.Status == ProofStatus.Refuted),
                Assumed = outcomes.Count(o => o.Status == ProofStatus.Assumed),
                Unknown = outcomes.Count(o => o.Status == ProofStatus.Unknown),
                Timeout = outcomes.Count(o => o.Status == ProofStatus.Timeout),
                Unavailable = outcomes.Count(o => o.Status == ProofStatus.Unavailable)
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
                    Assumptions = c.Outcome.Assumptions.Count > 0 ? c.Outcome.Assumptions.ToList() : null,
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
            // `Assumed` collapses into the legacy `Unproven` bucket (ProofOutcome.ToContractStatus),
            // so without this line a demoted proof reads as an outright failure to prove — visually
            // identical to a timeout — when in fact the solver discharged it and the result is
            // conditional on a NAMED assumption. Broken out here rather than added to
            // VerificationSummary, whose positional record every consumer would have to change.
            var assumed = file.Functions.Sum(f =>
                f.Contracts.Count(c => c.Outcome.Status == ProofStatus.Assumed));
            sb.AppendLine($"  Unproven:    {file.Summary.Unproven}");
            if (assumed > 0)
            {
                // Its own line rather than a suffix on the row above, so anything parsing
                // `Unproven:\s+(\d+)$` keeps working.
                sb.AppendLine($"    of which assumed (discharged under a named assumption): {assumed}");
            }
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
                            // Only a refutation's description is a counterexample; other
                            // statuses carry a reason (unsupported diagnosis, vacuity note).
                            var descLabel = status == "Disproven" ? "Counterexample" : "Note";
                            sb.AppendLine($"        {descLabel}: {contract.CounterexampleDescription}");
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
        /// <summary>
        /// True when any contract was refuted (disproven) — legacy summary
        /// column or five-status choke-point outcome. Drives exit code 1;
        /// unknown/timeout/unsupported do NOT (inconclusive is not failure).
        /// </summary>
        public bool HasRefuted =>
            Summary.Disproven > 0 ||
            Functions.Any(f => f.Contracts.Any(c => c.Outcome.Status == ProofStatus.Refuted));
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

        // Choke-point status counts (schema 2.0 seven-status vocabulary); proven and unsupported
        // are shared with the legacy columns above.
        public int Refuted { get; init; }
        public int Assumed { get; init; }
        public int Unknown { get; init; }
        public int Timeout { get; init; }
        public int Unavailable { get; init; }
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

        /// <summary>Choke-point wire name (schema 2.0 seven-status vocabulary).</summary>
        public required string Status { get; init; }

        /// <summary>Legacy enum name (Proven/Unproven/Disproven/Unsupported/Skipped); kept for one release.</summary>
        public required string LegacyStatus { get; init; }

        public string? Reason { get; init; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Assumptions { get; init; }

        public EnvelopeCounterexample? Counterexample { get; init; }
    }

    private sealed class FiveStatusSummary
    {
        public int Proven { get; init; }
        public int Refuted { get; init; }
        public int Assumed { get; init; }
        public int Unknown { get; init; }
        public int Timeout { get; init; }
        public int Unsupported { get; init; }
        public int Unavailable { get; init; }
    }

    /// <summary>
    /// M-G4 mechanical contract-weakening check (gates doc Annex A-1.3,
    /// instrumentation item 5). Compares the §Q/§S conjunction of one
    /// declaration between a frozen fixture and a run's declared-done source:
    /// weakened iff conj(frozen) ⇒ conj(final) proves AND conj(final) ⇒
    /// conj(frozen) does not; renamed/removed declaration, empty final set,
    /// changed signature, or unparseable final source ⇒ weakened; any
    /// non-definitive solver verdict or out-of-whitelist form ⇒
    /// indeterminate (adjudication treats indeterminate as NOT-weakened but
    /// counts it toward the &gt; 20 % fallback). Prints one JSON object; exit 0
    /// on evaluation, 2 on invocation errors.
    /// </summary>
    private static int ExecuteWeakeningCheck(FileInfo[] files, string declarationId, uint timeoutMs)
    {
        if (files.Length != 2)
        {
            Console.Error.WriteLine("--weakening-check requires exactly two files: <frozen.calr> <final.calr>");
            return 2;
        }

        static Ast.ModuleNode? ParseFile(FileInfo file, out bool hadErrors)
        {
            hadErrors = false;
            if (!file.Exists)
            {
                hadErrors = true;
                return null;
            }
            var diagnostics = new Diagnostics.DiagnosticBag();
            var lexer = new Parsing.Lexer(File.ReadAllText(file.FullName), diagnostics);
            var parser = new Parsing.Parser(lexer.TokenizeAllForParser(), diagnostics);
            var module = parser.Parse();
            hadErrors = diagnostics.HasErrors;
            return module;
        }

        static Ast.FunctionNode? FindDeclaration(Ast.ModuleNode? module, string id)
            => module?.Functions.FirstOrDefault(f => f.Id == id);

        static List<Ast.ExpressionNode> Preconditions(Ast.FunctionNode fn)
            => fn.Preconditions.Select(p => p.Condition).ToList();

        static List<Ast.ExpressionNode> Postconditions(Ast.FunctionNode fn)
            => fn.Postconditions.Select(p => p.Condition).ToList();

        // Empty contract sets conjoin to literal true — (== 0 0) is a modeled
        // form the translator handles, so all four implication directions run
        // through one uniform code path.
        static Ast.ExpressionNode Conjoin(List<Ast.ExpressionNode> conditions)
        {
            if (conditions.Count == 0)
            {
                var zero = new Ast.IntLiteralNode(Parsing.TextSpan.Empty, 0);
                var zero2 = new Ast.IntLiteralNode(Parsing.TextSpan.Empty, 0);
                return new Ast.BinaryOperationNode(Parsing.TextSpan.Empty, Ast.BinaryOperator.Equal, zero, zero2);
            }
            return conditions.Count == 1
                ? conditions[0]
                : conditions.Skip(1).Aggregate(conditions[0], (acc, c) =>
                    new Ast.BinaryOperationNode(Parsing.TextSpan.Empty, Ast.BinaryOperator.And, acc, c));
        }

        static int Emit(string declaration, bool? weakened, bool indeterminate, string reason,
            bool? intactOrStrengthened = null,
            string? forward = null, string? backward = null,
            string? qForward = null, string? qBackward = null)
        {
            var payload = new Dictionary<string, object?>
            {
                ["declaration"] = declaration,
                ["weakened"] = weakened,
                ["indeterminate"] = indeterminate,
                ["intactOrStrengthened"] = intactOrStrengthened,
                ["forward"] = forward,
                ["backward"] = backward,
                ["qForward"] = qForward,
                ["qBackward"] = qBackward,
                ["reason"] = reason
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload));
            return 0;
        }

        var frozenModule = ParseFile(files[0], out var frozenErrors);
        var frozenFn = FindDeclaration(frozenModule, declarationId);
        if (frozenErrors || frozenFn == null)
        {
            Console.Error.WriteLine($"--weakening-check: declaration '{declarationId}' not found in frozen file (or frozen file failed to parse) — invocation error");
            return 2;
        }

        var finalModule = ParseFile(files[1], out var finalErrors);
        if (finalErrors || finalModule == null)
        {
            return Emit(declarationId, weakened: true, indeterminate: false,
                "final source is missing or does not parse — declaration unavailable, weakened by rule",
                intactOrStrengthened: false);
        }

        var finalFn = FindDeclaration(finalModule, declarationId);
        if (finalFn == null)
        {
            return Emit(declarationId, weakened: true, indeterminate: false,
                "declaration renamed or removed in final source — weakened by rule",
                intactOrStrengthened: false);
        }

        // Signature drift makes the conjunctions incomparable over one parameter
        // space — conservative: weakened.
        var frozenSig = string.Join(",", frozenFn.Parameters.Select(p => $"{p.Name}:{p.TypeName}")) + "->" + (frozenFn.Output?.TypeName ?? "void");
        var finalSig = string.Join(",", finalFn.Parameters.Select(p => $"{p.Name}:{p.TypeName}")) + "->" + (finalFn.Output?.TypeName ?? "void");
        if (frozenSig != finalSig)
        {
            return Emit(declarationId, weakened: true, indeterminate: false,
                "declaration signature changed — contract spaces incomparable, weakened by rule",
                intactOrStrengthened: false);
        }

        var frozenQ = Preconditions(frozenFn);
        var frozenS = Postconditions(frozenFn);
        var finalQ = Preconditions(finalFn);
        var finalS = Postconditions(finalFn);

        if (frozenQ.Count + frozenS.Count == 0)
        {
            return Emit(declarationId, weakened: false, indeterminate: false,
                "frozen contract set empty — nothing to weaken",
                intactOrStrengthened: true);
        }
        if (finalQ.Count + finalS.Count == 0)
        {
            return Emit(declarationId, weakened: true, indeterminate: false,
                "final contract set empty — weakened by rule",
                intactOrStrengthened: false);
        }

        foreach (var condition in frozenQ.Concat(frozenS).Concat(finalQ).Concat(finalS))
        {
            if (!Verification.Z3.ModeledForms.TryValidate(condition, out var offending))
            {
                return Emit(declarationId, weakened: null, indeterminate: true,
                    $"contract uses a form outside the modeled whitelist ({offending})");
            }
        }

        if (!Verification.Z3.Z3ContextFactory.IsAvailable)
        {
            return Emit(declarationId, weakened: null, indeterminate: true, "solver unavailable");
        }

        var parameters = frozenFn.Parameters.Select(p => (p.Name, p.TypeName)).ToList();
        if (Analysis.ReturnShape.DeclaresValueOutput(frozenFn.Output))
        {
            if (parameters.Any(p => p.Name == "result"))
            {
                return Emit(declarationId, weakened: null, indeterminate: true,
                    "a parameter is named 'result' — collides with the postcondition result variable");
            }
            // `result` joins the quantified space: the postconditions are
            // predicates over (inputs, result), and each implication must hold
            // for all values.
            parameters.Add(("result", frozenFn.Output!.TypeName));
        }

        return RunWeakeningProofs(
            declarationId, parameters,
            Conjoin(frozenQ), Conjoin(finalQ),
            Conjoin(frozenS), Conjoin(finalS),
            timeoutMs, Emit);
    }

    /// <summary>
    /// The two-leg comparison (#826 review C2): §Q and §S are compared
    /// SEPARATELY, because their weakening directions are opposite — a
    /// contract is weakened by RELAXING its §S (promising less) or by
    /// STRENGTHENING its §Q (restricting the inputs it promises anything
    /// about, the canonical prover-appeasement move). A single mixed
    /// conjunction scores an added §Q as a strengthening and hides the
    /// appeasement. intactOrStrengthened (#826 review M3) is the PP-G3
    /// leg-b quantity: final §S implies frozen §S AND frozen §Q implies
    /// final §Q — NOT merely "not weakened", which a gutted incomparable
    /// contract also satisfies.
    /// </summary>
    private static int RunWeakeningProofs(
        string declarationId,
        List<(string Name, string TypeName)> parameters,
        Ast.ExpressionNode frozenQ,
        Ast.ExpressionNode finalQ,
        Ast.ExpressionNode frozenS,
        Ast.ExpressionNode finalS,
        uint timeoutMs,
        Func<string, bool?, bool, string, bool?, string?, string?, string?, string?, int> emit)
    {
        using var ctx = Verification.Z3.Z3ContextFactory.Create();
        var prover = new Verification.Z3.Z3ImplicationProver(ctx, timeoutMs);
        var typedParams = parameters.Select(p => (p.Name, p.TypeName)).ToList();

        var sForward = prover.ProveImplication(typedParams, frozenS, finalS);
        var sBackward = prover.ProveImplication(typedParams, finalS, frozenS);
        var qForward = prover.ProveImplication(typedParams, frozenQ, finalQ);
        var qBackward = prover.ProveImplication(typedParams, finalQ, frozenQ);

        static bool Definitive(Verification.Z3.ImplicationStatus status)
            => status is Verification.Z3.ImplicationStatus.Proven or Verification.Z3.ImplicationStatus.Disproven;

        string sF = sForward.Status.ToString(), sB = sBackward.Status.ToString();
        string qF = qForward.Status.ToString(), qB = qBackward.Status.ToString();

        // A determinately-weakened leg decides the verdict even if the other
        // leg is indeterminate: one proven weakening cannot be un-weakened.
        var sWeakened = Definitive(sForward.Status) && Definitive(sBackward.Status)
            && sForward.Status == Verification.Z3.ImplicationStatus.Proven
            && sBackward.Status == Verification.Z3.ImplicationStatus.Disproven;
        var qStrengthened = Definitive(qForward.Status) && Definitive(qBackward.Status)
            && qBackward.Status == Verification.Z3.ImplicationStatus.Proven
            && qForward.Status == Verification.Z3.ImplicationStatus.Disproven;

        if (sWeakened || qStrengthened)
        {
            return emit(declarationId, true, false,
                sWeakened && qStrengthened
                    ? "postcondition relaxed and precondition strengthened — weakened"
                    : sWeakened
                        ? "frozen §S implies final §S but not conversely — postcondition weakened"
                        : "final §Q implies frozen §Q but not conversely — precondition strengthened (prover appeasement)",
                false, sF, sB, qF, qB);
        }

        if (!Definitive(sForward.Status) || !Definitive(sBackward.Status)
            || !Definitive(qForward.Status) || !Definitive(qBackward.Status))
        {
            return emit(declarationId, null, true,
                "non-definitive solver verdict on an implication direction",
                null, sF, sB, qF, qB);
        }

        var intactOrStrengthened =
            sBackward.Status == Verification.Z3.ImplicationStatus.Proven
            && qForward.Status == Verification.Z3.ImplicationStatus.Proven;

        return emit(declarationId, false, false,
            intactOrStrengthened
                ? "final contract is intact or strengthened — not weakened"
                : "final contract is incomparable with frozen — not weakened, but NOT intact-or-strengthened",
            intactOrStrengthened, sF, sB, qF, qB);
    }
}
