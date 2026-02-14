using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Calor.Evaluation.LlmTasks.Execution;

/// <summary>
/// Runs effect discipline analyzers (ED001-ED007) on C# code.
/// This provides a programmatic way to check C# code for effect violations
/// without requiring the analyzers to be installed in the project.
/// </summary>
public sealed class EffectAnalyzerRunner
{
    private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;
    private readonly List<MetadataReference> _references;

    public EffectAnalyzerRunner()
    {
        _analyzers = LoadAnalyzers();
        _references = GetDefaultReferences();
    }

    /// <summary>
    /// Analyzes C# code and returns effect discipline diagnostics.
    /// </summary>
    /// <param name="code">The C# source code to analyze.</param>
    /// <param name="category">The task category for context-specific analysis.</param>
    /// <returns>List of analyzer diagnostics.</returns>
    public async Task<List<AnalyzerDiagnostic>> AnalyzeAsync(string code, string category)
    {
        var diagnostics = new List<AnalyzerDiagnostic>();

        // If no analyzers loaded, return empty (fall back to heuristics)
        if (_analyzers.IsEmpty)
        {
            return diagnostics;
        }

        try
        {
            // Create syntax tree
            var syntaxTree = CSharpSyntaxTree.ParseText(code);

            // Create compilation
            var compilation = CSharpCompilation.Create(
                "EffectAnalysis",
                syntaxTrees: new[] { syntaxTree },
                references: _references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // Run analyzers
            var compilationWithAnalyzers = compilation.WithAnalyzers(_analyzers);
            var analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            // Convert to our diagnostic type
            foreach (var diagnostic in analyzerDiagnostics)
            {
                // Only include ED* diagnostics (our effect discipline rules)
                if (!diagnostic.Id.StartsWith("ED"))
                {
                    continue;
                }

                // Apply category-specific filtering
                if (!IsRelevantForCategory(diagnostic.Id, category))
                {
                    continue;
                }

                var location = diagnostic.Location.GetLineSpan();
                diagnostics.Add(new AnalyzerDiagnostic
                {
                    Id = diagnostic.Id,
                    Message = diagnostic.GetMessage(),
                    Severity = ConvertSeverity(diagnostic.Severity),
                    Line = location.StartLinePosition.Line + 1,
                    Column = location.StartLinePosition.Character + 1
                });
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail - fall back to heuristics
            System.Diagnostics.Debug.WriteLine($"Analyzer execution failed: {ex.Message}");
        }

        return diagnostics;
    }

    /// <summary>
    /// Synchronous wrapper for AnalyzeAsync.
    /// </summary>
    public List<AnalyzerDiagnostic> Analyze(string code, string category)
    {
        return AnalyzeAsync(code, category).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Determines if a diagnostic is relevant for the given category.
    /// </summary>
    private static bool IsRelevantForCategory(string diagnosticId, string category)
    {
        return category switch
        {
            "flaky-test-prevention" => diagnosticId is "ED001" or "ED002" or "ED003" or "ED007",
            "security-boundaries" => diagnosticId is "ED004" or "ED006" or "ED007",
            "side-effect-transparency" => diagnosticId is "ED005" or "ED007",
            "cache-safety" => diagnosticId is "ED001" or "ED002" or "ED003" or "ED007",
            _ => true // Include all for unknown categories
        };
    }

    private static DiagnosticSeverity ConvertSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity severity)
    {
        return severity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Hidden
        };
    }

    /// <summary>
    /// Attempts to load the effect discipline analyzers.
    /// </summary>
    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        var analyzers = new List<DiagnosticAnalyzer>();

        try
        {
            // Try to load the analyzer assembly
            var assemblyPath = FindAnalyzerAssembly();
            if (assemblyPath != null && File.Exists(assemblyPath))
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                var analyzerTypes = assembly.GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(DiagnosticAnalyzer)) && !t.IsAbstract);

                foreach (var type in analyzerTypes)
                {
                    if (Activator.CreateInstance(type) is DiagnosticAnalyzer analyzer)
                    {
                        analyzers.Add(analyzer);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load analyzers: {ex.Message}");
        }

        return analyzers.ToImmutableArray();
    }

    private static string? FindAnalyzerAssembly()
    {
        // Look for the analyzer assembly in common locations
        var searchPaths = new[]
        {
            // Same directory as current assembly
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EffectDiscipline.Analyzers.dll"),
            // Relative to test project
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Analyzers", "bin", "Debug", "netstandard2.0", "EffectDiscipline.Analyzers.dll"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Analyzers", "bin", "Release", "netstandard2.0", "EffectDiscipline.Analyzers.dll"),
        };

