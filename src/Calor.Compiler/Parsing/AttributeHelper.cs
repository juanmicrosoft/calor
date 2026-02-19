using Calor.Compiler.Ast;

namespace Calor.Compiler.Parsing;

/// <summary>
/// Helper for interpreting positional attributes in context.
/// </summary>
public static class AttributeHelper
{
    /// <summary>
    /// Interprets attributes for MODULE/§M: {id:name}
    /// </summary>
    public static (string Id, string Name) InterpretModuleAttributes(AttributeCollection attrs)
    {
        return (attrs["_pos0"] ?? "", attrs["_pos1"] ?? "");
    }

    /// <summary>
    /// Interprets attributes for END_MODULE/§/M: {id}
    /// </summary>
    public static string InterpretEndModuleAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for FUNC/§F: {id:name:visibility}
    /// </summary>
    public static (string Id, string Name, string Visibility) InterpretFuncAttributes(AttributeCollection attrs)
    {
        var visibility = attrs["_pos2"] ?? "";
        if (string.IsNullOrEmpty(visibility))
        {
            visibility = "private";
        }
        else if (visibility == "pub")
        {
            visibility = "public";
        }
        else if (visibility == "pri")
        {
            visibility = "private";
        }
        else if (visibility == "int")
        {
            visibility = "internal";
        }

        return (attrs["_pos0"] ?? "", attrs["_pos1"] ?? "", visibility);
    }

    /// <summary>
    /// Interprets attributes for END_FUNC/§/F: {id}
    /// </summary>
    public static string InterpretEndFuncAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for IN/§I: {type:name:semantic}
    /// </summary>
    public static (string Type, string Name, string? Semantic) InterpretInputAttributes(AttributeCollection attrs)
    {
        var semantic = attrs["_pos2"];
        if (!string.IsNullOrEmpty(semantic) && semantic.StartsWith('#'))
        {
            semantic = ExpandSemanticShortcode(semantic);
        }

        var compactType = attrs["_pos0"] ?? "";
        return (ExpandType(compactType), attrs["_pos1"] ?? "", semantic);
    }

    /// <summary>
    /// Interprets attributes for OUT/§O: {type}
    /// </summary>
    public static string InterpretOutputAttributes(AttributeCollection attrs)
    {
        var compactType = attrs["_pos0"] ?? "";
        return ExpandType(compactType);
    }

    /// <summary>
    /// Interprets attributes for CALL/§C: {target} or {target!}
    /// </summary>
    public static (string Target, bool Fallible) InterpretCallAttributes(AttributeCollection attrs)
    {
        var target = attrs["_pos0"] ?? "";
        var isFallible = target.EndsWith('!');
        if (isFallible)
        {
            target = target[..^1]; // Remove the ! suffix
        }

        return (target, isFallible);
    }

    /// <summary>
    /// Interprets attributes for BIND/§B. Supports multiple formats:
    /// - {name}           - name only, immutable
    /// - {~name}          - name only, mutable (~ prefix)
    /// - {~name:type}     - mutable with type (original format)
    /// - {type:name}      - type first, then name, immutable (skill file format)
    /// - {type:name:mut}  - type first, then name, mutable (skill file format)
    /// </summary>
    public static (string Name, bool Mutable, string? TypeName) InterpretBindAttributes(AttributeCollection attrs)
    {
        var pos0 = attrs["_pos0"] ?? "";
        var pos1 = attrs["_pos1"];
        var pos2 = attrs["_pos2"];

        // Check for skill file format: {type:name:mut}
        if (pos2 == "mut" && !string.IsNullOrEmpty(pos1))
        {
            // Format: {type:name:mut}
            return (pos1, true, ExpandType(pos0));
        }

        // Check for ~ prefix (original mutable format)
        if (pos0.StartsWith('~'))
        {
            // Format: {~name} or {~name:type}
            var name = pos0[1..];
            var typeName = !string.IsNullOrEmpty(pos1) ? ExpandType(pos1) : null;
            return (name, true, typeName);
        }

        // Detect if pos0 is a type and pos1 is a name (skill file format: {type:name})
        // Known primitive types that would indicate type-first format
        // Both must be checked to disambiguate {Type:name} from {name:Type}
        if (!string.IsNullOrEmpty(pos1) && IsLikelyType(pos0) && !IsLikelyType(pos1))
        {
            // Format: {type:name} - immutable
            return (pos1, false, ExpandType(pos0));
        }

        // Original format: {name} or {name:type}
        var typeName2 = !string.IsNullOrEmpty(pos1) ? ExpandType(pos1) : null;
        return (pos0, false, typeName2);
    }

