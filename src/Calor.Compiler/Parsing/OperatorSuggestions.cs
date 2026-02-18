namespace Calor.Compiler.Parsing;

/// <summary>
/// Provides suggestions for unknown operators in Lisp expressions.
/// Includes typo correction using Levenshtein distance and C# → Calor hints.
/// </summary>
public static class OperatorSuggestions
{
    /// <summary>
    /// All valid operators in Calor Lisp expressions.
    /// </summary>
    public static readonly IReadOnlyList<string> AllOperators = new[]
    {
        // Arithmetic operators
        "+", "-", "*", "/", "%", "**",

        // Comparison operators
        "==", "!=", "<", "<=", ">", ">=",

        // Logical operators
        "&&", "||", "!",

        // Word forms of operators
        "and", "or", "not", "mod", "eq", "ne", "neq", "lt", "le", "lte", "gt", "ge", "gte",

        // Bitwise operators
        "&", "|", "^", "<<", ">>", "~",

        // Quantifiers
        "forall", "exists",

        // Implication
        "->",

        // Control flow
        "if", "cond", "let", "return", "set",

        // String operations
        "len", "contains", "starts", "ends", "indexof", "isempty", "isblank", "equals",
        "substr", "replace", "upper", "lower", "trim", "ltrim", "rtrim", "lpad", "rpad",
        "join", "fmt", "concat", "split", "str",
        "regex-test", "regex-match", "regex-replace", "regex-split",

        // Char operations
        "char-at", "char-code", "char-from-code",
        "is-letter", "is-digit", "is-whitespace", "is-upper", "is-lower",
        "char-upper", "char-lower",

        // StringBuilder operations
        "sb-new", "sb-append", "sb-appendline", "sb-insert", "sb-remove",
        "sb-clear", "sb-tostring", "sb-length",

        // List/array operations
        "list", "cons", "car", "cdr", "nth", "map", "filter", "reduce", "reverse",
        "append", "length",

        // Option operations
        "some", "none", "is-some", "is-none", "unwrap", "unwrap-or", "map-opt",

        // Result operations
        "ok", "err", "is-ok", "is-err", "unwrap-result",

        // Async operations
        "await",

        // Type operations
        "cast", "as", "is"
    };

    /// <summary>
    /// C# constructs that are not directly supported in Calor, with helpful alternatives.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CSharpToCalorHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // C# keywords with Calor equivalents
        ["nameof"] = "Use a string literal instead: \"VariableName\"",
        ["typeof"] = "Type reflection is not supported in Calor. Use type names directly in expressions.",
        ["sizeof"] = "Size operations are not supported in Calor. Use constants for known sizes.",
        ["default"] = "Use explicit default values (0 for numbers, \"\" for strings, §NN for None).",
        ["new"] = "Use §NEW for object creation: §NEW[Type] ... §/NEW",
        ["await"] = "Use (await expr) for async operations",
        ["async"] = "Mark functions with §AWAIT effect, then use (await ...) in expressions",
        ["throw"] = "Use (err message) to create error results, or §THROW for exceptions",
        ["try"] = "Use Result types with (ok ...) and (err ...) instead of try-catch",
        ["catch"] = "Use pattern matching on Result types: §MATCH[result] §ARM[Ok] ... §ARM[Err] ...",
        ["finally"] = "Use §DEFER for cleanup code that must run",
        ["lock"] = "Concurrency primitives are not supported in Calor",
        ["using"] = "Use §USING for resource management: §USING[var] ... §/USING",
        ["yield"] = "Use list comprehensions or §SEQ for lazy sequences",
        ["unsafe"] = "Unsafe code is not supported in Calor",
        ["fixed"] = "Fixed pointers are not supported in Calor",
        ["stackalloc"] = "Stack allocation is not supported in Calor",
        ["checked"] = "Use explicit overflow checking if needed",
        ["unchecked"] = "All arithmetic is unchecked by default in Calor",
        ["delegate"] = "Use lambda expressions: §LAM[params] ... §/LAM",
        ["event"] = "Events are not directly supported. Use callback functions.",
        ["volatile"] = "Volatile is not supported in Calor",
        ["extern"] = "Use §EXTERN for external function declarations",

        // C# operators
        ["++"] = "Use (+ x 1) or (set x (+ x 1))",
        ["--"] = "Use (- x 1) or (set x (- x 1))",
        ["+="] = "Use (set x (+ x value))",
        ["-="] = "Use (set x (- x value))",
        ["*="] = "Use (set x (* x value))",
        ["/="] = "Use (set x (/ x value))",
        ["??"] = "Use (unwrap-or option default) for null-coalescing",
        ["?."] = "Use pattern matching or (map-opt option fn) for null-conditional",
        ["?["] = "Use (if (is-some opt) (nth (unwrap opt) i) default)",
        ["!."] = "Use (unwrap option) to assert non-null",
        ["::"] = "Namespace resolution is automatic in Calor",

