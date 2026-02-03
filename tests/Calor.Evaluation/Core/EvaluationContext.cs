using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using RoslynDiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Calor.Evaluation.Core;

/// <summary>
/// Shared context for evaluation containing both Calor and C# source code,
/// along with lazy-loaded compilation results for each.
/// </summary>
public class EvaluationContext
{
    /// <summary>
    /// The Calor source code being evaluated.
    /// </summary>
    public required string CalorSource { get; init; }

    /// <summary>
    /// The equivalent C# source code being compared.
    /// </summary>
    public required string CSharpSource { get; init; }

    /// <summary>
    /// The file name or identifier for this benchmark case.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Complexity level (1-5) of this benchmark case.
    /// </summary>
    public int Level { get; init; } = 1;

    /// <summary>
    /// Feature tags describing what this benchmark tests.
    /// </summary>
    public List<string> Features { get; init; } = new();

    /// <summary>
    /// Optional metadata for this evaluation case.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    private CalorCompilationResult? _calorCompilation;
    private CSharpCompilationResult? _csharpCompilation;

    /// <summary>
    /// Gets the lazy-loaded Calor compilation result.
    /// </summary>
    public CalorCompilationResult CalorCompilation
    {
        get
        {
            _calorCompilation ??= CompileCalor();
            return _calorCompilation;
        }
    }

    /// <summary>
    /// Gets the lazy-loaded C# compilation result.
    /// </summary>
    public CSharpCompilationResult CSharpCompilation
    {
        get
        {
            _csharpCompilation ??= CompileCSharp();
            return _csharpCompilation;
        }
    }

    private CalorCompilationResult CompileCalor()
    {
        var diagnostics = new DiagnosticBag();

        try
        {
            var lexer = new Lexer(CalorSource, diagnostics);
            var tokens = lexer.TokenizeAll();

            if (diagnostics.HasErrors)
            {
                return new CalorCompilationResult(
                    Success: false,
                    Module: null,
                    Tokens: tokens,
                    Errors: diagnostics.Errors.Select(d => d.Message).ToList());
            }

            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            return new CalorCompilationResult(
                Success: !diagnostics.HasErrors,
                Module: module,
                Tokens: tokens,
                Errors: diagnostics.Errors.Select(d => d.Message).ToList());
        }
        catch (Exception ex)
        {
            return new CalorCompilationResult(
                Success: false,
                Module: null,
                Tokens: new List<Token>(),
                Errors: new List<string> { ex.Message });
        }
    }

    private CSharpCompilationResult CompileCSharp()
    {
        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(CSharpSource);
            var root = syntaxTree.GetCompilationUnitRoot();
            var diagnostics = root.GetDiagnostics().ToList();

            return new CSharpCompilationResult(
                Success: !diagnostics.Any(d => d.Severity == RoslynDiagnosticSeverity.Error),
                SyntaxTree: syntaxTree,
                Root: root,
                Errors: diagnostics
                    .Where(d => d.Severity == RoslynDiagnosticSeverity.Error)
                    .Select(d => d.GetMessage())
                    .ToList());
        }
        catch (Exception ex)
        {
            return new CSharpCompilationResult(
                Success: false,
                SyntaxTree: null,
                Root: null,
                Errors: new List<string> { ex.Message });
        }
    }

    /// <summary>
    /// Creates an evaluation context from paired Calor and C# files.
    /// </summary>
    public static async Task<EvaluationContext> FromFilesAsync(
        string calorPath,
        string csharpPath,
        int level = 1,
        List<string>? features = null)
    {
        var calorSource = await File.ReadAllTextAsync(calorPath);
        var csharpSource = await File.ReadAllTextAsync(csharpPath);

        return new EvaluationContext
        {
            CalorSource = calorSource,
            CSharpSource = csharpSource,
            FileName = Path.GetFileNameWithoutExtension(calorPath),
            Level = level,
            Features = features ?? new List<string>()
        };
    }
}

/// <summary>
/// Result of compiling Calor source code.
/// </summary>
public record CalorCompilationResult(
    bool Success,
    ModuleNode? Module,
    List<Token> Tokens,
    List<string> Errors);

/// <summary>
/// Result of compiling C# source code.
/// </summary>
public record CSharpCompilationResult(
    bool Success,
    SyntaxTree? SyntaxTree,
    Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax? Root,
    List<string> Errors);