    /// <summary>
    /// Checks if a string is likely a type name (for format detection).
    /// </summary>
    internal static bool IsLikelyType(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Array types start with '[' e.g., [i32], [str]
        if (value.StartsWith('['))
            return true;

        var lower = value.ToLowerInvariant();
        return lower switch
        {
            "i8" or "i16" or "i32" or "i64" => true,
            "u8" or "u16" or "u32" or "u64" => true,
            "f32" or "f64" => true,
            "int" or "float" or "str" or "string" or "bool" or "void" or "char" => true,
            _ => value.StartsWith('?') || value.Contains('!') || value.Contains('<')
                 || IsLikelyPascalCaseType(value)
        };
    }

    /// <summary>
    /// Detects PascalCase type names like ConsoleKeyInfo, StringBuilder, Encoding.
    /// Returns true if the value looks like a .NET type name (starts uppercase,
    /// not a single uppercase letter which could be a variable).
    /// </summary>
    private static bool IsLikelyPascalCaseType(string value)
    {
        // Must start with uppercase letter and have at least 2 chars
        if (value.Length < 2 || !char.IsUpper(value[0]))
            return false;

        // Must contain a lowercase letter (not all-caps abbreviation that could be a variable)
        return value.Any(char.IsLower);
    }

    /// <summary>
    /// Interprets attributes for FOR/§L (loop): {id:var:from:to:step}
    /// </summary>
    public static (string Id, string Var, string From, string To, string Step) InterpretForAttributes(AttributeCollection attrs)
    {
        var step = attrs["_pos4"] ?? "";
        if (string.IsNullOrEmpty(step))
        {
            step = "1";
        }

        return (attrs["_pos0"] ?? "", attrs["_pos1"] ?? "", attrs["_pos2"] ?? "", attrs["_pos3"] ?? "", step);
    }

    /// <summary>
    /// Interprets attributes for IF/§IF: {id}
    /// </summary>
    public static string InterpretIfAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for MATCH/§MATCH: {id}
    /// </summary>
    public static string InterpretMatchAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for END_MATCH/§/MATCH: {id}
    /// </summary>
    public static string InterpretEndMatchAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for REQUIRES/§Q: {message}
    /// </summary>
    public static string? InterpretRequiresAttributes(AttributeCollection attrs)
    {
        // No positional attributes for requires, just the expression
        return null;
    }

