namespace Calor.Compiler.Migration;

/// <summary>
/// Defines the support level for a C# feature during migration.
/// </summary>
public enum SupportLevel
{
    /// <summary>Feature is fully supported with direct mapping.</summary>
    Full,

    /// <summary>Feature is partially supported, may need manual review.</summary>
    Partial,

    /// <summary>Feature is not supported and will be skipped.</summary>
    NotSupported,

    /// <summary>Feature requires manual intervention.</summary>
    ManualRequired
}

/// <summary>
/// Describes a feature and its support status.
/// </summary>
public sealed class FeatureInfo
{
    public required string Name { get; init; }
    public required SupportLevel Support { get; init; }
    public string? Description { get; init; }
    public string? Workaround { get; init; }
}

/// <summary>
/// Registry of supported C# features for Calor conversion.
/// </summary>
public static class FeatureSupport
{
    /// <summary>
    /// All feature support information indexed by feature name.
    /// </summary>
    private static readonly Dictionary<string, FeatureInfo> Features = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fully supported features
        ["class"] = new FeatureInfo
        {
            Name = "class",
            Support = SupportLevel.Full,
            Description = "Classes are converted to Calor class definitions"
        },
        ["interface"] = new FeatureInfo
        {
            Name = "interface",
            Support = SupportLevel.Full,
            Description = "Interfaces are converted to Calor interface definitions"
        },
        ["record"] = new FeatureInfo
        {
            Name = "record",
            Support = SupportLevel.Full,
            Description = "Records are converted to Calor record definitions"
        },
        ["struct"] = new FeatureInfo
        {
            Name = "struct",
            Support = SupportLevel.Full,
            Description = "Structs are converted to Calor struct definitions"
        },
        ["enum"] = new FeatureInfo
        {
            Name = "enum",
            Support = SupportLevel.Full,
            Description = "Enums are converted to Calor enum definitions"
        },
        ["method"] = new FeatureInfo
        {
            Name = "method",
            Support = SupportLevel.Full,
            Description = "Methods are converted to Calor function definitions"
        },
        ["property"] = new FeatureInfo
        {
            Name = "property",
            Support = SupportLevel.Full,
            Description = "Properties are converted to Calor property definitions"
        },
        ["field"] = new FeatureInfo
        {
            Name = "field",
            Support = SupportLevel.Full,
            Description = "Fields are converted to Calor field definitions"
        },
        ["constructor"] = new FeatureInfo
        {
            Name = "constructor",
            Support = SupportLevel.Full,
            Description = "Constructors are converted to Calor constructors"
        },
        ["if"] = new FeatureInfo
        {
            Name = "if",
            Support = SupportLevel.Full,
            Description = "If statements are converted to Calor IF blocks"
        },
        ["for"] = new FeatureInfo
        {
            Name = "for",
            Support = SupportLevel.Full,
            Description = "For loops are converted to Calor LOOP blocks"
        },
        ["foreach"] = new FeatureInfo
        {
            Name = "foreach",
            Support = SupportLevel.Full,
            Description = "Foreach loops are converted to Calor FOREACH blocks"
        },
        ["while"] = new FeatureInfo
        {
            Name = "while",
            Support = SupportLevel.Full,
            Description = "While loops are converted to Calor WHILE blocks"
        },
        ["switch"] = new FeatureInfo
        {
            Name = "switch",
            Support = SupportLevel.Full,
            Description = "Switch statements are converted to Calor MATCH blocks"
        },
        ["try-catch"] = new FeatureInfo
        {
            Name = "try-catch",
            Support = SupportLevel.Full,
            Description = "Try/catch/finally blocks are converted to Calor TRY blocks"
        },
        ["async-await"] = new FeatureInfo
        {
            Name = "async-await",
            Support = SupportLevel.Full,
            Description = "Async/await is converted to Calor async functions"
        },
        ["lambda"] = new FeatureInfo
        {
            Name = "lambda",
            Support = SupportLevel.Full,
            Description = "Lambda expressions are converted to Calor lambda syntax"
        },
        ["generics"] = new FeatureInfo
        {
            Name = "generics",
            Support = SupportLevel.Full,
            Description = "Generic types and methods are supported"
        },
        ["pattern-matching-basic"] = new FeatureInfo
        {
            Name = "pattern-matching-basic",
            Support = SupportLevel.Full,
            Description = "Basic pattern matching (type, constant, var) is supported"
        },
        ["string-interpolation"] = new FeatureInfo
        {
            Name = "string-interpolation",
            Support = SupportLevel.Full,
            Description = "String interpolation is converted to Calor format"
        },
        ["null-coalescing"] = new FeatureInfo
        {
            Name = "null-coalescing",
            Support = SupportLevel.Full,
            Description = "Null coalescing operators are converted"
        },
        ["null-conditional"] = new FeatureInfo
        {
            Name = "null-conditional",
            Support = SupportLevel.Full,
            Description = "Null conditional operators are converted"
        },

