namespace Calor.Compiler.Migration;

/// <summary>
/// Provides bidirectional type mapping between C# and Calor types.
/// </summary>
public static class TypeMapper
{
    /// <summary>
    /// Maps C# type names to Calor type names.
    /// </summary>
    private static readonly Dictionary<string, string> CSharpToCalorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Numeric types
        ["int"] = "i32",
        ["Int32"] = "i32",
        ["System.Int32"] = "i32",
        ["long"] = "i64",
        ["Int64"] = "i64",
        ["System.Int64"] = "i64",
        ["short"] = "i16",
        ["Int16"] = "i16",
        ["System.Int16"] = "i16",
        ["byte"] = "u8",
        ["Byte"] = "u8",
        ["System.Byte"] = "u8",
        ["sbyte"] = "i8",
        ["SByte"] = "i8",
        ["System.SByte"] = "i8",
        ["uint"] = "u32",
        ["UInt32"] = "u32",
        ["System.UInt32"] = "u32",
        ["ulong"] = "u64",
        ["UInt64"] = "u64",
        ["System.UInt64"] = "u64",
        ["ushort"] = "u16",
        ["UInt16"] = "u16",
        ["System.UInt16"] = "u16",

        // Floating point
        ["float"] = "f32",
        ["Single"] = "f32",
        ["System.Single"] = "f32",
        ["double"] = "f64",
        ["Double"] = "f64",
        ["System.Double"] = "f64",
        ["decimal"] = "dec",
        ["Decimal"] = "dec",
        ["System.Decimal"] = "dec",

        // Boolean
        ["bool"] = "bool",
        ["Boolean"] = "bool",
        ["System.Boolean"] = "bool",

        // String and char
        ["string"] = "str",
        ["String"] = "str",
        ["System.String"] = "str",
        ["char"] = "char",
        ["Char"] = "char",
        ["System.Char"] = "char",

        // Void
        ["void"] = "void",
        ["Void"] = "void",
        ["System.Void"] = "void",

        // Object
        ["object"] = "any",
        ["Object"] = "any",
        ["System.Object"] = "any",

        // Common collections
        ["List"] = "List",
        ["Dictionary"] = "Dict",
        ["HashSet"] = "Set",
        ["IEnumerable"] = "Seq",
        ["IList"] = "List",
        ["IDictionary"] = "Dict",
        ["ICollection"] = "Collection",

        // Read-only collections
        ["IReadOnlyList"] = "ReadList",
        ["IReadOnlyCollection"] = "ReadCollection",
        ["IReadOnlyDictionary"] = "ReadDict",
        ["IReadOnlySet"] = "ReadSet",

        // Async types
        ["Task"] = "Task",
        ["ValueTask"] = "Task",

        // Date/Time types
        ["DateTime"] = "datetime",
        ["System.DateTime"] = "datetime",
        ["DateTimeOffset"] = "datetimeoffset",
        ["System.DateTimeOffset"] = "datetimeoffset",
        ["TimeSpan"] = "timespan",
        ["System.TimeSpan"] = "timespan",
        ["DateOnly"] = "date",
        ["System.DateOnly"] = "date",
        ["TimeOnly"] = "time",
        ["System.TimeOnly"] = "time",

