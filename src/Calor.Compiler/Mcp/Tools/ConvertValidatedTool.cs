using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool that chains the full convert → auto-fix → diagnose → compat-check pipeline in one call.
/// </summary>
public sealed class ConvertValidatedTool : McpToolBase
{
    public override string Name => "calor_convert_validated";

    public override string Description =>
        "Full validated conversion pipeline: convert C# to Calor, auto-fix parse errors, " +
        "run diagnostics, and verify generated C# compatibility — all in one call. " +
        "IMPORTANT: If the result contains §CSHARP interop blocks, check calor_syntax_lookup " +
        "or calor_feature_support — many C# constructs (foreach, switch, async, yield, structs, " +
        "events, operators, preprocessor directives) have native Calor equivalents.";

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
                    "description": "Path to a C# file to convert (alternative to source for large files)"
                },
                "outputPath": {
                    "type": "string",
                    "description": "Path to write the generated Calor output file (optional)"
                },
                "moduleName": {
                    "type": "string",
                    "description": "Module name for the generated Calor code"
                },
                "mode": {
                    "type": "string",
                    "enum": ["standard", "interop"],
                    "description": "Conversion mode: 'standard' (default) or 'interop'"
                },
                "expectedNamespace": {
                    "type": "string",
                    "description": "Expected namespace in generated C# (for compat check)"
                },
                "expectedPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must appear in generated C#"
                },
                "forbiddenPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must NOT appear in generated C#"
                }
            }
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
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
        var modeStr = GetString(arguments, "mode") ?? "standard";
        var mode = modeStr.Equals("interop", StringComparison.OrdinalIgnoreCase)
            ? ConversionMode.Interop : ConversionMode.Standard;
        var expectedNamespace = GetString(arguments, "expectedNamespace");
        var expectedPatterns = GetStringArray(arguments, "expectedPatterns");
        var forbiddenPatterns = GetStringArray(arguments, "forbiddenPatterns");

        var sw = Stopwatch.StartNew();

        try
        {
            var conversionIssues = new List<ConversionIssueOutput>();
            var autoFixes = new List<string>();
            var diagnosticMessages = new List<string>();
            var compatIssues = new List<string>();

            // Stage 1: Convert
            var options = new ConversionOptions
            {
                ModuleName = moduleName,
                PreserveComments = true,
                AutoGenerateIds = true,
                GracefulFallback = true,
                Explain = false,
                Mode = mode
            };

            var converter = new CSharpToCalorConverter(options);
            var convResult = converter.Convert(source);

            conversionIssues.AddRange(convResult.Issues.Select(i => new ConversionIssueOutput
            {
                Severity = i.Severity.ToString().ToLowerInvariant(),
                Message = i.Message,
                Line = i.Line ?? 0,
                Column = i.Column ?? 0,
                Suggestion = i.Suggestion
            }));

            if (!convResult.Success || string.IsNullOrWhiteSpace(convResult.CalorSource))
            {
                sw.Stop();
                return Task.FromResult(McpToolResult.Json(BuildOutput(
                    success: false, stage: "conversion",
                    calorSource: convResult.CalorSource, generatedCSharp: null,
                    conversionIssues, autoFixes, diagnosticMessages, compatIssues,
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
                        foreach (var error in retryParse.Errors)
                        {
                            diagnosticMessages.Add($"Parse error (after auto-fix): {error}");
                        }
                        return Task.FromResult(McpToolResult.Json(BuildOutput(
                            success: false, stage: "parse",
                            calorSource: fixResult.FixedSource, generatedCSharp: null,
                            conversionIssues, autoFixes, diagnosticMessages, compatIssues,
                            convResult.Context.Stats, sw.Elapsed), isError: true));
                    }
                }
                else
                {
                    sw.Stop();
                    foreach (var error in parseResult.Errors)
                    {
                        diagnosticMessages.Add($"Parse error: {error}");
                    }
                    return Task.FromResult(McpToolResult.Json(BuildOutput(
                        success: false, stage: "parse",
                        calorSource: calorSource, generatedCSharp: null,
                        conversionIssues, autoFixes, diagnosticMessages, compatIssues,
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
                    UnknownCallPolicy = UnknownCallPolicy.Permissive
                };
                var compileResult = Program.Compile(calorSource, "validated-output.calr", compileOptions);

                foreach (var diag in compileResult.Diagnostics.Errors)
                {
                    diagnosticMessages.Add($"error: {diag.Message}");
                }
                foreach (var diag in compileResult.Diagnostics.Warnings)
                {
                    diagnosticMessages.Add($"warning: {diag.Message}");
                }

                if (compileResult.HasErrors)
                {
                    sw.Stop();
                    return Task.FromResult(McpToolResult.Json(BuildOutput(
                        success: false, stage: "diagnose",
                        calorSource: calorSource, generatedCSharp: compileResult.GeneratedCode,
                        conversionIssues, autoFixes, diagnosticMessages, compatIssues,
                        convResult.Context.Stats, sw.Elapsed), isError: true));
                }

                generatedCSharp = compileResult.GeneratedCode;
            }
            catch (Exception ex)
            {
                sw.Stop();
                diagnosticMessages.Add($"Compilation exception: {ex.Message}");
                return Task.FromResult(McpToolResult.Json(BuildOutput(
                    success: false, stage: "compile",
                    calorSource: calorSource, generatedCSharp: null,
                    conversionIssues, autoFixes, diagnosticMessages, compatIssues,
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
                    return Task.FromResult(McpToolResult.Json(BuildOutput(
                        success: false, stage: "compat",
                        calorSource: calorSource, generatedCSharp: generatedCSharp,
                        conversionIssues, autoFixes, diagnosticMessages, compatIssues,
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
                    conversionIssues.Add(new ConversionIssueOutput
                    {
                        Severity = "warning",
                        Message = $"Failed to write output file: {ex.Message}",
                        Line = 0,
                        Column = 0,
                        Suggestion = null
                    });
                }
            }

            // All stages passed
            sw.Stop();
            return Task.FromResult(McpToolResult.Json(BuildOutput(
                success: true, stage: "complete",
                calorSource: calorSource, generatedCSharp: generatedCSharp,
                conversionIssues, autoFixes, diagnosticMessages, compatIssues,
                convResult.Context.Stats, sw.Elapsed)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Validated conversion failed: {ex.Message}"));
        }
    }

    private static ValidatedOutput BuildOutput(
        bool success, string stage,
        string? calorSource, string? generatedCSharp,
        List<ConversionIssueOutput> conversionIssues,
        List<string> autoFixes,
        List<string> diagnostics,
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

    private static List<string> GetStringArray(JsonElement? arguments, string propertyName)
    {
        if (arguments == null || arguments.Value.ValueKind != JsonValueKind.Object)
            return new List<string>();

        if (arguments.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            return prop.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        return new List<string>();
    }

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

        [JsonPropertyName("conversionIssues")]
        public required List<ConversionIssueOutput> ConversionIssues { get; init; }

        [JsonPropertyName("autoFixes")]
        public required List<string> AutoFixes { get; init; }

        [JsonPropertyName("diagnostics")]
        public required List<string> Diagnostics { get; init; }

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
}