        // Partially supported features
        ["linq-method"] = new FeatureInfo
        {
            Name = "linq-method",
            Support = SupportLevel.Partial,
            Description = "LINQ method syntax is converted but may need review",
            Workaround = "Consider using explicit loops for complex queries"
        },
        ["linq-query"] = new FeatureInfo
        {
            Name = "linq-query",
            Support = SupportLevel.Partial,
            Description = "LINQ query syntax is converted to method syntax",
            Workaround = "Review converted queries for correctness"
        },
        ["ref-parameter"] = new FeatureInfo
        {
            Name = "ref-parameter",
            Support = SupportLevel.Partial,
            Description = "Ref parameters are kept as-is with warning",
            Workaround = "Consider refactoring to return tuples"
        },
        ["out-parameter"] = new FeatureInfo
        {
            Name = "out-parameter",
            Support = SupportLevel.Partial,
            Description = "Out parameters are kept as-is with warning",
            Workaround = "Consider refactoring to return tuples or Result<T, E>"
        },
        ["pattern-matching-advanced"] = new FeatureInfo
        {
            Name = "pattern-matching-advanced",
            Support = SupportLevel.Partial,
            Description = "Advanced patterns (list, property, relational) may be simplified",
            Workaround = "Review converted patterns for semantic correctness"
        },
        ["attributes"] = new FeatureInfo
        {
            Name = "attributes",
            Support = SupportLevel.Partial,
            Description = "Attributes are converted to comments",
            Workaround = "Add Calor metadata annotations manually if needed"
        },
        ["dynamic"] = new FeatureInfo
        {
            Name = "dynamic",
            Support = SupportLevel.Partial,
            Description = "Dynamic type is converted to 'any' with warning",
            Workaround = "Consider using generics or interfaces"
        },

        // Not supported features
        ["goto"] = new FeatureInfo
        {
            Name = "goto",
            Support = SupportLevel.NotSupported,
            Description = "Goto statements are not supported",
            Workaround = "Refactor to use structured control flow"
        },
        ["labeled-statement"] = new FeatureInfo
        {
            Name = "labeled-statement",
            Support = SupportLevel.NotSupported,
            Description = "Labeled statements are not supported",
            Workaround = "Refactor to use structured control flow"
        },
        ["unsafe"] = new FeatureInfo
        {
            Name = "unsafe",
            Support = SupportLevel.NotSupported,
            Description = "Unsafe code blocks are not supported",
            Workaround = "Use safe alternatives or keep as C# interop"
        },
        ["pointer"] = new FeatureInfo
        {
            Name = "pointer",
            Support = SupportLevel.NotSupported,
            Description = "Pointer types are not supported",
            Workaround = "Use safe alternatives"
        },
        ["stackalloc"] = new FeatureInfo
        {
            Name = "stackalloc",
            Support = SupportLevel.NotSupported,
            Description = "Stackalloc is not supported",
            Workaround = "Use regular array allocation"
        },
        ["fixed"] = new FeatureInfo
        {
            Name = "fixed",
            Support = SupportLevel.NotSupported,
            Description = "Fixed buffers are not supported",
            Workaround = "Use regular arrays"
        },
        ["volatile"] = new FeatureInfo
        {
            Name = "volatile",
            Support = SupportLevel.NotSupported,
            Description = "Volatile keyword is not supported",
            Workaround = "Use proper synchronization primitives"
        },

        // Manual required
        ["extension-method"] = new FeatureInfo
        {
            Name = "extension-method",
            Support = SupportLevel.ManualRequired,
            Description = "Extension methods require manual conversion to regular methods or traits",
            Workaround = "Convert to instance methods or Calor traits"
        },
        ["operator-overload"] = new FeatureInfo
        {
            Name = "operator-overload",
            Support = SupportLevel.ManualRequired,
            Description = "Operator overloading requires manual conversion",
            Workaround = "Define explicit methods instead"
        },
        ["implicit-conversion"] = new FeatureInfo
        {
            Name = "implicit-conversion",
            Support = SupportLevel.ManualRequired,
            Description = "Implicit conversions require manual handling",
            Workaround = "Use explicit conversion methods"
        },
        ["explicit-conversion"] = new FeatureInfo
        {
            Name = "explicit-conversion",
            Support = SupportLevel.ManualRequired,
            Description = "Explicit conversions require manual handling",
            Workaround = "Use explicit conversion methods"
        },

