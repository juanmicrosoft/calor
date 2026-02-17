using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Migration;

/// <summary>
/// Result of a C# to Calor conversion.
/// </summary>
public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string? CalorSource { get; init; }
    public ModuleNode? Ast { get; init; }
    public ConversionContext Context { get; init; } = new();
    public TimeSpan Duration { get; init; }

    public bool HasErrors => Context.HasErrors;
    public bool HasWarnings => Context.HasWarnings;
    public IReadOnlyList<ConversionIssue> Issues => Context.Issues;
}

/// <summary>
/// Options for C# to Calor conversion.
/// </summary>
public sealed class ConversionOptions
{
    /// <summary>
    /// The module name to use in the generated Calor code.
    /// If not specified, derived from the source file name.
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// Whether to preserve original comments in the output.
    /// </summary>
    public bool PreserveComments { get; set; } = true;

    /// <summary>
    /// Whether to include benchmark metrics comparison.
    /// </summary>
    public bool IncludeBenchmark { get; set; }

    /// <summary>
    /// Whether to enable verbose output.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Whether to auto-generate unique IDs for Calor elements.
    /// </summary>
    public bool AutoGenerateIds { get; set; } = true;

    /// <summary>
    /// Whether to emit graceful fallback comments for unsupported constructs.
    /// When true, unsupported C# code is emitted as TODO comments instead of invalid Calor.
    /// Default is true.
    /// </summary>
    public bool GracefulFallback { get; set; } = true;

    /// <summary>
    /// Whether to include explanation details about unsupported features.
    /// When true, conversion results include a detailed explanation of what was not converted.
    /// </summary>
    public bool Explain { get; set; }
}

/// <summary>
/// Main converter that orchestrates the C# to Calor conversion pipeline.
///
/// Pipeline: C# Source → Roslyn Parse → RoslynSyntaxVisitor → Calor AST → CalorEmitter → Calor Source
/// </summary>
public sealed class CSharpToCalorConverter
{
    private readonly ConversionOptions _options;

    public CSharpToCalorConverter(ConversionOptions? options = null)
    {
        _options = options ?? new ConversionOptions();
    }

