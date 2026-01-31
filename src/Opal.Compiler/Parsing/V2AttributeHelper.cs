using Opal.Compiler.Ast;

namespace Opal.Compiler.Parsing;

/// <summary>
/// Helper for interpreting v2 positional attributes in context.
/// Maps positional attributes to their v1 named equivalents.
/// </summary>
public static class V2AttributeHelper
{
    /// <summary>
    /// Interprets attributes for MODULE/§M: [id:name]
    /// </summary>
    public static (string Id, string Name) InterpretModuleAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return (v1Id, attrs["name"] ?? "");
        }

        // v2 positional format: [id:name]
        return (attrs["_pos0"] ?? "", attrs["_pos1"] ?? "");
    }

    /// <summary>
    /// Interprets attributes for END_MODULE/§/M: [id]
    /// </summary>
    public static string InterpretEndModuleAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return v1Id;
        }

        // v2 positional format: [id]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for FUNC/§F: [id:name:visibility]
    /// </summary>
    public static (string Id, string Name, string Visibility) InterpretFuncAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return (v1Id, attrs["name"] ?? "", attrs["visibility"] ?? "private");
        }

        // v2 positional format: [id:name:visibility] or [id:name] (private default)
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
    /// Interprets attributes for END_FUNC/§/F: [id]
    /// </summary>
    public static string InterpretEndFuncAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return v1Id;
        }

        // v2 positional format: [id]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for IN/§I: [type:name:semantic]
    /// </summary>
    public static (string Type, string Name, string? Semantic) InterpretInputAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Type = attrs["type"];
        if (!string.IsNullOrEmpty(v1Type))
        {
            return (v1Type, attrs["name"] ?? "", attrs["semantic"]);
        }

        // v2 positional format: [type:name:semantic] or [type:name]
        var semantic = attrs["_pos2"];
        if (!string.IsNullOrEmpty(semantic) && semantic.StartsWith('#'))
        {
            semantic = ExpandSemanticShortcode(semantic);
        }

        var compactType = attrs["_pos0"] ?? "";
        return (ExpandType(compactType), attrs["_pos1"] ?? "", semantic);
    }

    /// <summary>
    /// Interprets attributes for OUT/§O: [type]
    /// </summary>
    public static string InterpretOutputAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Type = attrs["type"];
        if (!string.IsNullOrEmpty(v1Type))
        {
            return v1Type;
        }

        // v2 positional format: [type]
        var compactType = attrs["_pos0"] ?? "";
        return ExpandType(compactType);
    }

    /// <summary>
    /// Interprets attributes for CALL/§C: [target] or [target!]
    /// </summary>
    public static (string Target, bool Fallible) InterpretCallAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Target = attrs["target"];
        if (!string.IsNullOrEmpty(v1Target))
        {
            var fallible = attrs["fallible"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
            return (v1Target, fallible);
        }

        // v2 positional format: [target] or [target!]
        var target = attrs["_pos0"] ?? "";
        var isFallible = target.EndsWith('!');
        if (isFallible)
        {
            target = target[..^1]; // Remove the ! suffix
        }

        return (target, isFallible);
    }

    /// <summary>
    /// Interprets attributes for BIND/§B: [name] or [~name]
    /// </summary>
    public static (string Name, bool Mutable) InterpretBindAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Name = attrs["name"];
        if (!string.IsNullOrEmpty(v1Name))
        {
            var mutable = attrs["mutable"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
            return (v1Name, mutable);
        }

        // v2 positional format: [name] or [~name]
        var name = attrs["_pos0"] ?? "";
        var isMutable = name.StartsWith('~');
        if (isMutable)
        {
            name = name[1..]; // Remove the ~ prefix
        }

        return (name, isMutable);
    }

    /// <summary>
    /// Interprets attributes for FOR/§L (loop): [id:var:from:to:step]
    /// </summary>
    public static (string Id, string Var, string From, string To, string Step) InterpretForAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return (v1Id, attrs["var"] ?? "", attrs["from"] ?? "", attrs["to"] ?? "", attrs["step"] ?? "1");
        }

        // v2 positional format: [id:var:from:to:step] or [id:var:from:to]
        var step = attrs["_pos4"] ?? "";
        if (string.IsNullOrEmpty(step))
        {
            step = "1";
        }

        return (attrs["_pos0"] ?? "", attrs["_pos1"] ?? "", attrs["_pos2"] ?? "", attrs["_pos3"] ?? "", step);
    }

    /// <summary>
    /// Interprets attributes for IF/§I: [id]
    /// </summary>
    public static string InterpretIfAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return v1Id;
        }

        // v2 positional format: [id]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for MATCH/§MATCH: {id}
    /// </summary>
    public static string InterpretMatchAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return v1Id;
        }

        // v2 positional format: {id}
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for END_MATCH/§/MATCH: {id}
    /// </summary>
    public static string InterpretEndMatchAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return v1Id;
        }

        // v2 positional format: {id}
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for REQUIRES/§Q: [message]
    /// </summary>
    public static string? InterpretRequiresAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Msg = attrs["message"];
        if (!string.IsNullOrEmpty(v1Msg))
        {
            return v1Msg;
        }

        // v2 doesn't have positional attributes for requires, just the expression
        return null;
    }

    /// <summary>
    /// Interprets attributes for EFFECTS/§E: effect codes
    /// </summary>
    public static Dictionary<string, string> InterpretEffectsAttributes(AttributeCollection attrs)
    {
        var effects = new Dictionary<string, string>();

        // Check for v1 format first
        var v1Io = attrs["io"];
        if (!string.IsNullOrEmpty(v1Io))
        {
            effects["io"] = v1Io;
            return effects;
        }

        // v2 format: effect codes like cw, cr, fw, fr
        for (int i = 0; ; i++)
        {
            var code = attrs[$"_pos{i}"];
            if (string.IsNullOrEmpty(code)) break;

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
    /// Expands v2 compact type to v1 full type name.
    /// </summary>
    public static string ExpandType(string compactType)
    {
        if (string.IsNullOrEmpty(compactType))
            return compactType;

        // Handle Option type: ?T -> OPTION[inner=T]
        if (compactType.StartsWith('?'))
        {
            var inner = ExpandType(compactType[1..]);
            return $"OPTION[inner={inner}]";
        }

        // Handle Result type: T!E -> RESULT[ok=T][err=E]
        if (compactType.Contains('!'))
        {
            var parts = compactType.Split('!', 2);
            var ok = ExpandType(parts[0]);
            var err = parts.Length > 1 ? ExpandType(parts[1]) : "STRING";
            return $"RESULT[ok={ok}][err={err}]";
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
    /// Expands v2 effect shortcode to category and value.
    /// </summary>
    public static (string Category, string Value) ExpandEffectCode(string code)
    {
        return code.ToLowerInvariant() switch
        {
            // Console I/O
            "cw" => ("io", "console_write"),
            "cr" => ("io", "console_read"),

            // File I/O
            "fw" => ("io", "file_write"),
            "fr" => ("io", "file_read"),
            "fd" => ("io", "file_delete"),

            // Network
            "net" => ("io", "network"),
            "http" => ("io", "http"),

            // Database
            "db" => ("io", "database"),
            "dbr" => ("io", "database_read"),
            "dbw" => ("io", "database_write"),

            // System
            "env" => ("io", "environment"),
            "proc" => ("io", "process"),

            // Memory/Resources
            "alloc" => ("memory", "allocation"),

            // Non-determinism
            "time" => ("nondeterminism", "time"),
            "rand" => ("nondeterminism", "random"),

            // Default: treat as io with the code as value
            _ => ("io", code)
        };
    }

    /// <summary>
    /// Expands v2 semantic shortcode to full description.
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

    #region Extended Features - Phase 1: Quick Wins

    /// <summary>
    /// Interprets attributes for EXAMPLE/§EX: [id:msg:"message"]
    /// </summary>
    public static (string? Id, string? Message) InterpretExampleAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return (v1Id, attrs["msg"] ?? attrs["message"]);
        }

        // v2 positional format: [id:msg:"message"] or [id] or empty
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
    /// Interprets attributes for TODO/FIXME/HACK: [id:category:priority]
    /// </summary>
    public static (string? Id, string? Category, IssuePriority Priority) InterpretIssueAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            var v1Priority = ParseIssuePriority(attrs["priority"]);
            return (v1Id, attrs["category"], v1Priority);
        }

        // v2 positional format: [id:category:priority] or [id:category] or [id]
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

    #region Extended Features - Phase 2: Core Features

    /// <summary>
    /// Interprets attributes for USES/USEDBY: [dep1, dep2, dep3]
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
    /// Interprets attributes for ASSUME: [category]
    /// </summary>
    public static AssumptionCategory? InterpretAssumeAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Cat = attrs["category"];
        if (!string.IsNullOrEmpty(v1Cat))
        {
            return ParseAssumptionCategory(v1Cat);
        }

        // v2 positional format: [category]
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

    #region Extended Features - Phase 3: Enhanced Contracts

    /// <summary>
    /// Interprets attributes for COMPLEXITY: [time:O(n)][space:O(1)] or [worst:time:O(n)]
    /// </summary>
    public static (ComplexityClass? Time, ComplexityClass? Space, bool IsWorstCase, string? Custom) InterpretComplexityAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Time = attrs["time"];
        if (!string.IsNullOrEmpty(v1Time))
        {
            var time = ParseComplexityClass(v1Time);
            var space = ParseComplexityClass(attrs["space"]);
            var worst = attrs["worst"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
            return (time, space, worst, attrs["custom"]);
        }

        // v2 format: [time:O(n)][space:O(1)] or [worst:time:O(n)]
        // Check for positional attributes
        ComplexityClass? timeComplexity = null;
        ComplexityClass? spaceComplexity = null;
        bool isWorstCase = false;
        string? custom = null;

        for (int i = 0; ; i++)
        {
            var val = attrs[$"_pos{i}"];
            if (string.IsNullOrEmpty(val)) break;

            if (val.StartsWith("worst:", StringComparison.OrdinalIgnoreCase))
            {
                isWorstCase = true;
                val = val[6..];
            }

            if (val.StartsWith("time:", StringComparison.OrdinalIgnoreCase))
            {
                timeComplexity = ParseComplexityClass(val[5..]);
            }
            else if (val.StartsWith("space:", StringComparison.OrdinalIgnoreCase))
            {
                spaceComplexity = ParseComplexityClass(val[6..]);
            }
            else
            {
                // Check if it's a complexity class without prefix (assume time)
                var parsed = ParseComplexityClass(val);
                if (parsed != null)
                {
                    if (timeComplexity == null)
                        timeComplexity = parsed;
                    else if (spaceComplexity == null)
                        spaceComplexity = parsed;
                }
                else
                {
                    custom = val;
                }
            }
        }

        return (timeComplexity, spaceComplexity, isWorstCase, custom);
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
    /// Interprets attributes for SINCE: [version]
    /// </summary>
    public static string InterpretSinceAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Version = attrs["version"];
        if (!string.IsNullOrEmpty(v1Version))
        {
            return v1Version;
        }

        // v2 positional format: [version]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for DEPRECATED: [since:version][use:replacement][reason:"reason"]
    /// </summary>
    public static (string Since, string? Replacement, string? Reason, string? RemovedIn) InterpretDeprecatedAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Since = attrs["since"];
        if (!string.IsNullOrEmpty(v1Since))
        {
            return (v1Since, attrs["use"] ?? attrs["replacement"], attrs["reason"], attrs["removed"]);
        }

        // v2 format: parse key:value pairs from positional attributes
        string since = "";
        string? replacement = null;
        string? reason = null;
        string? removedIn = null;

        for (int i = 0; ; i++)
        {
            var val = attrs[$"_pos{i}"];
            if (string.IsNullOrEmpty(val)) break;

            if (val.StartsWith("since:", StringComparison.OrdinalIgnoreCase))
                since = val[6..];
            else if (val.StartsWith("use:", StringComparison.OrdinalIgnoreCase))
                replacement = val[4..];
            else if (val.StartsWith("reason:", StringComparison.OrdinalIgnoreCase))
                reason = val[7..].Trim('"');
            else if (val.StartsWith("removed:", StringComparison.OrdinalIgnoreCase))
                removedIn = val[8..];
            else if (i == 0 && !val.Contains(':'))
                since = val; // First positional without prefix is version
        }

        return (since, replacement, reason, removedIn);
    }

    /// <summary>
    /// Interprets attributes for BREAKING: [version]
    /// </summary>
    public static string InterpretBreakingAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Version = attrs["version"];
        if (!string.IsNullOrEmpty(v1Version))
        {
            return v1Version;
        }

        // v2 positional format: [version]
        return attrs["_pos0"] ?? "";
    }

    #endregion

    #region Extended Features - Phase 4: Future Extensions

    /// <summary>
    /// Interprets attributes for DECISION: [id]
    /// </summary>
    public static string InterpretDecisionAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Id = attrs["id"];
        if (!string.IsNullOrEmpty(v1Id))
        {
            return v1Id;
        }

        // v2 positional format: [id]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for CONTEXT: [partial] or empty
    /// </summary>
    public static bool InterpretContextAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Partial = attrs["partial"];
        if (!string.IsNullOrEmpty(v1Partial))
        {
            return v1Partial.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // v2 positional format: [partial] means partial=true
        var pos0 = attrs["_pos0"];
        return pos0?.Equals("partial", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    /// <summary>
    /// Interprets attributes for FILE: [path]
    /// </summary>
    public static string InterpretFileRefAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Path = attrs["path"];
        if (!string.IsNullOrEmpty(v1Path))
        {
            return v1Path;
        }

        // v2 positional format: [path]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for FOCUS: [target]
    /// </summary>
    public static string InterpretFocusAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Target = attrs["target"];
        if (!string.IsNullOrEmpty(v1Target))
        {
            return v1Target;
        }

        // v2 positional format: [target]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for LOCK: [agent:id][expires:datetime]
    /// </summary>
    public static (string AgentId, DateTime? Acquired, DateTime? Expires) InterpretLockAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Agent = attrs["agent"];
        if (!string.IsNullOrEmpty(v1Agent))
        {
            return (v1Agent, TryParseDateTime(attrs["acquired"]), TryParseDateTime(attrs["expires"]));
        }

        // v2 format: parse key:value pairs from positional attributes
        string agentId = "";
        DateTime? acquired = null;
        DateTime? expires = null;

        for (int i = 0; ; i++)
        {
            var val = attrs[$"_pos{i}"];
            if (string.IsNullOrEmpty(val)) break;

            if (val.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
                agentId = val[6..];
            else if (val.StartsWith("acquired:", StringComparison.OrdinalIgnoreCase))
                acquired = TryParseDateTime(val[9..]);
            else if (val.StartsWith("expires:", StringComparison.OrdinalIgnoreCase))
                expires = TryParseDateTime(val[8..]);
        }

        return (agentId, acquired, expires);
    }

    /// <summary>
    /// Interprets attributes for AUTHOR: [agent:id][date:date][task:taskId]
    /// </summary>
    public static (string AgentId, DateOnly Date, string? TaskId) InterpretAuthorAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Agent = attrs["agent"];
        if (!string.IsNullOrEmpty(v1Agent))
        {
            var v1Date = TryParseDateOnly(attrs["date"]) ?? DateOnly.FromDateTime(DateTime.Now);
            return (v1Agent, v1Date, attrs["task"]);
        }

        // v2 format: parse key:value pairs from positional attributes
        string agentId = "";
        DateOnly date = DateOnly.FromDateTime(DateTime.Now);
        string? taskId = null;

        for (int i = 0; ; i++)
        {
            var val = attrs[$"_pos{i}"];
            if (string.IsNullOrEmpty(val)) break;

            if (val.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
                agentId = val[6..];
            else if (val.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                date = TryParseDateOnly(val[5..]) ?? date;
            else if (val.StartsWith("task:", StringComparison.OrdinalIgnoreCase))
                taskId = val[5..];
        }

        return (agentId, date, taskId);
    }

    /// <summary>
    /// Interprets attributes for TASK: [taskId]
    /// </summary>
    public static string InterpretTaskRefAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Task = attrs["task"] ?? attrs["id"];
        if (!string.IsNullOrEmpty(v1Task))
        {
            return v1Task;
        }

        // v2 positional format: [taskId]
        return attrs["_pos0"] ?? "";
    }

    /// <summary>
    /// Interprets attributes for DATE: [date]
    /// </summary>
    public static DateOnly? InterpretDateAttributes(AttributeCollection attrs)
    {
        // Check for v1 format first
        var v1Date = attrs["date"];
        if (!string.IsNullOrEmpty(v1Date))
        {
            return TryParseDateOnly(v1Date);
        }

        // v2 positional format: date value directly
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
