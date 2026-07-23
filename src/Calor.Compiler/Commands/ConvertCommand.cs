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

        var passthroughOption = new Option<bool>(
            aliases: new[] { "--passthrough" },
            description: "Preserve unconvertible members as §CSHARP interop blocks so the output always parses (C# → Calor). If a member's emitted Calor would be invalid, its original C# is kept verbatim instead of writing broken output.");

        var timeoutOption = new Option<int>(
            aliases: new[] { "--timeout", "-t" },
            description: "Timeout in seconds for the conversion (0 = no timeout)",
            getDefaultValue: () => 0);

        var explicitCallClosersOption = new Option<bool>(
            aliases: new[] { "--explicit-call-closers" },
            description: "Emit explicit §/C for every §C call (v0.6.0-compatible output); disables zero-arg §/C elision");

        // No -f short alias (consistent with lint, where -f means --fix).
        var formatOption = new Option<string>(
            aliases: new[] { "--format" },
            getDefaultValue: () => "text",
            description: "Output format: text (human-readable) or json (envelope document on stdout). No short alias.");
        formatOption.FromAmong("text", "json");

        var command = new Command("convert", "Convert a single file between C# and Calor")
        {
            inputArgument,
            outputOption,
            benchmarkOption,
            verboseOption,
            explainOption,
            noFallbackOption,
            validateOption,
            passthroughOption,
            timeoutOption,
            explicitCallClosersOption,
            formatOption
        };

        command.SetHandler(async ctx =>
        {
            var input = ctx.ParseResult.GetValueForArgument(inputArgument);
            var output = ctx.ParseResult.GetValueForOption(outputOption);
            var benchmark = ctx.ParseResult.GetValueForOption(benchmarkOption);
            var verbose = ctx.ParseResult.GetValueForOption(verboseOption);
            var explain = ctx.ParseResult.GetValueForOption(explainOption);
            var noFallback = ctx.ParseResult.GetValueForOption(noFallbackOption);
            var validate = ctx.ParseResult.GetValueForOption(validateOption);
            var passthrough = ctx.ParseResult.GetValueForOption(passthroughOption);
            var timeoutSeconds = ctx.ParseResult.GetValueForOption(timeoutOption);
            var explicitCallClosers = ctx.ParseResult.GetValueForOption(explicitCallClosersOption);
            var format = ctx.ParseResult.GetValueForOption(formatOption) ?? "text";
            await ExecuteAsync(input, output, benchmark, verbose, explain, noFallback, validate, passthrough, timeoutSeconds, explicitCallClosers, format);
        });

        return command;
    }

    private static async Task ExecuteAsync(FileInfo input, FileInfo? output, bool benchmark, bool verbose, bool explain, bool noFallback, bool validate, bool passthrough, int timeoutSeconds, bool explicitCallClosers, string format)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("convert");
        if (telemetry != null)
        {
            var discovered = CalorConfigManager.Discover(input.FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();

        // Envelope output (--format json, schema v1.1, loop plan D1.3): stdout
        // carries exactly one envelope document on every path (success, failure,
        // timeout, crash — mirroring Program.CompileCore's Finish pattern); ALL
        // human-oriented output moves to stderr.
        var json = format.Equals("json", StringComparison.OrdinalIgnoreCase);
        var envelope = json ? new ConvertEnvelope() : null;
        envelope?.SetInput(input.FullName);

        if (!input.Exists)
        {
            envelope?.AddCommandError($"Input file not found: {input.FullName}", input.FullName);
            Console.Error.WriteLine($"Error: Input file not found: {input.FullName}");
            envelope?.Emit();
            Environment.ExitCode = 1;
            return;
        }

        var direction = CSharpToCalorConverter.DetectDirection(input.FullName);

        if (direction == ConversionDirection.Unknown)
        {
            envelope?.AddCommandError("Unknown file type. Expected .cs or .calr extension.", input.FullName);
            Console.Error.WriteLine($"Error: Unknown file type. Expected .cs or .calr extension.");
            envelope?.Emit();
            Environment.ExitCode = 1;
            return;
        }

        var outputPath = output?.FullName ?? GetDefaultOutputPath(input.FullName, direction);
        envelope?.SetRoute(direction, outputPath);

        var statusOut = json ? Console.Error : Console.Out;
        if (verbose)
        {
            statusOut.WriteLine($"Converting: {input.Name}");
            statusOut.WriteLine($"Direction: {(direction == ConversionDirection.CSharpToCalor ? "C# → Calor" : "Calor → C#")}");
        }

        try
        {
            ConversionResult? conversionResult = null;
            if (direction == ConversionDirection.CSharpToCalor)
            {
                conversionResult = await ConvertCSharpToCalorAsync(input.FullName, outputPath, benchmark, verbose, explain, noFallback, validate, passthrough, timeoutSeconds, explicitCallClosers, envelope);
            }
            else
            {
                if (passthrough)
                {
                    // --passthrough only affects the C# → Calor direction (it unlocks the
                    // §CSHARP preservation fallback). Say so rather than silently ignoring it.
                    Console.Error.WriteLine("Note: --passthrough has no effect converting Calor → C#; it is ignored.");
                }
                await ConvertCalorToCSharpAsync(input.FullName, outputPath, verbose, envelope);
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
            envelope?.AddCommandError($"Unhandled error: {ex.Message}", input.FullName);
            Console.Error.WriteLine($"Error: {ex.Message}");
            telemetry?.TrackException(ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            // Envelope mode: a document is ALWAYS emitted, including crash paths.
            envelope?.Emit();
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

    /// <summary>
    /// Builds the <see cref="ConversionOptions"/> for a C# → Calor CLI conversion from the
    /// parsed flags. Extracted so the flag→option mapping — in particular
    /// <c>--passthrough</c> → <see cref="ConversionOptions.PassthroughOnError"/> (#736) — is
    /// unit-testable without driving the full command handler.
    /// </summary>
    internal static ConversionOptions BuildCSharpToCalorOptions(
        bool benchmark, bool verbose, bool explain, bool noFallback, bool passthrough, bool explicitCallClosers) => new()
    {
        Verbose = verbose,
        IncludeBenchmark = benchmark,
        Explain = explain,
        GracefulFallback = !noFallback,
        // #736: enabling passthrough runs the #717 post-conversion §CSHARP fallback, so an
        // unconvertible member is preserved as an interop block rather than written as
        // invalid Calor. Off by default, so default CLI behavior is unchanged.
        PassthroughOnError = passthrough,
        UseImplicitCallCloser = !explicitCallClosers
    };

    private static async Task<ConversionResult?> ConvertCSharpToCalorAsync(string inputPath, string outputPath, bool benchmark, bool verbose, bool explain, bool noFallback, bool validate, bool passthrough, int timeoutSeconds, bool explicitCallClosers, ConvertEnvelope? envelope)
    {
        var statusOut = envelope != null ? Console.Error : Console.Out;

        var converter = new CSharpToCalorConverter(
            BuildCSharpToCalorOptions(benchmark, verbose, explain, noFallback, passthrough, explicitCallClosers));

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
                envelope?.AddCommandError($"Conversion timed out after {timeoutSeconds}s", inputPath);
                Console.Error.WriteLine($"Error: Conversion timed out after {timeoutSeconds}s");
                Environment.ExitCode = 1;
                return null;
            }
        }
        else
        {
            result = await converter.ConvertFileAsync(inputPath);
        }

        if (envelope != null)
        {
            envelope.AddConversionIssues(result.Issues, inputPath);
            envelope.Data.Success = result.Success;
            var explanation = result.Context.GetExplanation();
            envelope.Data.UnsupportedFeatureCount = explanation.TotalUnsupportedCount;
            var featureCounts = explanation.GetFeatureCounts();
            envelope.Data.FeatureCounts = featureCounts.Count > 0 ? featureCounts : null;
        }

        if (!result.Success)
        {
            Console.Error.WriteLine("Conversion failed:");
            var errorIssues = result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).ToList();
            foreach (var issue in errorIssues)
            {
                Console.Error.WriteLine($"  {issue}");
            }
            // Under --passthrough the converter can fail with only a warning when the
            // emitted Calor still does not parse and could not be fully preserved as
            // §CSHARP (#717's "never ship broken output" contract). Surface that reason
            // so the failure isn't silent — the parse-fallback warning first, so on a
            // conversion with many routine warnings the actual failure reason stays on top.
            if (errorIssues.Count == 0)
            {
                var failureWarnings = result.Issues
                    .Where(i => i.Severity == ConversionIssueSeverity.Warning)
                    .OrderByDescending(i => i.Feature == "post-validation-fallback")
                    .ToList();
                foreach (var warning in failureWarnings)
                {
                    Console.Error.WriteLine($"  ⚠ {warning.Message}");
                }
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
            if (envelope != null)
            {
                envelope.Data.Validated = true;
                envelope.Data.ValidationErrorCount = validationErrors.Count;
                // Warnings, not errors — the output file was still written.
                foreach (var err in validationErrors)
                {
                    envelope.Diagnostics.Add(new Diagnostic(
                        DiagnosticCode.ConvertValidationError,
                        $"Generated output failed validation: {err.Message}",
                        err.Span,
                        DiagnosticSeverity.Warning,
                        outputPath));
                }
            }
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

        statusOut.WriteLine($"✓ Conversion successful");

        // #736/#717: report §CSHARP interop blocks in the output — members preserved as
        // raw C# that still need migrating. Reported whenever present, NOT gated on
        // --passthrough: a file whose members were wrapped by the visitor (known-
        // unsupported features) carries the same "N raw-C# members" caveat and the notice
        // is useful there too. When the #717 passthrough fallback rescued some, attribute
        // that subset so the flag's effect is visible without over-claiming the rest.
        var interopCount = result.Context.Stats.InteropBlocksEmitted;
        if (interopCount > 0)
        {
            var fallbackCount = result.Context.Stats.FallbackInteropBlocksEmitted;
            var attribution = fallbackCount > 0 ? $" ({fallbackCount} via --passthrough fallback)" : "";
            statusOut.WriteLine(
                $"  ⓘ {interopCount} member{(interopCount == 1 ? "" : "s")} preserved as §CSHARP interop " +
                $"block(s){attribution} — the output parses, but this C# still needs migrating.");
        }

        // Show warnings
        var warnings = result.Issues.Where(i => i.Severity == ConversionIssueSeverity.Warning).ToList();
        if (warnings.Count > 0)
        {
            statusOut.WriteLine();
            statusOut.WriteLine($"Warnings ({warnings.Count}):");
            foreach (var warning in warnings.Take(5))
            {
                statusOut.WriteLine($"  ⚠ {warning.Message}");
            }
            if (warnings.Count > 5)
            {
                statusOut.WriteLine($"  ... and {warnings.Count - 5} more");
            }
        }

        // Show explanation if requested
        if (explain)
        {
            statusOut.WriteLine();
            var explanation = result.Context.GetExplanation();
            statusOut.WriteLine(explanation.FormatForCli());
        }

        // Show benchmark if requested
        if (benchmark && result.CalorSource != null)
        {
            var originalSource = await File.ReadAllTextAsync(inputPath);
            var metrics = BenchmarkIntegration.CalculateMetrics(originalSource, result.CalorSource);

            statusOut.WriteLine();
            statusOut.WriteLine(BenchmarkIntegration.FormatComparison(metrics));

            envelope?.SetBenchmark(metrics);
        }

        statusOut.WriteLine();
        statusOut.WriteLine($"Output: {outputPath}");

        return result;
    }

    private static async Task ConvertCalorToCSharpAsync(string inputPath, string outputPath, bool verbose, ConvertEnvelope? envelope)
    {
        var statusOut = envelope != null ? Console.Error : Console.Out;

        var source = await File.ReadAllTextAsync(inputPath);
        // In envelope mode verbose phase messages must not pollute stdout.
        var result = envelope != null
            ? Program.Compile(source, inputPath, new CompilationOptions { Verbose = verbose, StatusWriter = Console.Error })
            : Program.Compile(source, inputPath, verbose);

        // The compiler's own diagnostics (with their real codes) flow into the
        // envelope directly.
        envelope?.Diagnostics.AddRange(result.Diagnostics);

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

        if (envelope != null)
        {
            envelope.Data.Success = true;
        }

        statusOut.WriteLine($"✓ Conversion successful");
        statusOut.WriteLine($"Output: {outputPath}");
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
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagBag);
        parser.Parse();
        return diagBag.Where(d => d.IsError).ToList();
    }

    /// <summary>
    /// Accumulates envelope diagnostics and the command <c>data</c> payload for
    /// <c>--format json</c>, and guarantees exactly one document is written to
    /// stdout (mirroring Program.CompileCore's Finish pattern).
    /// </summary>
    private sealed class ConvertEnvelope
    {
        public DiagnosticBag Diagnostics { get; } = new();
        public ConvertData Data { get; } = new();

        private bool _emitted;

        public void SetInput(string inputPath) => Data.InputPath = inputPath;

        public void SetRoute(ConversionDirection direction, string outputPath)
        {
            Data.Direction = direction == ConversionDirection.CSharpToCalor
                ? "csharp-to-calor"
                : "calor-to-csharp";
            Data.OutputPath = outputPath;
        }

        /// <summary>
        /// Command-level failure (input not found, unknown type, timeout, crash)
        /// — Calor1345, error severity.
        /// </summary>
        public void AddCommandError(string message, string? filePath)
        {
            Diagnostics.Add(new Diagnostic(
                DiagnosticCode.ConvertCommandError,
                message,
                new TextSpan(0, 0, 1, 1),
                DiagnosticSeverity.Error,
                filePath));
        }

        /// <summary>
        /// Conversion issues → Calor1343, severity mapped from the issue's own
        /// severity, message prefixed with the feature name when present.
        /// </summary>
        public void AddConversionIssues(IReadOnlyList<ConversionIssue> issues, string inputPath)
        {
            foreach (var issue in issues)
            {
                var severity = issue.Severity switch
                {
                    ConversionIssueSeverity.Error => DiagnosticSeverity.Error,
                    ConversionIssueSeverity.Warning => DiagnosticSeverity.Warning,
                    _ => DiagnosticSeverity.Info
                };
                var message = issue.Feature != null
                    ? $"[{issue.Feature}] {issue.Message}"
                    : issue.Message;
                Diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ConversionIssue,
                    message,
                    new TextSpan(0, 0, issue.Line ?? 1, issue.Column ?? 1),
                    severity,
                    inputPath));
            }
        }

        public void SetBenchmark(FileMetrics metrics)
        {
            Data.Benchmark = new ConvertBenchmarkData
            {
                OriginalTokens = metrics.OriginalTokens,
                OutputTokens = metrics.OutputTokens,
                OriginalLines = metrics.OriginalLines,
                OutputLines = metrics.OutputLines,
                OriginalCharacters = metrics.OriginalCharacters,
                OutputCharacters = metrics.OutputCharacters,
                TokenReductionPercent = metrics.TokenReduction,
                LineReductionPercent = metrics.LineReduction,
                AdvantageRatio = BenchmarkIntegration.CalculateAdvantageRatio(metrics)
            };
        }

        public void Emit()
        {
            if (_emitted)
            {
                return;
            }
            _emitted = true;
            Console.WriteLine(CommandEnvelope.Serialize("convert", Diagnostics, Data));
        }
    }

    // ------------------------------------------------------------------
    // Envelope `data` payload (--format json). Serialized camelCase with
    // null fields omitted; shape documented in docs/cli/convert.md.
    // ------------------------------------------------------------------

    private sealed class ConvertData
    {
        /// <summary>csharp-to-calor | calor-to-csharp; absent when the input type is unknown.</summary>
        public string? Direction { get; set; }
        public string? InputPath { get; set; }
        public string? OutputPath { get; set; }
        public bool Success { get; set; }

        /// <summary>C# → Calor only: total unsupported feature instances.</summary>
        public int? UnsupportedFeatureCount { get; set; }

        /// <summary>C# → Calor only: feature name → occurrence count; absent when empty.</summary>
        public Dictionary<string, int>? FeatureCounts { get; set; }

        /// <summary>True when <c>--validate</c> ran against the generated output.</summary>
        public bool Validated { get; set; }
        public int? ValidationErrorCount { get; set; }

        /// <summary>Present when <c>--benchmark</c> produced metrics.</summary>
        public ConvertBenchmarkData? Benchmark { get; set; }
    }

    private sealed class ConvertBenchmarkData
    {
        public int OriginalTokens { get; init; }
        public int OutputTokens { get; init; }
        public int OriginalLines { get; init; }
        public int OutputLines { get; init; }
        public int OriginalCharacters { get; init; }
        public int OutputCharacters { get; init; }
        public double TokenReductionPercent { get; init; }
        public double LineReductionPercent { get; init; }
        public double AdvantageRatio { get; init; }
    }
}
