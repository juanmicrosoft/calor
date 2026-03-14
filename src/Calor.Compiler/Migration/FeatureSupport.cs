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
        ["indexer"] = new FeatureInfo
        {
            Name = "indexer",
            Support = SupportLevel.Full,
            Description = "Indexers (this[]) are converted to Calor indexer definitions"
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
            Support = SupportLevel.Full,
            Description = "LINQ method syntax is fully supported with chained call decomposition"
        },
        ["linq-query"] = new FeatureInfo
        {
            Name = "linq-query",
            Support = SupportLevel.Full,
            Description = "LINQ query syntax is desugared to equivalent method chains"
        },
        ["array-initializer"] = new FeatureInfo
        {
            Name = "array-initializer",
            Support = SupportLevel.Full,
            Description = "Bare array initializers are converted to Calor array nodes"
        },
        ["object-initializer"] = new FeatureInfo
        {
            Name = "object-initializer",
            Support = SupportLevel.Full,
            Description = "Object initializers are converted to Calor §NEW with property assignments"
        },
        ["dictionary-initializer"] = new FeatureInfo
        {
            Name = "dictionary-initializer",
            Support = SupportLevel.Full,
            Description = "Dictionary initializers ({ key, value } and [key] = value syntax) are supported for Dictionary, SortedDictionary, ConcurrentDictionary, FrozenDictionary, and ImmutableDictionary"
        },
        ["list-initializer"] = new FeatureInfo
        {
            Name = "list-initializer",
            Support = SupportLevel.Full,
            Description = "List initializers are converted to Calor §LIST nodes"
        },
        ["hashset-initializer"] = new FeatureInfo
        {
            Name = "hashset-initializer",
            Support = SupportLevel.Full,
            Description = "HashSet initializers are converted to Calor §SET nodes"
        },
        ["anonymous-type"] = new FeatureInfo
        {
            Name = "anonymous-type",
            Support = SupportLevel.Full,
            Description = "Anonymous types are converted to Calor §ANON blocks"
        },
        ["foreach-index"] = new FeatureInfo
        {
            Name = "foreach-index",
            Support = SupportLevel.Full,
            Description = "Indexed foreach via §EACH with optional index variable"
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
            Support = SupportLevel.Full,
            Description = "Attributes are preserved via [@Name] syntax with proper AST"
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
            Support = SupportLevel.Full,
            Description = "Relational patterns (is > x, is < x) are converted to comparison expressions"
        },
        ["compound-pattern"] = new FeatureInfo
        {
            Name = "compound-pattern",
            Support = SupportLevel.Full,
            Description = "Compound patterns (and/or) are converted to boolean expressions"
        },
        ["generic-type-constraint"] = new FeatureInfo
        {
            Name = "generic-type-constraint",
            Support = SupportLevel.Full,
            Description = "Generic type constraints are supported via §WHERE syntax"
        },
        ["goto"] = new FeatureInfo
        {
            Name = "goto",
            Support = SupportLevel.Full,
            Description = "Goto statements are converted to §GOTO{label} nodes"
        },
        ["labeled-statement"] = new FeatureInfo
        {
            Name = "labeled-statement",
            Support = SupportLevel.Full,
            Description = "Labeled statements are converted to §LABEL{name} nodes"
        },
        ["unsafe"] = new FeatureInfo
        {
            Name = "unsafe",
            Support = SupportLevel.Full,
            Description = "Unsafe code blocks are converted to §UNSAFE blocks"
        },
        ["pointer"] = new FeatureInfo
        {
            Name = "pointer",
            Support = SupportLevel.Full,
            Description = "Pointer types (int*) are supported with §ADDR and §DEREF"
        },
        ["stackalloc"] = new FeatureInfo
        {
            Name = "stackalloc",
            Support = SupportLevel.Full,
            Description = "Stackalloc expressions are converted to §SALLOC nodes"
        },
        ["fixed"] = new FeatureInfo
        {
            Name = "fixed",
            Support = SupportLevel.Full,
            Description = "Fixed statements are converted to §FIXED blocks"
        },
        ["volatile"] = new FeatureInfo
        {
            Name = "volatile",
            Support = SupportLevel.Full,
            Description = "Volatile modifier is passed through to generated C# fields"
        },

        // Manual required
        ["extension-method"] = new FeatureInfo
        {
            Name = "extension-method",
            Support = SupportLevel.Full,
            Description = "Extension methods are converted with 'this' parameter modifier preserved"
        },
        ["operator-overload"] = new FeatureInfo
        {
            Name = "operator-overload",
            Support = SupportLevel.Full,
            Description = "Operator overloads are fully supported via §OP tags and converted to op_ CIL-convention methods"
        },
        ["implicit-conversion"] = new FeatureInfo
        {
            Name = "implicit-conversion",
            Support = SupportLevel.Full,
            Description = "Implicit conversions are fully supported via §OP tags and converted to op_Implicit methods"
        },
        ["explicit-conversion"] = new FeatureInfo
        {
            Name = "explicit-conversion",
            Support = SupportLevel.Full,
            Description = "Explicit conversions are fully supported via §OP tags and converted to op_Explicit methods"
        },

        // Additional features based on agent feedback
        ["yield-return"] = new FeatureInfo
        {
            Name = "yield-return",
            Support = SupportLevel.Full,
            Description = "Yield return/break statements are converted to Calor yield syntax"
        },
        ["is-type-pattern"] = new FeatureInfo
        {
            Name = "is-type-pattern",
            Support = SupportLevel.Full,
            Description = "Type test patterns (is Type) are fully supported via TypeOperationNode"
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
            Support = SupportLevel.Full,
            Description = "Custom == and != operators are fully supported via §OP tags and converted to op_Equality/op_Inequality methods"
        },
        ["primary-constructor"] = new FeatureInfo
        {
            Name = "primary-constructor",
            Support = SupportLevel.Full,
            Description = "Primary constructors (class Foo(int x)) are converted to readonly fields",
            Workaround = null
        },
        ["range-expression"] = new FeatureInfo
        {
            Name = "range-expression",
            Support = SupportLevel.Full,
            Description = "Range expressions (0..5, ..5, 5..) are converted to §RANGE nodes"
        },
        ["index-from-end"] = new FeatureInfo
        {
            Name = "index-from-end",
            Support = SupportLevel.Full,
            Description = "Index from end expressions (^1) are converted to §^ nodes"
        },
        ["target-typed-new"] = new FeatureInfo
        {
            Name = "target-typed-new",
            Support = SupportLevel.Partial,
            Description = "Target-typed new with arguments is converted using 'object' placeholder type",
            Workaround = "Review and replace 'object' with the correct type name"
        },
        ["null-conditional-method"] = new FeatureInfo
        {
            Name = "null-conditional-method",
            Support = SupportLevel.Partial,
            Description = "Null-conditional method calls (obj?.Method()) are converted with AST-based args",
            Workaround = "Review converted null-conditional method calls for correctness"
        },
        ["named-argument"] = new FeatureInfo
        {
            Name = "named-argument",
            Support = SupportLevel.Full,
            Description = "Named arguments (param: value) are supported in both statement and expression contexts"
        },
        ["declaration-pattern"] = new FeatureInfo
        {
            Name = "declaration-pattern",
            Support = SupportLevel.Full,
            Description = "Declaration patterns (is Type name) are fully supported via TypeOperationNode with variable binding"
        },
        ["throw-expression"] = new FeatureInfo
        {
            Name = "throw-expression",
            Support = SupportLevel.Full,
            Description = "Throw expressions are converted to §ERR nodes"
        },
        ["nameof"] = new FeatureInfo
        {
            Name = "nameof",
            Support = SupportLevel.Full,
            Description = "nameof() expressions are preserved as (nameof x) in Calor and emit nameof(x) in C#"
        },
        ["nested-generic-type"] = new FeatureInfo
        {
            Name = "nested-generic-type",
            Support = SupportLevel.Full,
            Description = "Nested generic types like Dictionary<string, List<int>> are fully supported with inline angle bracket syntax"
        },
        ["nested-type"] = new FeatureInfo
        {
            Name = "nested-type",
            Support = SupportLevel.Full,
            Description = "Nested type declarations (classes, structs, interfaces, enums inside other types) are supported"
        },
        ["out-var"] = new FeatureInfo
        {
            Name = "out-var",
            Support = SupportLevel.Full,
            Description = "Inline out variable declarations (out var x) are pre-declared as bindings",
            Workaround = null
        },

        // Phase 2 features
        ["in-parameter"] = new FeatureInfo
        {
            Name = "in-parameter",
            Support = SupportLevel.Full,
            Description = "in parameters (readonly ref) are converted with ParameterModifier.In"
        },
        ["checked-block"] = new FeatureInfo
        {
            Name = "checked-block",
            Support = SupportLevel.Full,
            Description = "checked/unchecked wrapper stripped, body statements fully preserved"
        },
        ["with-expression"] = new FeatureInfo
        {
            Name = "with-expression",
            Support = SupportLevel.Full,
            Description = "with expressions (record copying) are converted to §WITH blocks"
        },
        ["init-accessor"] = new FeatureInfo
        {
            Name = "init-accessor",
            Support = SupportLevel.Full,
            Description = "init keyword is handled in property accessors"
        },
        ["required-member"] = new FeatureInfo
        {
            Name = "required-member",
            Support = SupportLevel.Full,
            Description = "required modifier on properties and fields is detected and emitted"
        },
        ["list-pattern"] = new FeatureInfo
        {
            Name = "list-pattern",
            Support = SupportLevel.Full,
            Description = "list/slice patterns ([a, b, ..rest])"
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
            Support = SupportLevel.Full,
            Description = "Lock statements are converted to §SYNC blocks with full semantics"
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
            Support = SupportLevel.Full,
            Description = "scoped keyword is stripped during conversion; parameter is preserved"
        },
        ["collection-expression"] = new FeatureInfo
        {
            Name = "collection-expression",
            Support = SupportLevel.Full,
            Description = "C# 12 collection expressions [1, 2, 3] are converted via ConvertCollectionExpression"
        },
        ["readonly-struct"] = new FeatureInfo
        {
            Name = "readonly-struct",
            Support = SupportLevel.Full,
            Description = "Readonly structs are converted with struct and readonly modifiers"
        },
        ["preprocessor-directive"] = new FeatureInfo
        {
            Name = "preprocessor-directive",
            Support = SupportLevel.Full,
            Description = "Preprocessor directives (#if/#else/#endif) are converted to §PP blocks"
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
            Support = SupportLevel.Full,
            Description = "UTF-8 string literals (C# 11) are fully supported — u8 suffix is preserved through round-trip",
            Workaround = null
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
            Support = SupportLevel.Full,
            Description = "Recursive patterns in is-expressions (is { Prop: val }, is { } name) are converted to boolean expressions"
        },
        ["collection-spread"] = new FeatureInfo
        {
            Name = "collection-spread",
            Support = SupportLevel.Full,
            Description = "Collection spread operator (..) including mixed spreads [..a, ..b] via Concat chains"
        },
        ["collection-spread-mixed"] = new FeatureInfo
        {
            Name = "collection-spread-mixed",
            Support = SupportLevel.Full,
            Description = "Mixed collection spreads [..a, x, ..b] converted to Concat chains"
        },
        ["implicit-new-with-args"] = new FeatureInfo
        {
            Name = "implicit-new-with-args",
            Support = SupportLevel.Partial,
            Description = "Target-typed new expressions are supported but type inference uses 'object' placeholder when target type cannot be determined"
        },
        ["binary pattern (and/or)"] = new FeatureInfo
        {
            Name = "binary pattern (and/or)",
            Support = SupportLevel.Full,
            Description = "Pattern combinators (and/or) are converted to AndPatternNode/OrPatternNode"
        },
        ["unary pattern (not)"] = new FeatureInfo
        {
            Name = "unary pattern (not)",
            Support = SupportLevel.Full,
            Description = "Negated patterns (not) are converted to NegatedPatternNode"
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
            Support = SupportLevel.Full,
            Description = "Property patterns and positional patterns are fully supported including nested patterns"
        },
        ["postfix-operator"] = new FeatureInfo
        {
            Name = "postfix-operator",
            Support = SupportLevel.Full,
            Description = "Postfix i++/i-- is supported via hoisting strategy — original value is preserved in a temp variable"
        },
        ["default-parameter"] = new FeatureInfo
        {
            Name = "default-parameter",
            Support = SupportLevel.Full,
            Description = "Default parameter values are converted to §I{type:name} = value syntax"
        },

        // Issue #325: Language gap features
        ["parallel"] = new FeatureInfo
        {
            Name = "parallel",
            Support = SupportLevel.Full,
            Description = "Parallel.For/ForEach/Invoke are converted as method calls"
        },
        ["plinq"] = new FeatureInfo
        {
            Name = "plinq",
            Support = SupportLevel.Full,
            Description = "PLINQ .AsParallel() chains are converted as method calls"
        },
        ["com-interop"] = new FeatureInfo
        {
            Name = "com-interop",
            Support = SupportLevel.Full,
            Description = "COM attributes ([ComImport], [Guid]) are preserved"
        },
        ["extern-method"] = new FeatureInfo
        {
            Name = "extern-method",
            Support = SupportLevel.Full,
            Description = "Extern methods with [DllImport] are fully supported"
        },
        ["dllimport"] = new FeatureInfo
        {
            Name = "dllimport",
            Support = SupportLevel.Full,
            Description = "[DllImport] attribute for P/Invoke is fully supported"
        },
        ["tuple-type"] = new FeatureInfo
        {
            Name = "tuple-type",
            Support = SupportLevel.Full,
            Description = "Tuple types in signatures like (int, string) are mapped"
        },
        ["tuple-literal"] = new FeatureInfo
        {
            Name = "tuple-literal",
            Support = SupportLevel.Full,
            Description = "Tuple literals (a, b) are fully supported"
        },
        ["tuple-deconstruction"] = new FeatureInfo
        {
            Name = "tuple-deconstruction",
            Support = SupportLevel.Full,
            Description = "Tuple deconstruction var (a, b) = ... is fully supported"
        },
        ["span"] = new FeatureInfo
        {
            Name = "span",
            Support = SupportLevel.Full,
            Description = "Span<T> and ReadOnlySpan<T> types are supported"
        },
        ["memory"] = new FeatureInfo
        {
            Name = "memory",
            Support = SupportLevel.Full,
            Description = "Memory<T> and ReadOnlyMemory<T> types are supported"
        },
        ["multidim-array"] = new FeatureInfo
        {
            Name = "multidim-array",
            Support = SupportLevel.Full,
            Description = "Multidimensional arrays (int[,], int[,,]) are fully supported"
        },

        // Dependent Types
        ["refinement-type"] = new FeatureInfo
        {
            Name = "refinement-type",
            Support = SupportLevel.Full,
            Description = "Refinement types (§RTYPE) define base types constrained by predicates; erased to base types in C#"
        },
        ["proof-obligation"] = new FeatureInfo
        {
            Name = "proof-obligation",
            Support = SupportLevel.Full,
            Description = "Proof obligations (§PROOF) declare conditions to be verified; emitted as comments or runtime checks"
        },
        ["indexed-type"] = new FeatureInfo
        {
            Name = "indexed-type",
            Support = SupportLevel.Full,
            Description = "Indexed types (§ITYPE) define size-parameterized types for bounds checking; erased to base types in C#"
        },
        ["index-bounds"] = new FeatureInfo
        {
            Name = "index-bounds",
            Support = SupportLevel.Full,
            Description = "Index bounds obligations verify array/list accesses are within bounds using Z3"
        },

        // Newtonsoft.Json gap features
        ["notnull-constraint"] = new FeatureInfo
        {
            Name = "notnull-constraint",
            Support = SupportLevel.Full,
            Description = "notnull type constraint supported via TypeConstraintKind.NotNull"
        },
        ["explicit-interface"] = new FeatureInfo
        {
            Name = "explicit-interface",
            Support = SupportLevel.Full,
            Description = "Explicit interface implementations preserved as IInterface.MethodName"
        },
        ["verbatim-identifier"] = new FeatureInfo
        {
            Name = "verbatim-identifier",
            Support = SupportLevel.Full,
            Description = "C# @keyword identifiers mapped to backtick syntax in Calor"
        },
        ["pragma"] = new FeatureInfo
        {
            Name = "pragma",
            Support = SupportLevel.Full,
            Description = "#pragma directives are stripped during conversion (trivia); cosmetic only"
        },
        ["conditional-using"] = new FeatureInfo
        {
            Name = "conditional-using",
            Support = SupportLevel.Full,
            Description = "Using directives inside #if blocks supported via §PP wrapping §U"
        },

        // Conversion gap fixes
        ["anonymous-method"] = new FeatureInfo
        {
            Name = "anonymous-method",
            Support = SupportLevel.Full,
            Description = "C# delegate { } anonymous methods are converted to Calor lambda expressions"
        },
        ["ref-expression"] = new FeatureInfo
        {
            Name = "ref-expression",
            Support = SupportLevel.Full,
            Description = "ref expressions are stripped during conversion; the referenced value is preserved"
        },
        ["global-qualified-name"] = new FeatureInfo
        {
            Name = "global-qualified-name",
            Support = SupportLevel.Full,
            Description = "global:: qualified names are converted to unqualified references"
        },
        ["implicit-element-access"] = new FeatureInfo
        {
            Name = "implicit-element-access",
            Support = SupportLevel.Full,
            Description = "Implicit element access (['key'] in initializers) is converted to index access"
        },
        ["var-pattern"] = new FeatureInfo
        {
            Name = "var-pattern",
            Support = SupportLevel.Full,
            Description = "var patterns (is var name, is var (a, b)) converted with variable hoisting"
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
        return Features.TryGetValue(featureName, out var info) ? info.Support : SupportLevel.NotSupported;
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
