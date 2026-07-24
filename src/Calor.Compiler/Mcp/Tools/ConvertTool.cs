using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Ids;
using Calor.Compiler.Migration;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for converting C# source code to Calor.
/// Supports multiple modes: convert, validate, roundtrip, and assess.
/// </summary>
public sealed class ConvertTool : McpToolBase
{
    public override string Name => "calor_convert";

    public override string Description =>
        "Convert C# source code to Calor. Returns generated Calor code, conversion issues, and validation diagnostics.";

    public override int TimeoutSeconds => 120;

    public override McpToolAnnotations? Annotations => new() { IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "C# source code to convert to Calor (required unless inputPath is provided)"
                },
                "inputPath": {
                    "type": "string",
                    "description": "Path to a C# file to convert (alternative to source)"
                },
                "outputPath": {
                    "type": "string",
                    "description": "Path to write the generated Calor output file"
                },
                "moduleName": {
                    "type": "string",
                    "description": "Module name for the generated Calor code"
                },
                "fallback": {
                    "type": "boolean",
                    "description": "Enable graceful fallback for unsupported constructs (default: true)"
                },
                "explain": {
                    "type": "boolean",
                    "description": "Include detailed explanation of unsupported features (default: false)"
                },
                "validate": {
                    "type": "boolean",
                    "description": "Run full validation pipeline: auto-fix, diagnose, and compat check (default: false)"
                },
                "conversionMode": {
                    "type": "string",
                    "enum": ["standard", "interop"],
                    "description": "Conversion mode: 'standard' (default) or 'interop' for §CSHARP blocks"
                },
                "mode": {
                    "type": "string",
                    "enum": ["convert", "validate", "roundtrip", "assess"],
                    "description": "Conversion mode. convert=C# to Calor, validate=convert+compile back, roundtrip=C#→Calor→C# fidelity check, assess=score convertibility without converting",
                    "default": "convert"
                },
                "expectedNamespace": {
                    "type": "string",
                    "description": "Expected namespace in generated C# (for validate mode compat check)"
                },
                "expectedPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must appear in generated C# (for validate mode)"
                },
                "forbiddenPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must NOT appear in generated C# (for validate mode)"
                },
                "options": {
                    "type": "object",
                    "properties": {
                        "quick": {
                            "type": "boolean",
                            "default": false,
                            "description": "Stage 1 only: static analysis without conversion attempt (faster, for assess mode)"
                        }
                    }
                },
                "stripPreprocessor": {
                    "type": "boolean",
                    "description": "Strip C# preprocessor directives (#if, #region, #pragma, etc.) before conversion to prevent hangs/OOM (default: true)"
                },
                "passthroughOnError": {
                    "type": "boolean",
                    "description": "Wrap unsupported constructs in §CSHARP blocks instead of emitting broken Calor (default: false)"
                },
                "explicitCallClosers": {
                    "type": "boolean",
                    "description": "Emit explicit §/C for every §C call (v0.6.0-compatible output); disables zero-arg §/C elision (default: false — v0.6.1 default elides zero-arg §/C)"
                }
            },
            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var modeParam = GetString(arguments, "mode") ?? "convert";

        return modeParam.ToLowerInvariant() switch
        {
            "validate" => HandleValidate(arguments, cancellationToken),
            "roundtrip" => HandleRoundtrip(arguments, cancellationToken),
            "assess" => HandleAssess(arguments, cancellationToken),
            _ => HandleConvert(arguments, cancellationToken)
        };
    }

    // ── mode=convert (default) ──────────────────────────────────────────

    private Task<McpToolResult> HandleConvert(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var source = GetString(arguments, "source");
        var inputPath = GetString(arguments, "inputPath");
        var outputPath = GetString(arguments, "outputPath");

        // Resolve source from file if inputPath provided
        if (!string.IsNullOrEmpty(inputPath))
        {
            if (!File.Exists(inputPath))
            {
                return Task.FromResult(McpToolResult.Error($"Input file not found: {inputPath}"));
            }

            try
            {
                source = File.ReadAllText(inputPath);
            }
            catch (Exception ex)
            {
                return Task.FromResult(McpToolResult.Error($"Failed to read input file: {ex.Message}"));
            }
        }

        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Either 'source' or 'inputPath' must be provided"));
        }

        var moduleName = GetString(arguments, "moduleName");
        // Derive module name from filename when not specified
        if (string.IsNullOrEmpty(moduleName) && !string.IsNullOrEmpty(inputPath))
        {
            moduleName = Path.GetFileNameWithoutExtension(inputPath);
        }
        var fallback = GetBool(arguments, "fallback", defaultValue: true);
        var explain = GetBool(arguments, "explain", defaultValue: false);
        var stripPreprocessor = GetBool(arguments, "stripPreprocessor", defaultValue: true);
        var passthroughOnError = GetBool(arguments, "passthroughOnError", defaultValue: false);
        var explicitCallClosers = GetBool(arguments, "explicitCallClosers", defaultValue: false);
        var conversionMode = ResolveConversionMode(arguments);

        try
        {
            var options = new ConversionOptions
            {
                ModuleName = moduleName,
                PreserveComments = true,
                AutoGenerateIds = true,
                GracefulFallback = fallback,
                Explain = explain,
                Mode = conversionMode,
                StripPreprocessor = stripPreprocessor,
                PassthroughOnError = passthroughOnError,
                UseImplicitCallCloser = !explicitCallClosers
            };

            var converter = new CSharpToCalorConverter(options);
            cancellationToken.ThrowIfCancellationRequested();
            var result = converter.Convert(source);

            // Always compute explanation for unsupported feature summary
            var explanation = result.Context.GetExplanation();
            var unsupportedFeatureCount = explanation.TotalUnsupportedCount;
            var unsupportedFeatureSummary = explanation.UnsupportedFeatures.Count > 0
                ? explanation.UnsupportedFeatures
                    .Select(kvp => $"{kvp.Key} ({kvp.Value.Count})")
                    .OrderBy(s => s)
                    .ToList()
                : null;

            // Build detailed explanation if requested
            ExplanationOutput? explanationOutput = null;
            if (explain)
            {
                explanationOutput = new ExplanationOutput
                {
                    UnsupportedFeatures = explanation.UnsupportedFeatures
                        .Select(kvp => new UnsupportedFeatureOutput
                        {
                            Feature = kvp.Key,
                            Count = kvp.Value.Count,
                            Instances = kvp.Value.Select(i => new FeatureInstanceOutput
                            {
                                Code = i.Code,
                                Line = i.Line,
                                Suggestion = i.Suggestion
                            }).ToList()
                        }).ToList(),
                    TotalUnsupportedCount = explanation.TotalUnsupportedCount,
                    PartialFeatures = explanation.PartialFeatures,
                    ManualRequiredFeatures = explanation.ManualRequiredFeatures
                };
            }

            // Track unsupported features in telemetry
            if (CalorTelemetry.IsInitialized)
            {
                if (explanation.TotalUnsupportedCount > 0)
                {
                    CalorTelemetry.Instance.TrackUnsupportedFeatures(
                        explanation.GetFeatureCounts(),
                        explanation.TotalUnsupportedCount);
                }
            }

            // Post-conversion validation: re-parse the generated Calor to catch invalid output.
            // Issues are envelope schema v1.1 entries (Calor1343, feature-prefixed).
            var issues = result.Issues
                .Select(i => ConversionIssueEnvelope.Build(i, inputPath))
                .ToList();

            var success = result.Success;
            var calorSourceForOutput = result.CalorSource;

            cancellationToken.ThrowIfCancellationRequested();
            if (success && !string.IsNullOrWhiteSpace(calorSourceForOutput))
            {
                var parseResult = CalorSourceHelper.Parse(calorSourceForOutput, "converted-output.calr");
                if (!parseResult.IsSuccess)
                {
                    // Attempt auto-fix before reporting errors
                    var fixer = new PostConversionFixer();
                    var fixResult = fixer.Fix(calorSourceForOutput);

                    if (fixResult.WasModified)
                    {
                        var retryParse = CalorSourceHelper.Parse(fixResult.FixedSource, "converted-output.calr");
                        if (retryParse.IsSuccess)
                        {
                            // Auto-fix succeeded — use fixed source
                            calorSourceForOutput = fixResult.FixedSource;
                            foreach (var fix in fixResult.AppliedFixes)
                            {
                                issues.Add(ConversionIssueEnvelope.Message(
                                    DiagnosticCode.ConversionIssue, "info",
                                    $"Auto-fixed: {fix.Description} (rule: {fix.Rule})"));
                            }
                        }
                        else
                        {
                            // Auto-fix didn't fully resolve — report original errors
                            success = false;
                            issues.AddRange(BuildGeneratedParseErrors(parseResult));
                        }
                    }
                    else
                    {
                        // No fixes applicable — report original errors
                        success = false;
                        issues.AddRange(BuildGeneratedParseErrors(parseResult));
                    }
                }
            }

            // Write output file if requested
            if (!string.IsNullOrEmpty(outputPath) && success && !string.IsNullOrWhiteSpace(calorSourceForOutput))
            {
                try
                {
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }
                    File.WriteAllText(outputPath, calorSourceForOutput);
                }
                catch (Exception ex)
                {
                    issues.Add(ConversionIssueEnvelope.Message(
                        DiagnosticCode.ConversionIssue, "warning",
                        $"Failed to write output file: {ex.Message}", inputPath));
                }
            }

            // Analyze interop blocks for features that have native Calor equivalents
            var featureHints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSourceForOutput);

            // Surface native features successfully used (helps agents discover capabilities)
            var nativeFeaturesUsed = result.Context.UsedFeatures
                .Where(f => FeatureSupport.IsFullySupported(f))
                .OrderBy(f => f)
                .ToList();

            var output = new ConvertToolOutput
            {
                Success = success,
                CalorSource = calorSourceForOutput,
                OutputPath = outputPath,
                Issues = issues,
                Stats = new ConversionStatsOutput
                {
                    ClassesConverted = result.Context.Stats.ClassesConverted,
                    InterfacesConverted = result.Context.Stats.InterfacesConverted,
                    MethodsConverted = result.Context.Stats.MethodsConverted,
                    PropertiesConverted = result.Context.Stats.PropertiesConverted,
                    FieldsConverted = result.Context.Stats.FieldsConverted,
                    InteropBlocksEmitted = result.Context.Stats.InteropBlocksEmitted,
                    MembersDropped = result.Context.Stats.MembersDropped,
                    DurationMs = (int)result.Duration.TotalMilliseconds
                },
                UnsupportedFeatureCount = unsupportedFeatureCount,
                UnsupportedFeatureSummary = unsupportedFeatureSummary,
                Explanation = explanationOutput,
                FeatureHints = featureHints.Count > 0 ? featureHints : null,
                NativeFeaturesUsed = nativeFeaturesUsed.Count > 0 ? nativeFeaturesUsed : null,
                Tip = "Use calor_help with feature='overview' to see all available Calor syntax before writing or editing .calr files."
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !success));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Conversion failed: {ex.Message}"));
        }
    }

    // ── mode=validate ───────────────────────────────────────────────────

    private Task<McpToolResult> HandleValidate(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var source = GetString(arguments, "source");
        var inputPath = GetString(arguments, "inputPath");
        var outputPath = GetString(arguments, "outputPath");

        // Resolve source from file if inputPath provided
        if (!string.IsNullOrEmpty(inputPath))
        {
            if (!File.Exists(inputPath))
            {
                return Task.FromResult(McpToolResult.Error($"Input file not found: {inputPath}"));
            }

            try
            {
                source = File.ReadAllText(inputPath);
            }
            catch (Exception ex)
            {
                return Task.FromResult(McpToolResult.Error($"Failed to read input file: {ex.Message}"));
            }
        }

        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Either 'source' or 'inputPath' must be provided"));
        }

        var moduleName = GetString(arguments, "moduleName");
        // Derive module name from filename when not specified
        if (string.IsNullOrEmpty(moduleName) && !string.IsNullOrEmpty(inputPath))
        {
            moduleName = Path.GetFileNameWithoutExtension(inputPath);
        }
        var conversionMode = ResolveConversionMode(arguments);
        var expectedNamespace = GetString(arguments, "expectedNamespace");
        var expectedPatterns = GetStringArray(arguments, "expectedPatterns");
        var forbiddenPatterns = GetStringArray(arguments, "forbiddenPatterns");

        var sw = Stopwatch.StartNew();

        try
        {
            var conversionIssues = new List<EnvelopeDiagnostic>();
            var autoFixes = new List<string>();
            var diagnosticEntries = new List<EnvelopeDiagnostic>();
            var compatIssues = new List<string>();

            // Stage 1: Convert
            var options = new ConversionOptions
            {
                ModuleName = moduleName,
                PreserveComments = true,
                AutoGenerateIds = true,
                GracefulFallback = true,
                Explain = false,
                Mode = conversionMode,
                StripPreprocessor = GetBool(arguments, "stripPreprocessor", defaultValue: true),
                PassthroughOnError = GetBool(arguments, "passthroughOnError", defaultValue: false),
                UseImplicitCallCloser = !GetBool(arguments, "explicitCallClosers", defaultValue: false)
            };

            var converter = new CSharpToCalorConverter(options);
            var convResult = converter.Convert(source);

            conversionIssues.AddRange(convResult.Issues
                .Select(i => ConversionIssueEnvelope.Build(i, inputPath)));

            if (!convResult.Success || string.IsNullOrWhiteSpace(convResult.CalorSource))
            {
                sw.Stop();
                return Task.FromResult(McpToolResult.Json(BuildValidatedOutput(
                    success: false, stage: "conversion",
                    calorSource: convResult.CalorSource, generatedCSharp: null,
                    conversionIssues, autoFixes, diagnosticEntries, compatIssues,
                    convResult.Context.Stats, sw.Elapsed), isError: true));
            }

            var calorSource = convResult.CalorSource!;

            // Stage 2: Parse + auto-fix
            var parseResult = CalorSourceHelper.Parse(calorSource, "validated-output.calr");
            if (!parseResult.IsSuccess)
            {
                var fixer = new PostConversionFixer();
                var fixResult = fixer.Fix(calorSource);

                if (fixResult.WasModified)
                {
                    foreach (var fix in fixResult.AppliedFixes)
                    {
                        autoFixes.Add($"{fix.Rule}: {fix.Description}");
                    }

                    var retryParse = CalorSourceHelper.Parse(fixResult.FixedSource, "validated-output.calr");
                    if (retryParse.IsSuccess)
                    {
                        calorSource = fixResult.FixedSource;
                    }
                    else
                    {
                        sw.Stop();
                        diagnosticEntries.AddRange(retryParse.ToEnvelopeDiagnostics());
                        return Task.FromResult(McpToolResult.Json(BuildValidatedOutput(
                            success: false, stage: "parse",
                            calorSource: fixResult.FixedSource, generatedCSharp: null,
                            conversionIssues, autoFixes, diagnosticEntries, compatIssues,
                            convResult.Context.Stats, sw.Elapsed), isError: true));
                    }
                }
                else
                {
                    sw.Stop();
                    diagnosticEntries.AddRange(parseResult.ToEnvelopeDiagnostics());
                    return Task.FromResult(McpToolResult.Json(BuildValidatedOutput(
                        success: false, stage: "parse",
                        calorSource: calorSource, generatedCSharp: null,
                        conversionIssues, autoFixes, diagnosticEntries, compatIssues,
                        convResult.Context.Stats, sw.Elapsed), isError: true));
                }
            }

            // Stage 3: Diagnose — compile Calor to check for semantic errors
            string? generatedCSharp = null;
            try
            {
                var compileOptions = new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    CancellationToken = cancellationToken
                };
                var compileResult = Program.Compile(calorSource, "validated-output.calr", compileOptions);

                // Real compile diagnostics as envelope entries; a resolver
                // built from the parsed AST populates declarationId.
                DeclarationIdResolver? declarationIds = null;
                if (compileResult.Ast != null)
                {
                    declarationIds = new DeclarationIdResolver();
                    declarationIds.AddFile("validated-output.calr", calorSource, compileResult.Ast);
                }
                diagnosticEntries.AddRange(DiagnosticEnvelope.Build(compileResult.Diagnostics, declarationIds));

                if (compileResult.HasErrors)
                {
                    sw.Stop();
                    return Task.FromResult(McpToolResult.Json(BuildValidatedOutput(
                        success: false, stage: "diagnose",
                        calorSource: calorSource, generatedCSharp: compileResult.GeneratedCode,
                        conversionIssues, autoFixes, diagnosticEntries, compatIssues,
                        convResult.Context.Stats, sw.Elapsed), isError: true));
                }

                generatedCSharp = compileResult.GeneratedCode;
            }
            catch (Exception ex)
            {
                sw.Stop();
                diagnosticEntries.Add(ConversionIssueEnvelope.Message(
                    DiagnosticCode.CliInternalError, "error",
                    $"Compilation exception: {ex.Message}"));
                return Task.FromResult(McpToolResult.Json(BuildValidatedOutput(
                    success: false, stage: "compile",
                    calorSource: calorSource, generatedCSharp: null,
                    conversionIssues, autoFixes, diagnosticEntries, compatIssues,
                    convResult.Context.Stats, sw.Elapsed), isError: true));
            }

            // Stage 4: Compat check — verify generated C# matches expectations
            if (!string.IsNullOrEmpty(generatedCSharp))
            {
                if (!string.IsNullOrEmpty(expectedNamespace))
                {
                    var nsPattern = $@"namespace\s+{Regex.Escape(expectedNamespace)}\b";
                    if (!Regex.IsMatch(generatedCSharp, nsPattern))
                    {
                        compatIssues.Add($"Expected namespace '{expectedNamespace}' not found in generated code");
                    }
                }

                foreach (var pattern in expectedPatterns)
                {
                    if (!generatedCSharp.Contains(pattern))
                    {
                        compatIssues.Add($"Expected pattern '{pattern}' not found in generated code");
                    }
                }

                foreach (var pattern in forbiddenPatterns)
                {
                    if (generatedCSharp.Contains(pattern))
                    {
                        compatIssues.Add($"Forbidden pattern '{pattern}' found in generated code");
                    }
                }

                if (compatIssues.Count > 0)
                {
                    sw.Stop();
                    return Task.FromResult(McpToolResult.Json(BuildValidatedOutput(
                        success: false, stage: "compat",
                        calorSource: calorSource, generatedCSharp: generatedCSharp,
                        conversionIssues, autoFixes, diagnosticEntries, compatIssues,
                        convResult.Context.Stats, sw.Elapsed), isError: true));
                }
            }

            // Write output file if requested
            if (!string.IsNullOrEmpty(outputPath))
            {
                try
                {
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }
                    File.WriteAllText(outputPath, calorSource);
                }
                catch (Exception ex)
                {
                    conversionIssues.Add(ConversionIssueEnvelope.Message(
                        DiagnosticCode.ConversionIssue, "warning",
                        $"Failed to write output file: {ex.Message}", inputPath));
                }
            }

            // All stages passed
            sw.Stop();
            return Task.FromResult(McpToolResult.Json(BuildValidatedOutput(
                success: true, stage: "complete",
                calorSource: calorSource, generatedCSharp: generatedCSharp,
                conversionIssues, autoFixes, diagnosticEntries, compatIssues,
                convResult.Context.Stats, sw.Elapsed)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Validated conversion failed: {ex.Message}"));
        }
    }

    // ── mode=roundtrip ──────────────────────────────────────────────────

    private Task<McpToolResult> HandleRoundtrip(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: 'source'"));
        }

        var moduleName = GetString(arguments, "moduleName") ?? "RoundTrip";

        var conversionErrors = new List<EnvelopeDiagnostic>();
        var compilationErrors = new List<EnvelopeDiagnostic>();
        string? calorSource = null;
        string? roundTrippedCSharp = null;
        var conversionSuccess = false;
        var compilationSuccess = false;

        // Step 1: Convert C# → Calor
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var options = new ConversionOptions
            {
                ModuleName = moduleName,
                PreserveComments = true,
                AutoGenerateIds = true,
                GracefulFallback = true,
                Mode = ConversionMode.Interop,
                StripPreprocessor = GetBool(arguments, "stripPreprocessor", defaultValue: true),
                UseImplicitCallCloser = !GetBool(arguments, "explicitCallClosers", defaultValue: false)
            };

            var converter = new CSharpToCalorConverter(options);
            var result = converter.Convert(source);

            if (result.Success && !string.IsNullOrWhiteSpace(result.CalorSource))
            {
                conversionSuccess = true;
                calorSource = result.CalorSource;
            }
            else
            {
                conversionErrors.AddRange(result.Issues.Select(i => ConversionIssueEnvelope.Build(i)));
            }
        }
        catch (Exception ex)
        {
            conversionErrors.Add(ConversionIssueEnvelope.Message(
                DiagnosticCode.CliInternalError, "error", $"Conversion exception: {ex.Message}"));
        }

        // Step 2: Compile Calor → C#
        cancellationToken.ThrowIfCancellationRequested();
        if (conversionSuccess && !string.IsNullOrWhiteSpace(calorSource))
        {
            try
            {
                var compileOptions = new CompilationOptions
                {
                    ContractMode = ContractMode.Off,
                    UnknownCallPolicy = UnknownCallPolicy.Permissive,
                    CancellationToken = cancellationToken
                };

                var compileResult = Program.Compile(calorSource, "roundtrip.calr", compileOptions);

                if (!compileResult.HasErrors && !string.IsNullOrWhiteSpace(compileResult.GeneratedCode))
                {
                    compilationSuccess = true;
                    roundTrippedCSharp = compileResult.GeneratedCode;
                }
                else
                {
                    DeclarationIdResolver? declarationIds = null;
                    if (compileResult.Ast != null)
                    {
                        declarationIds = new DeclarationIdResolver();
                        declarationIds.AddFile("roundtrip.calr", calorSource, compileResult.Ast);
                    }
                    compilationErrors.AddRange(
                        DiagnosticEnvelope.Build(compileResult.Diagnostics, declarationIds)
                            .Where(e => e.Severity == "error"));
                }
            }
            catch (Exception ex)
            {
                compilationErrors.Add(ConversionIssueEnvelope.Message(
                    DiagnosticCode.CliInternalError, "error", $"Compilation exception: {ex.Message}"));
            }
        }

        // Step 3: Compare original and round-tripped C#
        var originalLines = source.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var roundTrippedLines = (roundTrippedCSharp ?? "").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        var normalizedOriginal = NormalizeWhitespace(source);
        var normalizedRoundTripped = NormalizeWhitespace(roundTrippedCSharp ?? "");
        var roundTripMatch = compilationSuccess && normalizedOriginal == normalizedRoundTripped;

        // Compute line-level diffs (first 20)
        var differences = new List<LineDifference>();
        var maxLines = Math.Max(originalLines.Length, roundTrippedLines.Length);
        for (var i = 0; i < maxLines && differences.Count < 20; i++)
        {
            var orig = i < originalLines.Length ? originalLines[i] : null;
            var rt = i < roundTrippedLines.Length ? roundTrippedLines[i] : null;

            if (NormalizeWhitespace(orig ?? "") != NormalizeWhitespace(rt ?? ""))
            {
                differences.Add(new LineDifference
                {
                    LineNumber = i + 1,
                    Original = orig,
                    RoundTripped = rt
                });
            }
        }

        var output = new RoundTripCheckOutput
        {
            ConversionSuccess = conversionSuccess,
            CompilationSuccess = compilationSuccess,
            RoundTripMatch = roundTripMatch,
            OriginalLines = originalLines.Length,
            RoundTrippedLines = roundTrippedLines.Length,
            CalorSource = calorSource,
            RoundTrippedCSharp = roundTrippedCSharp,
            Differences = differences.Count > 0 ? differences : null,
            ConversionErrors = conversionErrors.Count > 0 ? conversionErrors : null,
            CompilationErrors = compilationErrors.Count > 0 ? compilationErrors : null
        };

        return Task.FromResult(McpToolResult.Json(output, isError: !roundTripMatch));
    }

    // ── mode=assess ─────────────────────────────────────────────────────

    private Task<McpToolResult> HandleAssess(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: 'source'"));
        }

        var opts = GetOptions(arguments);
        var quick = GetBool(opts, "quick", false);

        try
        {
            var analyzer = new ConvertibilityAnalyzer();
            cancellationToken.ThrowIfCancellationRequested();
            var result = quick
                ? analyzer.AnalyzeQuick(source)
                : analyzer.Analyze(source);

            var blockers = result.Blockers.Select(b => new AssessBlocker
            {
                Name = b.Name,
                Description = b.Description,
                Count = b.Count,
                Category = b.Category
            }).ToList();

            var output = new AssessOutput
            {
                Score = result.Score,
                Summary = result.Summary,
                ConversionAttempted = result.ConversionAttempted,
                ConversionSucceeded = result.ConversionSucceeded,
                CompilationSucceeded = result.CompilationSucceeded,
                ConversionRate = result.ConversionRate,
                Blockers = blockers,
                TotalBlockerInstances = result.TotalBlockerInstances,
                LanguageGapCount = blockers.Count(b => b.Category == "language_unsupported"),
                ConverterBugCount = blockers.Count(b => b.Category == "converter_not_implemented"),
                DurationMs = (int)result.Duration.TotalMilliseconds
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Analysis failed: {ex.Message}"));
        }
    }

    // ── Shared helpers ──────────────────────────────────────────────────

    private static ConversionMode ResolveConversionMode(JsonElement? arguments)
    {
        // Check "conversionMode" first (new name), fall back to legacy "mode" usage for standard/interop
        var modeStr = GetString(arguments, "conversionMode");
        if (string.IsNullOrEmpty(modeStr))
        {
            // Legacy: "mode" could be "standard"/"interop" in old callers — but now "mode"
            // is the dispatch param. Only honour standard/interop if explicitly set via conversionMode.
            modeStr = "standard";
        }
        return modeStr.Equals("interop", StringComparison.OrdinalIgnoreCase)
            ? ConversionMode.Interop : ConversionMode.Standard;
    }

    private static string NormalizeWhitespace(string text)
    {
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Envelope entries for parse failures of the converter's own output: the
    /// real parser diagnostics (code + location in the generated Calor), with a
    /// message prefix and converter-bug suggestion preserved from the old shape.
    /// </summary>
    private static List<EnvelopeDiagnostic> BuildGeneratedParseErrors(ParseResult parse)
    {
        return parse.ToEnvelopeDiagnostics()
            .Select(e => new EnvelopeDiagnostic
            {
                Code = e.Code,
                Message = $"Generated Calor failed to parse: {e.Message}",
                Severity = "error",
                Location = e.Location,
                DeclarationId = e.DeclarationId,
                Suggestion = "The converter produced invalid Calor syntax. This is a converter bug — please report it."
            })
            .ToList();
    }

    private static ValidatedOutput BuildValidatedOutput(
        bool success, string stage,
        string? calorSource, string? generatedCSharp,
        List<EnvelopeDiagnostic> conversionIssues,
        List<string> autoFixes,
        List<EnvelopeDiagnostic> diagnostics,
        List<string> compatIssues,
        ConversionStats stats,
        TimeSpan duration)
    {
        var featureHints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        return new ValidatedOutput
        {
            Success = success,
            Stage = stage,
            CalorSource = calorSource,
            GeneratedCSharp = generatedCSharp,
            ConversionIssues = conversionIssues,
            AutoFixes = autoFixes,
            Diagnostics = diagnostics,
            CompatIssues = compatIssues,
            Stats = new ConversionStatsOutput
            {
                ClassesConverted = stats.ClassesConverted,
                InterfacesConverted = stats.InterfacesConverted,
                MethodsConverted = stats.MethodsConverted,
                PropertiesConverted = stats.PropertiesConverted,
                FieldsConverted = stats.FieldsConverted,
                InteropBlocksEmitted = stats.InteropBlocksEmitted,
                MembersDropped = stats.MembersDropped,
                DurationMs = (int)duration.TotalMilliseconds
            },
            DurationMs = (int)duration.TotalMilliseconds,
            FeatureHints = featureHints.Count > 0 ? featureHints : null
        };
    }

    // ── Output DTOs ─────────────────────────────────────────────────────

    // mode=convert output
    private sealed class ConvertToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("calorSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalorSource { get; init; }

        [JsonPropertyName("outputPath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputPath { get; init; }

        /// <summary>Envelope schema v1.1 diagnostic entries (shared EnvelopeDiagnostic shape).</summary>
        [JsonPropertyName("issues")]
        public required List<EnvelopeDiagnostic> Issues { get; init; }

        [JsonPropertyName("stats")]
        public required ConversionStatsOutput Stats { get; init; }

        [JsonPropertyName("unsupportedFeatureCount")]
        public int UnsupportedFeatureCount { get; init; }

        [JsonPropertyName("unsupportedFeatureSummary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? UnsupportedFeatureSummary { get; init; }

        [JsonPropertyName("explanation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ExplanationOutput? Explanation { get; init; }

        [JsonPropertyName("featureHints")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FeatureHints { get; init; }

        [JsonPropertyName("nativeFeaturesUsed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? NativeFeaturesUsed { get; init; }

        [JsonPropertyName("tip")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Tip { get; init; }
    }

    private sealed class ExplanationOutput
    {
        [JsonPropertyName("unsupportedFeatures")]
        public required List<UnsupportedFeatureOutput> UnsupportedFeatures { get; init; }

        [JsonPropertyName("totalUnsupportedCount")]
        public int TotalUnsupportedCount { get; init; }

        [JsonPropertyName("partialFeatures")]
        public required List<string> PartialFeatures { get; init; }

        [JsonPropertyName("manualRequiredFeatures")]
        public required List<string> ManualRequiredFeatures { get; init; }
    }

    private sealed class UnsupportedFeatureOutput
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("instances")]
        public required List<FeatureInstanceOutput> Instances { get; init; }
    }

    private sealed class FeatureInstanceOutput
    {
        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("suggestion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Suggestion { get; init; }
    }

    // mode=validate output
    private sealed class ValidatedOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("stage")]
        public required string Stage { get; init; }

        [JsonPropertyName("calorSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalorSource { get; init; }

        [JsonPropertyName("generatedCSharp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GeneratedCSharp { get; init; }

        /// <summary>Envelope schema v1.1 diagnostic entries (shared EnvelopeDiagnostic shape).</summary>
        [JsonPropertyName("conversionIssues")]
        public required List<EnvelopeDiagnostic> ConversionIssues { get; init; }

        [JsonPropertyName("autoFixes")]
        public required List<string> AutoFixes { get; init; }

        /// <summary>Envelope schema v1.1 diagnostic entries (parse + compile diagnostics).</summary>
        [JsonPropertyName("diagnostics")]
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }

        [JsonPropertyName("compatIssues")]
        public required List<string> CompatIssues { get; init; }

        [JsonPropertyName("stats")]
        public required ConversionStatsOutput Stats { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }

        [JsonPropertyName("featureHints")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FeatureHints { get; init; }
    }

    // mode=roundtrip output
    private sealed class RoundTripCheckOutput
    {
        [JsonPropertyName("conversionSuccess")]
        public bool ConversionSuccess { get; init; }

        [JsonPropertyName("compilationSuccess")]
        public bool CompilationSuccess { get; init; }

        [JsonPropertyName("roundTripMatch")]
        public bool RoundTripMatch { get; init; }

        [JsonPropertyName("originalLines")]
        public int OriginalLines { get; init; }

        [JsonPropertyName("roundTrippedLines")]
        public int RoundTrippedLines { get; init; }

        [JsonPropertyName("calorSource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalorSource { get; init; }

        [JsonPropertyName("roundTrippedCSharp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RoundTrippedCSharp { get; init; }

        [JsonPropertyName("differences")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<LineDifference>? Differences { get; init; }

        /// <summary>Envelope schema v1.1 diagnostic entries (conversion issues, Calor1343).</summary>
        [JsonPropertyName("conversionErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? ConversionErrors { get; init; }

        /// <summary>Envelope schema v1.1 diagnostic entries (Calor→C# compile errors).</summary>
        [JsonPropertyName("compilationErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EnvelopeDiagnostic>? CompilationErrors { get; init; }
    }

    private sealed class LineDifference
    {
        [JsonPropertyName("lineNumber")]
        public int LineNumber { get; init; }

        [JsonPropertyName("original")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Original { get; init; }

        [JsonPropertyName("roundTripped")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RoundTripped { get; init; }
    }

    // mode=assess output
    private sealed class AssessOutput
    {
        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("summary")]
        public required string Summary { get; init; }

        [JsonPropertyName("conversionAttempted")]
        public bool ConversionAttempted { get; init; }

        [JsonPropertyName("conversionSucceeded")]
        public bool ConversionSucceeded { get; init; }

        [JsonPropertyName("compilationSucceeded")]
        public bool CompilationSucceeded { get; init; }

        [JsonPropertyName("conversionRate")]
        public double ConversionRate { get; init; }

        [JsonPropertyName("blockers")]
        public required List<AssessBlocker> Blockers { get; init; }

        [JsonPropertyName("totalBlockerInstances")]
        public int TotalBlockerInstances { get; init; }

        [JsonPropertyName("languageGapCount")]
        public int LanguageGapCount { get; init; }

        [JsonPropertyName("converterBugCount")]
        public int ConverterBugCount { get; init; }

        [JsonPropertyName("durationMs")]
        public int DurationMs { get; init; }
    }

    private sealed class AssessBlocker
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("category")]
        public required string Category { get; init; }
    }
}