        // Common C# methods
        ["ToString"] = "Use (str expr) to convert to string",
        ["ToLower"] = "Use (lower s) for lowercase conversion",
        ["ToUpper"] = "Use (upper s) for uppercase conversion",
        ["Substring"] = "Use (substr s start length) or (substr s start)",
        ["Contains"] = "Use (contains s substring)",
        ["StartsWith"] = "Use (starts s prefix)",
        ["EndsWith"] = "Use (ends s suffix)",
        ["IndexOf"] = "Use (indexof s substring)",
        ["Replace"] = "Use (replace s old new)",
        ["Split"] = "Use (split s separator)",
        ["Join"] = "Use (join separator items)",
        ["Trim"] = "Use (trim s)",
        ["Length"] = "Use (len s) for string length",
        ["Count"] = "Use (length list) for collection count",
        ["Add"] = "Use (append list item) or (cons item list)",
        ["Remove"] = "Use (filter list (not (== item x)))",
        ["FirstOrDefault"] = "Use (if (> (length list) 0) (nth list 0) default)",
        ["Where"] = "Use (filter list predicate)",
        ["Select"] = "Use (map list transform)",
        ["Any"] = "Use (exists (x) (in list) condition)",
        ["All"] = "Use (forall (x) (in list) condition)",
        ["Sum"] = "Use (reduce list + 0)",

        // Null-related
        ["null"] = "Use Option types: §SM for Some, §NN for None",
        ["NULL"] = "Use Option types: §SM for Some, §NN for None",
        ["Null"] = "Use Option types: §SM for Some, §NN for None",

        // Boolean operators
        ["true"] = "Use #true for boolean true",
        ["false"] = "Use #false for boolean false",
        ["True"] = "Use #true for boolean true",
        ["False"] = "Use #false for boolean false",

