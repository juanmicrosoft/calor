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
            _ => compactType.ToUpperInvariant() // Pass through unknown types
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
}
