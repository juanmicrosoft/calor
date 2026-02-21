using System.Diagnostics;
using System.Text.Json;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CalorProgram = Calor.Compiler.Program;
using CalorCompilationOptions = Calor.Compiler.CompilationOptions;
using CalorCompilationResult = Calor.Compiler.CompilationResult;

namespace Calor.Evaluation.Scorecard;

public class ConversionScorecardRunner
{
    private readonly CSharpToCalorConverter _converter = new();

    public ConversionScorecard Run(string testDataDir, string? commitHash = null)
    {
        var manifestPath = Path.Combine(testDataDir, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest not found: {manifestPath}");

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ManifestFile>(manifestJson)
            ?? throw new InvalidOperationException("Failed to deserialize manifest");

        var results = new List<SnippetResult>();

        foreach (var entry in manifest.Files)
        {
            var result = ProcessSnippet(testDataDir, entry);
            results.Add(result);
        }

        return Aggregate(results, commitHash);
    }

    private SnippetResult ProcessSnippet(string testDataDir, ManifestEntry entry)
    {
        var filePath = Path.Combine(testDataDir, entry.File);
        string csharpSource;

        try
        {
            csharpSource = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return new SnippetResult(
                Id: entry.Id,
                FileName: entry.File,
                Level: entry.Level,
                Features: entry.Features,
                Status: SnippetStatus.Crashed,
                ConversionSuccess: false,
                ConversionErrors: 1,
                ConversionWarnings: 0,
                ConversionIssues: new[] { $"File read error: {ex.Message}" },
                CompilationSuccess: false,
                CompilationErrors: 0,
                CompilationDiagnostics: Array.Empty<string>(),
                RoslynParseSuccess: false,
                ConversionDuration: TimeSpan.Zero,
                CompilationDuration: TimeSpan.Zero);
        }

        // Stage 1: C# → Calor
        ConversionResult conversionResult;
        TimeSpan conversionDuration;
        try
        {
            var sw = Stopwatch.StartNew();
            conversionResult = _converter.Convert(csharpSource, entry.File);
            conversionDuration = sw.Elapsed;
        }
        catch (Exception ex)
        {
            return new SnippetResult(
                Id: entry.Id,
                FileName: entry.File,
                Level: entry.Level,
                Features: entry.Features,
                Status: SnippetStatus.Crashed,
                ConversionSuccess: false,
                ConversionErrors: 1,
                ConversionWarnings: 0,
                ConversionIssues: new[] { $"Converter crash: {ex.GetType().Name}: {ex.Message}" },
                CompilationSuccess: false,
                CompilationErrors: 0,
                CompilationDiagnostics: Array.Empty<string>(),
                RoslynParseSuccess: false,
                ConversionDuration: TimeSpan.Zero,
                CompilationDuration: TimeSpan.Zero);
        }

        var conversionIssues = conversionResult.Issues
            .Select(i => i.ToString())
            .ToArray();
        var conversionErrors = conversionResult.Issues.Count(i => i.Severity == ConversionIssueSeverity.Error);
        var conversionWarnings = conversionResult.Issues.Count(i => i.Severity == ConversionIssueSeverity.Warning);

        if (!conversionResult.Success || string.IsNullOrWhiteSpace(conversionResult.CalorSource))
        {
            return new SnippetResult(
                Id: entry.Id,
                FileName: entry.File,
                Level: entry.Level,
                Features: entry.Features,
                Status: SnippetStatus.Blocked,
                ConversionSuccess: false,
                ConversionErrors: conversionErrors,
                ConversionWarnings: conversionWarnings,
                ConversionIssues: conversionIssues,
                CompilationSuccess: false,
                CompilationErrors: 0,
                CompilationDiagnostics: Array.Empty<string>(),
                RoslynParseSuccess: false,
                ConversionDuration: conversionDuration,
                CompilationDuration: TimeSpan.Zero);
        }

        // Stage 2: Calor → C#
        CalorCompilationResult compilationResult;
        TimeSpan compilationDuration;
        try
        {
            var sw = Stopwatch.StartNew();
            compilationResult = CalorProgram.Compile(
                conversionResult.CalorSource!,
                "scorecard.calr",
                new CalorCompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive
                });
            compilationDuration = sw.Elapsed;
        }
        catch (Exception ex)
        {
            return new SnippetResult(
                Id: entry.Id,
                FileName: entry.File,
                Level: entry.Level,
                Features: entry.Features,
                Status: SnippetStatus.Crashed,
                ConversionSuccess: true,
                ConversionErrors: conversionErrors,
                ConversionWarnings: conversionWarnings,
                ConversionIssues: conversionIssues,
                CompilationSuccess: false,
                CompilationErrors: 1,
                CompilationDiagnostics: new[] { $"Compiler crash: {ex.GetType().Name}: {ex.Message}" },
                RoslynParseSuccess: false,
                ConversionDuration: conversionDuration,
                CompilationDuration: TimeSpan.Zero);
        }

        var compilationDiagnostics = compilationResult.Diagnostics.Errors
            .Select(d => d.ToString())
            .ToArray();

        if (compilationResult.HasErrors)
        {
            return new SnippetResult(
                Id: entry.Id,
                FileName: entry.File,
                Level: entry.Level,
                Features: entry.Features,
                Status: SnippetStatus.PartiallyConverted,
                ConversionSuccess: true,
                ConversionErrors: conversionErrors,
                ConversionWarnings: conversionWarnings,
                ConversionIssues: conversionIssues,
                CompilationSuccess: false,
                CompilationErrors: compilationDiagnostics.Length,
                CompilationDiagnostics: compilationDiagnostics,
                RoslynParseSuccess: false,
                ConversionDuration: conversionDuration,
                CompilationDuration: compilationDuration);
        }

        // Stage 3: Roslyn syntax check
        var roslynSuccess = false;
        if (!string.IsNullOrWhiteSpace(compilationResult.GeneratedCode))
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(compilationResult.GeneratedCode);
            var roslynDiags = syntaxTree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            roslynSuccess = roslynDiags.Count == 0;

            if (!roslynSuccess)
            {
                compilationDiagnostics = compilationDiagnostics
                    .Concat(roslynDiags.Select(d => $"Roslyn: {d}"))
                    .ToArray();
            }
        }

