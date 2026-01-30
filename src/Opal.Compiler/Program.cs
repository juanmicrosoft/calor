using System.CommandLine;
using Opal.Compiler.Analysis;
using Opal.Compiler.Ast;
using Opal.Compiler.CodeGen;
using Opal.Compiler.Commands;
using Opal.Compiler.Diagnostics;
using Opal.Compiler.Parsing;

namespace Opal.Compiler;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var inputOption = new Option<FileInfo>(
            aliases: ["--input", "-i"],
            description: "The OPAL source file to compile");

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

        var rootCommand = new RootCommand("OPAL Compiler - Compiles OPAL source to C# and migrates between languages")
        {
            inputOption,
            outputOption,
            verboseOption,
            strictApiOption,
            requireDocsOption
        };

        // Legacy compile handler (when --input is provided)
        rootCommand.SetHandler(CompileAsync, inputOption, outputOption, verboseOption, strictApiOption, requireDocsOption);

        // Add subcommands
        rootCommand.AddCommand(ConvertCommand.Create());
        rootCommand.AddCommand(MigrateCommand.Create());
        rootCommand.AddCommand(BenchmarkCommand.Create());
        rootCommand.AddCommand(InitCommand.Create());
        rootCommand.AddCommand(FormatCommand.Create());
        rootCommand.AddCommand(DiagnoseCommand.Create());
        rootCommand.AddCommand(AnalyzeCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task CompileAsync(FileInfo? input, FileInfo? output, bool verbose, bool strictApi, bool requireDocs)
    {
        try
        {
            // If no input provided, show help
            if (input == null)
            {
                Console.WriteLine("OPAL Compiler - Compiles OPAL source to C# and migrates between languages");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  opalc --input <file.opal> [--output <file.cs>]  Compile OPAL to C#");
                Console.WriteLine("  opalc convert <file>                           Convert between C# and OPAL");
                Console.WriteLine("  opalc migrate <project>                        Migrate entire project");
                Console.WriteLine("  opalc analyze <directory>                      Analyze C# for migration potential");
                Console.WriteLine("  opalc benchmark [options]                      Compare token economics");
                Console.WriteLine("  opalc init --ai <agent>                        Initialize for AI coding agents");
                Console.WriteLine("  opalc format <files>                           Format OPAL source files");
                Console.WriteLine();
                Console.WriteLine("Strictness options:");
                Console.WriteLine("  --strict-api    Require §BREAKING markers for public API changes");
                Console.WriteLine("  --require-docs  Require documentation on public functions");
                Console.WriteLine();
                Console.WriteLine("Run 'opalc --help' for more information.");
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
            var options = new CompilationOptions
            {
                Verbose = verbose,
                StrictApi = strictApi,
                RequireDocs = requireDocs
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
    /// Compile OPAL source with default options.
    /// </summary>
    public static CompilationResult Compile(string source, string? filePath = null, bool verbose = false)
    {
        return Compile(source, filePath, new CompilationOptions { Verbose = verbose });
    }

    /// <summary>
    /// Compile OPAL source with full options.
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

        // Code generation
        var emitter = new CSharpEmitter();
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
