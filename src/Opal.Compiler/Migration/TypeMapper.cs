namespace Opal.Compiler.Migration;

/// <summary>
/// Provides bidirectional type mapping between C# and OPAL types.
/// </summary>
public static class TypeMapper
{
    /// <summary>
    /// Maps C# type names to OPAL type names.
    /// </summary>
    private static readonly Dictionary<string, string> CSharpToOpalMap = new(StringComparer.OrdinalIgnoreCase)
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
        ["decimal"] = "decimal",
        ["Decimal"] = "decimal",
        ["System.Decimal"] = "decimal",

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
    /// Maps OPAL type names to C# type names.
    /// Also handles uppercase C# type names for backward compatibility.
    /// </summary>
    private static readonly Dictionary<string, string> OpalToCSharpMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // OPAL numeric types
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

        // OPAL floating point
        ["f32"] = "float",
        ["f64"] = "double",
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
    /// Maps C# binary operators to OPAL operator kinds.
    /// </summary>
    private static readonly Dictionary<string, string> CSharpOperatorToOpal = new()
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
    /// Maps OPAL operator kinds to C# operators.
    /// </summary>
    private static readonly Dictionary<string, string> OpalOperatorToCSharp = new()
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
    /// Converts a C# type name to an OPAL type name.
    /// </summary>
    public static string CSharpToOpal(string csharpType)
    {
        if (string.IsNullOrEmpty(csharpType))
            return csharpType;

        // Handle nullable types T? -> ?T
        if (csharpType.EndsWith("?"))
        {
            var innerType = csharpType[..^1];
            var mappedInner = CSharpToOpal(innerType);
            return $"?{mappedInner}";
        }

        // Handle Nullable<T> -> ?T
        if (csharpType.StartsWith("Nullable<") && csharpType.EndsWith(">"))
        {
            var innerType = csharpType["Nullable<".Length..^1];
            var mappedInner = CSharpToOpal(innerType);
            return $"?{mappedInner}";
        }

        // Handle arrays T[] -> [T]
        if (csharpType.EndsWith("[]"))
        {
            var elementType = csharpType[..^2];
            var mappedElement = CSharpToOpal(elementType);
            return $"[{mappedElement}]";
        }

        // Handle generic types
        var genericIndex = csharpType.IndexOf('<');
        if (genericIndex > 0)
        {
            var baseName = csharpType[..genericIndex];
            var typeArgs = csharpType[(genericIndex + 1)..^1];
            var mappedBase = CSharpToOpalMap.TryGetValue(baseName, out var opalBase) ? opalBase : baseName;
            var mappedArgs = MapGenericArguments(typeArgs, CSharpToOpal);
            return $"{mappedBase}<{mappedArgs}>";
        }

        // Direct mapping
        return CSharpToOpalMap.TryGetValue(csharpType, out var result) ? result : csharpType;
    }

    /// <summary>
    /// Converts an OPAL type name to a C# type name.
    /// </summary>
    public static string OpalToCSharp(string opalType)
    {
        if (string.IsNullOrEmpty(opalType))
            return opalType;

        // Handle Option types ?T -> T?
        if (opalType.StartsWith("?"))
        {
            var innerType = opalType[1..];
            var mappedInner = OpalToCSharp(innerType);
            return $"{mappedInner}?";
        }

        // Handle array types [T] -> T[]
        if (opalType.StartsWith("[") && opalType.EndsWith("]"))
        {
            var elementType = opalType[1..^1];
            var mappedElement = OpalToCSharp(elementType);
            return $"{mappedElement}[]";
        }

        // Handle generic types
        var genericIndex = opalType.IndexOf('<');
        if (genericIndex > 0)
        {
            var baseName = opalType[..genericIndex];
            var typeArgs = opalType[(genericIndex + 1)..^1];
            var mappedBase = OpalToCSharpMap.TryGetValue(baseName, out var csharpBase) ? csharpBase : baseName;
            var mappedArgs = MapGenericArguments(typeArgs, OpalToCSharp);
            return $"{mappedBase}<{mappedArgs}>";
        }

        // Direct mapping
        return OpalToCSharpMap.TryGetValue(opalType, out var result) ? result : opalType;
    }

    /// <summary>
    /// Converts a C# operator to an OPAL operator kind.
    /// </summary>
    public static string CSharpOperatorToOpalKind(string csharpOp)
    {
        return CSharpOperatorToOpal.TryGetValue(csharpOp, out var result) ? result : csharpOp;
    }

    /// <summary>
    /// Converts an OPAL operator kind to a C# operator.
    /// </summary>
    public static string OpalOperatorToCSharpOp(string opalKind)
    {
        return OpalOperatorToCSharp.TryGetValue(opalKind, out var result) ? result : opalKind;
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
    public static string GetDefaultValueLiteral(string opalType)
    {
        return opalType.ToLowerInvariant() switch
        {
            "i32" or "i64" or "i16" or "i8" or "u32" or "u64" or "u16" or "u8" => "0",
            "f32" or "f64" => "0.0",
            "decimal" => "0m",
            "bool" => "false",
            "str" => "\"\"",
            "char" => "'\\0'",
            _ when opalType.StartsWith("?") => "none",
            _ => "default"
        };
    }
}
