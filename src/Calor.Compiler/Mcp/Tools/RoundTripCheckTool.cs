using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool that validates C#→Calor→C# round-trip fidelity.
/// </summary>
public sealed class RoundTripCheckTool : McpToolBase
{
    public override string Name => "calor_roundtrip_check";

    public override int TimeoutSeconds => 120;

    public override string Description =>
        "Validate C#→Calor→C# round-trip fidelity. Converts C# to Calor, compiles back to C#, " +
        "and reports whether the round-tripped output matches the original (after whitespace normalization).";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "C# source code to round-trip"
                },
                "moduleName": {
                    "type": "string",
                    "description": "Module name for conversion (default: RoundTrip)"
                }
            },
            "required": ["source"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: 'source'"));
        }

        var moduleName = GetString(arguments, "moduleName") ?? "RoundTrip";

        var conversionErrors = new List<string>();
        var compilationErrors = new List<string>();
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
                Mode = ConversionMode.Interop
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
                foreach (var issue in result.Issues)
                {
                    conversionErrors.Add($"L{issue.Line ?? 0}: {issue.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            conversionErrors.Add($"Conversion exception: {ex.Message}");
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
                    UnknownCallPolicy = Effects.UnknownCallPolicy.Permissive,
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
                    foreach (var diag in compileResult.Diagnostics.Errors)
                    {
                        compilationErrors.Add($"[{diag.Code}] L{diag.Span.Line}: {diag.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                compilationErrors.Add($"Compilation exception: {ex.Message}");
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

    private static string NormalizeWhitespace(string text)
    {
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

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

        [JsonPropertyName("conversionErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ConversionErrors { get; init; }

        [JsonPropertyName("compilationErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? CompilationErrors { get; init; }
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
}