        // Other common types
        ["Guid"] = "guid",
        ["System.Guid"] = "guid",
    };

    /// <summary>
    /// Maps Calor type names to C# type names.
    /// Also handles uppercase C# type names for backward compatibility.
    /// </summary>
    private static readonly Dictionary<string, string> CalorToCSharpMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Calor numeric types
        ["i32"] = "int",
        ["i64"] = "long",
        ["i16"] = "short",
        ["i8"] = "sbyte",
        ["u32"] = "uint",
        ["u64"] = "ulong",
        ["u16"] = "ushort",
        ["u8"] = "byte",

        // C# numeric types (for backward compatibility with uppercase usage)
        ["int"] = "int",
        ["int32"] = "int",
        ["long"] = "long",
        ["int64"] = "long",
        ["short"] = "short",
        ["int16"] = "short",
        ["byte"] = "byte",
        ["sbyte"] = "sbyte",
        ["uint"] = "uint",
        ["uint32"] = "uint",
        ["ulong"] = "ulong",
        ["uint64"] = "ulong",
        ["ushort"] = "ushort",
        ["uint16"] = "ushort",

        // Calor floating point
        ["f32"] = "float",
        ["f64"] = "double",
        ["dec"] = "decimal",
        ["decimal"] = "decimal",

        // C# floating point (for backward compatibility)
        ["float"] = "float",
        ["float32"] = "float",
        ["single"] = "float",
        ["double"] = "double",
        ["float64"] = "double",

        // Boolean
        ["bool"] = "bool",
        ["boolean"] = "bool",

        // String and char
        ["str"] = "string",
        ["string"] = "string",
        ["char"] = "char",

        // Void
        ["void"] = "void",

        // Object
        ["any"] = "object",
        ["object"] = "object",

        // Collections
        ["List"] = "List",
        ["Dict"] = "Dictionary",
        ["Set"] = "HashSet",
        ["Seq"] = "IEnumerable",
        ["Collection"] = "ICollection",

        // Read-only collections
        ["ReadList"] = "IReadOnlyList",
        ["ReadCollection"] = "IReadOnlyCollection",
        ["ReadDict"] = "IReadOnlyDictionary",
        ["ReadSet"] = "IReadOnlySet",

        // Async
        ["Task"] = "Task",
        ["ValueTask"] = "ValueTask",

        // Date/Time types
        ["datetime"] = "DateTime",
        ["datetimeoffset"] = "DateTimeOffset",
        ["timespan"] = "TimeSpan",
        ["date"] = "DateOnly",
        ["time"] = "TimeOnly",

        // Other common types
        ["guid"] = "Guid",
    };

    /// <summary>
    /// Maps C# binary operators to Calor operator kinds.
    /// </summary>
    private static readonly Dictionary<string, string> CSharpOperatorToCalor = new()
    {
        ["+"] = "add",
        ["-"] = "sub",
        ["*"] = "mul",
        ["/"] = "div",
        ["%"] = "mod",
        ["=="] = "eq",
        ["!="] = "neq",
        ["<"] = "lt",
        ["<="] = "lte",
        [">"] = "gt",
        [">="] = "gte",
        ["&&"] = "and",
        ["||"] = "or",
        ["&"] = "band",
        ["|"] = "bor",
        ["^"] = "xor",
        ["<<"] = "shl",
        [">>"] = "shr",
        ["??"] = "coalesce",
    };

    /// <summary>
    /// Maps Calor operator kinds to C# operators.
    /// </summary>
    private static readonly Dictionary<string, string> CalorOperatorToCSharp = new()
    {
        ["add"] = "+",
        ["sub"] = "-",
        ["mul"] = "*",
        ["div"] = "/",
        ["mod"] = "%",
        ["eq"] = "==",
        ["neq"] = "!=",
        ["lt"] = "<",
        ["lte"] = "<=",
        ["gt"] = ">",
        ["gte"] = ">=",
        ["and"] = "&&",
        ["or"] = "||",
        ["band"] = "&",
        ["bor"] = "|",
        ["xor"] = "^",
        ["shl"] = "<<",
        ["shr"] = ">>",
        ["coalesce"] = "??",
    };

    /// <summary>
    /// Converts a C# type name to an Calor type name.
    /// </summary>
    public static string CSharpToCalor(string csharpType)
    {
        if (string.IsNullOrEmpty(csharpType))
            return csharpType;

        // Handle nullable types T? -> ?T
        if (csharpType.EndsWith("?"))
        {
            var innerType = csharpType[..^1];
            var mappedInner = CSharpToCalor(innerType);
            return $"?{mappedInner}";
        }

        // Handle Nullable<T> -> ?T
        if (csharpType.StartsWith("Nullable<") && csharpType.EndsWith(">"))
        {
            var innerType = csharpType["Nullable<".Length..^1];
            var mappedInner = CSharpToCalor(innerType);
            return $"?{mappedInner}";
        }

        // Handle arrays T[] -> [T]
        if (csharpType.EndsWith("[]"))
        {
            var elementType = csharpType[..^2];
            var mappedElement = CSharpToCalor(elementType);
            return $"[{mappedElement}]";
        }

        // Handle generic types
        var genericIndex = csharpType.IndexOf('<');
        if (genericIndex > 0)
        {
            var baseName = csharpType[..genericIndex];
            var typeArgs = csharpType[(genericIndex + 1)..^1];
            var mappedBase = CSharpToCalorMap.TryGetValue(baseName, out var calorBase) ? calorBase : baseName;
            var mappedArgs = MapGenericArguments(typeArgs, CSharpToCalor);
            return $"{mappedBase}<{mappedArgs}>";
        }

        // Direct mapping
        return CSharpToCalorMap.TryGetValue(csharpType, out var result) ? result : csharpType;
    }

    /// <summary>
    /// Converts an Calor type name to a C# type name.
    /// </summary>
    public static string CalorToCSharp(string calorType)
    {
        if (string.IsNullOrEmpty(calorType))
            return calorType;

        // Handle expanded OPTION format: OPTION[inner=T] -> T?
        if (calorType.StartsWith("OPTION[inner=", StringComparison.Ordinal))
        {
            var innerType = ExtractBracketValue(calorType, "OPTION[inner=");
            if (innerType != null)
            {
                var mappedInner = CalorToCSharp(innerType);
                return $"{mappedInner}?";
            }
        }

        // Handle expanded RESULT format: RESULT[ok=T][err=E] -> Calor.Runtime.Result<T, E>
        if (calorType.StartsWith("RESULT[ok=", StringComparison.Ordinal))
        {
            var (okType, errType) = ExtractResultTypes(calorType);
            if (okType != null && errType != null)
            {
                var mappedOk = CalorToCSharp(okType);
                var mappedErr = CalorToCSharp(errType);
                return $"Calor.Runtime.Result<{mappedOk}, {mappedErr}>";
            }
        }

        // Handle expanded INT format: INT[bits=N][signed=B] -> sbyte/short/int/long/byte/ushort/uint/ulong
        if (calorType.StartsWith("INT[", StringComparison.Ordinal))
        {
            return MapExpandedIntType(calorType);
        }

        // Handle expanded FLOAT format: FLOAT[bits=N] -> float/double
        if (calorType.StartsWith("FLOAT[", StringComparison.Ordinal))
        {
            return MapExpandedFloatType(calorType);
        }

        // Handle bare expanded FLOAT (from ExpandType for f64/float) -> double
        // Must be before dictionary lookup since case-insensitive "FLOAT" matches "float" -> "float"
        if (calorType == "FLOAT")
        {
            return "double";
        }

        // Handle expanded ARRAY format: ARRAY[element=T] -> T[]
        if (calorType.StartsWith("ARRAY[element=", StringComparison.Ordinal))
        {
            var elementType = ExtractBracketValue(calorType, "ARRAY[element=");
            if (elementType != null)
            {
                var mappedElement = CalorToCSharp(elementType);
                return $"{mappedElement}[]";
            }
        }

        // Handle Option types ?T -> T?
        if (calorType.StartsWith("?"))
        {
            var innerType = calorType[1..];
            var mappedInner = CalorToCSharp(innerType);
            return $"{mappedInner}?";
        }

        // Handle array types [T] -> T[]
        if (calorType.StartsWith("[") && calorType.EndsWith("]"))
        {
            var elementType = calorType[1..^1];
            var mappedElement = CalorToCSharp(elementType);
            return $"{mappedElement}[]";
        }

        // Handle generic types
        var genericIndex = calorType.IndexOf('<');
        if (genericIndex > 0)
        {
            var baseName = calorType[..genericIndex];
            var typeArgs = calorType[(genericIndex + 1)..^1];
            var mappedBase = CalorToCSharpMap.TryGetValue(baseName, out var csharpBase) ? csharpBase : baseName;
            var mappedArgs = MapGenericArguments(typeArgs, CalorToCSharp);
            return $"{mappedBase}<{mappedArgs}>";
        }

        // Direct mapping
        return CalorToCSharpMap.TryGetValue(calorType, out var result) ? result : calorType;
    }

    /// <summary>
    /// Converts a C# operator to an Calor operator kind.
    /// </summary>
    public static string CSharpOperatorToCalorKind(string csharpOp)
    {
        return CSharpOperatorToCalor.TryGetValue(csharpOp, out var result) ? result : csharpOp;
    }

    /// <summary>
    /// Converts an Calor operator kind to a C# operator.
    /// </summary>
    public static string CalorOperatorToCSharpOp(string calorKind)
    {
        return CalorOperatorToCSharp.TryGetValue(calorKind, out var result) ? result : calorKind;
    }

    /// <summary>
    /// Maps generic type arguments using the provided mapper function.
    /// </summary>
    private static string MapGenericArguments(string typeArgs, Func<string, string> mapper)
    {
        var depth = 0;
        var current = "";
        var result = new List<string>();

        foreach (var c in typeArgs)
        {
            if (c == '<')
            {
                depth++;
                current += c;
            }
            else if (c == '>')
            {
                depth--;
                current += c;
            }
            else if (c == ',' && depth == 0)
            {
                result.Add(mapper(current.Trim()));
                current = "";
            }
            else
            {
                current += c;
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            result.Add(mapper(current.Trim()));
        }

        return string.Join(", ", result);
    }

    /// <summary>
    /// Checks if a type is a primitive type.
    /// </summary>
    public static bool IsPrimitiveType(string typeName)
    {
        var normalizedLower = typeName.ToLowerInvariant();
        return normalizedLower switch
        {
            "int" or "i32" or "long" or "i64" or "short" or "i16" or "byte" or "u8" or
            "sbyte" or "i8" or "uint" or "u32" or "ulong" or "u64" or "ushort" or "u16" or
            "float" or "f32" or "double" or "f64" or "decimal" or
            "bool" or "string" or "str" or "char" or "void" => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a type is a numeric type.
    /// </summary>
    public static bool IsNumericType(string typeName)
    {
        var normalizedLower = typeName.ToLowerInvariant();
        return normalizedLower switch
        {
            "int" or "i32" or "long" or "i64" or "short" or "i16" or "byte" or "u8" or
            "sbyte" or "i8" or "uint" or "u32" or "ulong" or "u64" or "ushort" or "u16" or
            "float" or "f32" or "double" or "f64" or "decimal" => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the default value literal for a type.
    /// </summary>
    public static string GetDefaultValueLiteral(string calorType)
    {
        return calorType.ToLowerInvariant() switch
        {
            "i32" or "i64" or "i16" or "i8" or "u32" or "u64" or "u16" or "u8" => "0",
            "f32" or "f64" => "0.0",
            "decimal" => "0m",
            "bool" => "false",
            "str" => "\"\"",
            "char" => "'\\0'",
            _ when calorType.StartsWith("?") => "none",
            _ => "default"
        };
    }

    /// <summary>
    /// Extracts the value from a bracket-annotated type like "PREFIX[key=VALUE]".
    /// Returns the value or null if the format doesn't match.
    /// </summary>
    private static string? ExtractBracketValue(string type, string prefix)
    {
        if (!type.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var valueStart = prefix.Length;
        // Find matching closing bracket, respecting nested brackets
        var depth = 1;
        var i = valueStart;
        while (i < type.Length && depth > 0)
        {
            if (type[i] == '[') depth++;
            else if (type[i] == ']') depth--;
            i++;
        }

        if (depth != 0)
            return null;

        // Value is between prefix end and closing bracket (exclusive)
        return type[valueStart..(i - 1)];
    }

    /// <summary>
    /// Extracts ok and err types from expanded RESULT format: RESULT[ok=T][err=E]
    /// </summary>
    private static (string? OkType, string? ErrType) ExtractResultTypes(string type)
    {
        var okType = ExtractBracketValue(type, "RESULT[ok=");
        if (okType == null)
            return (null, null);

        // Find the [err=...] portion after the [ok=...] bracket
        var errPrefix = $"RESULT[ok={okType}][err=";
        var errType = ExtractBracketValue(type, errPrefix);
        return (okType, errType);
    }

    /// <summary>
    /// Maps expanded INT format (INT[bits=N][signed=B]) to C# type.
    /// </summary>
    private static string MapExpandedIntType(string type)
    {
        var bits = 32;
        var signed = true;

        // Extract bits value
        var bitsStr = ExtractBracketValue(type, "INT[bits=");
        if (bitsStr != null && int.TryParse(bitsStr, out var b))
            bits = b;

        // Extract signed value
        if (type.Contains("[signed=false]", StringComparison.Ordinal))
            signed = false;
        else if (type.Contains("[signed=true]", StringComparison.Ordinal))
            signed = true;

        return (bits, signed) switch
        {
            (8, true) => "sbyte",
            (8, false) => "byte",
            (16, true) => "short",
            (16, false) => "ushort",
            (32, true) => "int",
            (32, false) => "uint",
            (64, true) => "long",
            (64, false) => "ulong",
            _ => "int"
        };
    }

    /// <summary>
    /// Maps expanded FLOAT format (FLOAT[bits=N]) to C# type.
    /// </summary>
    private static string MapExpandedFloatType(string type)
    {
        var bitsStr = ExtractBracketValue(type, "FLOAT[bits=");
        if (bitsStr != null && int.TryParse(bitsStr, out var bits))
        {
            return bits switch
            {
                32 => "float",
                64 => "double",
                _ => "double"
            };
        }
        return "double";
    }
}
