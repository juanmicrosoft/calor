using System.Text.RegularExpressions;
using Calor.Compiler.Mcp.Tools;

namespace Calor.Compiler.Migration;

/// <summary>
/// Describes a single fix applied by the PostConversionFixer.
/// </summary>
public sealed class AppliedFix
{
    public required string Rule { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Result of running the PostConversionFixer.
/// </summary>
public sealed class FixResult
{
    public required string FixedSource { get; init; }
    public required bool WasModified { get; init; }
    public required List<AppliedFix> AppliedFixes { get; init; }
}

/// <summary>
/// Preserves converter output for native parser validation.
/// Retired prefix syntax is not repaired into a native-positive candidate.
/// </summary>
public sealed class PostConversionFixer
{
    private const int MaxPasses = 3;

    private static readonly List<(string Name, string Description, Func<string, (string Result, bool Changed)> Apply)> Rules = new()
    {
    };

    /// <summary>
    /// Attempts to fix known invalid patterns in the given Calor source.
    /// Re-parses after each pass and returns immediately if valid.
    /// </summary>
    public FixResult Fix(string calorSource)
    {
        var allFixes = new List<AppliedFix>();
        var current = calorSource;

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var passModified = false;

            foreach (var (name, description, apply) in Rules)
            {
                var result = Parsing.RawCSharpFrame.RewriteOutsideFrames(
                    current, text => apply(text).Result);
                var changed = !string.Equals(result, current, StringComparison.Ordinal);
                if (changed)
                {
                    current = result;
                    passModified = true;
                    allFixes.Add(new AppliedFix { Rule = name, Description = description });
                }
            }

            if (!passModified)
                break;

            // Re-parse to check if source is now valid
            var parseResult = CalorSourceHelper.Parse(current, "post-fix-check.calr");
            if (parseResult.IsSuccess)
            {
                return new FixResult
                {
                    FixedSource = current,
                    WasModified = true,
                    AppliedFixes = allFixes
                };
            }
        }

        return new FixResult
        {
            FixedSource = current,
            WasModified = allFixes.Count > 0,
            AppliedFixes = allFixes
        };
    }

}
