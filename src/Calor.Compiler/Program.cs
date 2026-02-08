using System.CommandLine;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;

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

        var rootCommand = new RootCommand("Calor Compiler - Compiles Calor source to C# and migrates between languages")
        {
            inputOption,
            outputOption,
            verboseOption,
            strictApiOption,
            requireDocsOption,
            enforceEffectsOption,
            strictEffectsOption,
            contractModeOption
        };

        // Legacy compile handler (when --input is provided)
        rootCommand.SetHandler(CompileAsync, inputOption, outputOption, verboseOption, strictApiOption, requireDocsOption, enforceEffectsOption, strictEffectsOption, contractModeOption);

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

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task CompileAsync(FileInfo? input, FileInfo? output, bool verbose, bool strictApi, bool requireDocs, bool enforceEffects, bool strictEffects, string contractMode)
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
            var options = new CompilationOptions
            {
                Verbose = verbose,
                StrictApi = strictApi,
                RequireDocs = requireDocs,
                EnforceEffects = enforceEffects,
                StrictEffects = strictEffects,
                ContractMode = parsedContractMode,
                ProjectDirectory = Path.GetDirectoryName(input.FullName)
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

        // Lexical analysis
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (options.Verbose)
        {
            Console.WriteLine($"Lexer produced {tokens.Count} tokens");
        }

        if (diagnostics.HasErrors)
        {
            return new CompilationResult(diagnostics, null, "");
        }

        // Parsing
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (options.Verbose)
        {
            Console.WriteLine("Parsing completed successfully");
        }

        if (diagnostics.HasErrors)
        {
            return new CompilationResult(diagnostics, ast, "");
        }

        // Pattern exhaustiveness checking
        var patternChecker = new PatternChecker(diagnostics);
        patternChecker.Check(ast);

        // API strictness checking
        if (options.StrictApi || options.RequireDocs)
        {
            var apiOptions = new ApiStrictnessOptions
            {
                StrictApi = options.StrictApi,
                RequireDocs = options.RequireDocs
            };
            var apiChecker = new ApiStrictnessChecker(diagnostics, apiOptions);
            apiChecker.Check(ast);

            if (options.Verbose)
            {
                Console.WriteLine("API strictness checking completed");
            }
        }

        // Effect enforcement checking
        if (options.EnforceEffects)
        {
            var catalog = EffectsCatalog.CreateWithProjectStubs(options.ProjectDirectory);
            var enforcementPass = new EffectEnforcementPass(
                diagnostics,
                catalog,
                options.UnknownCallPolicy,
                resolver: null,
                strictEffects: options.StrictEffects,
                projectDirectory: options.ProjectDirectory);
            enforcementPass.Enforce(ast);

            if (options.Verbose)
            {
                Console.WriteLine("Effect enforcement completed");
            }
        }

        if (diagnostics.HasErrors)
        {
            return new CompilationResult(diagnostics, ast, "");
        }

        // Code generation
        var emitter = new CSharpEmitter(options.ContractMode);
        var generatedCode = emitter.Emit(ast);

        if (options.Verbose)
        {
            Console.WriteLine("Code generation completed successfully");
        }

        return new CompilationResult(diagnostics, ast, generatedCode);
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
