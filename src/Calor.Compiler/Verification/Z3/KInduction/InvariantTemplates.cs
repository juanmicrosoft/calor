namespace Calor.Compiler.Verification.Z3.KInduction;

/// <summary>
/// Common loop invariant templates for synthesis.
/// </summary>
public static class InvariantTemplates
{
    /// <summary>
    /// Represents a template for loop invariants.
    /// </summary>
    public sealed class InvariantTemplate
    {
        public string Name { get; }
        public string Description { get; }
        public Func<LoopContext, string?> Generate { get; }

        public InvariantTemplate(string name, string description, Func<LoopContext, string?> generate)
        {
            Name = name;
            Description = description;
            Generate = generate;
        }
    }

    /// <summary>
    /// Context information about a loop for invariant generation.
    /// </summary>
    public sealed class LoopContext
    {
        /// <summary>
        /// The loop variable name (for for-loops).
        /// </summary>
        public string? LoopVariable { get; init; }

        /// <summary>
        /// The lower bound of the loop (for for-loops).
        /// </summary>
        public long? LowerBound { get; init; }

        /// <summary>
        /// The upper bound of the loop (for for-loops).
        /// </summary>
        public long? UpperBound { get; init; }

        /// <summary>
        /// Variables modified in the loop body.
        /// </summary>
        public IReadOnlyList<string> ModifiedVariables { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Variables read in the loop body.
        /// </summary>
        public IReadOnlyList<string> ReadVariables { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Array variables accessed in the loop.
        /// </summary>
        public IReadOnlyList<string> ArrayVariables { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The loop condition expression (as string).
        /// </summary>
        public string? ConditionExpression { get; init; }

        /// <summary>
        /// Whether the loop is a for loop with known bounds.
        /// </summary>
        public bool HasKnownBounds => LowerBound.HasValue && UpperBound.HasValue;
    }

    /// <summary>
    /// Template: Loop variable is bounded by loop bounds.
    /// For loop "for i = a to b": a <= i <= b
    /// </summary>
    public static InvariantTemplate BoundedLoopVariable { get; } = new(
        "BoundedLoopVariable",
        "Loop variable is within loop bounds",
        ctx =>
        {
            if (ctx.LoopVariable == null || !ctx.HasKnownBounds)
                return null;

            return $"{ctx.LowerBound} <= {ctx.LoopVariable} && {ctx.LoopVariable} <= {ctx.UpperBound}";
        });

    /// <summary>
    /// Template: Variable increases monotonically.
    /// </summary>
    public static InvariantTemplate MonotonicallyIncreasing { get; } = new(
        "MonotonicallyIncreasing",
        "Variable increases or stays the same in each iteration",
        ctx =>
        {
            // For modified variables that are likely counters
            foreach (var v in ctx.ModifiedVariables)
            {
                if (v.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("sum", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("total", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{v}' >= {v}"; // Next value >= current value
                }
            }
            return null;
        });

    /// <summary>
    /// Template: Variable decreases monotonically (useful for termination).
    /// </summary>
    public static InvariantTemplate MonotonicallyDecreasing { get; } = new(
        "MonotonicallyDecreasing",
        "Variable decreases in each iteration (termination variant)",
        ctx =>
        {
            foreach (var v in ctx.ModifiedVariables)
            {
                if (v.Contains("remaining", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("countdown", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("left", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{v}' < {v}";
                }
            }
            return null;
        });

    /// <summary>
    /// Template: Array index is within bounds.
    /// </summary>
    public static InvariantTemplate ArrayIndexBounds { get; } = new(
        "ArrayIndexBounds",
        "Array index stays within valid bounds",
        ctx =>
        {
            if (ctx.LoopVariable == null || ctx.ArrayVariables.Count == 0)
                return null;

            // If loop variable is used as array index
            if (ctx.HasKnownBounds)
            {
                return $"0 <= {ctx.LoopVariable} && {ctx.LoopVariable} < len({ctx.ArrayVariables[0]})";
            }

            return null;
        });

    /// <summary>
    /// Template: Accumulator variable is non-negative.
    /// </summary>
    public static InvariantTemplate AccumulatorNonNegative { get; } = new(
        "AccumulatorNonNegative",
        "Accumulator/sum variable remains non-negative",
        ctx =>
        {
            foreach (var v in ctx.ModifiedVariables)
            {
                if (v.Contains("sum", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                    v.Contains("acc", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{v} >= 0";
                }
            }
            return null;
        });

    /// <summary>
    /// Template: Value stays within original range.
    /// </summary>
    public static InvariantTemplate RangePreservation { get; } = new(
        "RangePreservation",
        "Modified values stay within initial range",
        ctx =>
        {
            if (ctx.LoopVariable != null && ctx.HasKnownBounds)
            {
                // Common pattern: result stays between bounds
                foreach (var v in ctx.ModifiedVariables.Where(v => v != ctx.LoopVariable))
                {
                    if (v.Contains("min", StringComparison.OrdinalIgnoreCase) ||
                        v.Contains("max", StringComparison.OrdinalIgnoreCase) ||
                        v.Contains("result", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{ctx.LowerBound} <= {v} && {v} <= {ctx.UpperBound}";
                    }
                }
            }
            return null;
        });

    /// <summary>
    /// Template: Termination - some value must decrease to zero.
    /// </summary>
    public static InvariantTemplate TerminationDecreasing { get; } = new(
        "TerminationDecreasing",
        "Variant function decreases towards zero (proves termination)",
        ctx =>
        {
            if (ctx.LoopVariable != null && ctx.UpperBound.HasValue)
            {
                // For "for i = a to b": variant is (b - i)
                return $"{ctx.UpperBound} - {ctx.LoopVariable} >= 0";
            }
            return null;
        });

    /// <summary>
    /// Template: While loop with incrementing variable.
    /// For "while i < n": 0 <= i
    /// </summary>
    public static InvariantTemplate WhileIncrementing { get; } = new(
        "WhileIncrementing",
        "While loop variable stays non-negative during incrementing",
        ctx =>
        {
            if (ctx.LoopVariable == null)
                return null;

            // For incrementing while loops: loop variable >= lower bound
            if (ctx.LowerBound.HasValue)
            {
                return $"{ctx.LoopVariable} >= {ctx.LowerBound}";
            }

            // Default: loop variable is non-negative
            return $"{ctx.LoopVariable} >= 0";
        });

    /// <summary>
    /// Template: While loop with decrementing variable.
    /// For "while i > 0": i >= 0
    /// </summary>
    public static InvariantTemplate WhileDecrementing { get; } = new(
        "WhileDecrementing",
        "While loop variable stays non-negative during decrementing",
        ctx =>
        {
            if (ctx.LoopVariable == null)
                return null;

            // For decrementing loops, variable stays >= some lower bound
            if (ctx.LowerBound.HasValue)
            {
                return $"{ctx.LoopVariable} >= {ctx.LowerBound - 1}";
            }

            return $"{ctx.LoopVariable} >= 0";
        });

    /// <summary>
    /// Template: While loop condition implies invariant.
    /// Uses the extracted condition expression as the invariant itself.
    /// </summary>
    public static InvariantTemplate WhileConditionAsInvariant { get; } = new(
        "WhileConditionAsInvariant",
        "Loop condition itself as invariant (holds during loop)",
        ctx =>
        {
            // The loop condition is often a good starting point for invariant
            return ctx.ConditionExpression;
        });

    /// <summary>
    /// All available invariant templates.
    /// </summary>
    public static IReadOnlyList<InvariantTemplate> All { get; } = new InvariantTemplate[]
    {
        // Bounded loop variable
        BoundedLoopVariable,

        // Monotonicity templates
        MonotonicallyIncreasing,
        MonotonicallyDecreasing,

        // Array bounds
        ArrayIndexBounds,

        // Sum/accumulator patterns
        AccumulatorNonNegative,

        // Range preservation
        RangePreservation,

        // Termination templates
        TerminationDecreasing,

        // While loop specific templates
        WhileIncrementing,
        WhileDecrementing,
        WhileConditionAsInvariant
    };

    /// <summary>
    /// Attempts to synthesize invariants for a given loop context.
    /// </summary>
    public static IEnumerable<string> SynthesizeInvariants(LoopContext context)
    {
        foreach (var template in All)
        {
            var invariant = template.Generate(context);
            if (invariant != null)
            {
                yield return invariant;
            }
        }
    }

    /// <summary>
    /// Attempts to synthesize the strongest invariant for a loop context.
    /// </summary>
    public static string? SynthesizeStrongestInvariant(LoopContext context)
    {
        var invariants = SynthesizeInvariants(context).ToList();
        if (invariants.Count == 0)
            return null;

        // Combine all invariants with conjunction
        return string.Join(" && ", invariants);
    }
}