    /// <summary>
    /// Converts C# source code to Calor source code.
    /// </summary>
    public ConversionResult Convert(string csharpSource, string? sourceFile = null)
    {
        var startTime = DateTime.UtcNow;
        var context = CreateContext(sourceFile);
        context.OriginalSource = csharpSource;

        try
        {
            // Step 1: Parse C# with Roslyn
            var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);
            var root = syntaxTree.GetCompilationUnitRoot();

            // Check for parse errors
            var diagnostics = root.GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .ToList();

            if (diagnostics.Count > 0)
            {
                foreach (var diag in diagnostics)
                {
                    var lineSpan = diag.Location.GetLineSpan();
                    context.AddError(
                        $"C# parse error: {diag.GetMessage()}",
                        line: lineSpan.StartLinePosition.Line + 1,
                        column: lineSpan.StartLinePosition.Character + 1);
                }

                return new ConversionResult
                {
                    Success = false,
                    Context = context,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Step 2: Visit C# AST and build Calor AST
            var moduleName = _options.ModuleName ?? DeriveModuleName(sourceFile, root);
            var visitor = new RoslynSyntaxVisitor(context);
            var calorAst = visitor.Convert(root, moduleName);

            if (context.HasErrors)
            {
                return new ConversionResult
                {
                    Success = false,
                    Ast = calorAst,
                    Context = context,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Step 3: Emit Calor source code
            var emitter = new CalorEmitter(context);
            var calorSource = emitter.Emit(calorAst);

            if (_options.Verbose)
            {
                Console.WriteLine($"Converted {context.Stats.ConvertedNodes} nodes");
                Console.WriteLine($"  Classes: {context.Stats.ClassesConverted}");
                Console.WriteLine($"  Interfaces: {context.Stats.InterfacesConverted}");
                Console.WriteLine($"  Methods: {context.Stats.MethodsConverted}");
                Console.WriteLine($"  Properties: {context.Stats.PropertiesConverted}");
                Console.WriteLine($"  Fields: {context.Stats.FieldsConverted}");
            }

            return new ConversionResult
            {
                Success = true,
                CalorSource = calorSource,
                Ast = calorAst,
                Context = context,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            context.AddError($"Conversion failed: {ex.Message}");

            return new ConversionResult
            {
                Success = false,
                Context = context,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// Converts a C# file to Calor.
    /// </summary>
    public async Task<ConversionResult> ConvertFileAsync(string csharpFilePath)
    {
        if (!File.Exists(csharpFilePath))
        {
            var context = new ConversionContext { SourceFile = csharpFilePath };
            context.AddError($"Source file not found: {csharpFilePath}");
            return new ConversionResult { Success = false, Context = context };
        }

        var source = await File.ReadAllTextAsync(csharpFilePath);
        return Convert(source, csharpFilePath);
    }

    /// <summary>
    /// Converts a C# file and writes the output to an Calor file.
    /// </summary>
    public async Task<ConversionResult> ConvertFileAndSaveAsync(string csharpFilePath, string? outputPath = null)
    {
        var result = await ConvertFileAsync(csharpFilePath);

        if (result.Success && result.CalorSource != null)
        {
            var calorPath = outputPath ?? Path.ChangeExtension(csharpFilePath, ".calr");
            await File.WriteAllTextAsync(calorPath, result.CalorSource);
        }

        return result;
    }

    /// <summary>
    /// Detects the direction of conversion based on file extension.
    /// </summary>
    public static ConversionDirection DetectDirection(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => ConversionDirection.CSharpToCalor,
            ".calr" => ConversionDirection.CalorToCSharp,
            _ => ConversionDirection.Unknown
        };
    }

    private ConversionContext CreateContext(string? sourceFile)
    {
        return new ConversionContext
        {
            SourceFile = sourceFile,
            Verbose = _options.Verbose,
            IncludeBenchmark = _options.IncludeBenchmark,
            PreserveComments = _options.PreserveComments,
            AutoGenerateIds = _options.AutoGenerateIds,
            ModuleName = _options.ModuleName,
            GracefulFallback = _options.GracefulFallback
        };
    }

    private static string DeriveModuleName(string? sourceFile, CompilationUnitSyntax root)
    {
        // Try to get namespace from the source
        var namespaceDecl = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        if (namespaceDecl != null)
        {
            return namespaceDecl.Name.ToString();
        }

        // Fall back to file name
        if (!string.IsNullOrEmpty(sourceFile))
        {
            return Path.GetFileNameWithoutExtension(sourceFile);
        }

        return "ConvertedModule";
    }
}

/// <summary>
/// Direction of conversion.
/// </summary>
public enum ConversionDirection
{
    Unknown,
    CSharpToCalor,
    CalorToCSharp
}

/// <summary>
/// Provides a simple facade for bidirectional conversion.
/// </summary>
public static class Converter
{
    /// <summary>
    /// Converts a file in the detected direction.
    /// </summary>
    public static async Task<object> ConvertFileAsync(string filePath, string? outputPath = null)
    {
        var direction = CSharpToCalorConverter.DetectDirection(filePath);

        return direction switch
        {
            ConversionDirection.CSharpToCalor => await ConvertCSharpToCalorAsync(filePath, outputPath),
            ConversionDirection.CalorToCSharp => await ConvertCalorToCSharpAsync(filePath, outputPath),
            _ => throw new ArgumentException($"Unknown file type: {filePath}")
        };
    }

    /// <summary>
    /// Converts C# to Calor.
    /// </summary>
    public static async Task<ConversionResult> ConvertCSharpToCalorAsync(string csharpPath, string? outputPath = null)
    {
        var converter = new CSharpToCalorConverter();
        var result = await converter.ConvertFileAsync(csharpPath);

        if (result.Success && result.CalorSource != null)
        {
            var calorPath = outputPath ?? Path.ChangeExtension(csharpPath, ".calr");
            await File.WriteAllTextAsync(calorPath, result.CalorSource);
        }

        return result;
    }

    /// <summary>
    /// Converts Calor to C# using the existing compiler.
    /// </summary>
    public static async Task<CompilationResult> ConvertCalorToCSharpAsync(string calorPath, string? outputPath = null)
    {
        var source = await File.ReadAllTextAsync(calorPath);
        var result = Program.Compile(source, calorPath);

        if (!result.HasErrors && !string.IsNullOrEmpty(result.GeneratedCode))
        {
            var csPath = outputPath ?? Path.ChangeExtension(calorPath, ".g.cs");
            await File.WriteAllTextAsync(csPath, result.GeneratedCode);
        }

        return result;
    }
}
