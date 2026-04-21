using System.CommandLine;
using System.Diagnostics;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Init;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for single-file conversion.
/// </summary>
public static class ConvertCommand
{
    public static Command Create()
    {
        var inputArgument = new Argument<FileInfo>(
            name: "input",
            description: "The source file to convert (.cs or .calr)");

        var outputOption = new Option<FileInfo?>(
            aliases: new[] { "--output", "-o" },
            description: "The output file path (auto-detected if not specified)");

        var benchmarkOption = new Option<bool>(
            aliases: new[] { "--benchmark", "-b" },
            description: "Include benchmark metrics comparison");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        var explainOption = new Option<bool>(
            aliases: new[] { "--explain", "-e" },
            description: "Show detailed explanation of unsupported features");

        var noFallbackOption = new Option<bool>(
            aliases: new[] { "--no-fallback" },
            description: "Fail conversion when encountering unsupported constructs (instead of emitting TODO comments)");

        var validateOption = new Option<bool>(
            aliases: new[] { "--validate" },
            description: "Parse the generated Calor output and report any errors before writing");

        var timeoutOption = new Option<int>(
            aliases: new[] { "--timeout", "-t" },
            description: "Timeout in seconds for the conversion (0 = no timeout)",
            getDefaultValue: () => 0);

        var command = new Command("convert", "Convert a single file between C# and Calor")
        {
            inputArgument,
            outputOption,
            benchmarkOption,
            verboseOption,
            explainOption,
            noFallbackOption,
            validateOption,
            timeoutOption
        };

        command.SetHandler(ExecuteAsync, inputArgument, outputOption, benchmarkOption, verboseOption, explainOption, noFallbackOption, validateOption, timeoutOption);

        return command;
    }

    private static async Task ExecuteAsync(FileInfo input, FileInfo? output, bool benchmark, bool verbose, bool explain, bool noFallback, bool validate, int timeoutSeconds)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("convert");
        if (telemetry != null)
        {
            var discovered = CalorConfigManager.Discover(input.FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();
        if (!input.Exists)
        {
            Console.Error.WriteLine($"Error: Input file not found: {input.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        var direction = CSharpToCalorConverter.DetectDirection(input.FullName);

        if (direction == ConversionDirection.Unknown)
        {
            Console.Error.WriteLine($"Error: Unknown file type. Expected .cs or .calr extension.");
            Environment.ExitCode = 1;
            return;
        }

        var outputPath = output?.FullName ?? GetDefaultOutputPath(input.FullName, direction);

        if (verbose)
        {
            Console.WriteLine($"Converting: {input.Name}");
            Console.WriteLine($"Direction: {(direction == ConversionDirection.CSharpToCalor ? "C# → Calor" : "Calor → C#")}");
        }

        try
        {
            ConversionResult? conversionResult = null;
            if (direction == ConversionDirection.CSharpToCalor)
            {
                conversionResult = await ConvertCSharpToCalorAsync(input.FullName, outputPath, benchmark, verbose, explain, noFallback, validate, timeoutSeconds);
            }
            else
            {
                await ConvertCalorToCSharpAsync(input.FullName, outputPath, verbose);
            }

            if (conversionResult != null && telemetry != null)
            {
                var explanation = conversionResult.Context.GetExplanation();

                // Track conversion attempt metrics (Phase 4)
                try
                {
                    var inputSource = await File.ReadAllTextAsync(input.FullName);
                    var inputLines = inputSource.Split('\n').Length;
                    telemetry.TrackConversionAttempted(
                        inputLines,
                        conversionResult.Success,
                        sw.ElapsedMilliseconds,
                        conversionResult.Issues.Count,
                        explanation.TotalUnsupportedCount);

                    // Track individual conversion gaps
                    foreach (var issue in conversionResult.Issues.Where(i => i.Feature != null))
                    {
                        telemetry.TrackConversionGap(issue.Feature!, issue.Line);
                    }
                }
                catch
                {
                    // Never crash the CLI
                }

                if (explanation.TotalUnsupportedCount > 0)
                {
                    telemetry.TrackUnsupportedFeatures(
                        explanation.GetFeatureCounts(),
                        explanation.TotalUnsupportedCount);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            telemetry?.TrackException(ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            sw.Stop();
            telemetry?.TrackCommand("convert", Environment.ExitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            });
            if (Environment.ExitCode != 0)
            {
                IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "convert", "Conversion failed");
            }
        }
    }

    private static async Task<ConversionResult?> ConvertCSharpToCalorAsync(string inputPath, string outputPath, bool benchmark, bool verbose, bool explain, bool noFallback, bool validate, int timeoutSeconds)
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Verbose = verbose,
            IncludeBenchmark = benchmark,
            Explain = explain,
            GracefulFallback = !noFallback
        });

        ConversionResult result;
        if (timeoutSeconds > 0)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                // Run on a thread pool thread so the cancellation token can
                // interrupt even CPU-bound conversion hangs (e.g. infinite loops
                // in preprocessor region extraction).
                result = await Task.Run(() => converter.ConvertFileAsync(inputPath), cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"Error: Conversion timed out after {timeoutSeconds}s");
                Environment.ExitCode = 1;
                return null;
            }
        }
        else
        {
            result = await converter.ConvertFileAsync(inputPath);
        }

        if (!result.Success)
        {
            Console.Error.WriteLine("Conversion failed:");
            foreach (var issue in result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error))
            {
                Console.Error.WriteLine($"  {issue}");
            }
            Environment.ExitCode = 1;
            return result;
        }