    /// <summary>
    /// Interprets attributes for EFFECTS/§E: effect codes
    /// Handles effect codes that may have been split by the colon-based attribute parser.
    /// E.g., §E{fs:r} gets parsed as _pos0="fs", _pos1="r" instead of _pos0="fs:r"
    /// Also handles comma-separated effects like §E{cw,fs:w} which becomes _pos0="cw,fs", _pos1="w"
    /// </summary>
    public static Dictionary<string, string> InterpretEffectsAttributes(AttributeCollection attrs)
    {
        var effects = new Dictionary<string, string>();

        // Known effect prefixes that use colon-based modifiers
        var effectPrefixes = new HashSet<string> { "fs", "net", "db", "env" };
        var effectModifiers = new HashSet<string> { "r", "w", "rw" };

        // First, reconstruct all effect codes from the split positional attributes
        var effectCodes = new List<string>();

        for (int i = 0; ; i++)
        {
            var code = attrs[$"_pos{i}"];
            if (string.IsNullOrEmpty(code)) break;

            // Check if this contains comma-separated effects (e.g., "cw,fs" from §E{cw,fs:w})
            if (code.Contains(','))
            {
                var parts = code.Split(',');
                // Add all but the last part as complete effect codes
                for (int j = 0; j < parts.Length - 1; j++)
                {
                    effectCodes.Add(parts[j]);
                }
                // The last part might be a prefix that needs combining with next positional
                var lastPart = parts[parts.Length - 1];
                if (effectPrefixes.Contains(lastPart.ToLowerInvariant()))
                {
                    var nextCode = attrs[$"_pos{i + 1}"];
                    if (!string.IsNullOrEmpty(nextCode))
                    {
                        // Check if nextCode is a modifier or contains comma-separated values
                        var nextParts = nextCode.Split(',');
                        if (effectModifiers.Contains(nextParts[0].ToLowerInvariant()))
                        {
                            // Combine: "fs" + "w" -> "fs:w"
                            effectCodes.Add($"{lastPart}:{nextParts[0]}");
                            // If there were more parts after the modifier, add them
                            for (int k = 1; k < nextParts.Length; k++)
                            {
                                effectCodes.Add(nextParts[k]);
                            }
                            i++; // Skip the next positional since we consumed it
                        }
                        else
                        {
                            effectCodes.Add(lastPart);
                        }
                    }
                    else
                    {
                        effectCodes.Add(lastPart);
                    }
                }
                else
                {
                    effectCodes.Add(lastPart);
                }
            }
            // Check if this is an effect prefix that was split from its modifier
            // E.g., "fs" followed by "r" should become "fs:r"
            else if (effectPrefixes.Contains(code.ToLowerInvariant()))
            {
                var nextCode = attrs[$"_pos{i + 1}"];
                if (!string.IsNullOrEmpty(nextCode))
                {
                    var nextParts = nextCode.Split(',');
                    if (effectModifiers.Contains(nextParts[0].ToLowerInvariant()))
                    {
                        // Combine the split effect code
                        effectCodes.Add($"{code}:{nextParts[0]}");
                        // Add any remaining comma-separated parts
                        for (int k = 1; k < nextParts.Length; k++)
                        {
                            effectCodes.Add(nextParts[k]);
                        }
                        i++; // Skip the next positional since we consumed it
                    }
                    else
                    {
                        effectCodes.Add(code);
                    }
                }
                else
                {
                    effectCodes.Add(code);
                }
            }
            else
            {
                effectCodes.Add(code);
            }
        }

        // Now process all the reconstructed effect codes
        foreach (var code in effectCodes)
        {
            var (category, value) = ExpandEffectCode(code);
            if (effects.ContainsKey(category))
            {
                // Combine multiple effects in same category
                effects[category] = effects[category] + "," + value;
            }
            else
            {
                effects[category] = value;
            }
        }

        return effects;
    }

    /// <summary>
    /// Expands compact type to full type name.
    /// Handles generic types like List&lt;T&gt;, Dictionary&lt;str, T&gt;.
    /// </summary>
    public static string ExpandType(string compactType)
    {
        if (string.IsNullOrEmpty(compactType))
            return compactType;

        // Handle Calor-style array types: [T] -> ARRAY[element=T]
        // e.g., [i32] -> ARRAY[element=INT]
        if (compactType.StartsWith('[') && compactType.EndsWith(']'))
        {
            var inner = ExpandType(compactType[1..^1]);
            return $"ARRAY[element={inner}]";
        }

        // Handle Option type: ?T -> OPTION[inner=T]
        if (compactType.StartsWith('?'))
        {
            var inner = ExpandType(compactType[1..]);
            return $"OPTION[inner={inner}]";
        }

        // Handle Result type: T!E -> RESULT[ok=T][err=E]
        // But be careful not to match ! inside generic brackets
        var exclamationIndex = FindTopLevelChar(compactType, '!');
        if (exclamationIndex >= 0)
        {
            var ok = ExpandType(compactType[..exclamationIndex]);
            var err = exclamationIndex < compactType.Length - 1
                ? ExpandType(compactType[(exclamationIndex + 1)..])
                : "STRING";
            return $"RESULT[ok={ok}][err={err}]";
        }

        // Handle generic types: List<T>, Dictionary<str, T>
        var genericIndex = compactType.IndexOf('<');
        if (genericIndex > 0 && compactType.EndsWith('>'))
        {
            var baseName = compactType[..genericIndex];
            var argsStr = compactType[(genericIndex + 1)..^1];
            var args = SplitGenericArgs(argsStr);
            var expandedArgs = string.Join(", ", args.Select(ExpandType));
            return $"{baseName}<{expandedArgs}>";
        }

        // Handle array types: T[] or T[,]
        if (compactType.EndsWith("[]") || compactType.EndsWith("[,]"))
        {
            var arrayStart = compactType.LastIndexOf('[');
            var elementType = ExpandType(compactType[..arrayStart]);
            return elementType + compactType[arrayStart..];
        }

        // Primitive type mappings
        return compactType.ToLowerInvariant() switch
        {
            "i8" => "INT[bits=8][signed=true]",
            "i16" => "INT[bits=16][signed=true]",
            "i32" or "int" => "INT",
            "i64" => "INT[bits=64][signed=true]",
            "u8" => "INT[bits=8][signed=false]",
            "u16" => "INT[bits=16][signed=false]",
            "u32" => "INT[bits=32][signed=false]",
            "u64" => "INT[bits=64][signed=false]",
            "f32" => "FLOAT[bits=32]",
            "f64" or "float" => "FLOAT",
            "str" or "string" => "STRING",
            "bool" => "BOOL",
            "void" => "VOID",
            "never" => "NEVER",
            "char" => "CHAR",
            _ => compactType // Pass through unknown types preserving original casing
        };
    }

