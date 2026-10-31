using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool that auto-fixes common, mechanically fixable errors in Calor source code.
/// Targets patterns produced by the C# → Calor converter that need manual correction.
/// </summary>
public sealed class FixTool : McpToolBase
{
    public override string Name => "calor_fix";

    public override string Description =>
        "Auto-fix NEW{object} placeholders using their type context. Declaration names, call targets and sidecar identities are never rewritten.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "source": {
                    "type": "string",
                    "description": "Calor source code to fix"
                },
                "filePath": {
                    "type": "string",
                    "description": "Path to a .calr file to fix. If 'source' is also provided, 'source' takes precedence."
                },
                "errors": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Specific fix rules. Supported value: 'new_object'. Default: apply all supported rules. Source ID conflict repair is not supported."
                }
            },
            "additionalProperties": false
        }
        """;

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var source = GetString(arguments, "source");
        var filePath = GetString(arguments, "filePath");
        var errorCodes = GetStringArray(arguments, "errors");
        var unsupportedRule = errorCodes.FirstOrDefault(
            rule => !rule.Equals("new_object", StringComparison.OrdinalIgnoreCase));
        if (unsupportedRule != null)
        {
            return McpToolResult.Error(unsupportedRule.Equals("id_conflicts", StringComparison.OrdinalIgnoreCase)
                ? "Source ID conflict repair is no longer supported. Declaration identities live in .calor/ids.json; calor_fix does not rename declarations or call targets."
                : $"Unsupported fix rule: '{unsupportedRule}'. Supported rule: 'new_object'.");
        }

        // Resolve source from file if needed
        if (string.IsNullOrEmpty(source))
        {
            if (string.IsNullOrEmpty(filePath))
                return McpToolResult.Error("Either 'source' or 'filePath' must be provided.");

            var pathError = ValidatePath(filePath, "filePath");
            if (pathError != null) return pathError;

            if (!File.Exists(filePath))
                return McpToolResult.Error($"File not found: {filePath}");

            source = await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        var sizeError = ValidateSourceSize(source);
        if (sizeError != null) return sizeError;

        string current;
        List<FixEntry> fixes;
        try
        {
            (current, fixes) = FixNewObject(source);
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Fix failed: {ex.Message}");
        }

        var output = new FixToolOutput
        {
            Success = true,
            FixedSource = current,
            WasModified = fixes.Count > 0,
            FixCount = fixes.Count,
            Fixes = fixes.Count > 0 ? fixes : null
        };

        return McpToolResult.Json(output);
    }

    // ── Fix passes ──────────────────────────────────────────────────

    /// <summary>
    /// Replaces §NEW{object} with the correct concrete type when the context
    /// provides enough information to infer the type.
    /// Contexts: §B{name:TYPE} bindings, §FLD{TYPE:name:...} fields,
    /// §TH (throw) → infer Exception, and return-type from containing §O{TYPE}.
    /// </summary>
    internal static (string Result, List<FixEntry> Fixes) FixNewObject(string source)
    {
        var fixes = new List<FixEntry>();
        var lines = source.Split('\n');
        string? lastReturnType = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Track return type from §O{TYPE} declarations
            var outputMatch = Regex.Match(line, @"§O\{([^}]+)\}");
            if (outputMatch.Success)
                lastReturnType = outputMatch.Groups[1].Value;

            // Reset return type at function boundaries
            if (Regex.IsMatch(line, @"§F\{"))
                lastReturnType = null;

            if (!line.Contains("§NEW{object}"))
                continue;

            string? inferredType = null;

            // Context 1: §B{name:TYPE} ... §NEW{object}
            // Binding with explicit type annotation on same line
            var bindMatch = Regex.Match(line, @"§B\{[^}]*:([A-Z]\w*(?:<[^>]+>)?)\}\s.*§NEW\{object\}");
            if (bindMatch.Success)
                inferredType = bindMatch.Groups[1].Value;

            // Context 2: §FLD{TYPE:name:vis} on a preceding line, §NEW{object} on this line
            if (inferredType == null && i > 0)
            {
                for (var j = i - 1; j >= Math.Max(0, i - 5); j--)
                {
                    var fldMatch = Regex.Match(lines[j], @"§FLD\{([^:}]+):");
                    if (fldMatch.Success)
                    {
                        inferredType = MapCalorType(fldMatch.Groups[1].Value);
                        break;
                    }
                }
            }

            // Context 3: §TH (throw) line → infer Exception
            if (inferredType == null && Regex.IsMatch(line, @"§TH\s"))
                inferredType = "Exception";

            // Context 4: Return line in a function with known return type
            if (inferredType == null && Regex.IsMatch(line, @"§R\s") && lastReturnType != null)
                inferredType = MapCalorType(lastReturnType);

            if (inferredType != null && inferredType != "object" && inferredType != "void")
            {
                var newLine = line.Replace("§NEW{object}", $"§NEW{{{inferredType}}}");
                // Also fix matching closing tags
                newLine = newLine.Replace("§/NEW{object}", $"§/NEW{{{inferredType}}}");
                if (newLine != line)
                {
                    lines[i] = newLine;
                    fixes.Add(new FixEntry
                    {
                        Rule = "new_object",
                        Line = i + 1,
                        Description = $"Replaced §NEW{{object}} with §NEW{{{inferredType}}}"
                    });
                }
            }
        }

        return (string.Join("\n", lines), fixes);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Maps Calor short type names to C# type names for §NEW inference.</summary>
    private static string MapCalorType(string calorType) => calorType switch
    {
        "i32" => "int",
        "i64" => "long",
        "f32" => "float",
        "f64" => "double",
        "str" => "string",
        "bool" => "bool",
        _ => calorType
    };

    // ── Output DTOs ─────────────────────────────────────────────────

    private sealed class FixToolOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("fixedSource")]
        public required string FixedSource { get; init; }

        [JsonPropertyName("wasModified")]
        public bool WasModified { get; init; }

        [JsonPropertyName("fixCount")]
        public int FixCount { get; init; }

        [JsonPropertyName("fixes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FixEntry>? Fixes { get; init; }
    }
}

/// <summary>
/// Describes a single fix applied by the FixTool.
/// </summary>
public sealed class FixEntry
{
    [JsonPropertyName("rule")]
    public required string Rule { get; init; }

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}
