using Calor.Compiler.Ast;
using Calor.Compiler.Effects;

namespace Calor.Compiler.Parsing;

/// <summary>
/// Helper for interpreting positional attributes in context.
/// </summary>
public static class AttributeHelper
{
    /// <summary>
    /// Visibility tokens recognised by the parser.
    /// </summary>
    public static bool IsVisibilityToken(string value) => value is
        "pub" or "int" or "prot" or "priv" or "prot-int" or "priv-prot";

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

        // Legacy format: {type:name:const} - immutable with type
        if (pos2 == "const" && !string.IsNullOrEmpty(pos1))
        {
            return (pos1, false, ExpandType(pos0));
        }

        // Check for ~ prefix (original mutable format)
        if (pos0.StartsWith('~'))
        {
            // Format: {~name} or {~name:type}
            var name = pos0[1..];
            var typeName = !string.IsNullOrEmpty(pos1) ? ExpandType(pos1) : null;
            return (name, true, typeName);
        }

        // Legacy format: {name:const} - immutable (const is not a type)
        if (pos1 == "const")
        {
            return (pos0, false, null);
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

        // Array types: [i32], [str] (prefix) or i32[], str[] (postfix)
        if (value.StartsWith('['))
            return true;
        if (value.EndsWith("[]"))
            return IsLikelyType(value[..^2]);

        // Pointer types: i32*, void**, etc.
        if (value.EndsWith('*'))
            return IsLikelyType(value.TrimEnd('*'));

        // Multi-dim array types: int[,], i32[,,], etc.
        if (value.EndsWith(']') && value.Contains("[,"))
        {
            var bracketStart = value.IndexOf('[');
            if (bracketStart > 0)
                return IsLikelyType(value[..bracketStart]);
        }

        return value switch
        {
            "i8" or "i16" or "i32" or "i64" => true,
            "u8" or "u16" or "u32" or "u64" => true,
            "f32" or "f64" or "decimal" or "dec" => true,
            "int" or "long" or "short" or "sbyte" or "byte" or "ushort" or "uint" or "ulong"
                or "float" or "double" or "nint" or "nuint" or "str" or "string" or "bool"
                or "void" or "char" or "var" or "any" or "object" or "dynamic" => true,
            _ => value.StartsWith('?') || value.EndsWith('?') || value.Contains('!') || value.Contains('<')
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
    /// C# keyword types that <see cref="IsLikelyType"/> does not list. They are never read
    /// as a variable name by <see cref="IsAmbiguousTypeFirstBindHeader"/>.
    /// </summary>
    private static readonly HashSet<string> KeywordTypeSpellings = new(StringComparer.Ordinal)
    {
        "byte", "sbyte", "short", "ushort", "uint", "long", "ulong", "double", "nint", "nuint",
        "never", "dec", "unit"
    };

    private static bool IsTypeSpelling(string value) =>
        IsLikelyType(value) || KeywordTypeSpellings.Contains(value)
        || !string.Equals(ExpandType(value), value, StringComparison.Ordinal);

    private static bool IsPlainIdentifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_')
        && value.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// #1635: a spelling that reads as a type: an upper-case identifier (<c>W</c>, <c>URL</c>),
    /// a keyword type (<c>long</c>, <c>nint</c>), or one of those wrapped as an array, pointer,
    /// nullable or Calor array (<c>T[]</c>, <c>T?</c>, <c>[T]</c>, <c>long[,]</c>).
    /// </summary>
    private static bool IsTypeShaped(string value)
    {
        var core = value;
        while (true)
        {
            if (core.Length > 2 && core[0] == '[' && core[^1] == ']') { core = core[1..^1]; continue; }
            if (core.EndsWith('?') || core.EndsWith('*')) { core = core[..^1]; continue; }
            if (core.StartsWith('?')) { core = core[1..]; continue; }
            var bracket = core.LastIndexOf('[');
            if (bracket > 0 && core[^1] == ']' && core[(bracket + 1)..^1].All(c => c == ',')) { core = core[..bracket]; continue; }
            break;
        }
        return IsPlainIdentifier(core) && (char.IsUpper(core[0]) || IsTypeSpelling(core));
    }

    /// <summary>
    /// #1635: true when a two-field immutable header <c>§B{pos0:pos1}</c> is spelled
    /// type-first (<c>pos0</c> looks like a type name, <c>pos1</c> like a variable) but
    /// <see cref="InterpretBindAttributes"/> reads it name-first, because <c>pos0</c> is not a
    /// recognizable type spelling (a one-letter or all-caps type such as <c>W</c> or
    /// <c>T</c>). The reading would silently swap the name and the type, so the parser
    /// rejects the header with <c>Calor0123</c> instead.
    /// </summary>
    public static bool IsAmbiguousTypeFirstBindHeader(string pos0, string? pos1)
    {
        if (string.IsNullOrEmpty(pos0) || string.IsNullOrEmpty(pos1)
            || pos0.StartsWith('~') || pos1 is "const" or "mut")
            return false;
        // Read type-first: no swap.
        if (IsLikelyType(pos0) && !IsLikelyType(pos1))
            return false;
        // Read name-first: a swap when the first field is type-shaped and the second is a
        // plain lower-case identifier that is not itself a type spelling.
        var firstIsTypeShaped = IsTypeShaped(pos0);
        var secondIsVariableShaped = IsPlainIdentifier(pos1)
            && (char.IsLower(pos1[0]) || pos1[0] == '_')
            && !IsTypeSpelling(pos1);
        return firstIsTypeShaped && secondIsVariableShaped;
    }

    /// <summary>
    /// #1635: formats the positional fields of an immutable <c>§B</c> header so that
    /// <see cref="InterpretBindAttributes"/> reads them back as exactly (<paramref name="name"/>,
    /// <paramref name="type"/>). Type-first (<c>i32:x</c>) when the parser recognizes the type;
    /// otherwise name-first (<c>x:T</c>); and the explicit <c>type:name:const</c> form when
    /// neither two-field spelling is read correctly (a PascalCase local of a one-letter type).
    /// </summary>
    public static string FormatImmutableBindHeader(string name, string type)
    {
        if (IsLikelyType(type) && !IsLikelyType(name)
            && type != "const" && type != "mut" && name is not ("const" or "mut"))
            return $"{type}:{name}";
        if (!(IsLikelyType(name) && !IsLikelyType(type))
            && !IsAmbiguousTypeFirstBindHeader(name, type)
            && !name.StartsWith('~') && type is not ("const" or "mut"))
            return $"{name}:{type}";
        return $"{type}:{name}:const";
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
    /// <param name="isEffectVariable">
    /// Resolves a reconstructed code against the enclosing declaration's in-scope
    /// <c>eff</c> binders. Consulted <b>before</b> <see cref="EffectCodes.TryParseCompact"/>,
    /// per design-doc §7.2(b), so <c>§E{e}</c> binds the variable rather than failing
    /// the taxonomy lookup. A name the predicate rejects keeps today's behaviour.
    /// </param>
    /// <param name="effectVariableSink">Receives each code the predicate accepted, in source order.</param>
    public static Dictionary<string, string> InterpretEffectsAttributes(
        AttributeCollection attrs,
        Action<string>? reportUnknownCode = null,
        Func<string, bool>? isEffectVariable = null,
        ICollection<string>? effectVariableSink = null)
    {
        var effects = new Dictionary<string, string>();

        // Derive colon-bearing codes from the taxonomy. This keeps source parsing
        // aligned with entries such as mut:col without another hand-maintained list.
        var effectPrefixes = new HashSet<string>(
            EffectCodes.ColonPrefixes,
            StringComparer.OrdinalIgnoreCase);
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
                        if (!string.IsNullOrWhiteSpace(nextParts[0]))
                        {
                            // Combine: "fs" + "w" -> "fs:w"
                            effectCodes.Add($"{lastPart}:{nextParts[0]}");
                            i++; // Skip the next positional since we consumed it

                            // If there were more parts after the modifier, check if
                            // any are prefixes needing their own modifier from further positionals
                            for (int k = 1; k < nextParts.Length; k++)
                            {
                                var remainingPart = nextParts[k];
                                if (effectPrefixes.Contains(remainingPart.ToLowerInvariant()))
                                {
                                    // This remaining part is a prefix — look for its modifier
                                    var futureCode = attrs[$"_pos{i + 1}"];
                                    if (!string.IsNullOrEmpty(futureCode))
                                    {
                                        var futureParts = futureCode.Split(',');
                                        if (!string.IsNullOrWhiteSpace(futureParts[0]))
                                        {
                                            effectCodes.Add($"{remainingPart}:{futureParts[0]}");
                                            for (int f = 1; f < futureParts.Length; f++)
                                                effectCodes.Add(futureParts[f]);
                                            i++; // Skip the consumed positional
                                        }
                                        else
                                        {
                                            effectCodes.Add(remainingPart);
                                        }
                                    }
                                    else
                                    {
                                        effectCodes.Add(remainingPart);
                                    }
                                }
                                else
                                {
                                    effectCodes.Add(remainingPart);
                                }
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
                    if (!string.IsNullOrWhiteSpace(nextParts[0]))
                    {
                        // Combine the split effect code
                        effectCodes.Add($"{code}:{nextParts[0]}");
                        i++; // Skip the next positional since we consumed it

                        // Remaining parts may themselves be prefixes needing their own modifier
                        for (int k = 1; k < nextParts.Length; k++)
                        {
                            var remainingPart = nextParts[k];
                            if (effectPrefixes.Contains(remainingPart.ToLowerInvariant()))
                            {
                                var futureCode = attrs[$"_pos{i + 1}"];
                                if (!string.IsNullOrEmpty(futureCode))
                                {
                                    var futureParts = futureCode.Split(',');
                                    if (!string.IsNullOrWhiteSpace(futureParts[0]))
                                    {
                                        effectCodes.Add($"{remainingPart}:{futureParts[0]}");
                                        for (int f = 1; f < futureParts.Length; f++)
                                            effectCodes.Add(futureParts[f]);
                                        i++;
                                    }
                                    else
                                    {
                                        effectCodes.Add(remainingPart);
                                    }
                                }
                                else
                                {
                                    effectCodes.Add(remainingPart);
                                }
                            }
                            else
                            {
                                effectCodes.Add(remainingPart);
                            }
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
            else
            {
                effectCodes.Add(code);
            }
        }

        // Now process all the reconstructed effect codes
        foreach (var code in effectCodes)
        {
            // §7.2(b): effect variables resolve BEFORE the taxonomy, so a declaration
            // that binds `eff e` reads §E{e} as the variable and never as a code.
            if (isEffectVariable is not null && isEffectVariable(code))
            {
                effectVariableSink?.Add(code);
                continue;
            }

            if (!EffectCodes.TryParseCompact(code, out _, out var category, out var value))
            {
                reportUnknownCode?.Invoke(code);
                continue;
            }

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

        // Keep nullable annotations distinct from ordinary generic type references.
        if (compactType.StartsWith('?'))
        {
            var inner = ExpandType(compactType[1..]);
            return $"NULLABLE[inner={inner}]";
        }

        if (compactType.EndsWith('?'))
            return $"NULLABLE[inner={ExpandType(compactType[..^1])}]";

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

        // Handle array types: T[], T[,], T[,,], etc.
        if (compactType.EndsWith(']'))
        {
            var arrayStart = compactType.LastIndexOf('[');
            if (arrayStart > 0)
            {
                var suffix = compactType[arrayStart..];
                // Match [] or [,] or [,,] or [,,,] etc.
                if (suffix == "[]" || (suffix.Length >= 3 && suffix.Skip(1).SkipLast(1).All(c => c == ',')))
                {
                    var elementType = ExpandType(compactType[..arrayStart]);
                    return elementType + suffix;
                }
            }
        }

        // Primitive type mappings
        return compactType switch
        {
            "i8" or "sbyte" => "INT[bits=8][signed=true]",
            "i16" or "short" => "INT[bits=16][signed=true]",
            "i32" or "int" => "INT",
            "i64" or "long" => "INT[bits=64][signed=true]",
            "u8" or "byte" => "INT[bits=8][signed=false]",
            "u16" or "ushort" => "INT[bits=16][signed=false]",
            "u32" or "uint" => "INT[bits=32][signed=false]",
            "u64" or "ulong" => "INT[bits=64][signed=false]",
            "f32" or "float" => "FLOAT[bits=32]",
            "f64" or "double" => "FLOAT",
            "str" or "string" => "STRING",
            "bool" => "BOOL",
            "void" => "VOID",
            "never" => "NEVER",
            "char" => "CHAR",
            _ => compactType // Pass through unknown types preserving original casing
        };
    }

    /// <summary>
    /// Inverse of <see cref="ExpandType"/>: maps an expanded/internal type name back to
    /// the measured C# surface spelling (<c>INT</c> → <c>int</c>,
    /// <c>STRING</c> → <c>string</c>, <c>ARRAY[element=STRING]</c> → <c>string[]</c>). Use in
    /// any diagnostic that echoes a type so the message never teaches an internal
    /// spelling the user can't write. Idempotent on already-compact input.
    /// </summary>
    public static string ToSurfaceSpelling(string expandedType)
    {
        if (string.IsNullOrEmpty(expandedType))
        {
            return expandedType;
        }

        var t = expandedType.Trim();

        if (t.EndsWith(']'))
        {
            var start = t.LastIndexOf('[');
            if (start > 0 && t[(start + 1)..^1].All(c => c == ','))
                return ToSurfaceSpelling(t[..start]) + t[start..];
        }

        // Composite forms, mirroring ExpandType (recurse into the payload).
        if (t.StartsWith("ARRAY[element=", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal))
        {
            return $"{ToSurfaceSpelling(t["ARRAY[element=".Length..^1])}[]";
        }

        if (t.StartsWith("NULLABLE[inner=", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal))
        {
            return $"{ToSurfaceSpelling(t["NULLABLE[inner=".Length..^1])}?";
        }

        if (t.StartsWith('[') && t.EndsWith(']'))
            return ToSurfaceSpelling(t[1..^1]) + "[]";
        if (t.StartsWith('?'))
            return ToSurfaceSpelling(t[1..]) + "?";
        if (t.EndsWith('?') || t.EndsWith('*'))
            return ToSurfaceSpelling(t[..^1]) + t[^1];
        if (t.StartsWith('(') && t.EndsWith(')'))
            return Migration.TypeMapper.MapTupleType(t, ToSurfaceSpelling);

        // Generic: Base<A, B> — surface-spell each argument.
        var genericIndex = t.IndexOf('<');
        if (genericIndex > 0)
        {
            var depth = 1;
            var end = genericIndex + 1;
            for (; end < t.Length; end++)
            {
                if (t[end] == '<') depth++;
                if (t[end] == '>' && --depth == 0) break;
            }
            if (end < t.Length)
            {
                var args = SplitGenericArgs(t[(genericIndex + 1)..end]);
                return $"{t[..genericIndex]}<{string.Join(", ", args.Select(a => ToSurfaceSpelling(a.Trim())))}>"
                    + ToSurfaceSpelling(t[(end + 1)..]);
            }
        }

        // C#-style array suffix: X[] / X[,] — surface-spell the element.
        if (t.EndsWith("]", StringComparison.Ordinal))
        {
            var start = t.LastIndexOf('[');
            if (start > 0)
            {
                var suffix = t[start..];
                if (suffix == "[]" || (suffix.Length >= 3 && suffix.Skip(1).SkipLast(1).All(c => c == ',')))
                {
                    return ToSurfaceSpelling(t[..start]) + suffix;
                }
            }
        }

        return t switch
        {
            "INT" or "i32" => "int",
            "INT[bits=8][signed=true]" or "i8" => "sbyte",
            "INT[bits=16][signed=true]" or "i16" => "short",
            "INT[bits=64][signed=true]" or "i64" => "long",
            "INT[bits=8][signed=false]" or "u8" => "byte",
            "INT[bits=16][signed=false]" or "u16" => "ushort",
            "INT[bits=32][signed=false]" or "u32" => "uint",
            "INT[bits=64][signed=false]" or "u64" => "ulong",
            "FLOAT" or "f64" => "double",
            "FLOAT[bits=32]" or "f32" => "float",
            "STRING" or "str" => "string",
            "dec" => "decimal",
            "any" => "object",
            "BOOL" => "bool",
            "VOID" => "void",
            "NEVER" => "never",
            "CHAR" => "char",
            _ => t // already surface, or an unknown/user type — leave as-is
        };
    }

    /// <summary>
    /// Reads a nullable annotation, including the parser's expanded spelling.
    /// </summary>
    public static bool TryUnwrapNullableAnnotation(string typeName, out string innerType)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            innerType = string.Empty;
            return false;
        }
        var type = typeName.Trim();
        const string expandedPrefix = "NULLABLE[inner=";
        if (type.StartsWith(expandedPrefix, StringComparison.OrdinalIgnoreCase) && type.EndsWith(']'))
            innerType = type[expandedPrefix.Length..^1];
        else if (type.StartsWith('?') && type.Length > 1)
            innerType = type[1..];
        else if (type.EndsWith('?') && type.Length > 1)
            innerType = type[..^1];
        else
        {
            innerType = string.Empty;
            return false;
        }

        return !string.IsNullOrWhiteSpace(innerType);
    }

    /// <summary>
    /// Reference declarations visible in this module. A value declaration with the same name
    /// (including another preprocessor arm) prevents a reference classification.
    /// </summary>
    public static IReadOnlySet<string> GetDeclaredReferenceTypeNames(ModuleNode module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var references = new HashSet<string>(StringComparer.Ordinal);
        var nonReferences = new HashSet<string>(StringComparer.Ordinal);
        AddTypes(module.Classes, module.Interfaces, module.Enums, module.Delegates, null);
        foreach (var indexed in module.IndexedTypes)
            nonReferences.Add(indexed.Name);
        foreach (var refinement in module.RefinementTypes)
            nonReferences.Add(refinement.Name);
        foreach (var block in module.TypePreprocessorBlocks)
        for (var branch = block; branch is not null; branch = branch.ElseBranch)
            AddTypes(branch.Classes, branch.Interfaces, branch.Enums, branch.Delegates, null);
        references.ExceptWith(nonReferences);
        return references;

        void AddTypes(
            IReadOnlyList<ClassDefinitionNode> classes,
            IReadOnlyList<InterfaceDefinitionNode> interfaces,
            IReadOnlyList<EnumDefinitionNode> enums,
            IReadOnlyList<DelegateDefinitionNode> delegates,
            string? enclosing)
        {
            string Qualify(string name) => enclosing is null ? name : $"{enclosing}.{name}";
            foreach (var declaration in classes)
            {
                var name = Qualify(declaration.Name);
                (declaration.IsStruct ? nonReferences : references).Add(name);
                AddTypes(declaration.NestedClasses, declaration.NestedInterfaces,
                    declaration.NestedEnums, declaration.NestedDelegates, name);
            }
            foreach (var declaration in interfaces) references.Add(Qualify(declaration.Name));
            foreach (var declaration in delegates) references.Add(Qualify(declaration.Name));
            foreach (var declaration in enums) nonReferences.Add(Qualify(declaration.Name));
        }
    }

    private static List<string> SplitGenericArgs(string argsStr)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var c in argsStr)
        {
            if (c is '<' or '(' or '[') depth++;
            else if (c is '>' or ')' or ']') depth--;
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
        var parsed = EffectCodes.ParseCompact(code);
        return (parsed.Category, parsed.Value);
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