    /// <summary>
    /// Finds the index of a character at the top level (not inside angle brackets).
    /// Returns -1 if not found.
    /// </summary>
    private static int FindTopLevelChar(string str, char target)
    {
        var depth = 0;
        for (int i = 0; i < str.Length; i++)
        {
            var c = str[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == target && depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Splits generic type arguments respecting nested angle brackets.
    /// E.g., "str, List&lt;T&gt;" → ["str", "List&lt;T&gt;"]
    /// </summary>
    private static List<string> SplitGenericArgs(string argsStr)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var c in argsStr)
        {
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString().Trim());

        return args;
    }

    /// <summary>
    /// Expands effect shortcode to category and value.
    /// </summary>
    public static (string Category, string Value) ExpandEffectCode(string code)
    {
        return code.ToLowerInvariant() switch
        {
            // Console I/O
            "cw" => ("io", "console_write"),
            "cr" => ("io", "console_read"),

            // Filesystem effects
            "fs:r" => ("io", "filesystem_read"),
            "fs:w" => ("io", "filesystem_write"),
            "fs:rw" => ("io", "filesystem_readwrite"),

            // Network effects
            "net:r" => ("io", "network_read"),
            "net:w" => ("io", "network_write"),
            "net:rw" => ("io", "network_readwrite"),

            // Database effects
            "db:r" => ("io", "database_read"),
            "db:w" => ("io", "database_write"),
            "db:rw" => ("io", "database_readwrite"),

            // Environment effects
            "env:r" => ("io", "environment_read"),
            "env:w" => ("io", "environment_write"),

            // System
            "proc" => ("io", "process"),

            // Memory
            "alloc" => ("memory", "allocation"),
            "unsafe" => ("memory", "unsafe"),

            // Non-determinism
            "time" => ("nondeterminism", "time"),
            "rand" => ("nondeterminism", "random"),

            // Mutation/Exception
            "mut" => ("mutation", "heap_write"),
            "throw" => ("exception", "intentional"),

            // Default: treat as io with the code as value
            _ => ("io", code)
        };
    }

    /// <summary>
    /// Expands semantic shortcode to full description.
    /// </summary>
    public static string? ExpandSemanticShortcode(string? shortcode)
    {
        if (string.IsNullOrEmpty(shortcode))
            return null;

        if (!shortcode.StartsWith('#'))
            return shortcode;

        // Handle quoted custom semantics: #"custom description"
        if (shortcode.StartsWith("#\"") && shortcode.EndsWith("\""))
        {
            return shortcode[2..^1];
        }

        // Predefined shortcodes
        return shortcode.ToLowerInvariant() switch
        {
            "#input" => "user input",
            "#dbid" => "database identifier",
            "#errmsg" => "error message",
            "#counter" => "loop counter",
            "#retval" => "return value",
            "#index" => "array index",
            "#count" => "count value",
            "#name" => "name identifier",
            "#path" => "file path",
            "#url" => "URL",
            _ => shortcode[1..] // Remove # and use as-is
        };
    }

    #region Extended Features - Quick Wins

    /// <summary>
    /// Interprets attributes for EXAMPLE/§EX: {id:msg:"message"}
    /// </summary>
    public static (string? Id, string? Message) InterpretExampleAttributes(AttributeCollection attrs)
    {
        var id = attrs["_pos0"];
        var msg = attrs["_pos1"];

        // Handle msg: prefix
        if (!string.IsNullOrEmpty(msg) && msg.StartsWith("msg:"))
        {
            msg = msg[4..];
        }

        return (id, msg);
    }

    /// <summary>
    /// Interprets attributes for TODO/FIXME/HACK: {id:category:priority}
    /// </summary>
    public static (string? Id, string? Category, IssuePriority Priority) InterpretIssueAttributes(AttributeCollection attrs)
    {
        var id = attrs["_pos0"];
        var category = attrs["_pos1"];
        var priority = ParseIssuePriority(attrs["_pos2"]);

        return (id, category, priority);
    }

    /// <summary>
    /// Parses issue priority from string.
    /// </summary>
    public static IssuePriority ParseIssuePriority(string? priorityStr)
    {
        return priorityStr?.ToLowerInvariant() switch
        {
            "low" => IssuePriority.Low,
            "medium" or "med" => IssuePriority.Medium,
            "high" => IssuePriority.High,
            "critical" or "crit" => IssuePriority.Critical,
            _ => IssuePriority.Medium
        };
    }

    #endregion

    #region Extended Features - Core Features

    /// <summary>
    /// Interprets attributes for USES/USEDBY: {dep1, dep2, dep3}
    /// Returns list of dependency targets.
    /// </summary>
    public static IReadOnlyList<string> InterpretDependencyListAttributes(AttributeCollection attrs)
    {
        var dependencies = new List<string>();

        // Collect all positional attributes
        for (int i = 0; ; i++)
        {
            var dep = attrs[$"_pos{i}"];
            if (string.IsNullOrEmpty(dep)) break;
            dependencies.Add(dep);
        }

        return dependencies;
    }

    /// <summary>
    /// Interprets attributes for ASSUME: {category}
    /// </summary>
    public static AssumptionCategory? InterpretAssumeAttributes(AttributeCollection attrs)
    {
        var category = attrs["_pos0"];
        return ParseAssumptionCategory(category);
    }

    /// <summary>
    /// Parses assumption category from string.
    /// </summary>
    public static AssumptionCategory? ParseAssumptionCategory(string? categoryStr)
    {
        return categoryStr?.ToLowerInvariant() switch
        {
            "env" or "environment" => AssumptionCategory.Env,
            "auth" or "authentication" => AssumptionCategory.Auth,
            "data" => AssumptionCategory.Data,
            "timing" or "time" => AssumptionCategory.Timing,
            "resource" or "res" => AssumptionCategory.Resource,
            _ => null
        };
    }

    #endregion

    #region Extended Features - Enhanced Contracts

    /// <summary>
    /// Interprets attributes for COMPLEXITY: {timeComplexity} or {timeComplexity:spaceComplexity}
    /// Pure positional format in v2 syntax.
    /// </summary>
    public static (ComplexityClass? Time, ComplexityClass? Space, bool IsWorstCase, string? Custom) InterpretComplexityAttributes(AttributeCollection attrs)
    {
        // Pure positional: {time} or {time:space}
        var timeVal = attrs["_pos0"];
        var spaceVal = attrs["_pos1"];

        ComplexityClass? timeComplexity = timeVal != null ? ParseComplexityClass(timeVal) : null;
        ComplexityClass? spaceComplexity = spaceVal != null ? ParseComplexityClass(spaceVal) : null;

        return (timeComplexity, spaceComplexity, false, null);
    }

    /// <summary>
    /// Parses complexity class from string like "O(n)", "O(1)", "O(n log n)".
    /// </summary>
    public static ComplexityClass? ParseComplexityClass(string? complexityStr)
    {
        if (string.IsNullOrEmpty(complexityStr))
            return null;

        var normalized = complexityStr.Replace(" ", "").ToLowerInvariant();
        return normalized switch
        {
            "o(1)" => ComplexityClass.O1,
            "o(logn)" or "o(log(n))" => ComplexityClass.OLogN,
            "o(n)" => ComplexityClass.ON,
            "o(nlogn)" or "o(n*logn)" or "o(nlog(n))" => ComplexityClass.ONLogN,
            "o(n^2)" or "o(n2)" => ComplexityClass.ON2,
            "o(n^3)" or "o(n3)" => ComplexityClass.ON3,
            "o(2^n)" or "o(2n)" => ComplexityClass.O2N,
            "o(n!)" => ComplexityClass.ONFact,
            _ => null
        };
    }

    /// <summary>
    /// Interprets attributes for SINCE: {version}
    /// </summary>
    public static string InterpretSinceAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for DEPRECATED: {version:replacement} or {version}
    /// Pure positional format - no prefixes needed in v2 syntax.
    /// </summary>
    public static (string Since, string? Replacement, string? Reason, string? RemovedIn) InterpretDeprecatedAttributes(AttributeCollection attrs)
    {
        // Pure positional: {version} or {version:replacement}
        var since = attrs["_pos0"] ?? "";
        var replacement = attrs["_pos1"];

        // If replacement is empty string, treat as null
        if (string.IsNullOrEmpty(replacement))
            replacement = null;

        return (since, replacement, null, null);
    }

    /// <summary>
    /// Interprets attributes for BREAKING: {version}
    /// </summary>
    public static string InterpretBreakingAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    #endregion

    #region Extended Features - Future Extensions

    /// <summary>
    /// Interprets attributes for DECISION: {id}
    /// </summary>
    public static string InterpretDecisionAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for CONTEXT: {partial} or empty
    /// </summary>
    public static bool InterpretContextAttributes(AttributeCollection attrs)
    {
        var pos0 = attrs["_pos0"];
        return pos0?.Equals("partial", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    /// <summary>
    /// Interprets attributes for FILE: {path}
    /// </summary>
    public static string InterpretFileRefAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for FOCUS: {target}
    /// </summary>
    public static string InterpretFocusAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for LOCK: {agentId}
    /// Pure positional format in v2 syntax.
    /// </summary>
    public static (string AgentId, DateTime? Acquired, DateTime? Expires) InterpretLockAttributes(AttributeCollection attrs)
    {
        // Pure positional: {agentId}
        var agentId = attrs["_pos0"] ?? "";
        return (agentId, null, null);
    }

    /// <summary>
    /// Interprets attributes for AUTHOR: {agentId:taskId}
    /// Pure positional format in v2 syntax.
    /// </summary>
    public static (string AgentId, DateOnly Date, string? TaskId) InterpretAuthorAttributes(AttributeCollection attrs)
    {
        // Pure positional: {agentId} or {agentId:taskId}
        var agentId = attrs["_pos0"] ?? "";
        var taskId = attrs["_pos1"];
        var date = DateOnly.FromDateTime(DateTime.Now);

        // If taskId is empty string, treat as null
        if (string.IsNullOrEmpty(taskId))
            taskId = null;

        return (agentId, date, taskId);
    }

    /// <summary>
    /// Interprets attributes for TASK: {taskId}
    /// </summary>
    public static string InterpretTaskRefAttributes(AttributeCollection attrs)
    {
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for DATE: {date}
    /// </summary>
    public static DateOnly? InterpretDateAttributes(AttributeCollection attrs)
    {
        return TryParseDateOnly(attrs["_pos0"]);
    }

    private static DateTime? TryParseDateTime(string? dateTimeStr)
    {
        if (string.IsNullOrEmpty(dateTimeStr))
            return null;

        if (DateTime.TryParse(dateTimeStr, out var result))
            return result;

        return null;
    }

    private static DateOnly? TryParseDateOnly(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return null;

        if (DateOnly.TryParse(dateStr, out var result))
            return result;

        return null;
    }

    #endregion
}
