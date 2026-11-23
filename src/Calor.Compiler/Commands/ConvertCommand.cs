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
    /// <summary>
    /// Test-only observer (#1586): receives the exact <see cref="ConversionOptions"/> a
    /// C# → Calor <c>convert</c> run hands to the converter, after every handler-side
    /// adjustment. The round-trip harness's default-mode parity test uses it to compare
    /// a real flagless CLI run with the options the harness converts with. Async-local,
    /// so one test's observer never sees another invocation.
    /// </summary>
    internal static readonly AsyncLocal<Action<ConversionOptions>?> ConversionOptionsObserver = new();

    // Test-only race/cancellation controls at the actual CLI conversion boundary.
    internal static readonly AsyncLocal<Action<ConversionResult, CancellationTokenSource?>?> ConversionCompletedObserver = new();
    // Can add a rejection, never suppress the independent mandatory validator.
    internal static readonly AsyncLocal<Func<string, Diagnostic?>?> AdditionalValidationError = new();

    /// <summary>
    /// Exit code for preserved output that was written but
    /// failed the round-trip compile check (<c>conversionPaths.outcome: "unverified"</c>).
    /// </summary>
    internal const int UnverifiedExitCode = 2;

    public static Command Create()
    {
        var inputArgument = new Argument<FileInfo>("input")
        {
            Description = "The source file to convert (.cs or .calr)"
        };

        var outputOption = new Option<FileInfo?>("--output", "-o")
        {
            Description = "The output file path (auto-detected if not specified)"
        };

        var benchmarkOption = new Option<bool>("--benchmark", "-b")
        {
            Description = "Include benchmark metrics comparison"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        var explainOption = new Option<bool>("--explain", "-e")
        {
            Description = "Show detailed explanation of unsupported features"
        };

        var noFallbackOption = new Option<bool>("--no-fallback")
        {
            Description = "Disable TODO fallback; default lossless conversion still preserves unsupported C#."
        };

        var validateOption = new Option<bool>("--validate")
        {
            Description = "Parse the generated Calor output and report any errors before writing"
        };

        var noImplicitSystemOption = new Option<bool>("--no-implicit-system")
        {
            Description = "Use standalone C# validation without the default System-only project import"
        };

        var passthroughOption = new Option<bool>("--passthrough")
        {
            Description = "Request member-level C# preservation on conversion errors (C# → Calor). Default lossless rescue also preserves unsupported code in converter-owned raw frames."
        };

        var defineOption = new Option<string[]>("--define")
        {
            Description = "Conditional-compilation symbol. Repeat for multiple symbols.",
            AllowMultipleArgumentsPerToken = true
        };
        var referenceOption = new Option<string[]>("--reference")
        {
            Description = "Metadata reference path optionally followed by =alias1,alias2.",
            AllowMultipleArgumentsPerToken = true
        };
        var configurationOption = new Option<string?>("--configuration")
        {
            Description = "Configuration name recorded in conversion metadata."
        };
        var targetFrameworkOption = new Option<string?>("--framework")
        {
            Description = "Target framework recorded in conversion metadata."
        };
        var languageVersionOption = new Option<string?>("--language-version")
        {
            Description = "C# language version used by Roslyn (for example preview or 13)."
        };
        var documentationModeOption = new Option<string>("--documentation-mode")
        {
            Description = "Roslyn documentation mode: none, parse, or diagnose.",
            DefaultValueFactory = _ => "parse"
        };
        documentationModeOption.AcceptOnlyFromAmong("none", "parse", "diagnose");
        var sourceKindOption = new Option<string>("--source-kind")
        {
            Description = "Roslyn source kind: regular or script.",
            DefaultValueFactory = _ => "regular"
        };
        sourceKindOption.AcceptOnlyFromAmong("regular", "script");
        var featureOption = new Option<string[]>("--feature")
        {
            Description = "Roslyn parse feature key=value. Repeat for multiple features.",
            AllowMultipleArgumentsPerToken = true
        };

        var timeoutOption = new Option<int>("--timeout", "-t")
        {
            Description = "Timeout in seconds for the conversion (0 = no timeout)",
            DefaultValueFactory = _ => 0
        };

        // No -f short alias (consistent with lint, where -f means --fix).
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: text (human-readable) or json (envelope document on stdout). No short alias.",
            DefaultValueFactory = _ => "text"
        };
        formatOption.AcceptOnlyFromAmong("text", "json");

        var command = new Command("convert", "Convert a single file between C# and Calor")
        {
            inputArgument,
            outputOption,
            benchmarkOption,
            verboseOption,
            explainOption,
            noFallbackOption,
            validateOption,
            noImplicitSystemOption,
            passthroughOption,
            defineOption,
            configurationOption,
            targetFrameworkOption,
            languageVersionOption,
            documentationModeOption,
            sourceKindOption,
            featureOption,
            referenceOption,
            timeoutOption,
            formatOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArgument)!;
            var output = parseResult.GetValue(outputOption);
            var benchmark = parseResult.GetValue(benchmarkOption);
            var verbose = parseResult.GetValue(verboseOption);
            var explain = parseResult.GetValue(explainOption);
            var noFallback = parseResult.GetValue(noFallbackOption);
            var validate = parseResult.GetValue(validateOption);
            var passthrough = parseResult.GetValue(passthroughOption);
            var definedSymbols = parseResult.GetValue(defineOption) ?? [];
            var configuration = parseResult.GetValue(configurationOption);
            var targetFramework = parseResult.GetValue(targetFrameworkOption);
            var languageVersion = parseResult.GetValue(languageVersionOption);
            var documentationMode = parseResult.GetValue(documentationModeOption) ?? "parse";
            var sourceKind = parseResult.GetValue(sourceKindOption) ?? "regular";
            var features = parseResult.GetValue(featureOption) ?? [];
            var references = parseResult.GetValue(referenceOption) ?? [];
            var timeoutSeconds = parseResult.GetValue(timeoutOption);
            var format = parseResult.GetValue(formatOption) ?? "text";
            // Exit code returned from the action: a code parked only on
            // Environment.ExitCode is overwritten by Main's InvokeAsync return.
            return await ExecuteAsync(
                input, output, benchmark, verbose, explain, noFallback, validate,
                passthrough, definedSymbols, configuration,
                targetFramework, languageVersion, timeoutSeconds,
                documentationMode, sourceKind, features,
                references, format,
                !parseResult.GetValue(noImplicitSystemOption));
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo input, FileInfo? output, bool benchmark, bool verbose,
        bool explain, bool noFallback, bool validate, bool passthrough,
        IReadOnlyCollection<string> definedSymbols,
        string? configuration, string? targetFramework, string? languageVersion,
        int timeoutSeconds, string documentationMode, string sourceKind,
        IReadOnlyCollection<string> features,
        IReadOnlyCollection<string> references,
        string format, bool includeImplicitSystemUsing)
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
            return 1;
        }

        var direction = CSharpToCalorConverter.DetectDirection(input.FullName);

        if (direction == ConversionDirection.Unknown)
        {
            envelope?.AddCommandError("Unknown file type. Expected .cs, .csx, or .calr extension.", input.FullName);
            Console.Error.WriteLine($"Error: Unknown file type. Expected .cs, .csx, or .calr extension.");
            envelope?.Emit();
            return 1;
        }

        var outputPath = output?.FullName ?? GetDefaultOutputPath(input.FullName, direction);
        envelope?.SetRoute(direction, outputPath);

        var statusOut = json ? Console.Error : Console.Out;
        if (verbose)
        {
            statusOut.WriteLine($"Converting: {input.Name}");
            statusOut.WriteLine($"Direction: {(direction == ConversionDirection.CSharpToCalor ? "C# → Calor" : "Calor → C#")}");
        }

        var exitCode = 0;
        try
        {
            ConversionResult? conversionResult = null;
            if (direction == ConversionDirection.CSharpToCalor)
            {
                (exitCode, conversionResult) = await ConvertCSharpToCalorAsync(
                    input.FullName, outputPath, benchmark, verbose, explain,
                    noFallback, validate, passthrough,
                    definedSymbols, configuration, targetFramework,
                    languageVersion, documentationMode, sourceKind,
                    features, references, timeoutSeconds,
                    envelope, includeImplicitSystemUsing);
            }
            else
            {
                if (passthrough)
                {
                    // --passthrough only affects the C# → Calor direction (it unlocks the
                    // §CSHARP preservation fallback). Say so rather than silently ignoring it.
                    Console.Error.WriteLine("Note: --passthrough has no effect converting Calor → C#; it is ignored.");
                }
                exitCode = await ConvertCalorToCSharpAsync(input.FullName, outputPath, verbose, envelope, includeImplicitSystemUsing);
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
            envelope?.MarkRefused();
            Console.Error.WriteLine($"Error: {ex.Message}");
            telemetry?.TrackException(ex);
            exitCode = 1;
        }
        finally
        {
            // Envelope mode: a document is ALWAYS emitted, including crash paths.
            envelope?.Emit();
            sw.Stop();
            telemetry?.TrackCommand("convert", exitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            });
            // #1514: exit 2 (written but unverified) is a reported result, not a crash.
            if (exitCode != 0 && exitCode != UnverifiedExitCode)
            {
                IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "convert", "Conversion failed");
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Builds the <see cref="ConversionOptions"/> for a C# → Calor CLI conversion from the
    /// parsed flags. Extracted so the flag→option mapping — in particular
    /// <c>--passthrough</c> → <see cref="ConversionOptions.PassthroughOnError"/> (#736) — is
    /// unit-testable without driving the full command handler.
    /// </summary>
    internal static ConversionOptions BuildCSharpToCalorOptions(
        bool benchmark, bool verbose, bool explain, bool noFallback, bool passthrough,
        bool lossy = false,
        bool selectActivePreprocessorBranchLossy = false,
        IReadOnlyCollection<string>? definedSymbols = null,
        string? configuration = null,
        string? targetFramework = null,
        string? languageVersion = null,
        string documentationMode = "parse",
        string sourceKind = "regular",
        IReadOnlyCollection<string>? features = null,
        IReadOnlyCollection<string>? references = null,
        bool includeImplicitSystemUsing = true) => new()
    {
        IncludeImplicitSystemUsing = includeImplicitSystemUsing,
        Fidelity = lossy || selectActivePreprocessorBranchLossy
            ? ConversionFidelity.Lossy
            : ConversionFidelity.Lossless,
        PreprocessorMode = selectActivePreprocessorBranchLossy
            ? PreprocessorConversionMode.SelectActiveBranchLossy
            : PreprocessorConversionMode.PreserveAllBranches,
        DefinedSymbols = definedSymbols?.ToArray() ?? [],
        Configuration = configuration,
        TargetFramework = targetFramework,
        ParseOptions = ParseOptions(
            languageVersion,
            documentationMode,
            sourceKind,
            features),
        References = ParseReferences(references),
        Verbose = verbose,
        IncludeBenchmark = benchmark,
        Explain = explain,
        GracefulFallback = !noFallback,
        // #736: enabling passthrough runs the #717 post-conversion §CSHARP fallback, so an
        // unconvertible member is preserved as an interop block rather than written as
        // invalid Calor. Off by default, so default CLI behavior is unchanged.
        PassthroughOnError = passthrough,
        // Review C1(b)/N2: the CLI discards the output entirely when conversion
        // fails, so preserving one unusable member as §CSHARP beats writing
        // nothing. Library callers keep the native tree (option defaults false).
        RescueUnusableMembers = true,
        // #1609: every input that parses is written. An original that does not
        // compile is preserved exactly, reported, and exits 2 (unverified).
        PreserveDespiteOriginalErrors = !(lossy || selectActivePreprocessorBranchLossy)
    };

    private static Microsoft.CodeAnalysis.CSharp.CSharpParseOptions
        ParseOptions(
            string? languageVersion,
            string documentationMode,
            string sourceKind,
            IReadOnlyCollection<string>? features)
    {
        var parsedLanguage = string.IsNullOrWhiteSpace(languageVersion)
            ? Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview
            : Microsoft.CodeAnalysis.CSharp.LanguageVersionFacts.TryParse(
                languageVersion,
                out var parsed)
                ? parsed
                : throw new ArgumentException(
                    $"Unknown C# language version '{languageVersion}'.",
                    nameof(languageVersion));
        if (!Enum.TryParse<Microsoft.CodeAnalysis.DocumentationMode>(
                documentationMode,
                ignoreCase: true,
                out var parsedDocumentation))
        {
            throw new ArgumentException(
                $"Unknown documentation mode '{documentationMode}'.",
                nameof(documentationMode));
        }
        if (!Enum.TryParse<Microsoft.CodeAnalysis.SourceCodeKind>(
                sourceKind,
                ignoreCase: true,
                out var parsedKind))
        {
            throw new ArgumentException(
                $"Unknown source kind '{sourceKind}'.",
                nameof(sourceKind));
        }
        var options = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            parsedLanguage,
            parsedDocumentation,
            parsedKind);
        return options.WithFeatures(ParseFeatures(features));
    }

    private static IReadOnlyDictionary<string, string> ParseFeatures(
        IReadOnlyCollection<string>? features)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in features ?? [])
        {
            var separator = feature.IndexOf('=');
            if (separator <= 0)
            {
                throw new ArgumentException(
                    $"Parse feature '{feature}' must use key=value syntax.",
                    nameof(features));
            }
            result[feature[..separator]] = feature[(separator + 1)..];
        }
        return result;
    }

    private static IReadOnlyList<ConversionReference> ParseReferences(
        IReadOnlyCollection<string>? references)
        => (references ?? [])
            .Select(reference =>
            {
                var separator = reference.IndexOf('=');
                var path = separator < 0
                    ? reference
                    : reference[..separator];
                var aliases = separator < 0
                    ? Array.Empty<string>()
                    : reference[(separator + 1)..].Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries);
                return new ConversionReference(
                    Path.GetFullPath(path),
                    aliases);
            })
            .ToArray();

    private static async Task<(int ExitCode, ConversionResult? Result)> ConvertCSharpToCalorAsync(
        string inputPath, string outputPath, bool benchmark, bool verbose,
        bool explain, bool noFallback, bool validate, bool passthrough,
        IReadOnlyCollection<string> definedSymbols,
        string? configuration, string? targetFramework, string? languageVersion,
        string documentationMode, string sourceKind,
        IReadOnlyCollection<string> features,
        IReadOnlyCollection<string> references,
        int timeoutSeconds, ConvertEnvelope? envelope,
        bool includeImplicitSystemUsing)
    {
        var statusOut = envelope != null ? Console.Error : Console.Out;

        var conversionOptions = BuildCSharpToCalorOptions(
            benchmark, verbose, explain, noFallback, passthrough,
            definedSymbols: definedSymbols,
            configuration: configuration, targetFramework: targetFramework, languageVersion: languageVersion,
            documentationMode: documentationMode, sourceKind: sourceKind, features: features, references: references,
            includeImplicitSystemUsing: includeImplicitSystemUsing);
        // Envelope mode: the converter's --verbose progress lines go to stderr
        // with the rest of the human-readable report, so stdout stays one
        // JSON document. Text mode keeps them on stdout.
        conversionOptions.StatusWriter = envelope != null ? Console.Error : null;
        ConversionOptionsObserver.Value?.Invoke(conversionOptions);
        // A --reference that does not exist is a command-line error, like a missing input.
        foreach (var reference in conversionOptions.References.Where(reference => !File.Exists(reference.Path)))
        {
            envelope?.AddCommandError($"Reference not found: {reference.Path}", inputPath);
            Console.Error.WriteLine($"Error: Reference not found: {reference.Path}");
            return (1, null);
        }

        var converter = new CSharpToCalorConverter(conversionOptions);

        using var generation = converter.CreateGeneration(inputPath, outputPath);
        // Destination-safety checks and the writer lock come first and are not timed:
        // --timeout bounds the native conversion attempt only (#1609).
        await generation.PrepareAsync();
        using var timeoutCts = timeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
            : null;
        var cancellationToken = timeoutCts?.Token ?? CancellationToken.None;

        ConversionResult? converted = null;
        if (timeoutCts != null)
        {
            try
            {
                // Run on a thread pool thread so the cancellation token can
                // interrupt even CPU-bound conversion hangs (e.g. infinite loops
                // in preprocessor region extraction).
                converted = await Task.Run(
                    () => generation.ConvertAsync(cancellationToken),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                converted = null;
            }
        }
        else
        {
            converted = await generation.ConvertAsync(cancellationToken);
        }
        if (converted != null)
            ConversionCompletedObserver.Value?.Invoke(converted, timeoutCts);

        ConversionResult result;
        if (converted == null || timeoutCts?.IsCancellationRequested == true)
        {
            // #1609: a timeout is not a refusal. Whole-file preservation needs no
            // conversion, so the original C# is written unchanged inside Calor.
            Console.Error.WriteLine(
                $"Native conversion did not finish within {timeoutSeconds}s; preserving the original C# whole.");
            result = await generation.PreserveWithoutConversionAsync(CancellationToken.None,
                [new ConversionIssue
                {
                    Severity = ConversionIssueSeverity.Warning,
                    Feature = "conversion-timeout",
                    Message = $"Native conversion did not finish within {timeoutSeconds}s; "
                        + "the original C# is preserved whole."
                }], ConversionTrigger.Timeout);
        }
        else
        {
            result = converted;
        }

        // Validation is mandatory in every mode. The converter additionally compiles
        // round-tripped C# in lossless mode; --validate remains accepted for compatibility.
        // #1609: output that fails it drops to whole-file preservation instead of being refused.
        var validationErrors = result.Success && result.CalorSource != null
            ? ValidateOutput(result.CalorSource)
            : new List<Diagnostic>();
        if (validationErrors.Count > 0
            && result.Losses.All(loss => loss.Feature != "whole-file-preservation"))
        {
            var failures = validationErrors.Select(error => new ConversionIssue
            {
                Severity = ConversionIssueSeverity.Error,
                Feature = "generated-calor-validation",
                Message = $"Generated output failed validation: {error.Message}",
                Line = error.Span.Line,
                Column = error.Span.Column
            }).ToList();
            result = generation.PreserveOriginal(CancellationToken.None, failures);
            validationErrors = result.Success && result.CalorSource != null
                ? ValidateOutput(result.CalorSource)
                : new List<Diagnostic>();
        }

        if (envelope != null)
        {
            envelope.AddConversionIssues(result.Issues, inputPath);
            envelope.Data.Success = result.Success;
            envelope.Data.Fidelity = result.Context.Fidelity
                .ToString().ToLowerInvariant();
            envelope.Data.Configuration = result.Metadata.Configuration;
            envelope.Data.TargetFramework = result.Metadata.TargetFramework;
            envelope.Data.LanguageVersion = result.Metadata.LanguageVersion;
            envelope.Data.DocumentationMode = result.Metadata.DocumentationMode;
            envelope.Data.SourceCodeKind = result.Metadata.SourceCodeKind;
            envelope.Data.ImplicitSystem = result.Metadata.IncludeImplicitSystemUsing;
            envelope.Data.Features = result.Metadata.Features;
            envelope.Data.DefinedSymbols = result.Metadata.DefinedSymbols;
            var explanation = result.Context.GetExplanation();
            envelope.Data.UnsupportedFeatureCount = explanation.TotalUnsupportedCount;
            var featureCounts = explanation.GetFeatureCounts();
            envelope.Data.FeatureCounts = featureCounts.Count > 0 ? featureCounts : null;
            envelope.Data.ConversionPaths = result.Paths;
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
            // #1144: name the path taken (refused). #1609: only two causes are left.
            envelope?.SetConversionSummary(result);
            Console.Error.WriteLine(InputHasSyntaxErrors(inputPath, conversionOptions)
                ? "  Conversion refused; no output written: the input C# does not parse, so it cannot be preserved."
                : "  Conversion refused; no output written: whole-file preservation could not reproduce the original C# exactly. "
                  + "This is a converter bug; please report it.");
            return (1, result);
        }

        if (result.CalorSource != null)
        {
            if (envelope != null)
            {
                envelope.Data.Validated = true;
                envelope.Data.ValidationErrorCount = validationErrors.Count;
                foreach (var err in validationErrors)
                {
                    envelope.Diagnostics.Add(new Diagnostic(
                        DiagnosticCode.ConvertValidationError,
                        $"Generated output failed validation: {err.Message}",
                        err.Span,
                        DiagnosticSeverity.Error,
                        outputPath));
                }
            }
        }

        if (validationErrors.Count > 0)
        {
            envelope?.Data.Success = false;
            envelope?.SetConversionSummary(result);
            envelope?.MarkRefused();
            Console.Error.WriteLine($"Validation failed ({validationErrors.Count} error{(validationErrors.Count == 1 ? "" : "s")}):");
            foreach (var err in validationErrors.Take(5))
                Console.Error.WriteLine($"  {err}");
            if (validationErrors.Count > 5)
                Console.Error.WriteLine($"  ... and {validationErrors.Count - 5} more");
            // #1609: this is the last rung. Even whole-file preservation failed the check.
            Console.Error.WriteLine("Whole-file preservation also failed validation. This is a converter bug; please report it.");
            Console.Error.WriteLine("Destination was not modified.");
            return (1, result);
        }

        // #1144: record losses before writing, so a write failure still reports them.
        // #1609: the write is not bound by --timeout; it bounds the conversion attempt only.
        envelope?.SetConversionSummary(result);
        await generation.WriteAsync(result, CancellationToken.None, outputWritten: () =>
        {
            if (envelope != null)
                envelope.OutputWritten = true;
        });

        // #770: structured loss accounting. The unconditional "✓ Conversion
        // successful" line was a false-success vector — it is now printed ONLY
        // when the conversion is fully native (zero recorded losses) and any
        // requested validation passed. Otherwise a loss summary with file:line
        // locations is printed instead.
        var losses = result.Context.Losses;
        envelope?.SetConversionSummary(result);
        // Writing exact preserved source is independent of standalone compilation.
        var unverified = result.Context.RoundTrip == ConversionRoundTrip.Failed;
        if (losses.Count == 0 && validationErrors.Count == 0 && !unverified)
        {
            statusOut.WriteLine($"✓ Conversion successful");
        }
        else if (losses.Count > 0 || validationErrors.Count > 0)
        {
            statusOut.WriteLine(
                $"Conversion completed with {result.InteropPreservationCount} interop preservation(s), " +
                $"{result.LossySubstitutionCount} lossy substitution(s), and {result.DropCount} drop(s)" +
                (validationErrors.Count > 0 ? $" and {validationErrors.Count} validation error(s)" : "") +
                " — output is not fully native Calor:");
            foreach (var group in losses.GroupBy(l => l.Kind))
            {
                statusOut.WriteLine($"  {group.Key}: {group.Count()}");
                foreach (var loss in group.Take(5))
                {
                    var file = loss.File ?? Path.GetFileName(inputPath);
                    var path = loss.Trigger != null ? $" [{loss.Path}: {loss.Trigger}]" : "";
                    statusOut.WriteLine($"    {file}:{loss.Line?.ToString() ?? "?"} [{loss.Feature}]{path} {loss.Description}");
                }
                if (group.Count() > 5)
                    statusOut.WriteLine($"    ... and {group.Count() - 5} more");
            }
        }

        if (unverified)
        {
            var roundTripFailures = result.Issues
                .Where(i => i.Feature is "roundtrip-calor-validation" or "roundtrip-csharp-validation")
                .ToList();
            statusOut.WriteLine(
                $"✗ Round-trip check failed: the generated C# does not compile ({roundTripFailures.Count} error(s)). " +
                "Exact original C# was preserved; its build is unverified (exit 2).");
            foreach (var failure in roundTripFailures.Take(5))
                statusOut.WriteLine($"    {failure.Message}");
            if (roundTripFailures.Count > 5)
                statusOut.WriteLine($"    ... and {roundTripFailures.Count - 5} more");
            statusOut.WriteLine("  Validate with the original project references and configuration.");
        }

        // #736/#717: report §CSHARP interop blocks in the output — members preserved as
        // raw C# that still need migrating. Reported whenever present, NOT gated on
        // --passthrough: a file whose members were wrapped by the visitor (known-
        // unsupported features) carries the same "N raw-C# members" caveat and the notice
        // is useful there too. #1144: the path that preserved each one (automatic
        // rescue, requested passthrough, converter interop) is reported from the loss
        // ledger, never inferred from the flags — rescue fires without --passthrough.
        var interopCount = result.Context.Stats.InteropBlocksEmitted;
        if (interopCount > 0)
        {
            statusOut.WriteLine(
                $"  ⓘ {interopCount} member{(interopCount == 1 ? "" : "s")} preserved as raw C# " +
                "block(s) — the output parses, but this C# still needs migrating.");
        }
        if (ConversionPathSummary.Describe(losses, "--passthrough") is { } pathLine)
            statusOut.WriteLine($"  {pathLine}");

        // Show warnings
        // The round-trip failures were listed above (#1514), so they are not repeated here.
        var warnings = result.Issues
            .Where(i => i.Severity == ConversionIssueSeverity.Warning)
            .Where(i => !unverified || i.Feature is not ("roundtrip-calor-validation" or "roundtrip-csharp-validation"))
            .ToList();
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

        // #1514: written but unverified is neither success (0) nor refusal (1).
        return (unverified ? UnverifiedExitCode : 0, result);
    }

    private static async Task<int> ConvertCalorToCSharpAsync(string inputPath, string outputPath, bool verbose, ConvertEnvelope? envelope, bool includeImplicitSystemUsing)
    {
        var statusOut = envelope != null ? Console.Error : Console.Out;
        if (envelope != null)
            envelope.Data.ImplicitSystem = includeImplicitSystemUsing;

        var source = await File.ReadAllTextAsync(inputPath);
        // In envelope mode verbose phase messages must not pollute stdout.
        var result = Program.Compile(source, inputPath, new CompilationOptions
        {
            Verbose = verbose,
            StatusWriter = envelope != null ? Console.Error : null,
            IncludeImplicitSystemUsing = includeImplicitSystemUsing
        });

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
            return 1;
        }

        await ConversionFileWriter.WriteAtomicAsync(outputPath, result.GeneratedCode);

        if (envelope != null)
        {
            envelope.Data.Success = true;
        }

        // Non-error diagnostics must be visible on the default text path too.
        // Envelope mode already carries them under diagnostics.
        if (envelope == null)
        {
            var nonErrors = result.Diagnostics.Where(d => !d.IsError).ToList();
            if (nonErrors.Count > 0)
            {
                Console.Error.WriteLine($"{nonErrors.Count} warning(s)/note(s):");
                foreach (var diag in nonErrors)
                {
                    Console.Error.WriteLine($"  {diag}");
                }
            }
        }

        statusOut.WriteLine($"✓ Conversion successful");
        statusOut.WriteLine($"Output: {outputPath}");
        return 0;
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
    /// <summary>The mandatory output check, plus the test hook's injected error.</summary>
    private static List<Diagnostic> ValidateOutput(string calorSource)
    {
        var errors = ValidateCalorSource(calorSource);
        if (AdditionalValidationError.Value?.Invoke(calorSource) is { } additionalError)
            errors.Add(additionalError);
        return errors;
    }

    /// <summary>#1609: whether a refusal is the allowed one (the input does not parse).</summary>
    private static bool InputHasSyntaxErrors(string inputPath, ConversionOptions options)
    {
        try
        {
            var source = File.ReadAllText(inputPath);
            return Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, options.ParseOptions)
                .GetDiagnostics()
                .Any(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        }
        catch (IOException)
        {
            return false;
        }
    }

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

        /// <summary>
        /// #770: structured loss accounting in the envelope — kind/feature/
        /// line/description per loss, plus the total count.
        /// </summary>
        public void SetConversionSummary(ConversionResult result)
        {
            var losses = result.Losses;
            Data.LossCount = losses.Count;
            Data.NativeConversionCount = result.NativeConversionCount;
            Data.InteropPreservationCount = result.InteropPreservationCount;
            Data.LossySubstitutionCount = result.LossySubstitutionCount;
            Data.DropCount = result.DropCount;
            Data.Losses = losses.Count > 0
                ? losses.Select(l => new ConvertLossData
                {
                    Kind = l.Kind.ToString(),
                    Feature = l.Feature,
                    Line = l.Line,
                    File = l.File,
                    Description = l.Description,
                    Path = l.Path,
                    Trigger = l.Trigger,
                    EnabledBy = l.EnabledBy
                }).ToList()
                : null;
            Data.ConversionPaths = result.Paths;
        }

        /// <summary>#1144: set once the converted output is on disk.</summary>
        public bool OutputWritten { get; set; }

        /// <summary>#1144: the command failed; the file's outcome is refused unless its output was already written.</summary>
        public void MarkRefused()
        {
            Data.Success = false;
            if (!OutputWritten)
                Data.ConversionPaths = Data.ConversionPaths?.WithSuccess(false);
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
                CompactnessRatio = BenchmarkIntegration.CalculateAdvantageRatio(metrics),
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
        public string? Fidelity { get; set; }
        public bool? ImplicitSystem { get; set; }
        public string? Configuration { get; set; }
        public string? TargetFramework { get; set; }
        public string? LanguageVersion { get; set; }
        public string? DocumentationMode { get; set; }
        public string? SourceCodeKind { get; set; }
        public IReadOnlyDictionary<string, string>? Features { get; set; }
        public IReadOnlyList<string>? DefinedSymbols { get; set; }

        /// <summary>C# → Calor only: total unsupported feature instances.</summary>
        public int? UnsupportedFeatureCount { get; set; }

        /// <summary>C# → Calor only: feature name → occurrence count; absent when empty.</summary>
        public Dictionary<string, int>? FeatureCounts { get; set; }

        /// <summary>True when <c>--validate</c> ran against the generated output.</summary>
        public bool Validated { get; set; }
        public int? ValidationErrorCount { get; set; }

        /// <summary>C# → Calor only: total recorded semantic losses (#770). 0 = fully native output.</summary>
        public int? LossCount { get; set; }
        public int? NativeConversionCount { get; set; }
        public int? InteropPreservationCount { get; set; }
        public int? LossySubstitutionCount { get; set; }
        public int? DropCount { get; set; }

        /// <summary>C# → Calor only: per-loss detail (kind, feature, file:line, description); absent when empty.</summary>
        public List<ConvertLossData>? Losses { get; set; }

        /// <summary>C# → Calor only (#1144): per-file path summary — outcome, per-path counts, triggers, and the rescue/passthrough options that applied.</summary>
        public ConversionPathSummary? ConversionPaths { get; set; }

        /// <summary>Present when <c>--benchmark</c> produced metrics.</summary>
        public ConvertBenchmarkData? Benchmark { get; set; }
    }

    private sealed class ConvertLossData
    {
        public string Kind { get; init; } = "";
        public string Feature { get; init; } = "";
        public int? Line { get; init; }
        public string? File { get; init; }
        public string Description { get; init; } = "";

        /// <summary>#1144: interop | rescue | passthrough | lossy | dropped.</summary>
        public string Path { get; init; } = "";
        public string? Trigger { get; init; }
        public string? EnabledBy { get; init; }
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
        public double CompactnessRatio { get; init; }
        public double AdvantageRatio { get; init; }
    }
}
