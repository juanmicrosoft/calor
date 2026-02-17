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
        ["relational-pattern"] = new FeatureInfo
        {
            Name = "relational-pattern",
            Support = SupportLevel.NotSupported,
            Description = "Relational patterns (is > x, is < x) are not supported",
            Workaround = "Use explicit comparison expressions"
        },
        ["compound-pattern"] = new FeatureInfo
        {
            Name = "compound-pattern",
            Support = SupportLevel.NotSupported,
            Description = "Compound patterns (and/or) are not supported",
            Workaround = "Use explicit boolean expressions"
        },
        ["generic-type-constraint"] = new FeatureInfo
        {
            Name = "generic-type-constraint",
            Support = SupportLevel.NotSupported,
            Description = "Generic type constraints (where T : class) are not supported",
            Workaround = "Remove constraints or add runtime type checks"
        },
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

        // Additional features based on agent feedback
        ["yield-return"] = new FeatureInfo
        {
            Name = "yield-return",
            Support = SupportLevel.NotSupported,
            Description = "Yield return (iterator methods) is not supported",
            Workaround = "Use explicit List<T> construction and return the complete list"
        },
        ["is-type-pattern"] = new FeatureInfo
        {
            Name = "is-type-pattern",
            Support = SupportLevel.Partial,
            Description = "Type patterns (is Type) are partially supported; declaration patterns (is Type varName) are not",
            Workaround = "Use GetType() comparison or explicit type checks with separate variable declaration"
        },
        ["generic-method-expression"] = new FeatureInfo
        {
            Name = "generic-method-expression",
            Support = SupportLevel.Partial,
            Description = "Generic method calls like Option<T>.Some() in expressions may have issues",
            Workaround = "Assign generic method results to intermediate variables before use"
        },
        ["equals-operator"] = new FeatureInfo
        {
            Name = "equals-operator",
            Support = SupportLevel.ManualRequired,
            Description = "Custom == and != operator overloading requires manual conversion",
            Workaround = "Define an Equals method instead"
        },
        ["primary-constructor"] = new FeatureInfo
        {
            Name = "primary-constructor",
            Support = SupportLevel.NotSupported,
            Description = "Primary constructors (class Foo(int x)) are not supported",
            Workaround = "Use traditional constructor syntax"
        },
        ["range-expression"] = new FeatureInfo
        {
            Name = "range-expression",
            Support = SupportLevel.NotSupported,
            Description = "Range expressions (0..5, ..5, 5..) are not supported",
            Workaround = "Use explicit loop bounds or Substring/Take methods"
        },
        ["index-from-end"] = new FeatureInfo
        {
            Name = "index-from-end",
            Support = SupportLevel.NotSupported,
            Description = "Index from end expressions (^1) are not supported",
            Workaround = "Use array.Length - 1 instead of ^1"
        },
        ["target-typed-new"] = new FeatureInfo
        {
            Name = "target-typed-new",
            Support = SupportLevel.NotSupported,
            Description = "Target-typed new expressions (new()) are not supported",
            Workaround = "Use explicit type in new expression: new TypeName()"
        },
        ["null-conditional-method"] = new FeatureInfo
        {
            Name = "null-conditional-method",
            Support = SupportLevel.NotSupported,
            Description = "Null-conditional method calls (obj?.Method()) are not supported",
            Workaround = "Use explicit null check: if (obj != null) obj.Method()"
        },
        ["named-argument"] = new FeatureInfo
        {
            Name = "named-argument",
            Support = SupportLevel.NotSupported,
            Description = "Named arguments (param: value) are not supported",
            Workaround = "Use positional arguments in the correct order"
        },
        ["declaration-pattern"] = new FeatureInfo
        {
            Name = "declaration-pattern",
            Support = SupportLevel.NotSupported,
            Description = "Declaration patterns (is Type varName) are not supported",
            Workaround = "Check type separately and cast to a new variable"
        },
        ["throw-expression"] = new FeatureInfo
        {
            Name = "throw-expression",
            Support = SupportLevel.NotSupported,
            Description = "Throw expressions (?? throw new ...) are not supported",
            Workaround = "Use explicit if-throw statement"
        },
        ["nested-generic-type"] = new FeatureInfo
        {
            Name = "nested-generic-type",
            Support = SupportLevel.Partial,
            Description = "Nested generic types (Expression<Func<T, U>>) may have issues",
            Workaround = "Simplify generic nesting where possible"
        },
        ["out-var"] = new FeatureInfo
        {
            Name = "out-var",
            Support = SupportLevel.NotSupported,
            Description = "Inline out variable declarations (out var x) are not supported",
            Workaround = "Declare the variable before the method call"
        },

        // Phase 2 features
        ["in-parameter"] = new FeatureInfo
        {
            Name = "in-parameter",
            Support = SupportLevel.NotSupported,
            Description = "in parameters (readonly ref) are not supported",
            Workaround = "Pass by value or use regular ref parameter"
        },
        ["checked-block"] = new FeatureInfo
        {
            Name = "checked-block",
            Support = SupportLevel.NotSupported,
            Description = "checked/unchecked blocks are not supported",
            Workaround = "Remove checked/unchecked wrapper; handle overflow manually if needed"
        },
        ["with-expression"] = new FeatureInfo
        {
            Name = "with-expression",
            Support = SupportLevel.NotSupported,
            Description = "with expressions (record copying) are not supported",
            Workaround = "Create a new instance and copy properties manually"
        },
        ["init-accessor"] = new FeatureInfo
        {
            Name = "init-accessor",
            Support = SupportLevel.NotSupported,
            Description = "init accessors are not supported",
            Workaround = "Use regular set accessor or constructor initialization"
        },
        ["required-member"] = new FeatureInfo
        {
            Name = "required-member",
            Support = SupportLevel.NotSupported,
            Description = "required members (C# 11) are not supported",
            Workaround = "Use constructor parameters to enforce required values"
        },
        ["list-pattern"] = new FeatureInfo
        {
            Name = "list-pattern",
            Support = SupportLevel.NotSupported,
            Description = "list/slice patterns ([a, b, ..rest]) are not supported",
            Workaround = "Use explicit indexing and Length checks"
        },
        ["static-abstract-member"] = new FeatureInfo
        {
            Name = "static-abstract-member",
            Support = SupportLevel.NotSupported,
            Description = "static abstract/virtual interface members are not supported",
            Workaround = "Use instance methods or regular static methods"
        },
        ["ref-struct"] = new FeatureInfo
        {
            Name = "ref-struct",
            Support = SupportLevel.NotSupported,
            Description = "ref struct types are not supported",
            Workaround = "Use regular struct or class types"
        },

        // Phase 3 features
        ["lock-statement"] = new FeatureInfo
        {
            Name = "lock-statement",
            Support = SupportLevel.NotSupported,
            Description = "lock statements are not supported",
            Workaround = "Use explicit Monitor.Enter/Exit or other synchronization primitives"
        },
        ["await-foreach"] = new FeatureInfo
        {
            Name = "await-foreach",
            Support = SupportLevel.NotSupported,
            Description = "await foreach (async streams) is not supported",
            Workaround = "Enumerate the async enumerable manually with explicit await"
        },
        ["await-using"] = new FeatureInfo
        {
            Name = "await-using",
            Support = SupportLevel.NotSupported,
            Description = "await using statements are not supported",
            Workaround = "Use explicit try/finally with await DisposeAsync()"
        },
        ["scoped-parameter"] = new FeatureInfo
        {
            Name = "scoped-parameter",
            Support = SupportLevel.NotSupported,
            Description = "scoped parameters and locals are not supported",
            Workaround = "Remove scoped keyword; ensure ref safety manually"
        },
        ["collection-expression"] = new FeatureInfo
        {
            Name = "collection-expression",
            Support = SupportLevel.NotSupported,
            Description = "collection expressions [1, 2, 3] (C# 12) are not supported",
            Workaround = "Use explicit array or list construction: new[] { 1, 2, 3 }"
        },
        ["readonly-struct"] = new FeatureInfo
        {
            Name = "readonly-struct",
            Support = SupportLevel.NotSupported,
            Description = "readonly struct types are not supported",
            Workaround = "Use regular struct; readonly semantics cannot be enforced"
        },

        // Phase 4 features (C# 11-13)
        ["default-lambda-parameter"] = new FeatureInfo
        {
            Name = "default-lambda-parameter",
            Support = SupportLevel.NotSupported,
            Description = "default lambda parameters (C# 12) are not supported",
            Workaround = "Use method overloads or null checks inside lambda"
        },
        ["file-scoped-type"] = new FeatureInfo
        {
            Name = "file-scoped-type",
            Support = SupportLevel.NotSupported,
            Description = "file-scoped types (C# 11) are not supported",
            Workaround = "Use internal or private nested types"
        },
        ["utf8-string-literal"] = new FeatureInfo
        {
            Name = "utf8-string-literal",
            Support = SupportLevel.NotSupported,
            Description = "UTF-8 string literals (C# 11) are not supported",
            Workaround = "Use Encoding.UTF8.GetBytes(\"text\") instead"
        },
        ["generic-attribute"] = new FeatureInfo
        {
            Name = "generic-attribute",
            Support = SupportLevel.NotSupported,
            Description = "generic attributes (C# 11) are not supported",
            Workaround = "Use typeof() parameter in non-generic attribute"
        },
        ["using-type-alias"] = new FeatureInfo
        {
            Name = "using-type-alias",
            Support = SupportLevel.NotSupported,
            Description = "using type aliases for tuples/complex types (C# 12) are not supported",
            Workaround = "Define explicit record or class types"
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