        var status = roslynSuccess
            ? SnippetStatus.FullyConverted
            : SnippetStatus.PartiallyConverted;

        return new SnippetResult(
            Id: entry.Id,
            FileName: entry.File,
            Level: entry.Level,
            Features: entry.Features,
            Status: status,
            ConversionSuccess: true,
            ConversionErrors: conversionErrors,
            ConversionWarnings: conversionWarnings,
            ConversionIssues: conversionIssues,
            CompilationSuccess: true,
            CompilationErrors: roslynSuccess ? 0 : compilationDiagnostics.Length,
            CompilationDiagnostics: compilationDiagnostics,
            RoslynParseSuccess: roslynSuccess,
            ConversionDuration: conversionDuration,
            CompilationDuration: compilationDuration);
    }

    private static ConversionScorecard Aggregate(List<SnippetResult> results, string? commitHash)
    {
        var total = results.Count;
        var fullyConverted = results.Count(r => r.Status == SnippetStatus.FullyConverted);
        var partiallyConverted = results.Count(r => r.Status == SnippetStatus.PartiallyConverted);
        var blocked = results.Count(r => r.Status == SnippetStatus.Blocked);
        var crashed = results.Count(r => r.Status == SnippetStatus.Crashed);
        var roundTripPassing = results.Count(r => r.RoundTripSuccess);

        // By level
        var byLevel = results
            .GroupBy(r => r.Level)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var levelTotal = g.Count();
                    var levelPassed = g.Count(r => r.RoundTripSuccess);
                    return new LevelBreakdown(levelTotal, levelPassed,
                        levelTotal > 0 ? (double)levelPassed / levelTotal : 0);
                });

        // By feature
        var featureResults = new Dictionary<string, (int total, int passed)>();
        foreach (var result in results)
        {
            foreach (var feature in result.Features)
            {
                if (!featureResults.ContainsKey(feature))
                    featureResults[feature] = (0, 0);

                var (t, p) = featureResults[feature];
                featureResults[feature] = (t + 1, p + (result.RoundTripSuccess ? 1 : 0));
            }
        }

        var byFeature = featureResults.ToDictionary(
            kv => kv.Key,
            kv => new FeatureBreakdown(kv.Value.total, kv.Value.passed,
                kv.Value.total > 0 ? (double)kv.Value.passed / kv.Value.total : 0));

        return new ConversionScorecard(
            Timestamp: DateTime.UtcNow,
            CommitHash: commitHash,
            Version: "1.0",
            Total: total,
            FullyConverted: fullyConverted,
            PartiallyConverted: partiallyConverted,
            Blocked: blocked,
            Crashed: crashed,
            RoundTripPassing: roundTripPassing,
            ByLevel: byLevel,
            ByFeature: byFeature,
            Results: results);
    }
}
