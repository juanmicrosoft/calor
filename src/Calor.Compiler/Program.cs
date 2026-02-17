using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
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
        var inputOption = new Option<FileInfo>(
            aliases: ["--input", "-i"],
            description: "The Calor source file to compile");

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
            description: "Enforce effect declarations (default: true)",
            getDefaultValue: () => true);

        var strictEffectsOption = new Option<bool>(
            aliases: ["--strict-effects"],
            description: "Promote unknown external call warnings (Calor0411) to errors",
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
            description: "Disable verification result caching");

        var clearCacheOption = new Option<bool>(
            aliases: ["--clear-cache"],
            description: "Clear verification cache before compiling");

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

        var rootCommand = new RootCommand("Calor Compiler - Compiles Calor source to C# and migrates between languages")
        {
            inputOption,
            outputOption,
            verboseOption,
            strictApiOption,
            requireDocsOption,
            enforceEffectsOption,
            strictEffectsOption,
            contractModeOption,
            verifyOption,
            noCacheOption,
            clearCacheOption,
            verificationTimeoutOption,
            noTelemetryOption,
            analyzeOption
        };

        // Legacy compile handler (when --input is provided)
        rootCommand.SetHandler(async (InvocationContext ctx) =>
        {
            var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
            telemetry?.SetCommand("compile");

            var sw = Stopwatch.StartNew();
            var input = ctx.ParseResult.GetValueForOption(inputOption);

            // Discover .calor/config.json for coding agent telemetry
            if (telemetry != null && input != null)
            {
                var discovered = CalorConfigManager.Discover(input.FullName);
                telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
            }
            var output = ctx.ParseResult.GetValueForOption(outputOption);
            var verbose = ctx.ParseResult.GetValueForOption(verboseOption);
            var strictApi = ctx.ParseResult.GetValueForOption(strictApiOption);
            var requireDocs = ctx.ParseResult.GetValueForOption(requireDocsOption);
            var enforceEffects = ctx.ParseResult.GetValueForOption(enforceEffectsOption);
            var strictEffects = ctx.ParseResult.GetValueForOption(strictEffectsOption);
            var contractMode = ctx.ParseResult.GetValueForOption(contractModeOption) ?? "debug";
            var verify = ctx.ParseResult.GetValueForOption(verifyOption);
            var noCache = ctx.ParseResult.GetValueForOption(noCacheOption);
            var clearCache = ctx.ParseResult.GetValueForOption(clearCacheOption);
            var verificationTimeout = ctx.ParseResult.GetValueForOption(verificationTimeoutOption);
            var analyze = ctx.ParseResult.GetValueForOption(analyzeOption);

            telemetry?.TrackEvent("CompileOptions", new Dictionary<string, string>
            {
                ["strictApi"] = strictApi.ToString(),
                ["requireDocs"] = requireDocs.ToString(),
                ["enforceEffects"] = enforceEffects.ToString(),
                ["strictEffects"] = strictEffects.ToString(),
                ["contractMode"] = contractMode,
                ["verify"] = verify.ToString(),
                ["noCache"] = noCache.ToString(),
                ["verificationTimeout"] = verificationTimeout.ToString(),
                ["analyze"] = analyze.ToString()
            });

            try
            {
                await CompileAsync(input, output, verbose, strictApi, requireDocs, enforceEffects, strictEffects, contractMode, verify, noCache, clearCache, verificationTimeout, analyze);
            }
            catch (Exception ex)
            {
                telemetry?.TrackException(ex);
                throw;
            }
            finally
            {
                sw.Stop();
                telemetry?.TrackCommand("compile", Environment.ExitCode, new Dictionary<string, string>
                {
                    ["durationMs"] = sw.ElapsedMilliseconds.ToString()
                });

                if (Environment.ExitCode != 0)
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
        rootCommand.AddCommand(DiagnoseCommand.Create());
        rootCommand.AddCommand(AnalyzeCommand.Create());
        rootCommand.AddCommand(HookCommand.Create());
        rootCommand.AddCommand(IdsCommand.Create());
        rootCommand.AddCommand(EffectsCommand.Create());
        rootCommand.AddCommand(VerifyCommand.Create());
        rootCommand.AddCommand(LspCommand.Create());
        rootCommand.AddCommand(McpCommand.Create());

        // Initialize telemetry for subcommands
        // Parse --no-telemetry early from args
        var noTelemetryEarly = args.Contains("--no-telemetry");
        if (!CalorTelemetry.IsInitialized)
        {
            CalorTelemetry.Initialize(noTelemetryEarly);
        }

        var result = await rootCommand.InvokeAsync(args);

        if (CalorTelemetry.IsInitialized)
        {
            CalorTelemetry.Instance.Flush();
        }

        return result;
    }

    private static async Task CompileAsync(FileInfo? input, FileInfo? output, bool verbose, bool strictApi, bool requireDocs, bool enforceEffects, bool strictEffects, string contractMode, bool verify, bool noCache, bool clearCache, int verificationTimeout, bool analyze)
    {
        try
        {
            // If no input provided, show help
            if (input == null)
            {
                Console.WriteLine("Calor Compiler - Compiles Calor source to C# and migrates between languages");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  calor --input <file.calr> [--output <file.cs>]  Compile Calor to C#");
                Console.WriteLine("  calor convert <file>                           Convert between C# and Calor");
                Console.WriteLine("  calor migrate <project>                        Migrate entire project");
                Console.WriteLine("  calor analyze <directory>                      Analyze C# for migration potential");
                Console.WriteLine("  calor benchmark [options]                      Compare token economics");
                Console.WriteLine("  calor init --ai <agent>                        Initialize for AI coding agents");
                Console.WriteLine("  calor format <files>                           Format Calor source files");
                Console.WriteLine();
                Console.WriteLine("Strictness options:");
                Console.WriteLine("  --strict-api      Require §BREAKING markers for public API changes");
                Console.WriteLine("  --require-docs    Require documentation on public functions");
                Console.WriteLine("  --enforce-effects Enforce effect declarations (default: true)");
                Console.WriteLine("  --strict-effects  Promote unknown external call warnings to errors");
                Console.WriteLine("  --contract-mode   Contract mode: off, debug, release (default: debug)");
                Console.WriteLine("  --verify          Enable static contract verification with Z3");
                Console.WriteLine("  --verification-timeout  Z3 solver timeout per contract in ms (default: 5000)");
                Console.WriteLine("  --analyze         Enable advanced analyses (dataflow, bugs, taint)");
                Console.WriteLine("  --no-cache        Disable verification result caching");
                Console.WriteLine("  --clear-cache     Clear verification cache before compiling");
                Console.WriteLine();
                Console.WriteLine("Run 'calor --help' for more information.");
                return;
            }

            if (!input.Exists)
            {
                Console.Error.WriteLine($"Error: Input file not found: {input.FullName}");
                Environment.ExitCode = 1;
                return;
            }

            if (verbose)
            {
                Console.WriteLine($"Compiling: {input.FullName}");
            }

            var source = await File.ReadAllTextAsync(input.FullName);
            var parsedContractMode = contractMode?.ToLowerInvariant() switch
            {
                "off" => ContractMode.Off,
                "release" => ContractMode.Release,
                _ => ContractMode.Debug
            };
            var cacheOptions = new VerificationCacheOptions
            {
                Enabled = !noCache,
                ClearBeforeVerification = clearCache,
                ProjectDirectory = Path.GetDirectoryName(input.FullName)
            };
            var options = new CompilationOptions
            {
                Verbose = verbose,
                StrictApi = strictApi,
                RequireDocs = requireDocs,
                EnforceEffects = enforceEffects,
                StrictEffects = strictEffects,
                ContractMode = parsedContractMode,
                VerifyContracts = verify,
                ProjectDirectory = Path.GetDirectoryName(input.FullName),
                VerificationCacheOptions = cacheOptions,
                VerificationTimeoutMs = (uint)verificationTimeout,
                EnableVerificationAnalyses = analyze
            };
            var result = Compile(source, input.FullName, options);

            if (result.HasErrors)
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    Console.Error.WriteLine(diagnostic);
                }
                Environment.ExitCode = 1;
                return;
            }

            // Determine output path
            var outputPath = output?.FullName
                ?? Path.ChangeExtension(input.FullName, ".g.cs");

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            await File.WriteAllTextAsync(outputPath, result.GeneratedCode);

            if (verbose)
            {
                Console.WriteLine($"Output written to: {outputPath}");
            }

            Console.WriteLine($"Compilation successful: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
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

        // Lexical analysis
        phaseSw.Restart();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        phaseSw.Stop();
        telemetry?.TrackPhase("Lexer", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors,
            new Dictionary<string, string> { ["tokenCount"] = tokens.Count.ToString() });

        if (options.Verbose)
        {
            Console.WriteLine($"Lexer produced {tokens.Count} tokens");
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
            Console.WriteLine("Parsing completed successfully");
        }

        if (diagnostics.HasErrors)
        {
            TrackDiagnostics(telemetry, diagnostics);
            return new CompilationResult(diagnostics, ast, "");
        }

        // Pattern exhaustiveness checking
        phaseSw.Restart();
        var patternChecker = new PatternChecker(diagnostics);
        patternChecker.Check(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("PatternChecker", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

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
                Console.WriteLine("API strictness checking completed");
            }
        }

        // Effect enforcement checking
        if (options.EnforceEffects)
        {
            phaseSw.Restart();
            var catalog = EffectsCatalog.CreateWithProjectStubs(options.ProjectDirectory);
            var enforcementPass = new EffectEnforcementPass(
                diagnostics,
                catalog,
                options.UnknownCallPolicy,
                resolver: null,
                strictEffects: options.StrictEffects,
                projectDirectory: options.ProjectDirectory);
            enforcementPass.Enforce(ast);
            phaseSw.Stop();
            telemetry?.TrackPhase("EffectEnforcement", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

            if (options.Verbose)
            {
                Console.WriteLine("Effect enforcement completed");
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
            Console.WriteLine("Contract inheritance checking completed");
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
            Console.WriteLine("Contract semantic verification completed");
        }

        // Contract simplification pass
        phaseSw.Restart();
        var simplificationPass = new ContractSimplificationPass(diagnostics);
        ast = simplificationPass.Simplify(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("ContractSimplification", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

        if (options.Verbose)
        {
            Console.WriteLine("Contract simplification completed");
        }

        // Static contract verification with Z3 (optional)
        if (options.VerifyContracts)
        {
            phaseSw.Restart();
            var verificationOptions = new VerificationOptions
            {
                Verbose = options.Verbose,
                TimeoutMs = options.VerificationTimeoutMs,
                CacheOptions = options.VerificationCacheOptions ?? VerificationCacheOptions.Default
            };
            var verificationPass = new ContractVerificationPass(diagnostics, verificationOptions);
            options.VerificationResults = verificationPass.Verify(ast);
            phaseSw.Stop();
            telemetry?.TrackPhase("Z3Verification", phaseSw.ElapsedMilliseconds, !diagnostics.HasErrors);

            if (options.Verbose)
            {
                Console.WriteLine("Contract verification completed");
            }
        }

        // Advanced verification analyses (dataflow, bug patterns, taint tracking)
        if (options.EnableVerificationAnalyses)
        {
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
                Console.WriteLine($"Verification analyses completed: {options.VerificationAnalysisResult.FunctionsAnalyzed} functions, " +
                    $"{options.VerificationAnalysisResult.BugPatternsFound} bug patterns, " +
                    $"{options.VerificationAnalysisResult.TaintVulnerabilities} taint issues");
            }
        }

        // Code generation
        phaseSw.Restart();
        var emitter = new CSharpEmitter(options.ContractMode, options.VerificationResults, inheritanceResult);
        var generatedCode = emitter.Emit(ast);
        phaseSw.Stop();
        telemetry?.TrackPhase("CodeGen", phaseSw.ElapsedMilliseconds, true);

        if (options.Verbose)
        {
            Console.WriteLine("Code generation completed successfully");
        }

        TrackDiagnostics(telemetry, diagnostics);
        return new CompilationResult(diagnostics, ast, generatedCode);
    }

    private static void TrackDiagnostics(CalorTelemetry? telemetry, DiagnosticBag diagnostics)
    {
        if (telemetry == null) return;

        foreach (var diag in diagnostics.Errors)
        {
            telemetry.TrackDiagnostic(diag.Code, diag.Message, SeverityLevel.Error);
        }

        foreach (var diag in diagnostics.Warnings)
        {
            telemetry.TrackDiagnostic(diag.Code, diag.Message, SeverityLevel.Warning);
        }
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
    /// Enable strict API mode: requires §BREAKING markers for public API changes.
    /// </summary>
    public bool StrictApi { get; init; }

    /// <summary>
    /// Require documentation on public functions and types.
    /// </summary>
    public bool RequireDocs { get; init; }

    /// <summary>
    /// Enable effect enforcement checking.
    /// Enabled by default to catch missing effect annotations early.
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
    /// Project directory for loading calor.effects.json stubs.
    /// </summary>
    public string? ProjectDirectory { get; init; }

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
