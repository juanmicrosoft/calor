using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Calor.Conversion.Tests;

/// <summary>
/// Shared helpers for conversion tests.
/// </summary>
public static class TestHelpers
{
    private static readonly CSharpToCalorConverter Converter = new();

    /// <summary>
    /// Converts C# source to Calor source. Returns the ConversionResult.
    /// </summary>
    public static ConversionResult ConvertCSharp(string csharpSource, string? moduleName = null)
    {
        var options = new ConversionOptions
        {
            ModuleName = moduleName,
            GracefulFallback = true,
            AutoGenerateIds = true
        };
        var converter = new CSharpToCalorConverter(options);
        return converter.Convert(csharpSource);
    }

    /// <summary>
    /// Compiles Calor source back to C# (Calor → AST → C# emit).
    /// Returns null if there are parse errors or parser throws.
    /// </summary>
    public static string? CompileCalorToCSharp(string calorSource)
    {
        try
        {
            var diagnostics = new DiagnosticBag();
            diagnostics.SetFilePath("test.calr");

            var lexer = new Lexer(calorSource, diagnostics);
            var tokens = lexer.TokenizeAll();
            if (diagnostics.HasErrors) return null;

            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();
            if (diagnostics.HasErrors) return null;

            var emitter = new CSharpEmitter();
            return emitter.Emit(module);
        }
        catch
        {
            // Parser may throw for unsupported Calor syntax
            return null;
        }
    }

    /// <summary>
    /// Compiles C# source with Roslyn and returns diagnostics.
    /// Only returns errors (not warnings).
    /// </summary>
    public static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> RoslynCompile(string csharpSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "RoundTripTest",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();
    }

    /// <summary>
    /// Full round-trip: C# → Calor → C# → Roslyn compile.
    /// Returns (success, calorSource, emittedCSharp, roslynErrors).
    /// </summary>
    public static RoundTripResult FullRoundTrip(string csharpSource, string? moduleName = null)
    {
        var conversionResult = ConvertCSharp(csharpSource, moduleName);

        if (!conversionResult.Success || conversionResult.CalorSource == null)
        {
            return new RoundTripResult
            {
                ConversionSuccess = false,
                ConversionIssues = conversionResult.Issues.Select(i => i.Message).ToList(),
            };
        }

        var emittedCSharp = CompileCalorToCSharp(conversionResult.CalorSource);

        if (emittedCSharp == null)
        {
            return new RoundTripResult
            {
                ConversionSuccess = true,
                CalorSource = conversionResult.CalorSource,
                CalorParseSuccess = false,
            };
        }

        var roslynErrors = RoslynCompile(emittedCSharp);

        return new RoundTripResult
        {
            ConversionSuccess = true,
            CalorSource = conversionResult.CalorSource,
            CalorParseSuccess = true,
            EmittedCSharp = emittedCSharp,
            RoslynErrors = roslynErrors.Select(d => d.GetMessage()).ToList(),
            RoslynSuccess = roslynErrors.Count == 0,
        };
    }

    /// <summary>
    /// Reads a snapshot file from the Snapshots directory.
    /// </summary>
    public static string? ReadSnapshot(string snapshotName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}

public sealed class RoundTripResult
{
    public bool ConversionSuccess { get; init; }
    public List<string> ConversionIssues { get; init; } = new();
    public string? CalorSource { get; init; }
    public bool CalorParseSuccess { get; init; }
    public string? EmittedCSharp { get; init; }
    public List<string> RoslynErrors { get; init; } = new();
    public bool RoslynSuccess { get; init; }

    public bool FullSuccess => ConversionSuccess && CalorParseSuccess && RoslynSuccess;
}