        // Write output (use replacement fallback for files containing unpairable surrogates)
        var writeEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        await File.WriteAllTextAsync(outputPath, result.CalorSource, writeEncoding);

        // Validate generated Calor by parsing it
        if (validate && result.CalorSource != null)
        {
            var validationErrors = ValidateCalorSource(result.CalorSource);
            if (validationErrors.Count > 0)
            {
                Console.Error.WriteLine($"⚠ Validation failed ({validationErrors.Count} error{(validationErrors.Count == 1 ? "" : "s")}):");
                foreach (var err in validationErrors.Take(5))
                    Console.Error.WriteLine($"  {err}");
                if (validationErrors.Count > 5)
                    Console.Error.WriteLine($"  ... and {validationErrors.Count - 5} more");
                Console.Error.WriteLine("Output written but may not compile. Use 'calor --input <file>' to compile, or calor_compile MCP tool (autoFix on by default).");
            }
        }

        Console.WriteLine($"✓ Conversion successful");

        // Show warnings
        var warnings = result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Warning).ToList();
        if (warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Warnings ({warnings.Count}):");
            foreach (var warning in warnings.Take(5))
            {
                Console.WriteLine($"  ⚠ {warning.Message}");
            }
            if (warnings.Count > 5)
            {
                Console.WriteLine($"  ... and {warnings.Count - 5} more");
            }
        }

        // Show explanation if requested
        if (explain)
        {
            Console.WriteLine();
            var explanation = result.Context.GetExplanation();
            Console.WriteLine(explanation.FormatForCli());
        }

        // Show benchmark if requested
        if (benchmark && result.CalorSource != null)
        {
            var originalSource = await File.ReadAllTextAsync(inputPath);
            var metrics = BenchmarkIntegration.CalculateMetrics(originalSource, result.CalorSource);

            Console.WriteLine();
            Console.WriteLine(BenchmarkIntegration.FormatComparison(metrics));
        }

        Console.WriteLine();
        Console.WriteLine($"Output: {outputPath}");

        return result;
    }

    private static async Task ConvertCalorToCSharpAsync(string inputPath, string outputPath, bool verbose)
    {
        var source = await File.ReadAllTextAsync(inputPath);
        var result = Program.Compile(source, inputPath, verbose);

        if (result.HasErrors)
        {
            Console.Error.WriteLine("Compilation failed:");
            foreach (var diag in result.Diagnostics.Errors)
            {
                Console.Error.WriteLine($"  {diag}");
            }
            Environment.ExitCode = 1;
            return;
        }

        await File.WriteAllTextAsync(outputPath, result.GeneratedCode);

        Console.WriteLine($"✓ Conversion successful");
        Console.WriteLine($"Output: {outputPath}");
    }

    private static string GetDefaultOutputPath(string inputPath, ConversionDirection direction)
    {
        return direction == ConversionDirection.CSharpToCalor
            ? Path.ChangeExtension(inputPath, ".calr")
            : Path.ChangeExtension(inputPath, ".g.cs");
    }

    /// <summary>
    /// Validates Calor source by lexing and parsing it. Returns a list of error diagnostics.
    /// </summary>
    internal static List<Diagnostic> ValidateCalorSource(string calorSource)
    {
        var diagBag = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagBag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagBag);
        parser.Parse();
        return diagBag.Where(d => d.IsError).ToList();
    }
}
