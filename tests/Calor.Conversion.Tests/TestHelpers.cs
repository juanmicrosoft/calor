using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;

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
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = moduleName,
            GracefulFallback = true,
            AutoGenerateIds = true,
            StripPreprocessor = false
        };
        var converter = new CSharpToCalorConverter(options);
        return converter.Convert(csharpSource);
    }

    /// <summary>
    /// Compiles Calor source back to C# (Calor → AST → C# emit).
    /// Returns null if there are parse errors or parser throws.
    /// </summary>
    public static string? CompileCalorToCSharp(string calorSource)
        => CompileCalorToCSharpWithDiagnostics(calorSource).GeneratedCode;

    public static CalorCompilationAttempt CompileCalorToCSharpWithDiagnostics(
        string calorSource)
    {
        try
        {
            var diagnostics = new DiagnosticBag();
            diagnostics.SetFilePath("test.calr");

            var lexer = new Lexer(calorSource, diagnostics);
            var tokens = lexer.TokenizeAllForParser();
            if (diagnostics.HasErrors)
                return new CalorCompilationAttempt(null, FormatDiagnostics(diagnostics));

            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();
            if (diagnostics.HasErrors)
                return new CalorCompilationAttempt(null, FormatDiagnostics(diagnostics));

            var emitter = new CSharpEmitter();
            return new CalorCompilationAttempt(
                emitter.Emit(module),
                FormatDiagnostics(diagnostics));
        }
        catch (Exception ex)
        {
            return new CalorCompilationAttempt(
                null,
                [$"Compiler exception: {ex.GetType().Name}: {ex.Message}"]);
        }

        static IReadOnlyList<string> FormatDiagnostics(DiagnosticBag diagnostics)
            => diagnostics.Select(diagnostic => diagnostic.ToString()).ToList();
    }

    /// <summary>
    /// Compiles C# source with Roslyn and returns the split syntax/compilation
    /// validation. Uses the shared production helper
    /// (<see cref="GeneratedCSharpCompiler"/>) — full trusted-platform-assembly
    /// reference set plus Calor.Runtime — so compile failures are assertable
    /// emitter defects, not missing-reference noise (#771).
    /// </summary>
    public static GeneratedCSharpValidation RoslynCompile(string csharpSource)
        => GeneratedCSharpCompiler.Validate(csharpSource);

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

        var calorCompilation = CompileCalorToCSharpWithDiagnostics(
            conversionResult.CalorSource);
        var emittedCSharp = calorCompilation.GeneratedCode;

        if (emittedCSharp == null)
        {
            return new RoundTripResult
            {
                ConversionSuccess = true,
                CalorSource = conversionResult.CalorSource,
                CalorParseSuccess = false,
                CalorDiagnostics = calorCompilation.Diagnostics.ToList(),
            };
        }

        var validation = RoslynCompile(emittedCSharp);

        return new RoundTripResult
        {
            ConversionSuccess = true,
            CalorSource = conversionResult.CalorSource,
            CalorParseSuccess = true,
            CalorDiagnostics = calorCompilation.Diagnostics.ToList(),
            EmittedCSharp = emittedCSharp,
            RoslynErrors = validation.FormattedCompilationErrors.ToList(),
            CSharpSyntaxSuccess = validation.SyntaxSuccess,
            RoslynSuccess = validation.CompilationSuccess,
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
    public List<string> CalorDiagnostics { get; init; } = new();
    public string? EmittedCSharp { get; init; }
    public List<string> RoslynErrors { get; init; } = new();

    /// <summary>The emitted C# parses with zero syntax errors (#771 split status).</summary>
    public bool CSharpSyntaxSuccess { get; init; }

    /// <summary>The emitted C# compiles with zero Roslyn errors (full semantic compilation).</summary>
    public bool RoslynSuccess { get; init; }

    public bool CSharpCompilationSuccess => RoslynSuccess;

    public bool FullSuccess => ConversionSuccess && CalorParseSuccess && RoslynSuccess;
}

public sealed record CalorCompilationAttempt(
    string? GeneratedCode,
    IReadOnlyList<string> Diagnostics);