        foreach (var path in searchPaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (File.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        return null;
    }

    private static List<MetadataReference> GetDefaultReferences()
    {
        var references = new List<MetadataReference>();

        // Add core runtime references
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? Array.Empty<string>();

        var neededAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Runtime",
            "System.Console",
            "System.Net.Http",
            "System.IO",
            "System.Private.CoreLib",
            "netstandard",
            "mscorlib"
        };

        foreach (var assembly in trustedAssemblies)
        {
            var name = Path.GetFileNameWithoutExtension(assembly);
            if (neededAssemblies.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
            {
                references.Add(MetadataReference.CreateFromFile(assembly));
            }
        }

        return references;
    }
}

/// <summary>
/// Static helper for running effect discipline analysis without instantiating the runner.
/// Uses heuristic analysis as a fallback when analyzers aren't available.
/// </summary>
public static class EffectAnalysis
{
    private static readonly Lazy<EffectAnalyzerRunner> Runner = new(() => new EffectAnalyzerRunner());

    /// <summary>
    /// Analyzes C# code for effect discipline violations.
    /// Tries to use Roslyn analyzers first, falls back to heuristics.
    /// </summary>
    public static async Task<EffectAnalysisResult> AnalyzeAsync(string code, string category)
    {
        var result = new EffectAnalysisResult();

        // Try Roslyn analyzers first
        var analyzerDiagnostics = await Runner.Value.AnalyzeAsync(code, category);
        if (analyzerDiagnostics.Count > 0)
        {
            result.Diagnostics = analyzerDiagnostics;
            result.UsedAnalyzers = true;
            return result;
        }

        // Fall back to heuristics
        result.Diagnostics = AnalyzeWithHeuristics(code, category);
        result.UsedAnalyzers = false;
        return result;
    }

    /// <summary>
    /// Heuristic-based analysis for when Roslyn analyzers aren't available.
    /// </summary>
    private static List<AnalyzerDiagnostic> AnalyzeWithHeuristics(string code, string category)
    {
        var diagnostics = new List<AnalyzerDiagnostic>();

        // ED001: DateTime.Now/UtcNow
        if (category is "flaky-test-prevention" or "cache-safety")
        {
            if (Regex.IsMatch(code, @"DateTime\.(Now|UtcNow|Today)"))
            {
                diagnostics.Add(new AnalyzerDiagnostic
                {
                    Id = "ED001",
                    Message = "DateTime.Now usage detected (heuristic)",
                    Severity = DiagnosticSeverity.Error
                });
            }

            // ED002: Unseeded Random
            if (Regex.IsMatch(code, @"new\s+Random\s*\(\s*\)"))
            {
                diagnostics.Add(new AnalyzerDiagnostic
                {
                    Id = "ED002",
                    Message = "Unseeded Random usage detected (heuristic)",
                    Severity = DiagnosticSeverity.Error
                });
            }

            // ED003: Guid.NewGuid
            if (Regex.IsMatch(code, @"Guid\.NewGuid\s*\(\s*\)"))
            {
                diagnostics.Add(new AnalyzerDiagnostic
                {
                    Id = "ED003",
                    Message = "Guid.NewGuid usage detected (heuristic)",
                    Severity = DiagnosticSeverity.Error
                });
            }
        }

        // ED004: Network access
        if (category == "security-boundaries")
        {
            if (Regex.IsMatch(code, @"\b(HttpClient|WebClient|WebRequest|Socket|TcpClient|UdpClient)\b"))
            {
                diagnostics.Add(new AnalyzerDiagnostic
                {
                    Id = "ED004",
                    Message = "Network access detected (heuristic)",
                    Severity = DiagnosticSeverity.Error
                });
            }
        }

        // ED005: Console output
        if (category == "side-effect-transparency")
        {
            if (Regex.IsMatch(code, @"Console\.(Write|WriteLine|Error)"))
            {
                diagnostics.Add(new AnalyzerDiagnostic
                {
                    Id = "ED005",
                    Message = "Console output detected (heuristic)",
                    Severity = DiagnosticSeverity.Warning
                });
            }
        }

        // ED006: File operations
        if (category == "security-boundaries")
        {
            if (Regex.IsMatch(code, @"\bFile\.(Read|Write|Open|Create|Delete|Exists|Copy|Move)"))
            {
                diagnostics.Add(new AnalyzerDiagnostic
                {
                    Id = "ED006",
                    Message = "File operation detected (heuristic)",
                    Severity = DiagnosticSeverity.Error
                });
            }
        }

        return diagnostics;
    }
}

/// <summary>
/// Result of effect discipline analysis.
/// </summary>
public class EffectAnalysisResult
{
    /// <summary>
    /// The diagnostics found during analysis.
    /// </summary>
    public List<AnalyzerDiagnostic> Diagnostics { get; set; } = new();

    /// <summary>
    /// Whether Roslyn analyzers were used (true) or heuristics (false).
    /// </summary>
    public bool UsedAnalyzers { get; set; }

    /// <summary>
    /// Whether any errors were found.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Whether any warnings were found.
    /// </summary>
    public bool HasWarnings => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);
}
