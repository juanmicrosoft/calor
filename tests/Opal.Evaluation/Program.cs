using System.CommandLine;
using Opal.Evaluation.Benchmarks;
using Opal.Evaluation.Reports;

namespace Opal.Evaluation;

/// <summary>
/// Entry point for running evaluations from the command line.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("OPAL vs C# Evaluation Framework")
        {
            Description = "Benchmark and compare OPAL and C# code for AI agent effectiveness"
        };

        // Run command
        var runCommand = new Command("run", "Run benchmarks and generate reports");

        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "Output file path for the report",
            getDefaultValue: () => "report.json");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            description: "Output format (json, markdown, both)",
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

        runCommand.AddOption(outputOption);
        runCommand.AddOption(formatOption);
        runCommand.AddOption(categoryOption);
        runCommand.AddOption(verboseOption);
        runCommand.AddOption(manifestOption);

        runCommand.SetHandler(async (output, format, categories, verbose, manifest) =>
        {
            await RunBenchmarksAsync(output, format, categories, verbose, manifest);
        }, outputOption, formatOption, categoryOption, verboseOption, manifestOption);

        rootCommand.AddCommand(runCommand);

        // Quick command for inline testing
        var quickCommand = new Command("quick", "Run a quick test with inline code");

        var opalOption = new Option<string>(
            aliases: new[] { "--opal" },
            description: "OPAL source code or file path")
        { IsRequired = true };

        var csharpOption = new Option<string>(
            aliases: new[] { "--csharp" },
            description: "C# source code or file path")
        { IsRequired = true };

        quickCommand.AddOption(opalOption);
        quickCommand.AddOption(csharpOption);

        quickCommand.SetHandler(async (opal, csharp) =>
        {
            await RunQuickTestAsync(opal, csharp);
        }, opalOption, csharpOption);

        rootCommand.AddCommand(quickCommand);

        // Discover command
        var discoverCommand = new Command("discover", "Discover benchmark files in a directory");

        var dirOption = new Option<string>(
            aliases: new[] { "--dir", "-d" },
            description: "Directory to scan for OPAL/C# pairs")
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
        string? manifestPath)
    {
        Console.WriteLine("OPAL vs C# Evaluation Framework");
        Console.WriteLine("================================");
        Console.WriteLine();

        var options = new BenchmarkRunnerOptions
        {
            Verbose = verbose,
            Categories = categories.ToList()
        };

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

        // Print summary
        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Overall OPAL Advantage: {result.Summary.OverallOpalAdvantage:F2}x");
        Console.WriteLine();
        Console.WriteLine("  Category Advantages:");
        foreach (var (category, advantage) in result.Summary.CategoryAdvantages.OrderByDescending(kv => kv.Value))
        {
            var indicator = advantage > 1.0 ? "+" : (advantage < 1.0 ? "-" : "=");
            Console.WriteLine($"    {indicator} {category}: {advantage:F2}x");
        }
    }

    private static async Task RunQuickTestAsync(string opal, string csharp)
    {
        // Load from files if paths provided
        var opalSource = File.Exists(opal) ? await File.ReadAllTextAsync(opal) : opal;
        var csharpSource = File.Exists(csharp) ? await File.ReadAllTextAsync(csharp) : csharp;

        Console.WriteLine("Quick Evaluation");
        Console.WriteLine("================");
        Console.WriteLine();

        var runner = new BenchmarkRunner(new BenchmarkRunnerOptions { Verbose = true });
        var result = await runner.RunFromSourceAsync(opalSource, csharpSource, "quick-test");

        Console.WriteLine();
        Console.WriteLine("Results:");
        Console.WriteLine($"  OPAL compiles: {result.OpalSuccess}");
        Console.WriteLine($"  C# compiles: {result.CSharpSuccess}");
        Console.WriteLine($"  Average advantage: {result.AverageAdvantage:F2}x");
        Console.WriteLine();
        Console.WriteLine("Metrics:");

        foreach (var metric in result.Metrics)
        {
            var indicator = metric.AdvantageRatio > 1.0 ? "+" : (metric.AdvantageRatio < 1.0 ? "-" : "=");
            Console.WriteLine($"  {indicator} {metric.Category}/{metric.MetricName}: {metric.AdvantageRatio:F2}x (OPAL={metric.OpalScore:F2}, C#={metric.CSharpScore:F2})");
        }
    }

    private static async Task DiscoverBenchmarksAsync(string directory, string output)
    {
        Console.WriteLine($"Discovering benchmarks in: {directory}");

        var manifest = await TestDataAdapter.DiscoverBenchmarksAsync(directory);

        Console.WriteLine($"Found {manifest.Benchmarks.Count} paired OPAL/C# files");

        await manifest.SaveAsync(output);
        Console.WriteLine($"Manifest saved to: {output}");
    }

    private static BenchmarkManifest CreateSampleManifest()
    {
        return new BenchmarkManifest
        {
            Version = "1.0",
            Description = "Sample benchmarks for OPAL vs C# evaluation",
            Benchmarks = new List<BenchmarkEntry>
            {
                new()
                {
                    Id = "001",
                    Name = "HelloWorld",
                    Category = "TokenEconomics",
                    OpalFile = "TokenEconomics/HelloWorld.opal",
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
                    OpalFile = "TokenEconomics/Calculator.opal",
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
                    OpalFile = "TokenEconomics/FizzBuzz.opal",
                    CSharpFile = "TokenEconomics/FizzBuzz.cs",
                    Level = 2,
                    Features = new List<string> { "module", "function", "conditional", "loop" },
                    Notes = "Classic FizzBuzz implementation"
                }
            }
        };
    }
}
