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
        "Auto-fix common Calor compilation errors. Fixes NEW{object} placeholders, arrow-body multi-statement issues, and ID conflicts. Non-destructive — unfixable code is left as-is.";

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
                    "description": "Specific error codes to fix. Values: 'new_object', 'arrow_multi_statement', 'id_conflicts'. Default: fix all known errors."
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

        var fixAll = errorCodes.Count == 0;
        var fixes = new List<FixEntry>();
        var current = source;

        try
        {
            if (fixAll || errorCodes.Contains("new_object", StringComparer.OrdinalIgnoreCase))
            {
                var (result, applied) = FixNewObject(current);
                current = result;
                fixes.AddRange(applied);
            }

            if (fixAll || errorCodes.Contains("arrow_multi_statement", StringComparer.OrdinalIgnoreCase))
            {
                var (result, applied) = FixArrowMultiStatement(current);
                current = result;
                fixes.AddRange(applied);
            }

            if (fixAll || errorCodes.Contains("id_conflicts", StringComparer.OrdinalIgnoreCase))
            {
                var (result, applied) = FixIdConflicts(current);
                current = result;
                fixes.AddRange(applied);
            }
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

            // Context 1: §B{name:id:varName:TYPE} ... §NEW{object}
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

    /// <summary>
    /// Detects arrow-body (→) functions with multiple statements (semicolons or multiple §-tags)
    /// and converts them to block bodies.
    /// </summary>
    internal static (string Result, List<FixEntry> Fixes) FixArrowMultiStatement(string source)
    {
        var fixes = new List<FixEntry>();
        var lines = source.Split('\n');
        var result = new List<string>(lines.Length + 10);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Match: §IF{id} (condition) → stmt1 → stmt2  (multiple → on one line after condition)
            // or: §F/§FN body with → that contains multiple §-tagged statements
            var arrowMatch = Regex.Match(line, @"^(\s*)(§(?:IF|EI)\{([^}]+)\}\s*\([^)]*\))\s*→\s*(.+)$");
            if (arrowMatch.Success)
            {
                var indent = arrowMatch.Groups[1].Value;
                var header = arrowMatch.Groups[2].Value;
                var id = arrowMatch.Groups[3].Value;
                var body = arrowMatch.Groups[4].Value;

                // Check if body has multiple statements (multiple §-tagged items separated by spaces)
                var stmtCount = CountStatements(body);
                if (stmtCount > 1)
                {
                    var statements = SplitStatements(body);
                    result.Add($"{indent}{header}");
                    foreach (var stmt in statements)
                        result.Add($"{indent}  {stmt.Trim()}");
                    result.Add($"{indent}§/I{{{id}}}");

                    fixes.Add(new FixEntry
                    {
                        Rule = "arrow_multi_statement",
                        Line = i + 1,
                        Description = $"Converted arrow body with {stmtCount} statements to block"
                    });
                    continue;
                }
            }

            result.Add(line);
        }

        return (string.Join("\n", result), fixes);
    }

    /// <summary>
    /// Detects and renumbers conflicting/duplicate IDs within a module.
    /// Scans for declarations (§F, §M, §C, §IF, etc.) and renumbers duplicates.
    /// </summary>
    internal static (string Result, List<FixEntry> Fixes) FixIdConflicts(string source)
    {
        var fixes = new List<FixEntry>();
        var seenIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lines = source.Split('\n');

        // Pattern matches opening tags: §F{id:...}, §M{id:...}, §C{id:...}, §IF{id}, etc.
        var declPattern = new Regex(@"§(F|M|C|IF|EI|EL|L|W|SW|SC|TR|CA|FI)\{([^:}]+)");

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var matches = declPattern.Matches(line);

            foreach (Match match in matches)
            {
                var kind = match.Groups[1].Value;
                var id = match.Groups[2].Value;

                if (!seenIds.TryGetValue(id, out var count))
                {
                    seenIds[id] = 1;
                    continue;
                }

                // Duplicate found — generate new ID
                seenIds[id] = count + 1;
                var prefix = GetIdPrefix(kind);
                var newId = GenerateUniqueId(prefix, seenIds);
                seenIds[newId] = 1;

                // Replace this ID in both opening and closing tags
                var oldOpening = $"§{kind}{{{id}";
                var newOpening = $"§{kind}{{{newId}";
                var oldClosing = GetClosingTag(kind, id);
                var newClosing = GetClosingTag(kind, newId);

                lines[i] = lines[i].Replace(oldOpening, newOpening);

                // Find and replace corresponding closing tag
                for (var j = i; j < lines.Length; j++)
                {
                    if (lines[j].Contains(oldClosing))
                    {
                        lines[j] = lines[j].Replace(oldClosing, newClosing);
                        break;
                    }
                }

                fixes.Add(new FixEntry
                {
                    Rule = "id_conflicts",
                    Line = i + 1,
                    Description = $"Renumbered duplicate ID '{id}' → '{newId}'"
                });
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

    /// <summary>Counts statement-level §-tags in a body string.</summary>
    private static int CountStatements(string body)
    {
        return Regex.Matches(body, @"§(?:B|R|C|IF|TH|ASSIGN)\b").Count;
    }

    /// <summary>Splits a body string at statement boundaries (§-tagged items).</summary>
    private static List<string> SplitStatements(string body)
    {
        var parts = new List<string>();
        var stmtStarts = Regex.Matches(body, @"§(?:B|R|C|IF|TH|ASSIGN)");

        for (var i = 0; i < stmtStarts.Count; i++)
        {
            var start = stmtStarts[i].Index;
            var end = i + 1 < stmtStarts.Count ? stmtStarts[i + 1].Index : body.Length;
            parts.Add(body[start..end]);
        }

        return parts.Count > 0 ? parts : [body];
    }

    private static string GetIdPrefix(string kind) => kind switch
    {
        "F" => "f",
        "M" => "m",
        "C" => "c",
        "IF" or "EI" or "EL" => "if",
        "L" => "l",
        "W" => "w",
        "SW" => "sw",
        "SC" => "sc",
        "TR" => "tr",
        "CA" => "ca",
        "FI" => "fi",
        _ => "x"
    };

    private static string GenerateUniqueId(string prefix, Dictionary<string, int> seenIds)
    {
        for (var n = 1; n < 10000; n++)
        {
            var candidate = $"{prefix}{n}";
            if (!seenIds.ContainsKey(candidate))
                return candidate;
        }
        return $"{prefix}{Guid.NewGuid().ToString("N")[..6]}";
    }

    private static string GetClosingTag(string kind, string id) => kind switch
    {
        "F" => $"§/F{{{id}}}",
        "M" => $"§/M{{{id}}}",
        "C" => $"§/C{{{id}}}",
        "IF" or "EI" or "EL" => $"§/I{{{id}}}",
        "L" => $"§/L{{{id}}}",
        "W" => $"§/W{{{id}}}",
        "SW" => $"§/SW{{{id}}}",
        "SC" => $"§/SC{{{id}}}",
        "TR" => $"§/TR{{{id}}}",
        "CA" => $"§/CA{{{id}}}",
        "FI" => $"§/FI{{{id}}}",
        _ => $"§/{kind}{{{id}}}"
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
