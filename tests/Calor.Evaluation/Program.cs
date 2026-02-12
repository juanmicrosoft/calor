using System.CommandLine;
using Calor.Evaluation.Benchmarks;
using Calor.Evaluation.Core;
using Calor.Evaluation.Reports;

namespace Calor.Evaluation;

/// <summary>
/// Entry point for running evaluations from the command line.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Calor vs C# Evaluation Framework")
        {
            Description = "Benchmark and compare Calor and C# code for AI agent effectiveness"
        };

        // Run command
        var runCommand = new Command("run", "Run benchmarks and generate reports");

        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Output file path for the report",
            getDefaultValue: () => "report.json");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            description: "Output format (json, markdown, both, website, html)",
            getDefaultValue: () => "json");

        var categoryOption = new Option<string[]>(
            aliases: new[] { "--category", "-c" },
            description: "Specific categories to run (default: all)")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        var manifestOption = new Option<string>(
            aliases: new[] { "--manifest", "-m" },
            description: "Path to benchmark manifest file");

        var statisticalOption = new Option<bool>(
            aliases: new[] { "--statistical", "-s" },
            description: "Enable statistical analysis with multiple runs");

        var runsOption = new Option<int>(
            aliases: new[] { "--runs", "-r" },
            description: "Number of runs for statistical analysis",
            getDefaultValue: () => 30);

        runCommand.AddOption(outputOption);
        runCommand.AddOption(formatOption);
        runCommand.AddOption(categoryOption);
        runCommand.AddOption(verboseOption);
        runCommand.AddOption(manifestOption);
        runCommand.AddOption(statisticalOption);
        runCommand.AddOption(runsOption);

        runCommand.SetHandler(async (output, format, categories, verbose, manifest, statistical, runs) =>
        {
            await RunBenchmarksAsync(output, format, categories, verbose, manifest, statistical, runs);
        }, outputOption, formatOption, categoryOption, verboseOption, manifestOption, statisticalOption, runsOption);

        rootCommand.AddCommand(runCommand);

        // Quick command for inline testing
        var quickCommand = new Command("quick", "Run a quick test with inline code");

        var calorOption = new Option<string>(
            aliases: new[] { "--calor" },
            description: "Calor source code or file path")
        { IsRequired = true };

        var csharpOption = new Option<string>(
            aliases: new[] { "--csharp" },
            description: "C# source code or file path")
        { IsRequired = true };

        quickCommand.AddOption(calorOption);
        quickCommand.AddOption(csharpOption);

        quickCommand.SetHandler(async (calor, csharp) =>
        {
            await RunQuickTestAsync(calor, csharp);
        }, calorOption, csharpOption);

        rootCommand.AddCommand(quickCommand);

        // Discover command
        var discoverCommand = new Command("discover", "Discover benchmark files in a directory");

        var dirOption = new Option<string>(
            aliases: new[] { "--dir", "-d" },
            description: "Directory to scan for Calor/C# pairs")
        { IsRequired = true };

        var discoverOutputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Output manifest file path",
            getDefaultValue: () => "manifest.json");

        discoverCommand.AddOption(dirOption);
        discoverCommand.AddOption(discoverOutputOption);

        discoverCommand.SetHandler(async (dir, discoverOutput) =>
        {
            await DiscoverBenchmarksAsync(dir, discoverOutput);
        }, dirOption, discoverOutputOption);

        rootCommand.AddCommand(discoverCommand);

        // Default: run benchmarks if no command specified
        rootCommand.SetHandler(async () =>
        {
            await RunBenchmarksAsync("report.json", "both", Array.Empty<string>(), false, null);
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task RunBenchmarksAsync(
        string output,
        string format,
        string[] categories,
        bool verbose,
        string? manifestPath,
        bool statistical = false,
        int runs = 30)
    {
        Console.WriteLine("Calor vs C# Evaluation Framework");
        Console.WriteLine("================================");
        Console.WriteLine();

        var options = new BenchmarkRunnerOptions
        {
            Verbose = verbose,
            Categories = categories.ToList(),
            StatisticalMode = statistical,
            StatisticalRuns = runs
        };

        if (statistical)
        {
            Console.WriteLine($"Statistical analysis mode: {runs} runs");
            Console.WriteLine();
        }

        var runner = new BenchmarkRunner(options);

        // Load or create manifest
        BenchmarkManifest manifest;
        if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
        {
            Console.WriteLine($"Loading manifest: {manifestPath}");
            manifest = await BenchmarkManifest.LoadAsync(manifestPath);
        }
        else
        {
            // Try to load default manifest
            var defaultPath = Path.Combine(TestDataAdapter.GetBenchmarkPath(), "manifest.json");
            if (File.Exists(defaultPath))
            {
                Console.WriteLine($"Loading default manifest: {defaultPath}");
                manifest = await BenchmarkManifest.LoadAsync(defaultPath);
            }
            else
            {
                Console.WriteLine("No manifest found. Creating sample manifest...");
                manifest = CreateSampleManifest();
            }
        }

        Console.WriteLine($"Running {manifest.Benchmarks.Count} benchmarks...");
        Console.WriteLine();

        var result = await runner.RunAllAsync(manifest);

        Console.WriteLine($"Completed {result.BenchmarkCount} benchmarks.");
        Console.WriteLine();

        // Generate reports
        if (format is "json" or "both")
        {
            var jsonGenerator = new JsonReportGenerator();
            var jsonPath = format == "both" ? Path.ChangeExtension(output, ".json") : output;
            await jsonGenerator.SaveAsync(result, jsonPath);
            Console.WriteLine($"JSON report saved to: {jsonPath}");
        }

        if (format is "markdown" or "both")
        {
            var mdGenerator = new MarkdownReportGenerator();
            var mdPath = format == "both" ? Path.ChangeExtension(output, ".md") : output;
            await mdGenerator.SaveAsync(result, mdPath);
            Console.WriteLine($"Markdown report saved to: {mdPath}");
        }

        if (format is "website")
        {
            var websiteGenerator = new WebsiteReportGenerator();
            await websiteGenerator.SaveAsync(result, output);
            Console.WriteLine($"Website JSON saved to: {output}");
        }

        if (format is "html")
        {
            // Generate both website JSON and an HTML dashboard
            var websiteGenerator = new WebsiteReportGenerator();
            var jsonPath = Path.ChangeExtension(output, ".json");
            await websiteGenerator.SaveAsync(result, jsonPath);

            var htmlPath = Path.ChangeExtension(output, ".html");
            await GenerateHtmlDashboard(result, htmlPath);
            Console.WriteLine($"HTML dashboard saved to: {htmlPath}");
        }

        // Print summary
        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Overall Calor Advantage: {result.Summary.OverallCalorAdvantage:F2}x");

        if (result.HasStatisticalAnalysis)
        {
            Console.WriteLine($"  Statistical runs: {result.StatisticalRunCount}");
        }

        // Identify Calor-only categories
        var calorOnlyCategories = result.Metrics
            .Where(m => m.IsCalorOnly)
            .Select(m => m.Category)
            .Distinct()
            .ToHashSet();

        Console.WriteLine();
        Console.WriteLine("  Category Advantages:");
        foreach (var (category, advantage) in result.Summary.CategoryAdvantages.OrderByDescending(kv => kv.Value))
        {
            // Calor-only categories are always Calor wins (when they have any score)
            var isCalorOnly = calorOnlyCategories.Contains(category);
            var indicator = (advantage > 1.0 || isCalorOnly) ? "+" : (advantage < 1.0 ? "-" : "=");
            var ciStr = "";
            if (result.Summary.CategoryConfidenceIntervals.TryGetValue(category, out var ci))
            {
                ciStr = $" (95% CI: [{ci.Lower:F2}, {ci.Upper:F2}])";
            }
            var suffix = isCalorOnly ? " (Calor-only)" : "";
            Console.WriteLine($"    {indicator} {category}: {advantage:F2}x{ciStr}{suffix}");
        }

        // Print statistical significance if available
        if (result.HasStatisticalAnalysis && result.StatisticalSummaries.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Statistical Significance (p < 0.05):");
            var significantCategories = result.StatisticalSummaries
                .Where(s => s.TTest?.IsSignificant == true)
                .GroupBy(s => s.Category)
                .Select(g => g.Key)
                .ToList();

            if (significantCategories.Count > 0)
            {
                foreach (var cat in significantCategories)
                {
                    var summary = result.StatisticalSummaries.First(s => s.Category == cat);
                    // Calor-only categories are always Calor wins
                    var isCalorOnly = calorOnlyCategories.Contains(cat);
                    var winner = (summary.AdvantageRatioMean > 1.0 || isCalorOnly) ? "Calor" : "C#";
                    Console.WriteLine($"    * {cat}: {winner} wins (d={summary.CohensD:F2}, {summary.EffectSizeInterpretation} effect)");
                }
            }
            else
            {
                Console.WriteLine("    No statistically significant differences found.");
            }
        }
    }

    private static async Task RunQuickTestAsync(string calor, string csharp)
    {
        // Load from files if paths provided
        var calorSource = File.Exists(calor) ? await File.ReadAllTextAsync(calor) : calor;
        var csharpSource = File.Exists(csharp) ? await File.ReadAllTextAsync(csharp) : csharp;

        Console.WriteLine("Quick Evaluation");
        Console.WriteLine("================");
        Console.WriteLine();

        var runner = new BenchmarkRunner(new BenchmarkRunnerOptions { Verbose = true });
        var result = await runner.RunFromSourceAsync(calorSource, csharpSource, "quick-test");

        Console.WriteLine();
        Console.WriteLine("Results:");
        Console.WriteLine($"  Calor compiles: {result.CalorSuccess}");
        Console.WriteLine($"  C# compiles: {result.CSharpSuccess}");
        Console.WriteLine($"  Average advantage: {result.AverageAdvantage:F2}x");
        Console.WriteLine();
        Console.WriteLine("Metrics:");

        foreach (var metric in result.Metrics)
        {
            var indicator = metric.AdvantageRatio > 1.0 ? "+" : (metric.AdvantageRatio < 1.0 ? "-" : "=");
            Console.WriteLine($"  {indicator} {metric.Category}/{metric.MetricName}: {metric.AdvantageRatio:F2}x (Calor={metric.CalorScore:F2}, C#={metric.CSharpScore:F2})");
        }
    }

    private static async Task DiscoverBenchmarksAsync(string directory, string output)
    {
        Console.WriteLine($"Discovering benchmarks in: {directory}");

        var manifest = await TestDataAdapter.DiscoverBenchmarksAsync(directory);

        Console.WriteLine($"Found {manifest.Benchmarks.Count} paired Calor/C# files");

        await manifest.SaveAsync(output);
        Console.WriteLine($"Manifest saved to: {output}");
    }

    private static BenchmarkManifest CreateSampleManifest()
    {
        return new BenchmarkManifest
        {
            Version = "1.0",
            Description = "Sample benchmarks for Calor vs C# evaluation",
            Benchmarks = new List<BenchmarkEntry>
            {
                new()
                {
                    Id = "001",
                    Name = "HelloWorld",
                    Category = "TokenEconomics",
                    CalorFile = "TokenEconomics/HelloWorld.calr",
                    CSharpFile = "TokenEconomics/HelloWorld.cs",
                    Level = 1,
                    Features = new List<string> { "module", "function", "console_write" },
                    Notes = "Simple hello world comparison"
                },
                new()
                {
                    Id = "002",
                    Name = "Calculator",
                    Category = "TokenEconomics",
                    CalorFile = "TokenEconomics/Calculator.calr",
                    CSharpFile = "TokenEconomics/Calculator.cs",
                    Level = 2,
                    Features = new List<string> { "module", "function", "parameters", "return_type" },
                    Notes = "Basic arithmetic operations"
                },
                new()
                {
                    Id = "003",
                    Name = "FizzBuzz",
                    Category = "TokenEconomics",
                    CalorFile = "TokenEconomics/FizzBuzz.calr",
                    CSharpFile = "TokenEconomics/FizzBuzz.cs",
                    Level = 2,
                    Features = new List<string> { "module", "function", "conditional", "loop" },
                    Notes = "Classic FizzBuzz implementation"
                }
            }
        };
    }

    private static async Task GenerateHtmlDashboard(EvaluationResult result, string outputPath)
    {
        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Calor Benchmark Dashboard</title>
    <style>
        :root {{
            --calor-green: #22c55e;
            --csharp-blue: #3b82f6;
            --bg-dark: #0f172a;
            --bg-card: #1e293b;
            --text-primary: #f1f5f9;
            --text-secondary: #94a3b8;
        }}
        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: var(--bg-dark);
            color: var(--text-primary);
            padding: 2rem;
        }}
        .container {{ max-width: 1200px; margin: 0 auto; }}
        h1 {{ font-size: 2rem; margin-bottom: 0.5rem; }}
        .subtitle {{ color: var(--text-secondary); margin-bottom: 2rem; }}
        .summary-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 1rem;
            margin-bottom: 2rem;
        }}
        .card {{
            background: var(--bg-card);
            border-radius: 0.75rem;
            padding: 1.5rem;
        }}
        .card-label {{ color: var(--text-secondary); font-size: 0.875rem; }}
        .card-value {{ font-size: 2rem; font-weight: bold; margin-top: 0.5rem; }}
        .card-value.calor {{ color: var(--calor-green); }}
        .card-value.csharp {{ color: var(--csharp-blue); }}
        .metrics-table {{
            width: 100%;
            border-collapse: collapse;
            margin-top: 1rem;
        }}
        .metrics-table th, .metrics-table td {{
            padding: 1rem;
            text-align: left;
            border-bottom: 1px solid rgba(255,255,255,0.1);
        }}
        .metrics-table th {{ color: var(--text-secondary); font-weight: 500; }}
        .winner-calor {{ color: var(--calor-green); }}
        .winner-csharp {{ color: var(--csharp-blue); }}
        .badge {{
            display: inline-block;
            padding: 0.25rem 0.5rem;
            border-radius: 0.25rem;
            font-size: 0.75rem;
            font-weight: 500;
        }}
        .badge-calor {{ background: rgba(34, 197, 94, 0.2); color: var(--calor-green); }}
        .badge-csharp {{ background: rgba(59, 130, 246, 0.2); color: var(--csharp-blue); }}
        .ci {{ color: var(--text-secondary); font-size: 0.875rem; }}
        .timestamp {{ color: var(--text-secondary); font-size: 0.875rem; margin-top: 2rem; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Calor Benchmark Results</h1>
        <p class=""subtitle"">Comparing Calor vs C# for AI coding agent effectiveness</p>

        <div class=""summary-grid"">
            <div class=""card"">
                <div class=""card-label"">Overall Advantage</div>
                <div class=""card-value {(result.Summary.OverallCalorAdvantage > 1 ? "calor" : "csharp")}"">{result.Summary.OverallCalorAdvantage:F2}x</div>
            </div>
            <div class=""card"">
                <div class=""card-label"">Programs Tested</div>
                <div class=""card-value"">{result.BenchmarkCount}</div>
            </div>
            <div class=""card"">
                <div class=""card-label"">Calor Wins</div>
                <div class=""card-value calor"">{result.Summary.TopCalorCategories.Count}</div>
            </div>
            <div class=""card"">
                <div class=""card-label"">C# Wins</div>
                <div class=""card-value csharp"">{result.Summary.CSharpAdvantageCategories.Count}</div>
            </div>
        </div>

        <div class=""card"">
            <h2>Metric Results</h2>
            <table class=""metrics-table"">
                <thead>
                    <tr>
                        <th>Category</th>
                        <th>Ratio</th>
                        <th>Winner</th>
                        <th>95% CI</th>
                    </tr>
                </thead>
                <tbody>
{GenerateMetricRows(result)}
                </tbody>
            </table>
        </div>

        <p class=""timestamp"">Generated: {result.Timestamp:yyyy-MM-dd HH:mm:ss UTC}{(result.CommitHash != null ? $" | Commit: {result.CommitHash}" : "")}</p>
    </div>
</body>
</html>";

        await File.WriteAllTextAsync(outputPath, html);
    }

    private static string GenerateMetricRows(EvaluationResult result)
    {
        var rows = new System.Text.StringBuilder();

        // Identify Calor-only categories
        var calorOnlyCategories = result.Metrics
            .Where(m => m.IsCalorOnly)
            .Select(m => m.Category)
            .Distinct()
            .ToHashSet();

        foreach (var (category, advantage) in result.Summary.CategoryAdvantages.OrderByDescending(kv => kv.Value))
        {
            var isCalorOnly = calorOnlyCategories.Contains(category);
            var winner = (advantage > 1.0 || isCalorOnly) ? "Calor" : (advantage < 1.0 ? "C#" : "Tie");
            var winnerClass = (advantage > 1.0 || isCalorOnly) ? "winner-calor" : (advantage < 1.0 ? "winner-csharp" : "");
            var badgeClass = (advantage > 1.0 || isCalorOnly) ? "badge-calor" : "badge-csharp";

            var ciStr = "";
            if (result.Summary.CategoryConfidenceIntervals.TryGetValue(category, out var ci))
            {
                ciStr = $"<span class=\"ci\">[{ci.Lower:F2}, {ci.Upper:F2}]</span>";
            }

            rows.AppendLine($@"                    <tr>
                        <td>{category}</td>
                        <td class=""{winnerClass}"">{advantage:F2}x</td>
                        <td><span class=""badge {badgeClass}"">{winner}</span></td>
                        <td>{ciStr}</td>
                    </tr>");
        }

        return rows.ToString();
    }
}