        // Common typos that look like C#
        ["string.IsNullOrEmpty"] = "Use (isempty s)",
        ["string.IsNullOrWhiteSpace"] = "Use (isblank s)",
        ["string.Format"] = "Use (fmt template arg1 arg2 ...)",
        ["String.Format"] = "Use (fmt template arg1 arg2 ...)",
        ["Console.WriteLine"] = "Use §PRINT or (print ...)",
        ["Math.Abs"] = "Math.Abs is not directly supported. Use (if (< x 0) (- 0 x) x) for absolute value.",
        ["Math.Max"] = "Math.Max is not directly supported. Use (if (> a b) a b) for maximum.",
        ["Math.Min"] = "Math.Min is not directly supported. Use (if (< a b) a b) for minimum.",
        ["Math.Pow"] = "Use (** base exponent) for exponentiation",
        ["Math.Sqrt"] = "Math.Sqrt is not directly supported. Use (** x 0.5) for square root.",
        ["Math.Floor"] = "Math.Floor is not directly supported. Use (cast i32 x) to truncate.",
        ["Math.Ceiling"] = "Math.Ceiling is not directly supported.",
        ["Math.Round"] = "Math.Round is not directly supported.",
    };

    /// <summary>
    /// Finds the most similar operator using Levenshtein distance.
    /// Returns null if no sufficiently similar operator is found.
    /// </summary>
    /// <param name="unknown">The unknown operator text.</param>
    /// <param name="maxDistance">Maximum edit distance to consider a match (default: 2).</param>
    /// <returns>The most similar operator, or null if none found within distance threshold.</returns>
    public static string? FindSimilarOperator(string unknown, int maxDistance = 2)
    {
        if (string.IsNullOrEmpty(unknown))
            return null;

        var lowerUnknown = unknown.ToLowerInvariant();
        string? bestMatch = null;
        var bestDistance = int.MaxValue;

        foreach (var op in AllOperators)
        {
            var distance = LevenshteinDistance(lowerUnknown, op.ToLowerInvariant());
            if (distance <= maxDistance && distance < bestDistance)
            {
                // Prefer exact prefix matches (e.g., "contain" -> "contains")
                if (op.ToLowerInvariant().StartsWith(lowerUnknown) || lowerUnknown.StartsWith(op.ToLowerInvariant()))
                {
                    distance--; // Boost prefix matches
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMatch = op;
                }
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Gets a hint for converting a C# construct to Calor.
    /// Returns null if the construct is not a known C# pattern.
    /// </summary>
    /// <param name="unknown">The unknown text (may be a C# keyword or construct).</param>
    /// <returns>A helpful hint for the Calor equivalent, or null if not a known C# pattern.</returns>
    public static string? GetCSharpHint(string unknown)
    {
        if (string.IsNullOrEmpty(unknown))
            return null;

        // Direct lookup (case-insensitive)
        if (CSharpToCalorHints.TryGetValue(unknown, out var hint))
            return hint;

        // Check for method call patterns like "x.ToString()"
        var dotIndex = unknown.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < unknown.Length - 1)
        {
            var methodPart = unknown.Substring(dotIndex + 1).TrimEnd('(', ')');
            if (CSharpToCalorHints.TryGetValue(methodPart, out var methodHint))
                return methodHint;
        }

        return null;
    }

    /// <summary>
    /// Gets the operator name formatted for display in error messages.
    /// Groups operators by category for a more helpful message.
    /// </summary>
    public static string GetOperatorCategories()
    {
        return "arithmetic (+, -, *, /, %), comparison (==, !=, <, <=, >, >=), " +
               "logical (&&, ||, !), string (len, contains, substr, ...), " +
               "or char (char-at, is-letter, ...)";
    }

    /// <summary>
    /// Gets a usage example for a string operation.
    /// </summary>
    public static string GetStringOpExample(string opName)
    {
        return opName.ToLowerInvariant() switch
        {
            "len" => "(len str)",
            "contains" => "(contains str \"substring\")",
            "starts" => "(starts str \"prefix\")",
            "ends" => "(ends str \"suffix\")",
            "indexof" => "(indexof str \"substring\")",
            "isempty" => "(isempty str)",
            "isblank" => "(isblank str)",
            "equals" => "(equals str1 str2)",
            "substr" => "(substr str start) or (substr str start length)",
            "replace" => "(replace str \"old\" \"new\")",
            "upper" => "(upper str)",
            "lower" => "(lower str)",
            "trim" => "(trim str)",
            "ltrim" => "(ltrim str)",
            "rtrim" => "(rtrim str)",
            "lpad" => "(lpad str width)",
            "rpad" => "(rpad str width)",
            "join" => "(join separator list)",
            "fmt" => "(fmt \"template {0}\" arg1 arg2 ...)",
            "concat" => "(concat str1 str2 ...)",
            "split" => "(split str separator)",
            "str" => "(str value)",
            "regex-test" => "(regex-test str pattern)",
            "regex-match" => "(regex-match str pattern)",
            "regex-replace" => "(regex-replace str pattern replacement)",
            "regex-split" => "(regex-split str pattern)",
            _ => $"({opName} ...)"
        };
    }

    /// <summary>
    /// Gets a usage example for a char operation.
    /// </summary>
    public static string GetCharOpExample(string opName)
    {
        return opName.ToLowerInvariant() switch
        {
            "char-at" => "(char-at str index)",
            "char-code" => "(char-code char)",
            "char-from-code" => "(char-from-code int)",
            "is-letter" => "(is-letter char)",
            "is-digit" => "(is-digit char)",
            "is-whitespace" => "(is-whitespace char)",
            "is-upper" => "(is-upper char)",
            "is-lower" => "(is-lower char)",
            "char-upper" => "(char-upper char)",
            "char-lower" => "(char-lower char)",
            _ => $"({opName} ...)"
        };
    }

    /// <summary>
    /// Gets a usage example for a StringBuilder operation.
    /// </summary>
    public static string GetStringBuilderOpExample(string opName)
    {
        return opName.ToLowerInvariant() switch
        {
            "sb-new" => "(sb-new) or (sb-new \"initial\")",
            "sb-append" => "(sb-append builder \"text\")",
            "sb-appendline" => "(sb-appendline builder \"text\")",
            "sb-insert" => "(sb-insert builder index \"text\")",
            "sb-remove" => "(sb-remove builder start length)",
            "sb-clear" => "(sb-clear builder)",
            "sb-tostring" => "(sb-tostring builder)",
            "sb-length" => "(sb-length builder)",
            _ => $"({opName} ...)"
        };
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1))
            return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
        if (string.IsNullOrEmpty(s2))
            return s1.Length;

        var m = s1.Length;
        var n = s2.Length;

        // Use two rows instead of full matrix for memory efficiency
        var prev = new int[n + 1];
        var curr = new int[n + 1];

        // Initialize first row
        for (var j = 0; j <= n; j++)
            prev[j] = j;

        for (var i = 1; i <= m; i++)
        {
            curr[0] = i;

            for (var j = 1; j <= n; j++)
            {
                var cost = char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            // Swap rows
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
