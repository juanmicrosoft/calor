using System.CommandLine;
using System.Text.Json;
using Calor.Evaluation.Benchmarks;
using Calor.Evaluation.Core;
using Calor.Evaluation.LlmTasks;
using Calor.Evaluation.LlmTasks.Caching;
using Calor.Evaluation.LlmTasks.Providers;
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

        // LLM tasks command
        var llmTasksCommand = new Command("llm-tasks", "Run LLM-based task completion benchmarks");

        var providerOption = new Option<string>(
            aliases: new[] { "--provider", "-p" },
            description: "LLM provider to use (claude, mock)",
            getDefaultValue: () => "claude");

        var modelOption = new Option<string?>(
            aliases: new[] { "--model" },
            description: "Specific model to use (e.g., claude-opus-4-5-20251101, claude-sonnet-4-20250514)");

        var budgetOption = new Option<decimal>(
            aliases: new[] { "--budget", "-b" },
            description: "Maximum budget in USD",
            getDefaultValue: () => 5.00m);

        var llmOutputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Output file for results",
            getDefaultValue: () => "llm-results.json");

        var dryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run" },
            description: "Estimate costs without making API calls");

        var refreshCacheOption = new Option<bool>(
            aliases: new[] { "--refresh-cache" },
            description: "Refresh cached responses");

        var tasksOption = new Option<string[]>(
            aliases: new[] { "--tasks", "-t" },
            description: "Specific task IDs to run (comma-separated)")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var llmCategoryOption = new Option<string>(
            aliases: new[] { "--category", "-c" },
            description: "Run only tasks in this category");

        var sampleOption = new Option<int?>(
            aliases: new[] { "--sample", "-s" },
            description: "Number of tasks to sample");

        var llmManifestOption = new Option<string>(
            aliases: new[] { "--manifest", "-m" },
            description: "Path to task manifest file");

        var llmVerboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        llmTasksCommand.AddOption(providerOption);
        llmTasksCommand.AddOption(modelOption);
        llmTasksCommand.AddOption(budgetOption);
        llmTasksCommand.AddOption(llmOutputOption);
        llmTasksCommand.AddOption(dryRunOption);
        llmTasksCommand.AddOption(refreshCacheOption);
        llmTasksCommand.AddOption(tasksOption);
        llmTasksCommand.AddOption(llmCategoryOption);
        llmTasksCommand.AddOption(sampleOption);
        llmTasksCommand.AddOption(llmManifestOption);
        llmTasksCommand.AddOption(llmVerboseOption);

        llmTasksCommand.SetHandler(async (context) =>
        {
            var provider = context.ParseResult.GetValueForOption(providerOption)!;
            var model = context.ParseResult.GetValueForOption(modelOption);
            var budget = context.ParseResult.GetValueForOption(budgetOption);
            var llmOutput = context.ParseResult.GetValueForOption(llmOutputOption)!;
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var refreshCache = context.ParseResult.GetValueForOption(refreshCacheOption);
            var tasks = context.ParseResult.GetValueForOption(tasksOption) ?? Array.Empty<string>();
            var llmCategory = context.ParseResult.GetValueForOption(llmCategoryOption);
            var sample = context.ParseResult.GetValueForOption(sampleOption);
            var llmManifest = context.ParseResult.GetValueForOption(llmManifestOption);
            var llmVerbose = context.ParseResult.GetValueForOption(llmVerboseOption);

            await RunLlmTasksAsync(provider, model, budget, llmOutput, dryRun, refreshCache, tasks, llmCategory, sample, llmManifest, llmVerbose);
        });

        rootCommand.AddCommand(llmTasksCommand);

        // Safety benchmark command
        var safetyBenchmarkCommand = new Command("safety-benchmark", "Run safety benchmarks measuring contract enforcement quality");

        var safetyProviderOption = new Option<string>(
            aliases: new[] { "--provider", "-p" },
            description: "LLM provider to use (claude, mock)",
            getDefaultValue: () => "claude");

        var safetyModelOption = new Option<string?>(
            aliases: new[] { "--model" },
            description: "Specific model to use");

        var safetyBudgetOption = new Option<decimal>(
            aliases: new[] { "--budget", "-b" },
            description: "Maximum budget in USD",
            getDefaultValue: () => 5.00m);

        var safetyOutputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Output file for results",
            getDefaultValue: () => "safety-results.json");

        var safetyDryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run" },
            description: "Estimate costs without making API calls");

        var safetyRefreshCacheOption = new Option<bool>(
            aliases: new[] { "--refresh-cache" },
            description: "Refresh cached responses");

        var safetyTasksOption = new Option<string[]>(
            aliases: new[] { "--tasks", "-t" },
            description: "Specific task IDs to run")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var safetyCategoryOption = new Option<string>(
            aliases: new[] { "--category", "-c" },
            description: "Run only tasks in this category");

        var safetySampleOption = new Option<int?>(
            aliases: new[] { "--sample", "-s" },
            description: "Number of tasks to sample");

        var safetyManifestOption = new Option<string>(
            aliases: new[] { "--manifest", "-m" },
            description: "Path to task manifest file");

        var safetyVerboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        safetyBenchmarkCommand.AddOption(safetyProviderOption);
        safetyBenchmarkCommand.AddOption(safetyModelOption);
        safetyBenchmarkCommand.AddOption(safetyBudgetOption);
        safetyBenchmarkCommand.AddOption(safetyOutputOption);
        safetyBenchmarkCommand.AddOption(safetyDryRunOption);
        safetyBenchmarkCommand.AddOption(safetyRefreshCacheOption);
        safetyBenchmarkCommand.AddOption(safetyTasksOption);
        safetyBenchmarkCommand.AddOption(safetyCategoryOption);
        safetyBenchmarkCommand.AddOption(safetySampleOption);
        safetyBenchmarkCommand.AddOption(safetyManifestOption);
        safetyBenchmarkCommand.AddOption(safetyVerboseOption);

        safetyBenchmarkCommand.SetHandler(async (context) =>
        {
            var provider = context.ParseResult.GetValueForOption(safetyProviderOption)!;
            var model = context.ParseResult.GetValueForOption(safetyModelOption);
            var budget = context.ParseResult.GetValueForOption(safetyBudgetOption);
            var output = context.ParseResult.GetValueForOption(safetyOutputOption)!;
            var dryRun = context.ParseResult.GetValueForOption(safetyDryRunOption);
            var refreshCache = context.ParseResult.GetValueForOption(safetyRefreshCacheOption);
            var tasks = context.ParseResult.GetValueForOption(safetyTasksOption) ?? Array.Empty<string>();
            var category = context.ParseResult.GetValueForOption(safetyCategoryOption);
            var sample = context.ParseResult.GetValueForOption(safetySampleOption);
            var manifest = context.ParseResult.GetValueForOption(safetyManifestOption);
            var verbose = context.ParseResult.GetValueForOption(safetyVerboseOption);

            await RunSafetyBenchmarkAsync(provider, model, budget, output, dryRun, refreshCache, tasks, category, sample, manifest, verbose);
        });

        rootCommand.AddCommand(safetyBenchmarkCommand);

        // Effect discipline benchmark command
        var effectDisciplineCommand = new Command("effect-discipline", "Run effect discipline benchmarks measuring side effect management");

        var effectProviderOption = new Option<string>(
            aliases: new[] { "--provider", "-p" },
            description: "LLM provider to use (claude, mock)",
            getDefaultValue: () => "claude");

        var effectModelOption = new Option<string?>(
            aliases: new[] { "--model" },
            description: "Specific model to use");

        var effectBudgetOption = new Option<decimal>(
            aliases: new[] { "--budget", "-b" },
            description: "Maximum budget in USD",
            getDefaultValue: () => 5.00m);

        var effectOutputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Output file for results",
            getDefaultValue: () => "effect-discipline-results.json");

        var effectDryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run" },
            description: "Estimate costs without making API calls");

        var effectRefreshCacheOption = new Option<bool>(
            aliases: new[] { "--refresh-cache" },
            description: "Refresh cached responses");

        var effectTasksOption = new Option<string[]>(
            aliases: new[] { "--tasks", "-t" },
            description: "Specific task IDs to run")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var effectCategoryOption = new Option<string>(
            aliases: new[] { "--category", "-c" },
            description: "Run only tasks in this category (flaky-test-prevention, security-boundaries, side-effect-transparency, cache-safety)");

        var effectSampleOption = new Option<int?>(
            aliases: new[] { "--sample", "-s" },
            description: "Number of tasks to sample");

        var effectManifestOption = new Option<string>(
            aliases: new[] { "--manifest", "-m" },
            description: "Path to task manifest file");

        var effectVerboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        var effectEnableAnalyzersOption = new Option<bool>(
            aliases: new[] { "--enable-analyzers", "-a" },
            description: "Enable Roslyn analyzers for C# code evaluation (ED001-ED007)");

        effectDisciplineCommand.AddOption(effectProviderOption);
        effectDisciplineCommand.AddOption(effectModelOption);
        effectDisciplineCommand.AddOption(effectBudgetOption);
        effectDisciplineCommand.AddOption(effectOutputOption);
        effectDisciplineCommand.AddOption(effectDryRunOption);
        effectDisciplineCommand.AddOption(effectRefreshCacheOption);
        effectDisciplineCommand.AddOption(effectTasksOption);
        effectDisciplineCommand.AddOption(effectCategoryOption);
        effectDisciplineCommand.AddOption(effectSampleOption);
        effectDisciplineCommand.AddOption(effectManifestOption);
        effectDisciplineCommand.AddOption(effectVerboseOption);
        effectDisciplineCommand.AddOption(effectEnableAnalyzersOption);

        effectDisciplineCommand.SetHandler(async (context) =>
        {
            var provider = context.ParseResult.GetValueForOption(effectProviderOption)!;
            var model = context.ParseResult.GetValueForOption(effectModelOption);
            var budget = context.ParseResult.GetValueForOption(effectBudgetOption);
            var output = context.ParseResult.GetValueForOption(effectOutputOption)!;
            var dryRun = context.ParseResult.GetValueForOption(effectDryRunOption);
            var refreshCache = context.ParseResult.GetValueForOption(effectRefreshCacheOption);
            var tasks = context.ParseResult.GetValueForOption(effectTasksOption) ?? Array.Empty<string>();
            var category = context.ParseResult.GetValueForOption(effectCategoryOption);
            var sample = context.ParseResult.GetValueForOption(effectSampleOption);
            var manifest = context.ParseResult.GetValueForOption(effectManifestOption);
            var verbose = context.ParseResult.GetValueForOption(effectVerboseOption);
            var enableAnalyzers = context.ParseResult.GetValueForOption(effectEnableAnalyzersOption);

            await RunEffectDisciplineAsync(provider, model, budget, output, dryRun, refreshCache, tasks, category, sample, manifest, verbose, enableAnalyzers);
        });

        rootCommand.AddCommand(effectDisciplineCommand);

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

    private static async Task RunLlmTasksAsync(
        string provider,
        string? model,
        decimal budget,
        string output,
        bool dryRun,
        bool refreshCache,
        string[] tasks,
        string? category,
        int? sample,
        string? manifestPath,
        bool verbose)
    {
        Console.WriteLine("LLM Task Completion Benchmark");
        Console.WriteLine("=============================");
        Console.WriteLine();

        // Load manifest
        LlmTaskManifest manifest;
        if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
        {
            Console.WriteLine($"Loading manifest: {manifestPath}");
            manifest = await LlmTaskManifest.LoadAsync(manifestPath);
        }
        else
        {
            // Try default location
            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tasks", "task-manifest.json");
            if (!File.Exists(defaultPath))
            {
                // Try relative to working directory
                defaultPath = Path.Combine("Tasks", "task-manifest.json");
            }

            if (File.Exists(defaultPath))
            {
                Console.WriteLine($"Loading default manifest: {defaultPath}");
                manifest = await LlmTaskManifest.LoadAsync(defaultPath);
            }
            else
            {
                Console.Error.WriteLine("No task manifest found. Use --manifest to specify a path.");
                Console.Error.WriteLine("Expected locations:");
                Console.Error.WriteLine($"  - {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tasks", "task-manifest.json")}");
                Console.Error.WriteLine($"  - {Path.Combine(Directory.GetCurrentDirectory(), "Tasks", "task-manifest.json")}");
                return;
            }
        }

        Console.WriteLine($"Loaded {manifest.Tasks.Count} task definitions");
        Console.WriteLine();

        // Create provider
        ILlmProvider llmProvider;
        switch (provider.ToLowerInvariant())
        {
            case "claude":
                var claudeProvider = new ClaudeProvider();
                if (!claudeProvider.IsAvailable)
                {
                    Console.Error.WriteLine($"Claude provider unavailable: {claudeProvider.UnavailabilityReason}");
                    Console.Error.WriteLine("Set the ANTHROPIC_API_KEY environment variable or use --provider mock");
                    return;
                }
                llmProvider = claudeProvider;
                break;

            case "mock":
                llmProvider = MockProvider.WithWorkingImplementations();
                Console.WriteLine("Using mock provider (no actual API calls)");
                break;

            default:
                Console.Error.WriteLine($"Unknown provider: {provider}");
                Console.Error.WriteLine("Available providers: claude, mock");
                return;
        }

        // Create runner options
        var options = new LlmTaskRunnerOptions
        {
            BudgetLimit = budget,
            UseCache = true,
            RefreshCache = refreshCache,
            DryRun = dryRun,
            Verbose = verbose,
            TaskFilter = tasks.Length > 0 ? tasks.ToList() : null,
            CategoryFilter = category,
            SampleSize = sample,
            Model = model
        };

        // Create cache
        var cache = new LlmResponseCache();
        var cacheStats = cache.GetStatistics();
        Console.WriteLine($"Cache: {cacheStats.EntryCount} entries ({cacheStats.TotalSizeFormatted})");
        Console.WriteLine();

        // Create runner
        using var runner = new LlmTaskRunner(llmProvider, cache);

        // Estimate cost first
        var estimate = runner.EstimateCost(manifest, options);

        Console.WriteLine("Cost Estimate:");
        Console.WriteLine($"  Tasks to run: {estimate.TaskCount}");
        Console.WriteLine($"  Cached responses: {estimate.CachedResponses}");
        Console.WriteLine($"  Estimated cost: ${estimate.EstimatedCost:F4}");
        Console.WriteLine($"  Cost with cache: ${estimate.CostWithCache:F4}");
        Console.WriteLine($"  Budget: ${estimate.BudgetLimit:F2}");

        if (estimate.ExceedsBudget)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: Estimated cost exceeds budget!");
            Console.ResetColor();
        }

        Console.WriteLine();

        if (dryRun)
        {
            Console.WriteLine("Dry run - no API calls made");
            Console.WriteLine();
            Console.WriteLine("Category breakdown:");
            foreach (var (cat, cost) in estimate.ByCategory.OrderByDescending(kv => kv.Value))
            {
                Console.WriteLine($"  {cat}: ${cost:F4}");
            }
            return;
        }

        // Run tasks
        Console.WriteLine("Running tasks...");
        Console.WriteLine();

        var results = await runner.RunAllAsync(manifest, options);

        // Save results
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(results, jsonOptions);
        await File.WriteAllTextAsync(output, json);
        Console.WriteLine($"Results saved to: {output}");
        Console.WriteLine();

        // Print summary
        var summary = results.Summary;

        Console.WriteLine("Results Summary");
        Console.WriteLine("---------------");
        Console.WriteLine($"Total tasks:           {summary.TotalTasks}");
        Console.WriteLine($"Calor wins:            {summary.CalorWins}");
        Console.WriteLine($"C# wins:               {summary.CSharpWins}");
        Console.WriteLine($"Ties:                  {summary.Ties}");
        Console.WriteLine();

        Console.WriteLine("Scores:");
        Console.WriteLine($"  Average Calor score:   {summary.AverageCalorScore:F3}");
        Console.WriteLine($"  Average C# score:      {summary.AverageCSharpScore:F3}");
        Console.WriteLine($"  Advantage ratio:       {summary.OverallAdvantageRatio:F2}x");
        Console.WriteLine();

        Console.WriteLine("Compilation Rates:");
        Console.WriteLine($"  Calor:                 {summary.CalorCompilationRate:P1}");
        Console.WriteLine($"  C#:                    {summary.CSharpCompilationRate:P1}");
        Console.WriteLine();

        Console.WriteLine("Test Pass Rates:");
        Console.WriteLine($"  Calor:                 {summary.CalorTestPassRate:P1}");
        Console.WriteLine($"  C#:                    {summary.CSharpTestPassRate:P1}");
        Console.WriteLine();

        Console.WriteLine($"Total cost:            ${results.TotalCost:F4}");
        Console.WriteLine($"Remaining budget:      ${runner.RemainingBudget:F2}");
        Console.WriteLine();

        if (summary.ByCategory.Count > 0)
        {
            Console.WriteLine("By Category:");
            foreach (var (cat, catSummary) in summary.ByCategory.OrderByDescending(kv => kv.Value.AdvantageRatio))
            {
                var indicator = catSummary.AdvantageRatio > 1.0 ? "+" : (catSummary.AdvantageRatio < 1.0 ? "-" : "=");
                Console.WriteLine($"  {indicator} {cat}: {catSummary.AdvantageRatio:F2}x (Calor={catSummary.AverageCalorScore:F2}, C#={catSummary.AverageCSharpScore:F2})");
            }
        }

        // Highlight the winner
        Console.WriteLine();
        if (summary.OverallAdvantageRatio > 1.05)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Calor shows {(summary.OverallAdvantageRatio - 1) * 100:F1}% advantage in LLM task completion!");
        }
        else if (summary.OverallAdvantageRatio < 0.95)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"C# shows {(1 - summary.OverallAdvantageRatio) * 100:F1}% advantage in LLM task completion.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Results are roughly equivalent between Calor and C#.");
        }
        Console.ResetColor();
    }

    private static async Task RunSafetyBenchmarkAsync(
        string provider,
        string? model,
        decimal budget,
        string output,
        bool dryRun,
        bool refreshCache,
        string[] tasks,
        string? category,
        int? sample,
        string? manifestPath,
        bool verbose)
    {
        Console.WriteLine("Safety Benchmark - Contract Enforcement Quality");
        Console.WriteLine("================================================");
        Console.WriteLine();
        Console.WriteLine("This benchmark measures how well contracts catch bugs");
        Console.WriteLine("and the quality of error messages when violations occur.");
        Console.WriteLine();

        // Load manifest
        LlmTaskManifest manifest;
        if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
        {
            Console.WriteLine($"Loading manifest: {manifestPath}");
            manifest = await LlmTaskManifest.LoadAsync(manifestPath);
        }
        else
        {
            // Try default safety manifest location
            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tasks", "task-manifest-safety.json");
            if (!File.Exists(defaultPath))
            {
                defaultPath = Path.Combine("Tasks", "task-manifest-safety.json");
            }
            if (!File.Exists(defaultPath))
            {
                defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "Calor.Evaluation", "Tasks", "task-manifest-safety.json");
            }

            if (File.Exists(defaultPath))
            {
                Console.WriteLine($"Loading default safety manifest: {defaultPath}");
                manifest = await LlmTaskManifest.LoadAsync(defaultPath);
            }
            else
            {
                Console.Error.WriteLine("No safety task manifest found. Use --manifest to specify a path.");
                Console.Error.WriteLine("Expected: tests/Calor.Evaluation/Tasks/task-manifest-safety.json");
                return;
            }
        }

        Console.WriteLine($"Loaded {manifest.Tasks.Count} safety task definitions");
        Console.WriteLine();

        // Create provider
        ILlmProvider llmProvider;
        switch (provider.ToLowerInvariant())
        {
            case "claude":
                var claudeProvider = new ClaudeProvider();
                if (!claudeProvider.IsAvailable)
                {
                    Console.Error.WriteLine($"Claude provider unavailable: {claudeProvider.UnavailabilityReason}");
                    Console.Error.WriteLine("Set the ANTHROPIC_API_KEY environment variable or use --provider mock");
                    return;
                }
                llmProvider = claudeProvider;
                break;

            case "mock":
                llmProvider = MockProvider.WithWorkingImplementations();
                Console.WriteLine("Using mock provider (no actual API calls)");
                break;

            default:
                Console.Error.WriteLine($"Unknown provider: {provider}");
                Console.Error.WriteLine("Available providers: claude, mock");
                return;
        }

        // Create runner options
        var options = new SafetyBenchmarkOptions
        {
            BudgetLimit = budget,
            UseCache = true,
            RefreshCache = refreshCache,
            DryRun = dryRun,
            Verbose = verbose,
            TaskFilter = tasks.Length > 0 ? tasks.ToList() : null,
            CategoryFilter = category,
            SampleSize = sample,
            Model = model
        };

        // Create cache
        var cache = new LlmResponseCache();
        var cacheStats = cache.GetStatistics();
        Console.WriteLine($"Cache: {cacheStats.EntryCount} entries ({cacheStats.TotalSizeFormatted})");
        Console.WriteLine();

        // Create runner
        using var runner = new SafetyBenchmarkRunner(llmProvider, cache);

        if (dryRun)
        {
            Console.WriteLine("Dry run - no API calls made");
            return;
        }

        // Run tasks
        Console.WriteLine("Running safety benchmark tasks...");
        Console.WriteLine();

        var results = await runner.RunAllAsync(manifest, options);

        // Save results
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(results, jsonOptions);
        await File.WriteAllTextAsync(output, json);
        Console.WriteLine($"Results saved to: {output}");
        Console.WriteLine();

        // Print summary
        var summary = results.Summary;

        Console.WriteLine("Safety Benchmark Results");
        Console.WriteLine("------------------------");
        Console.WriteLine($"Total tasks:              {summary.TotalTasks}");
        Console.WriteLine($"Calor wins:               {summary.CalorWins}");
        Console.WriteLine($"C# wins:                  {summary.CSharpWins}");
        Console.WriteLine($"Ties:                     {summary.Ties}");
        Console.WriteLine();

        Console.WriteLine("Safety Scores:");
        Console.WriteLine($"  Calor:                  {summary.AverageCalorSafetyScore:F3}");
        Console.WriteLine($"  C#:                     {summary.AverageCSharpSafetyScore:F3}");
        Console.WriteLine($"  Advantage ratio:        {summary.SafetyAdvantageRatio:F2}x");
        Console.WriteLine();

        Console.WriteLine("Violation Detection Rate:");
        Console.WriteLine($"  Calor:                  {summary.CalorViolationDetectionRate:P1}");
        Console.WriteLine($"  C#:                     {summary.CSharpViolationDetectionRate:P1}");
        Console.WriteLine();

        Console.WriteLine("Error Quality (0-1 scale):");
        Console.WriteLine($"  Calor:                  {summary.CalorAverageErrorQuality:F2}");
        Console.WriteLine($"  C#:                     {summary.CSharpAverageErrorQuality:F2}");
        Console.WriteLine();

        Console.WriteLine("Normal Test Correctness:");
        Console.WriteLine($"  Calor:                  {summary.CalorNormalCorrectness:P1}");
        Console.WriteLine($"  C#:                     {summary.CSharpNormalCorrectness:P1}");
        Console.WriteLine();

        Console.WriteLine($"Total cost:               ${results.TotalCost:F4}");
        Console.WriteLine($"Remaining budget:         ${runner.RemainingBudget:F2}");
        Console.WriteLine();

        if (summary.ByCategory.Count > 0)
        {
            Console.WriteLine("By Category:");
            foreach (var (cat, catSummary) in summary.ByCategory.OrderByDescending(kv => kv.Value.SafetyAdvantageRatio))
            {
                var indicator = catSummary.SafetyAdvantageRatio > 1.05 ? "+" : (catSummary.SafetyAdvantageRatio < 0.95 ? "-" : "=");
                Console.WriteLine($"  {indicator} {cat}:");
                Console.WriteLine($"      Safety: {catSummary.SafetyAdvantageRatio:F2}x (Calor={catSummary.AverageCalorSafetyScore:F2}, C#={catSummary.AverageCSharpSafetyScore:F2})");
                Console.WriteLine($"      Detection: Calor={catSummary.CalorViolationDetectionRate:P0}, C#={catSummary.CSharpViolationDetectionRate:P0}");
                Console.WriteLine($"      Error Quality: Calor={catSummary.CalorErrorQuality:F2}, C#={catSummary.CSharpErrorQuality:F2}");
            }
        }

        // Highlight the winner
        Console.WriteLine();
        if (summary.SafetyAdvantageRatio > 1.10)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Calor demonstrates {(summary.SafetyAdvantageRatio - 1) * 100:F1}% safety advantage!");
            Console.WriteLine("Contracts catch more bugs and provide better error messages.");
        }
        else if (summary.SafetyAdvantageRatio < 0.90)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"C# shows {(1 - summary.SafetyAdvantageRatio) * 100:F1}% safety advantage.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Safety results are roughly equivalent between Calor and C#.");
        }
        Console.ResetColor();
    }

    private static async Task RunEffectDisciplineAsync(
        string provider,
        string? model,
        decimal budget,
        string output,
        bool dryRun,
        bool refreshCache,
        string[] tasks,
        string? category,
        int? sample,
        string? manifestPath,
        bool verbose,
        bool enableAnalyzers)
    {
        Console.WriteLine("Effect Discipline Benchmark - Side Effect Management");
        Console.WriteLine("=====================================================");
        Console.WriteLine();
        Console.WriteLine("This benchmark measures how well code prevents real-world bugs:");
        Console.WriteLine("  - Flaky tests (non-determinism from time/random)");
        Console.WriteLine("  - Security boundary violations (unauthorized I/O)");
        Console.WriteLine("  - Hidden side effects (logging, telemetry)");
        Console.WriteLine("  - Cache safety issues (non-pure memoization)");
        Console.WriteLine();

        // Load manifest
        LlmTaskManifest manifest;
        if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
        {
            Console.WriteLine($"Loading manifest: {manifestPath}");
            manifest = await LlmTaskManifest.LoadAsync(manifestPath);
        }
        else
        {
            // Try default effect discipline manifest location
            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tasks", "task-manifest-effects.json");
            if (!File.Exists(defaultPath))
            {
                defaultPath = Path.Combine("Tasks", "task-manifest-effects.json");
            }
            if (!File.Exists(defaultPath))
            {
                defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "Calor.Evaluation", "Tasks", "task-manifest-effects.json");
            }

            if (File.Exists(defaultPath))
            {
                Console.WriteLine($"Loading default effect discipline manifest: {defaultPath}");
                manifest = await LlmTaskManifest.LoadAsync(defaultPath);
            }
            else
            {
                Console.Error.WriteLine("No effect discipline task manifest found. Use --manifest to specify a path.");
                Console.Error.WriteLine("Expected: tests/Calor.Evaluation/Tasks/task-manifest-effects.json");
                return;
            }
        }

        Console.WriteLine($"Loaded {manifest.Tasks.Count} effect discipline tasks");
        Console.WriteLine();

        // Create provider
        ILlmProvider llmProvider;
        switch (provider.ToLowerInvariant())
        {
            case "claude":
                var claudeProvider = new ClaudeProvider();
                if (!claudeProvider.IsAvailable)
                {
                    Console.Error.WriteLine($"Claude provider unavailable: {claudeProvider.UnavailabilityReason}");
                    Console.Error.WriteLine("Set the ANTHROPIC_API_KEY environment variable or use --provider mock");
                    return;
                }
                llmProvider = claudeProvider;
                break;

            case "mock":
                llmProvider = MockProvider.WithWorkingImplementations();
                Console.WriteLine("Using mock provider (no actual API calls)");
                break;

            default:
                Console.Error.WriteLine($"Unknown provider: {provider}");
                Console.Error.WriteLine("Available providers: claude, mock");
                return;
        }

        // Create runner options
        var options = new EffectDisciplineOptions
        {
            BudgetLimit = budget,
            UseCache = true,
            RefreshCache = refreshCache,
            DryRun = dryRun,
            Verbose = verbose,
            TaskFilter = tasks.Length > 0 ? tasks.ToList() : null,
            CategoryFilter = category,
            SampleSize = sample,
            Model = model,
            EnableAnalyzers = enableAnalyzers
        };

        if (enableAnalyzers)
        {
            Console.WriteLine("Roslyn analyzers enabled (ED001-ED007)");
        }

        // Create cache
        var cache = new LlmResponseCache();
        var cacheStats = cache.GetStatistics();
        Console.WriteLine($"Cache: {cacheStats.EntryCount} entries ({cacheStats.TotalSizeFormatted})");
        Console.WriteLine();

        // Create runner
        using var runner = new EffectDisciplineBenchmarkRunner(llmProvider, cache);

        if (dryRun)
        {
            Console.WriteLine("Dry run - no API calls made");
            return;
        }

        // Run tasks
        Console.WriteLine("Running effect discipline benchmark tasks...");
        Console.WriteLine();

        var results = await runner.RunAllAsync(manifest, options);

        // Save results
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(results, jsonOptions);
        await File.WriteAllTextAsync(output, json);
        Console.WriteLine($"Results saved to: {output}");
        Console.WriteLine();

        // Print summary
        var summary = results.Summary;

        Console.WriteLine("Effect Discipline Benchmark Results");
        Console.WriteLine("------------------------------------");
        Console.WriteLine($"Total tasks:              {summary.TotalTasks}");
        Console.WriteLine($"Calor wins:               {summary.CalorWins}");
        Console.WriteLine($"C# wins:                  {summary.CSharpWins}");
        Console.WriteLine($"Ties:                     {summary.Ties}");
        Console.WriteLine();

        Console.WriteLine("Discipline Scores:");
        Console.WriteLine($"  Calor:                  {summary.AverageCalorDisciplineScore:F3}");
        Console.WriteLine($"  C#:                     {summary.AverageCSharpDisciplineScore:F3}");
        Console.WriteLine($"  Advantage ratio:        {summary.DisciplineAdvantageRatio:F2}x");
        Console.WriteLine();

        Console.WriteLine("Bug Prevention Rate:");
        Console.WriteLine($"  Calor:                  {summary.CalorBugPreventionRate:P1}");
        Console.WriteLine($"  C#:                     {summary.CSharpBugPreventionRate:P1}");
        Console.WriteLine();

        Console.WriteLine("Functional Correctness:");
        Console.WriteLine($"  Calor:                  {summary.CalorCorrectnessRate:P1}");
        Console.WriteLine($"  C#:                     {summary.CSharpCorrectnessRate:P1}");
        Console.WriteLine();

        Console.WriteLine($"Total cost:               ${results.TotalCost:F4}");
        Console.WriteLine($"Remaining budget:         ${runner.RemainingBudget:F2}");
        Console.WriteLine();

        if (summary.ByCategory.Count > 0)
        {
            Console.WriteLine("By Category:");
            foreach (var (cat, catSummary) in summary.ByCategory.OrderByDescending(kv => kv.Value.DisciplineAdvantageRatio))
            {
                var indicator = catSummary.DisciplineAdvantageRatio > 1.05 ? "+" : (catSummary.DisciplineAdvantageRatio < 0.95 ? "-" : "=");
                Console.WriteLine($"  {indicator} {cat}:");
                Console.WriteLine($"      Discipline: {catSummary.DisciplineAdvantageRatio:F2}x (Calor={catSummary.AverageCalorDisciplineScore:F2}, C#={catSummary.AverageCSharpDisciplineScore:F2})");
                Console.WriteLine($"      Bug Prevention: Calor={catSummary.CalorBugPreventionRate:P0}, C#={catSummary.CSharpBugPreventionRate:P0}");
                Console.WriteLine($"      Correctness: Calor={catSummary.CalorCorrectnessRate:P0}, C#={catSummary.CSharpCorrectnessRate:P0}");
            }
        }

        // Highlight the winner
        Console.WriteLine();
        if (summary.DisciplineAdvantageRatio > 1.10)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Calor demonstrates {(summary.DisciplineAdvantageRatio - 1) * 100:F1}% effect discipline advantage!");
            Console.WriteLine("Effect system prevents more real-world bugs at compile time.");
        }
        else if (summary.DisciplineAdvantageRatio < 0.90)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"C# shows {(1 - summary.DisciplineAdvantageRatio) * 100:F1}% effect discipline advantage.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Effect discipline results are roughly equivalent between Calor and C#.");
        }
        Console.ResetColor();
    }
}