        // Fallback features (for explain mode)
        ["unknown-expression"] = new FeatureInfo
        {
            Name = "unknown-expression",
            Support = SupportLevel.NotSupported,
            Description = "Unknown or unsupported expression syntax",
            Workaround = "Review and manually convert the expression"
        },
        ["unknown-literal"] = new FeatureInfo
        {
            Name = "unknown-literal",
            Support = SupportLevel.NotSupported,
            Description = "Unknown or unsupported literal type",
            Workaround = "Use a supported literal type"
        },
        ["complex-is-pattern"] = new FeatureInfo
        {
            Name = "complex-is-pattern",
            Support = SupportLevel.NotSupported,
            Description = "Complex 'is' pattern matching expression",
            Workaround = "Break down into simpler type checks or use match expression"
        },
        ["collection-spread"] = new FeatureInfo
        {
            Name = "collection-spread",
            Support = SupportLevel.NotSupported,
            Description = "Collection spread operator (..)",
            Workaround = "Use explicit collection concatenation methods"
        },
        ["implicit-new-with-args"] = new FeatureInfo
        {
            Name = "implicit-new-with-args",
            Support = SupportLevel.NotSupported,
            Description = "Target-typed new with arguments: new(args)",
            Workaround = "Use explicit type: new TypeName(args)"
        },
        ["binary pattern (and/or)"] = new FeatureInfo
        {
            Name = "binary pattern (and/or)",
            Support = SupportLevel.NotSupported,
            Description = "Pattern combinators: pattern1 and pattern2, pattern1 or pattern2",
            Workaround = "Use separate match cases or if-else with explicit conditions"
        },
        ["unary pattern (not)"] = new FeatureInfo
        {
            Name = "unary pattern (not)",
            Support = SupportLevel.NotSupported,
            Description = "Negated patterns: not null, not 0",
            Workaround = "Use guard clause with negated condition"
        },
        ["unknown-pattern"] = new FeatureInfo
        {
            Name = "unknown-pattern",
            Support = SupportLevel.NotSupported,
            Description = "Unrecognized pattern syntax",
            Workaround = "Simplify pattern or use if-else with explicit conditions"
        },
        ["complex-recursive-pattern"] = new FeatureInfo
        {
            Name = "complex-recursive-pattern",
            Support = SupportLevel.NotSupported,
            Description = "Complex recursive pattern without clear type",
            Workaround = "Use positional or property patterns with explicit type"
        },
        ["postfix-operator"] = new FeatureInfo
        {
            Name = "postfix-operator",
            Support = SupportLevel.NotSupported,
            Description = "Postfix increment/decrement as expression: i++, i--",
            Workaround = "Use as statement or rewrite as x = x + 1"
        },
    };

    /// <summary>
    /// Gets the support info for a feature.
    /// </summary>
    public static FeatureInfo? GetFeatureInfo(string featureName)
    {
        return Features.TryGetValue(featureName, out var info) ? info : null;
    }

    /// <summary>
    /// Gets the support level for a feature.
    /// </summary>
    public static SupportLevel GetSupportLevel(string featureName)
    {
        return Features.TryGetValue(featureName, out var info) ? info.Support : SupportLevel.Full;
    }

    /// <summary>
    /// Checks if a feature is fully supported.
    /// </summary>
    public static bool IsFullySupported(string featureName)
    {
        return GetSupportLevel(featureName) == SupportLevel.Full;
    }

    /// <summary>
    /// Checks if a feature is supported (full or partial).
    /// </summary>
    public static bool IsSupported(string featureName)
    {
        var level = GetSupportLevel(featureName);
        return level == SupportLevel.Full || level == SupportLevel.Partial;
    }

    /// <summary>
    /// Gets all features with a specific support level.
    /// </summary>
    public static IEnumerable<FeatureInfo> GetFeaturesBySupport(SupportLevel level)
    {
        return Features.Values.Where(f => f.Support == level);
    }

    /// <summary>
    /// Gets all registered features.
    /// </summary>
    public static IEnumerable<FeatureInfo> GetAllFeatures()
    {
        return Features.Values;
    }

    /// <summary>
    /// Gets the workaround text for an unsupported or partially supported feature.
    /// </summary>
    public static string? GetWorkaround(string featureName)
    {
        return Features.TryGetValue(featureName, out var info) ? info.Workaround : null;
    }
}
