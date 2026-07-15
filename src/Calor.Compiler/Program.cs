using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;
using Calor.Compiler.Telemetry;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.ApplicationInsights.DataContracts;

namespace Calor.Compiler;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var inputOption = new Option<FileInfo[]>(
            aliases: ["--input", "-i"],
            description: "The Calor source file(s) to compile. Pass multiple files to enable cross-module effect enforcement.")
        { Arity = ArgumentArity.OneOrMore };

        var outputOption = new Option<FileInfo>(
            aliases: ["--output", "-o"],
            description: "The output C# file path");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");

        var strictApiOption = new Option<bool>(
            aliases: ["--strict-api"],
            description: "Enable strict API mode: requires §BREAKING markers for public API changes");

        var requireDocsOption = new Option<bool>(
            aliases: ["--require-docs"],
            description: "Require documentation on public functions and types");

        var enforceEffectsOption = new Option<bool>(
            aliases: ["--enforce-effects"],
            description: "Enforce effect declarations (default: false; opt in for strict checking)",
            getDefaultValue: () => false);

        var strictEffectsOption = new Option<bool>(
            aliases: ["--strict-effects"],
            description: "Promote unknown external call warnings (Calor0411) to errors",
            getDefaultValue: () => false);

        var permissiveEffectsOption = new Option<bool>(
            aliases: ["--permissive-effects"],
            description: "Permissive effect mode: unknown calls assumed pure, forbidden effects demoted to warnings. Designed for converted code.",
            getDefaultValue: () => false);

        var contractModeOption = new Option<string>(
            aliases: ["--contract-mode"],
            description: "Contract enforcement mode: off, debug, or release (default: debug)",
            getDefaultValue: () => "debug");

        var verifyOption = new Option<bool>(
            aliases: ["--verify"],
            description: "Enable static contract verification with Z3 SMT solver");

        var noCacheOption = new Option<bool>(
            aliases: ["--no-cache"],
            description: "Disable caching (verification results and the incremental-build cache)");

        var clearCacheOption = new Option<bool>(
            aliases: ["--clear-cache"],
            description: "Clear caches before compiling (verification cache and .calor-build-state.json)");

        var verificationTimeoutOption = new Option<int>(
            aliases: ["--verification-timeout"],
            getDefaultValue: () => (int)VerificationOptions.DefaultTimeoutMs,
            description: "Z3 solver timeout per contract in milliseconds (default: 5000)");
        verificationTimeoutOption.AddValidator(result =>
        {
            var value = result.GetValueOrDefault<int>();
            if (value <= 0)
            {
                result.ErrorMessage = "Verification timeout must be a positive integer";
            }
        });

        var noTelemetryOption = new Option<bool>(
            aliases: ["--no-telemetry"],
            description: "Disable anonymous usage telemetry");

        var analyzeOption = new Option<bool>(
            aliases: ["--analyze"],
            description: "Enable advanced verification analyses (dataflow, bug patterns, taint tracking)");

        var allFindingsOption = new Option<bool>(
            aliases: ["--all-findings"],
            description: "Report all analysis findings including inconclusive and low-confidence results (default: only report verified findings)");

        var strictBindInferenceOption = new Option<bool>(
            aliases: ["--strict-bind-inference"],
            description: "Enable strict bind-inference diagnostics Calor0251-0253 (null/none, generic factory, ambiguous-numeric). Default-on as of v0.6.3.",
            getDefaultValue: () => true);

        var noStrictBindInferenceOption = new Option<bool>(
            aliases: ["--no-strict-bind-inference"],
            description: "Disable the strict bind-inference diagnostics Calor0251-0253 (opt-out of the v0.6.3 default).",
            getDefaultValue: () => false);

        var experimentalOption = new Option<string[]>(
            aliases: ["--experimental"],
            description: "Enable an experimental feature flag (repeatable). Flag names are defined in docs/experiments/registry.json. Unknown flags are accepted silently.")
        { Arity = ArgumentArity.ZeroOrMore };

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Diagnostic output format: text (human-readable, stderr), json, or sarif (machine-readable, stdout)");
        formatOption.FromAmong("text", "json", "sarif");

        var rootCommand = new RootCommand("Calor Compiler - Compiles Calor source to C# and migrates between languages")
        {
            inputOption,
            outputOption,
            verboseOption,
            strictApiOption,
            requireDocsOption,
            enforceEffectsOption,
            strictEffectsOption,
            permissiveEffectsOption,
            contractModeOption,
            verifyOption,
            noCacheOption,
            clearCacheOption,
            verificationTimeoutOption,
            noTelemetryOption,
            analyzeOption,
            allFindingsOption,
            strictBindInferenceOption,
            noStrictBindInferenceOption,
            experimentalOption,
            formatOption
        };

        // Legacy compile handler (when --input is provided)
        rootCommand.SetHandler(async (InvocationContext ctx) =>
        {
            var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
            telemetry?.SetCommand("compile");

            var sw = Stopwatch.StartNew();
            var input = ctx.ParseResult.GetValueForOption(inputOption);

            // Discover .calor/config.json for coding agent telemetry (use first input for discovery)
            if (telemetry != null && input != null && input.Length > 0)
            {
                var discovered = CalorConfigManager.Discover(input[0].FullName);
                telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
            }
            var output = ctx.ParseResult.GetValueForOption(outputOption);
            var verbose = ctx.ParseResult.GetValueForOption(verboseOption);
            var strictApi = ctx.ParseResult.GetValueForOption(strictApiOption);
            var requireDocs = ctx.ParseResult.GetValueForOption(requireDocsOption);
            var enforceEffects = ctx.ParseResult.GetValueForOption(enforceEffectsOption);
            var strictEffects = ctx.ParseResult.GetValueForOption(strictEffectsOption);
            var permissiveEffects = ctx.ParseResult.GetValueForOption(permissiveEffectsOption);
            var contractMode = ctx.ParseResult.GetValueForOption(contractModeOption) ?? "debug";
            var verify = ctx.ParseResult.GetValueForOption(verifyOption);
            var noCache = ctx.ParseResult.GetValueForOption(noCacheOption);
            var clearCache = ctx.ParseResult.GetValueForOption(clearCacheOption);
            var verificationTimeout = ctx.ParseResult.GetValueForOption(verificationTimeoutOption);
            var analyze = ctx.ParseResult.GetValueForOption(analyzeOption);
            var allFindings = ctx.ParseResult.GetValueForOption(allFindingsOption);
            var strictBindInference = ctx.ParseResult.GetValueForOption(strictBindInferenceOption);
            var noStrictBindInference = ctx.ParseResult.GetValueForOption(noStrictBindInferenceOption);
            // --no-strict-bind-inference always wins over the default
            if (noStrictBindInference) strictBindInference = false;
            var experimental = ctx.ParseResult.GetValueForOption(experimentalOption) ?? Array.Empty<string>();
            var format = ctx.ParseResult.GetValueForOption(formatOption) ?? "text";

            telemetry?.TrackEvent("CompileOptions", new Dictionary<string, string>
            {
                ["strictApi"] = strictApi.ToString(),
                ["requireDocs"] = requireDocs.ToString(),
                ["enforceEffects"] = enforceEffects.ToString(),
                ["strictEffects"] = strictEffects.ToString(),
                ["permissiveEffects"] = permissiveEffects.ToString(),
                ["contractMode"] = contractMode,
                ["verify"] = verify.ToString(),
                ["noCache"] = noCache.ToString(),
                ["verificationTimeout"] = verificationTimeout.ToString(),
                ["analyze"] = analyze.ToString(),
                ["strictBindInference"] = strictBindInference.ToString(),
                ["experimentalFlagCount"] = experimental.Length.ToString()
            });

            try
            {
                ctx.ExitCode = await CompileAsync(input, output, verbose, strictApi, requireDocs, enforceEffects, strictEffects, permissiveEffects, contractMode, verify, noCache, clearCache, verificationTimeout, analyze, allFindings, experimental, strictBindInference, format);
            }
            catch (Exception ex)
            {
                telemetry?.TrackException(ex);
                throw;
            }
            finally
            {
                sw.Stop();
                telemetry?.TrackCommand("compile", ctx.ExitCode, new Dictionary<string, string>
                {
                    ["durationMs"] = sw.ElapsedMilliseconds.ToString()
                });

                if (ctx.ExitCode != 0)
                {
                    IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "compile",
                        "Compilation failed (see errors above)");
                }
            }
        });

        // Add subcommands
        rootCommand.AddCommand(ConvertCommand.Create());
        rootCommand.AddCommand(MigrateCommand.Create());
        rootCommand.AddCommand(BenchmarkCommand.Create());
        rootCommand.AddCommand(InitCommand.Create());
        rootCommand.AddCommand(FormatCommand.Create());
        rootCommand.AddCommand(LintCommand.Create());
        rootCommand.AddCommand(AssessCommand.Create());
        rootCommand.AddCommand(AnalyzeConvertibilityCommand.Create());
        rootCommand.AddCommand(HookCommand.Create());
        rootCommand.AddCommand(IdsCommand.Create());
        rootCommand.AddCommand(FixCommand.Create());
        rootCommand.AddCommand(EffectsCommand.Create());
        rootCommand.AddCommand(VerifyCommand.Create());
        rootCommand.AddCommand(LspCommand.Create());
        rootCommand.AddCommand(McpCommand.Create());
        rootCommand.AddCommand(FeatureCheckCommand.Create());
        rootCommand.AddCommand(CoverageCommand.Create());
        rootCommand.AddCommand(SelfTestCommand.Create());
        rootCommand.AddCommand(EvaluationCommand.Create());
        rootCommand.AddCommand(RunCommand.Create());
        rootCommand.AddCommand(TestCommand.Create());
        rootCommand.AddCommand(WatchCommand.Create());

        // Initialize telemetry for subcommands
        // Parse --no-telemetry early from args
        var noTelemetryEarly = args.Contains("--no-telemetry");
        if (!CalorTelemetry.IsInitialized)
        {
            CalorTelemetry.Initialize(noTelemetryEarly);
        }
        if (CalorTelemetry.IsInitialized)
        {
            CalorTelemetry.Instance.TrackSessionStarted();
        }

        var result = await rootCommand.InvokeAsync(args);

        if (CalorTelemetry.IsInitialized)
        {
            CalorTelemetry.Instance.TrackSessionEnded();
            CalorTelemetry.Instance.Flush();
        }

        return result;
    }

    private static Task<int> CompileAsync(FileInfo[]? input, FileInfo? output, bool verbose, bool strictApi, bool requireDocs, bool enforceEffects, bool strictEffects, bool permissiveEffects, string contractMode, bool verify, bool noCache, bool clearCache, int verificationTimeout, bool analyze, bool allFindings = false, string[]? experimentalFlags = null, bool strictBindInference = true, string format = "text")
        => Task.FromResult(CompileCore(input, output, verbose, strictApi, requireDocs, enforceEffects, strictEffects, permissiveEffects, contractMode, verify, noCache, clearCache, verificationTimeout, analyze, allFindings, experimentalFlags, strictBindInference, format));

    private static int CompileCore(FileInfo[]? input, FileInfo? output, bool verbose, bool strictApi, bool requireDocs, bool enforceEffects, bool strictEffects, bool permissiveEffects, string contractMode, bool verify, bool noCache, bool clearCache, int verificationTimeout, bool analyze, bool allFindings, string[]? experimentalFlags, bool strictBindInference, string format = "text")
    {
        // Structured diagnostic output (--format json|sarif): diagnostics are
        // aggregated across files and serialized once through the shared
        // DiagnosticFormatter surface to stdout; human-oriented status messages
        // move to stderr so stdout stays machine-parseable.
        var structuredOutput = !format.Equals("text", StringComparison.OrdinalIgnoreCase);
        var diagnosticSink = structuredOutput ? new DiagnosticBag() : null;
        var structuredEmitted = false;

        // In structured mode a JSON/SARIF document is ALWAYS emitted to stdout,
        // including early-exit error paths (missing input, usage errors, crashes),
        // so machine consumers never have to special-case an empty stdout.
        int Finish(int exitCode)
        {
            if (diagnosticSink != null && !structuredEmitted)
            {
                structuredEmitted = true;
                Console.WriteLine(DiagnosticFormatterFactory.Create(format).Format(diagnosticSink));
            }
            return exitCode;
        }

        try
        {
            // If no input provided, show help. In structured mode the help text
            // goes to stderr so stdout stays reserved for the (empty) document.
            if (input == null || input.Length == 0)
            {
                var helpOut = structuredOutput ? Console.Error : Console.Out;
                WriteHelp(helpOut);
                return Finish(0);
            }

            var anyMissing = false;
            foreach (var file in input)
            {
                if (!file.Exists)
                {
                    diagnosticSink?.Add(new Diagnostic(
                        DiagnosticCode.CliInputNotFound,
                        $"Input file not found: {file.FullName}",
                        new TextSpan(0, 0, 1, 1),
                        DiagnosticSeverity.Error,
                        file.FullName));
                    Console.Error.WriteLine($"Error: Input file not found: {file.FullName}");
                    anyMissing = true;
                }
            }
            if (anyMissing)
            {
                return Finish(1);
            }

            if (output != null && input.Length > 1)
            {
                const string usageError = "--output is only supported when compiling a single file. When passing multiple --input files, generated .g.cs files are written alongside each input.";
                diagnosticSink?.Add(new Diagnostic(
                    DiagnosticCode.CliUsageError,
                    usageError,
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error));
                Console.Error.WriteLine($"Error: {usageError}");
                return Finish(1);
            }

            var parsedContractMode = CompilationDriver.ParseContractMode(contractMode);

            // Incremental-build cache (.calor-build-state.json next to the outputs):
            // active for the default output layout (.g.cs alongside each input);
            // --output redirects the single output elsewhere, so it stays uncached.
            // --no-cache / --clear-cache govern this cache and the verification cache.
            CompilationDriver.DriverCacheSettings? buildCache = null;
            if (output == null)
            {
                var stateDirectory = Incremental.BuildStateCache.ComputeCommonDirectory(input);
                if (noCache)
                {
                    if (clearCache)
                    {
                        Incremental.BuildStateCache.Delete(stateDirectory);
                    }
                }
                else
                {
                    buildCache = new CompilationDriver.DriverCacheSettings(
                        stateDirectory,
                        BuildOptionsToken(strictApi, requireDocs, enforceEffects, strictEffects,
                            permissiveEffects, contractMode, verify, verificationTimeout, analyze,
                            allFindings, strictBindInference, experimentalFlags),
                        ClearFirst: clearCache,
                        OutputPathFor: file => Path.ChangeExtension(file.FullName, ".g.cs"));
                }
            }

            var driverResult = CompilationDriver.CompileAll(
                input,
                file =>
                {
                    var cacheOptions = new VerificationCacheOptions
                    {
                        Enabled = !noCache,
                        ClearBeforeVerification = clearCache,
                        ProjectDirectory = Path.GetDirectoryName(file.FullName)
                    };
                    return new CompilationOptions
                    {
                        Verbose = verbose,
                        // Keep stdout machine-parseable in structured mode:
                        // verbose phase messages go to stderr.
                        StatusWriter = structuredOutput ? Console.Error : null,
                        StrictApi = strictApi,
                        RequireDocs = requireDocs,
                        EnforceEffects = enforceEffects,
                        StrictEffects = strictEffects,
                        UnknownCallPolicy = permissiveEffects ? UnknownCallPolicy.Permissive : UnknownCallPolicy.Strict,
                        ContractMode = parsedContractMode,
                        VerifyContracts = verify,
                        ProjectDirectory = Path.GetDirectoryName(file.FullName),
                        VerificationCacheOptions = cacheOptions,
                        VerificationTimeoutMs = (uint)verificationTimeout,
                        EnableVerificationAnalyses = analyze,
                        VerificationAnalysisOptions = analyze ? new Analysis.VerificationAnalysisOptions
                        {
                            BugPatternOptions = new Analysis.BugPatterns.BugPatternOptions
                            {
                                ReportOnlyVerified = !allFindings,
                                Z3TimeoutMs = (uint)verificationTimeout
                            },
                            TaintOptions = new Analysis.Security.TaintAnalysisOptions
                            {
                                MinTaintHops = allFindings ? 1 : 2
                            }
                        } : null,
                        ExperimentalFlags = experimentalFlags != null && experimentalFlags.Length > 0
                            ? new ExperimentalFlags(experimentalFlags)
                            : ExperimentalFlags.None,
                        StrictBindInference = strictBindInference
                    };
                },
                // Cross-module enforcement always ran for the top-level compile
                // command (independent of --enforce-effects); preserved here.
                crossModuleEnforcement: true,
                crossModulePolicy: permissiveEffects ? UnknownCallPolicy.Permissive : UnknownCallPolicy.Strict,
                onCompiled: (file, result) =>
                {
                    // Determine output path
                    var outputPath = (output?.FullName)
                        ?? Path.ChangeExtension(file.FullName, ".g.cs");

                    // Ensure output directory exists
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    File.WriteAllText(outputPath, result.GeneratedCode);

                    // In structured mode stdout is reserved for the serialized
                    // diagnostics; status messages go to stderr.
                    var statusOut = structuredOutput ? Console.Error : Console.Out;

                    if (verbose)
                    {
                        statusOut.WriteLine($"Output written to: {outputPath}");
                    }

                    statusOut.WriteLine($"Compilation successful: {outputPath}");
                },
                diagnosticSink: diagnosticSink,
                cache: buildCache,
                onSkipped: (file, outputPath) =>
                {
                    var statusOut = structuredOutput ? Console.Error : Console.Out;
                    statusOut.WriteLine($"Up-to-date (cached): {outputPath}");
                });

            return Finish(driverResult.AnyErrors ? 1 : 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            diagnosticSink?.Add(new Diagnostic(
                DiagnosticCode.CliInternalError,
                $"Unhandled error: {ex.Message}",
                new TextSpan(0, 0, 1, 1),
                DiagnosticSeverity.Error));
            return Finish(1);
        }
    }

    /// <summary>
    /// Canonical token of every diagnostics-affecting compile option, folded into the
    /// incremental-build cache's options hash — flipping any of these invalidates all
    /// cached (skipped) files so their diagnostics are recomputed under the new option
    /// set. Presentation-only options (verbose, --format) are deliberately excluded.
    /// Shared by the top-level compile command and <c>calor watch</c>.
    /// </summary>
    internal static string BuildOptionsToken(bool strictApi, bool requireDocs, bool enforceEffects,
        bool strictEffects, bool permissiveEffects, string contractMode, bool verify,
        int verificationTimeout, bool analyze, bool allFindings, bool strictBindInference,
        string[]? experimentalFlags)
    {
        var experimental = experimentalFlags == null
            ? ""
            : string.Join(",", experimentalFlags.OrderBy(f => f, StringComparer.Ordinal));
        return $"strictApi:{strictApi}|requireDocs:{requireDocs}|enforceEffects:{enforceEffects}" +
               $"|strictEffects:{strictEffects}|permissiveEffects:{permissiveEffects}" +
               $"|contractMode:{contractMode.ToLowerInvariant()}|verify:{verify}" +
               $"|verificationTimeout:{verificationTimeout}|analyze:{analyze}|allFindings:{allFindings}" +
               $"|strictBindInference:{strictBindInference}|experimental:{experimental}";
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Calor Compiler - Compiles Calor source to C# and migrates between languages");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  calor --input <file.calr> [--output <file.cs>]   Compile a single Calor file");
        writer.WriteLine("  calor --input <a.calr> --input <b.calr>          Compile multiple files with cross-module effect checking");
        writer.WriteLine("  calor watch <dir|files>                          Watch sources and recompile incrementally on change");
        writer.WriteLine("  calor convert <file>                             Convert between C# and Calor");
        writer.WriteLine("  calor migrate <project>                          Migrate entire project");
        writer.WriteLine("  calor assess <directory>                         Assess C# for migration potential");
        writer.WriteLine("  calor benchmark [options]                        Compare token economics");
        writer.WriteLine("  calor init --ai <agent>                          Initialize for AI coding agents");
        writer.WriteLine("  calor format <files>                             Format Calor source files");
        writer.WriteLine("  calor feature-check <feature>                    Check C# feature support");
        writer.WriteLine("  calor coverage <file>                            Analyze C# file for conversion coverage");
        writer.WriteLine();
        writer.WriteLine("Strictness options:");
        writer.WriteLine("  --strict-api      Require §BREAKING markers for public API changes");
        writer.WriteLine("  --require-docs    Require documentation on public functions");
        writer.WriteLine("  --enforce-effects Enforce effect declarations (default: false)");
        writer.WriteLine("  --strict-effects  Promote unknown external call warnings to errors");
        writer.WriteLine("  --permissive-effects  Permissive mode for converted code (suppress effect errors)");
        writer.WriteLine("  --contract-mode   Contract mode: off, debug, release (default: debug)");
        writer.WriteLine("  --verify          Enable static contract verification with Z3");
        writer.WriteLine("  --verification-timeout  Z3 solver timeout per contract in ms (default: 5000)");
        writer.WriteLine("  --analyze         Enable advanced analyses (dataflow, bugs, taint)");
        writer.WriteLine("  --no-cache        Disable caching (verification results and incremental builds)");
        writer.WriteLine("  --clear-cache     Clear caches before compiling (verification + build state)");
        writer.WriteLine("  --format          Diagnostic output format: text, json, sarif (default: text)");
        writer.WriteLine();
        writer.WriteLine("Run 'calor --help' for more information.");
    }

    /// <summary>
    /// Compile Calor source with default options.
    /// </summary>
    public static CompilationResult Compile(string source, string? filePath = null, bool verbose = false)
    {
        return Compile(source, filePath, new CompilationOptions { Verbose = verbose });
    }

    /// <summary>
    /// Compile Calor source with full options.
    /// </summary>
    public static CompilationResult Compile(string source, string? filePath, CompilationOptions options)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        var phaseSw = new Stopwatch();

        // Verbose/status messages go through the configurable status writer so
        // structured output modes can keep stdout machine-parseable.
        var status = options.StatusWriter ?? Console.Out;

        // Experimental pilot flag — emits one info diagnostic per compilation when
        // the pilot-hello-world flag is enabled. Verifies end-to-end plumbing from
        // CLI (--experimental pilot-hello-world) and MSBuild (CalorExperimentalFlags)
        // through CompilationOptions.ExperimentalFlags. See Phase 0a of
        // docs/plans/calor-native-type-system-v2.md.
        if (options.ExperimentalFlags.IsEnabled("pilot-hello-world"))
        {
            diagnostics.ReportInfo(
                new Parsing.TextSpan(0, 0, 1, 1),
                DiagnosticCode.ExperimentalFlagPilot,
                $"Experimental flag 'pilot-hello-world' is enabled; "
                + $"{options.ExperimentalFlags.Count} flag(s) set on this compilation. "
                + "This pilot flag is a plumbing probe — no feature behavior is gated on it.");
        }

        // Input profile telemetry (Phase 2)
        try
        {
            var inputProfile = TelemetryEnricher.AnalyzeInput(source);
            telemetry?.TrackInputProfile(inputProfile);
        }
        catch
        {
            // Never crash the CLI
        }

        // Lexical analysis
        phaseSw.Restart();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        phaseSw.Stop();
        telemetry?.TrackPhase("Lexer", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors,
            new Dictionary<string, string> { ["tokenCount"] = tokens.Count.ToString() });

        if (options.Verbose)
        {
            status.WriteLine($"Lexer produced {tokens.Count} tokens");
        }

        if (diagnostics.HasErrors)
        {
            TrackDiagnostics(telemetry, diagnostics);
            return new CompilationResult(diagnostics, null, "");
        }

        // Parsing
        phaseSw.Restart();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();
        phaseSw.Stop();
        telemetry?.TrackPhase("Parser", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        if (options.Verbose)
        {
            status.WriteLine("Parsing completed successfully");
        }

        if (diagnostics.HasErrors)
        {
            TrackDiagnostics(telemetry, diagnostics);
            return new CompilationResult(diagnostics, ast, "");
        }

        // Type checking (optional)
        if (options.EnableTypeChecking)
        {
            phaseSw.Restart();
            var typeChecker = new TypeChecking.TypeChecker(diagnostics);
            typeChecker.Check(ast);
            phaseSw.Stop();
            telemetry?.TrackPhase("TypeChecker", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

            if (options.Verbose)
            {
                status.WriteLine("Type checking completed");
            }

            if (diagnostics.HasErrors)
            {
                TrackDiagnostics(telemetry, diagnostics);
                return new CompilationResult(diagnostics, ast, "");
            }
        }

        // Pattern exhaustiveness checking
        phaseSw.Restart();
        var patternChecker = new PatternChecker(diagnostics);
        patternChecker.Check(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("PatternChecker", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        // Bind validation (Calor0250: §B requires type or initializer)
        phaseSw.Restart();
        var bindValidator = new BindValidationPass(diagnostics, source, options.StrictBindInference);
        bindValidator.Check(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("BindValidation", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        // Return validation (Calor0205: value returned from a no-value owner).
        // Always-on hard error — runs regardless of EnableTypeChecking so the
        // generated C# never silently fails with CS0127/CS1622.
        phaseSw.Restart();
        var returnValidator = new ReturnValidationPass(diagnostics);
        returnValidator.Check(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("ReturnValidation", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        if (diagnostics.HasErrors)
        {
            TrackDiagnostics(telemetry, diagnostics);
            return new CompilationResult(diagnostics, ast, "");
        }

        // API strictness checking
        if (options.StrictApi || options.RequireDocs)
        {
            phaseSw.Restart();
            var apiOptions = new ApiStrictnessOptions
            {
                StrictApi = options.StrictApi,
                RequireDocs = options.RequireDocs
            };
            var apiChecker = new ApiStrictnessChecker(diagnostics, apiOptions);
            apiChecker.Check(ast);
            phaseSw.Stop();
            telemetry?.TrackPhase("ApiStrictness", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

            if (options.Verbose)
            {
                status.WriteLine("API strictness checking completed");
            }
        }

        // Effect enforcement checking
        if (options.EnforceEffects)
        {
            phaseSw.Restart();
            // Use shared resolver from CompilationContext if available (IL analysis enabled, task-level caching)
            var sharedResolver = options.Context?.SharedEffectResolver;
            var enforcementPass = new EffectEnforcementPass(
                diagnostics,
                options.UnknownCallPolicy,
                resolver: sharedResolver,
                strictEffects: options.StrictEffects,
                projectDirectory: options.ProjectDirectory);
            enforcementPass.Enforce(ast);
            phaseSw.Stop();
            telemetry?.TrackPhase("EffectEnforcement", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

            if (options.Verbose)
            {
                status.WriteLine("Effect enforcement completed");
            }
        }

        if (diagnostics.HasErrors)
        {
            TrackDiagnostics(telemetry, diagnostics);
            return new CompilationResult(diagnostics, ast, "");
        }

        // Contract inheritance checking
        phaseSw.Restart();
        using var contractInheritanceChecker = new ContractInheritanceChecker(
            diagnostics,
            useZ3: true,
            timeoutMs: options.VerificationTimeoutMs);
        var inheritanceResult = contractInheritanceChecker.Check(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("ContractInheritance", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        if (options.Verbose)
        {
            status.WriteLine("Contract inheritance checking completed");
        }

        if (diagnostics.HasErrors)
        {
            TrackDiagnostics(telemetry, diagnostics);
            return new CompilationResult(diagnostics, ast, "");
        }

        // Contract semantic verification (type checking, reference validation for quantifiers, etc.)
        phaseSw.Restart();
        var contractVerifier = new ContractVerifier(diagnostics);
        contractVerifier.Verify(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("ContractVerifier", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        if (options.Verbose)
        {
            status.WriteLine("Contract semantic verification completed");
        }

        // Contract simplification pass
        phaseSw.Restart();
        var simplificationPass = new ContractSimplificationPass(diagnostics);
        ast = simplificationPass.Simplify(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("ContractSimplification", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        if (options.Verbose)
        {
            status.WriteLine("Contract simplification completed");
        }

        // Refinement type obligation generation and verification (optional)
        if (options.VerifyRefinements)
        {
            phaseSw.Restart();
            var obligationTracker = new Verification.Obligations.ObligationTracker();
            var obligationGenerator = new Verification.Obligations.ObligationGenerator(obligationTracker);
            obligationGenerator.Generate(ast);

            // Solve obligations with Z3 if available
            if (Verification.Z3.Z3ContextFactory.IsAvailable)
            {
                using var ctx = Verification.Z3.Z3ContextFactory.Create();
                using var solver = new Verification.Obligations.ObligationSolver(
                    ctx, options.VerificationTimeoutMs);
                solver.SolveAll(obligationTracker, ast);
            }

            options.ObligationResults = obligationTracker;

            // Report diagnostics for obligation results using policy
            var obligationPolicy = options.ObligationPolicy;
            foreach (var obl in obligationTracker.Obligations)
            {
                var action = obligationPolicy.GetAction(obl.Status);
                if (action == Verification.Obligations.ObligationAction.Ignore)
                    continue;

                var code = (obl.Status, obl.Kind) switch
                {
                    (Verification.Obligations.ObligationStatus.Discharged, Verification.Obligations.ObligationKind.ProofObligation) => DiagnosticCode.ProofObligationDischarged,
                    (Verification.Obligations.ObligationStatus.Discharged, _) => DiagnosticCode.ObligationDischarged,
                    (Verification.Obligations.ObligationStatus.Failed, Verification.Obligations.ObligationKind.ProofObligation) => DiagnosticCode.ProofObligationFailed,
                    (Verification.Obligations.ObligationStatus.Failed, _) => DiagnosticCode.ObligationFailed,
                    (Verification.Obligations.ObligationStatus.Timeout, _) => DiagnosticCode.ObligationTimeout,
                    (Verification.Obligations.ObligationStatus.Boundary, _) => DiagnosticCode.ObligationBoundary,
                    (Verification.Obligations.ObligationStatus.Unsupported, _) => DiagnosticCode.ObligationUnsupported,
                    _ => DiagnosticCode.ObligationUnsupported
                };

                var severity = Verification.Obligations.ObligationPolicy.IsError(action)
                    ? DiagnosticSeverity.Error
                    : action == Verification.Obligations.ObligationAction.WarnOnly
                        || action == Verification.Obligations.ObligationAction.WarnAndGuard
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Info;

                var message = obl.Status switch
                {
                    Verification.Obligations.ObligationStatus.Discharged =>
                        $"Obligation [{obl.Id}] discharged: {obl.Description}",
                    Verification.Obligations.ObligationStatus.Failed =>
                        $"Obligation [{obl.Id}] failed: {obl.Description}. {obl.CounterexampleDescription}",
                    Verification.Obligations.ObligationStatus.Timeout =>
                        $"Obligation [{obl.Id}] timed out: {obl.Description}",
                    Verification.Obligations.ObligationStatus.Boundary =>
                        $"Obligation [{obl.Id}] is a boundary check: {obl.Description}",
                    _ =>
                        $"Obligation [{obl.Id}] unsupported: {obl.Description}. {obl.CounterexampleDescription}"
                };

                diagnostics.Report(obl.Span, code, message, severity);
            }

            phaseSw.Stop();
            telemetry?.TrackPhase("ObligationVerification", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

            if (options.Verbose)
            {
                var summary = obligationTracker.GetSummary();
                status.WriteLine($"Obligation verification: {summary.Total} total, " +
                    $"{summary.Discharged} discharged, {summary.Failed} failed, " +
                    $"{summary.Boundary} boundary, {summary.Timeout} timeout");
            }
        }

        // Static contract verification with Z3 (optional)
        if (options.VerifyContracts)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            phaseSw.Restart();
            var verificationOptions = new VerificationOptions
            {
                Verbose = options.Verbose,
                TimeoutMs = options.VerificationTimeoutMs,
                CacheOptions = options.VerificationCacheOptions ?? VerificationCacheOptions.Default,
                CancellationToken = options.CancellationToken
            };
            var verificationPass = new ContractVerificationPass(diagnostics, verificationOptions);
            options.VerificationResults = verificationPass.Verify(ast);
            phaseSw.Stop();
            telemetry?.TrackPhase("Z3Verification", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

            if (options.Verbose)
            {
                status.WriteLine("Contract verification completed");
            }
        }

        // Advanced verification analyses (dataflow, bug patterns, taint tracking)
        if (options.EnableVerificationAnalyses)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            phaseSw.Restart();
            var analysisOptions = options.VerificationAnalysisOptions ?? Analysis.VerificationAnalysisOptions.Default;
            var analysisPass = new Analysis.VerificationAnalysisPass(diagnostics, analysisOptions);
            options.VerificationAnalysisResult = analysisPass.Analyze(ast);
            phaseSw.Stop();
            telemetry?.TrackPhase("VerificationAnalyses", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors,
                new Dictionary<string, string>
                {
                    ["functionsAnalyzed"] = options.VerificationAnalysisResult.FunctionsAnalyzed.ToString(),
                    ["bugPatternsFound"] = options.VerificationAnalysisResult.BugPatternsFound.ToString(),
                    ["taintVulnerabilities"] = options.VerificationAnalysisResult.TaintVulnerabilities.ToString()
                });

            if (options.Verbose)
            {
                status.WriteLine($"Verification analyses completed: {options.VerificationAnalysisResult.FunctionsAnalyzed} functions, " +
                    $"{options.VerificationAnalysisResult.BugPatternsFound} bug patterns, " +
                    $"{options.VerificationAnalysisResult.TaintVulnerabilities} taint issues");
            }
        }

        // Code generation
        phaseSw.Restart();
        var emitter = new CSharpEmitter(options.ContractMode, options.VerificationResults, inheritanceResult, options.ObligationResults);
        if (options.EmitLineDirectives && !string.IsNullOrEmpty(filePath))
        {
            emitter.LineDirectiveFilePath = filePath;
        }
        var generatedCode = emitter.Emit(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("CodeGen", phaseSw.ElapsedMilliseconds, true);

        if (options.Verbose)
        {
            status.WriteLine("Code generation completed successfully");
        }

        // Compilation outcome & determinism telemetry (Phase 5)
        try
        {
            var inputHash = ComputeHash(source);
            var outputHash = ComputeHash(generatedCode);
            telemetry?.TrackCompilationOutcome(inputHash, !diagnostics.HasErrors,
                diagnostics.Errors.Count(), diagnostics.Warnings.Count());
            telemetry?.TrackCompilationDeterminism(inputHash, outputHash);
        }
        catch
        {
            // Never crash the CLI
        }

        TrackDiagnostics(telemetry, diagnostics);

        // Diagnostic co-occurrence telemetry (Phase 2)
        try
        {
            var codes = diagnostics.Select(d => d.Code).Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (codes.Count > 1)
            {
                var pairs = TelemetryEnricher.AnalyzeCoOccurrence(codes);
                telemetry?.TrackDiagnosticCoOccurrence(pairs);
            }
        }
        catch
        {
            // Never crash the CLI
        }

        return new CompilationResult(diagnostics, ast, generatedCode);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static void TrackDiagnostics(CalorTelemetry? telemetry, DiagnosticBag diagnostics)
    {
        if (telemetry == null) return;

        foreach (var diag in diagnostics.Errors)
        {
            telemetry.TrackDiagnostic(diag.Code, diag.Message, SeverityLevel.Error);
            telemetry.TrackDiagnosticEvent(diag.Code, "Error", GetDiagnosticCategory(diag.Code));
        }

        foreach (var diag in diagnostics.Warnings)
        {
            telemetry.TrackDiagnostic(diag.Code, diag.Message, SeverityLevel.Warning);
            telemetry.TrackDiagnosticEvent(diag.Code, "Warning", GetDiagnosticCategory(diag.Code));
        }
    }

    private static string GetDiagnosticCategory(string code)
    {
        // Extract numeric part from codes like "Calor0001"
        if (code.Length > 5 && int.TryParse(code.AsSpan(5), out var num))
        {
            return num switch
            {
                < 100 => "Lexer",
                < 200 => "Parser",
                < 300 => "Semantic",
                < 400 => "Contract",
                < 500 => "Effect",
                < 600 => "Pattern",
                < 700 => "ApiStrictness",
                < 800 => "Verification",
                < 900 => "Import",
                < 1000 => "Conversion",
                < 1100 => "CodeGen",
                < 1200 => "Verification",
                < 1300 => "Experimental",
                < 1400 => "Cli",
                _ => "Other"
            };
        }
        return "Unknown";
    }
}

/// <summary>
/// Options for compilation.
/// </summary>
public sealed class CompilationOptions
{
    /// <summary>
    /// Enable verbose output.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Writer for verbose/status messages emitted during compilation phases.
    /// Null (the default) means standard output. Structured output modes
    /// (<c>--format json|sarif</c>) redirect this to standard error so stdout
    /// stays reserved for the machine-readable diagnostic document.
    /// </summary>
    public TextWriter? StatusWriter { get; init; }

    /// <summary>
    /// Enable strict API mode: requires §BREAKING markers for public API changes.
    /// </summary>
    public bool StrictApi { get; init; }

    /// <summary>
    /// Require documentation on public functions and types.
    /// </summary>
    public bool RequireDocs { get; init; }

    /// <summary>
    /// Enable effect enforcement checking.
    /// Enabled by default; tests and analysis passes rely on this.
    /// </summary>
    public bool EnforceEffects { get; init; } = true;

    /// <summary>
    /// Policy for handling unknown external calls.
    /// </summary>
    public UnknownCallPolicy UnknownCallPolicy { get; init; } = UnknownCallPolicy.Strict;

    /// <summary>
    /// Promote unknown external call warnings (Calor0411) to errors.
    /// </summary>
    public bool StrictEffects { get; init; }

    /// <summary>
    /// Contract enforcement mode.
    /// </summary>
    public ContractMode ContractMode { get; init; } = ContractMode.Debug;

    /// <summary>
    /// Project directory for loading .calor-effects.json manifests.
    /// </summary>
    public string? ProjectDirectory { get; init; }

    /// <summary>
    /// Enable compile-time IL analysis of referenced assemblies to resolve
    /// effects for methods not covered by manifests. Default: false (opt-in).
    /// </summary>
    public bool EnableILAnalysis { get; init; }

    /// <summary>
    /// Paths to referenced assemblies for cross-assembly IL analysis.
    /// Populated from MSBuild @(ReferencePath) items.
    /// </summary>
    public IReadOnlyList<string>? ReferencedAssemblyPaths { get; init; }

    /// <summary>
    /// Shared compilation context for reusing expensive state across file compilations.
    /// Holds the shared EffectResolver with IL analyzer attached.
    /// </summary>
    public CompilationContext? Context { get; init; }

    /// <summary>
    /// Enable static contract verification with Z3 SMT solver.
    /// </summary>
    public bool VerifyContracts { get; init; }

    /// <summary>
    /// Options for verification result caching.
    /// </summary>
    public VerificationCacheOptions? VerificationCacheOptions { get; init; }

    /// <summary>
    /// Z3 solver timeout per contract in milliseconds.
    /// Default: 5000ms (5 seconds).
    /// </summary>
    public uint VerificationTimeoutMs { get; init; } = VerificationOptions.DefaultTimeoutMs;

    /// <summary>
    /// Verification results populated after running verification pass.
    /// </summary>
    public ModuleVerificationResult? VerificationResults { get; internal set; }

    /// <summary>
    /// Enable advanced verification analyses (dataflow, bug patterns, taint tracking).
    /// </summary>
    public bool EnableVerificationAnalyses { get; init; }

    /// <summary>
    /// Options for verification analyses.
    /// </summary>
    public Analysis.VerificationAnalysisOptions? VerificationAnalysisOptions { get; init; }

    /// <summary>
    /// Results from verification analyses.
    /// </summary>
    public Analysis.VerificationAnalysisResult? VerificationAnalysisResult { get; internal set; }

    /// <summary>
    /// Enable type checking phase.
    /// </summary>
    public bool EnableTypeChecking { get; init; }

    /// <summary>
    /// Enable strict bind-inference diagnostics (Calor0251-0253). Default-on
    /// since v0.6.3 (RFC v0.6 bind-inference-formalization §6). When false,
    /// only Calor0250 (BindRequiresTypeOrInitializer) is enforced. Opt-out
    /// via the CLI flag <c>--no-strict-bind-inference</c>.
    /// </summary>
    public bool StrictBindInference { get; init; } = true;

    /// <summary>
    /// Cancellation token for aborting long-running operations (e.g., Z3 verification).
    /// Checked between compilation phases and between individual contract verifications.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Enable refinement type verification (obligation generation + Z3 solving).
    /// </summary>
    public bool VerifyRefinements { get; init; }

    /// <summary>
    /// Obligation tracker populated after running obligation verification.
    /// </summary>
    public Verification.Obligations.ObligationTracker? ObligationResults { get; internal set; }

    /// <summary>
    /// Policy controlling how obligation statuses map to compiler behavior.
    /// Default: failed=Error, boundary=AlwaysGuard, timeout=WarnAndGuard.
    /// </summary>
    public Verification.Obligations.ObligationPolicy ObligationPolicy { get; init; } = Verification.Obligations.ObligationPolicy.Default;

    /// <summary>
    /// Emit <c>#line</c> directives in generated C# mapping each statement back
    /// to its <c>.calr</c> source location, so Roslyn diagnostics and runtime
    /// stack traces point at the Calor source instead of the generated file.
    /// Default: true. Requires a source file path to be passed to
    /// <see cref="Program.Compile(string, string?, CompilationOptions)"/>;
    /// with a null path no directives are emitted.
    /// </summary>
    public bool EmitLineDirectives { get; init; } = true;

    /// <summary>
    /// Experimental feature flags. Used by Phase 0+ of the Calor-native type-system
    /// research plan (<c>docs/plans/calor-native-type-system-v2.md</c>). Each hypothesis
    /// lands behind a named flag; features check <c>ExperimentalFlags.IsEnabled(name)</c>
    /// before acting.
    /// </summary>
    public ExperimentalFlags ExperimentalFlags { get; init; } = ExperimentalFlags.None;
}

/// <summary>
/// Contract enforcement mode.
/// </summary>
public enum ContractMode
{
    /// <summary>
    /// No contract checks emitted.
    /// </summary>
    Off,

    /// <summary>
    /// Full contract checks with detailed messages.
    /// </summary>
    Debug,

    /// <summary>
    /// Lean contract checks with minimal messages.
    /// </summary>
    Release
}

public sealed class CompilationResult
{
    public DiagnosticBag Diagnostics { get; }
    public ModuleNode? Ast { get; }
    public string GeneratedCode { get; }
    public bool HasErrors => Diagnostics.HasErrors;

    public CompilationResult(DiagnosticBag diagnostics, ModuleNode? ast, string generatedCode)
    {
        Diagnostics = diagnostics;
        Ast = ast;
        GeneratedCode = generatedCode;
    }
}
