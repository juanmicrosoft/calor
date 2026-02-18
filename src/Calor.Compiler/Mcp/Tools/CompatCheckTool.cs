using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for verifying generated C# is API-compatible with original.
/// Checks namespace preservation, enum values, and attribute emission.
/// </summary>
public sealed class CompatCheckTool : McpToolBase
{
    public override string Name => "calor_compile_check_compat";

    public override string Description =>
        "Verify generated C# is API-compatible with original. " +
        "Checks namespace preservation, enum values, and attribute emission.";

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to compile and check"
                },
                "expectedNamespace": {
                    "type": "string",
                    "description": "Expected namespace in generated code (e.g., 'Calor.Runtime')"
                },
                "expectedPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must appear in generated code (e.g., 'AttributeTargets.Method')"
                },
                "forbiddenPatterns": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Patterns that must NOT appear in generated code (e.g., '\"AttributeTargets.Method\"')"
                }
            },
            "required": ["source"]
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));
        }

        var expectedNamespace = GetString(arguments, "expectedNamespace");
        var expectedPatterns = GetStringArray(arguments, "expectedPatterns");
        var forbiddenPatterns = GetStringArray(arguments, "forbiddenPatterns");

        try
        {
            // Compile the Calor source to C#
            var compileResult = Program.Compile(source, "mcp-input.calr", new CompilationOptions());

            if (compileResult.HasErrors)
            {
                var output = new CompatCheckOutput
                {
                    Compatible = false,
                    Issues = compileResult.Diagnostics
                        .Where(d => d.IsError)
                        .Select(d => $"Compilation error: {d.Message}")
                        .ToList(),
                    GeneratedCode = null
                };
                return Task.FromResult(McpToolResult.Json(output, isError: true));
            }

            var generatedCode = compileResult.GeneratedCode ?? "";
            var issues = new List<string>();

            // Check namespace preservation
            if (!string.IsNullOrEmpty(expectedNamespace))
            {
                var namespacePattern = $@"namespace\s+{Regex.Escape(expectedNamespace)}\b";
                if (!Regex.IsMatch(generatedCode, namespacePattern))
                {
                    issues.Add($"Expected namespace '{expectedNamespace}' not found in generated code");
                }
            }

            // Check expected patterns
            foreach (var pattern in expectedPatterns)
            {
                if (!generatedCode.Contains(pattern))
                {
                    issues.Add($"Expected pattern '{pattern}' not found in generated code");
                }
            }

            // Check forbidden patterns
            foreach (var pattern in forbiddenPatterns)
            {
                if (generatedCode.Contains(pattern))
                {
                    issues.Add($"Forbidden pattern '{pattern}' found in generated code");
                }
            }

            var result = new CompatCheckOutput
            {
                Compatible = issues.Count == 0,
                Issues = issues,
                GeneratedCode = generatedCode
            };

            return Task.FromResult(McpToolResult.Json(result, isError: issues.Count > 0));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Compatibility check failed: {ex.Message}"));
        }
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

    private sealed class CompatCheckOutput
    {
        [JsonPropertyName("compatible")]
        public bool Compatible { get; init; }

        [JsonPropertyName("issues")]
        public required List<string> Issues { get; init; }

        [JsonPropertyName("generatedCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GeneratedCode { get; init; }
    }
}
