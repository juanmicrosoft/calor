using System.CommandLine;
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

        var rootCommand = new RootCommand("OPAL Compiler - Compiles OPAL source to C# and migrates between languages")
        {
            inputOption,
            outputOption,
            verboseOption
        };

        // Legacy compile handler (when --input is provided)
        rootCommand.SetHandler(CompileAsync, inputOption, outputOption, verboseOption);

        // Add subcommands
        rootCommand.AddCommand(ConvertCommand.Create());
        rootCommand.AddCommand(MigrateCommand.Create());
        rootCommand.AddCommand(BenchmarkCommand.Create());
        rootCommand.AddCommand(InitCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task CompileAsync(FileInfo? input, FileInfo? output, bool verbose)
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
                Console.WriteLine("  opalc benchmark [options]                      Compare token economics");
                Console.WriteLine("  opalc init --ai <agent>                        Initialize for AI coding agents");
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
            var result = Compile(source, input.FullName, verbose);

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

    public static CompilationResult Compile(string source, string? filePath = null, bool verbose = false)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);

        // Lexical analysis
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (verbose)
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

        if (verbose)
        {
            Console.WriteLine("Parsing completed successfully");
        }

        if (diagnostics.HasErrors)
        {
            return new CompilationResult(diagnostics, ast, "");
        }

        // Code generation
        var emitter = new CSharpEmitter();
        var generatedCode = emitter.Emit(ast);

        if (verbose)
        {
            Console.WriteLine("Code generation completed successfully");
        }

        return new CompilationResult(diagnostics, ast, generatedCode);
    }
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
