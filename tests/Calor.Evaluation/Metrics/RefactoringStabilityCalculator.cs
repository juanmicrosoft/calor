using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 8: Refactoring Stability Calculator
/// Measures how well unique IDs preserve references during code transformations.
///
/// Calor's unique IDs (§F{funcId:, §M{modId:, etc.) are hypothesized to survive
/// refactoring operations better than C#'s name-based references.
/// </summary>
public class RefactoringStabilityCalculator : IMetricCalculator
{
    public string Category => "RefactoringStability";

    public string Description => "Measures ID survivability and reference stability during refactoring operations";

    public Task<MetricResult> CalculateAsync(EvaluationContext context)
    {
        // Calculate stability scores based on structural characteristics
        var calorStability = CalculateCalorStability(context);
        var csharpStability = CalculateCSharpStability(context);

        var details = new Dictionary<string, object>
        {
            ["calorIdAnalysis"] = AnalyzeCalorIds(context),
            ["csharpReferenceAnalysis"] = AnalyzeCSharpReferences(context),
            ["refactoringScenarios"] = GetSupportedRefactoringScenarios()
        };

        return Task.FromResult(MetricResult.CreateHigherIsBetter(
            Category,
            "OverallStability",
            calorStability,
            csharpStability,
            details));
    }

    /// <summary>
    /// Evaluates a specific refactoring scenario with before/after code.
    /// </summary>
    public RefactoringResult EvaluateRefactoring(
        RefactoringScenario scenario,
        string calorBefore,
        string calorAfter,
        string csharpBefore,
        string csharpAfter)
    {
        var calorResult = EvaluateCalorRefactoring(scenario, calorBefore, calorAfter);
        var csharpResult = EvaluateCSharpRefactoring(scenario, csharpBefore, csharpAfter);

        return new RefactoringResult
        {
            Scenario = scenario,
            CalorScore = calorResult,
            CSharpScore = csharpResult,
            CalorPreservedIds = CountPreservedIds(calorBefore, calorAfter),
            CSharpPreservedNames = CountPreservedNames(csharpBefore, csharpAfter),
            CalorDiffSize = CalculateDiffSize(calorBefore, calorAfter),
            CSharpDiffSize = CalculateDiffSize(csharpBefore, csharpAfter)
        };
    }

    /// <summary>
    /// Calculates Calor's refactoring stability based on ID structure.
    /// </summary>
    private static double CalculateCalorStability(EvaluationContext context)
    {
        var source = context.CalorSource;
        var score = 0.0;

        // ID Preservation potential (30% weight)
        var idScore = CalculateIdPreservationScore(source);
        score += idScore * 0.30;

        // Reference validity potential (25% weight)
        var referenceScore = CalculateReferenceValidityScore(source);
        score += referenceScore * 0.25;

        // Diff minimization potential (20% weight) - based on structural clarity
        var structureScore = CalculateStructuralClarityScore(source);
        score += structureScore * 0.20;

        // Semantic equivalence potential (25% weight) - based on explicit semantics
        var semanticScore = CalculateSemanticExplicitnessScore(source);
        score += semanticScore * 0.25;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates C#'s refactoring stability based on code structure.
    /// </summary>
    private static double CalculateCSharpStability(EvaluationContext context)
    {
        var source = context.CSharpSource;
        var score = 0.0;

        // C# relies on names - name changes break references (30% weight)
        var hasStableNames = CountPattern(source, @"\b(readonly|const)\s+") > 0;
        score += (hasStableNames ? 0.6 : 0.4) * 0.30;

        // Reference validity through IDE support (25% weight)
        // C# has good IDE refactoring, but without IDs, references are fragile
        var hasGoodStructure = source.Contains("namespace") && source.Contains("class");
        score += (hasGoodStructure ? 0.7 : 0.5) * 0.25;

        // Diff minimization - C# often requires cascading changes (20% weight)
        var braceDepth = source.Count(c => c == '{');
        var nestedPenalty = Math.Max(0, 1.0 - (braceDepth * 0.05));
        score += nestedPenalty * 0.20;

        // Semantic preservation - C# lacks explicit contracts (25% weight)
        var hasContracts = CountPattern(source, @"Contract\.(Requires|Ensures)") > 0;
        var hasAsserts = CountPattern(source, @"Debug\.Assert") > 0;
        score += ((hasContracts ? 0.3 : 0) + (hasAsserts ? 0.2 : 0) + 0.3) * 0.25;

        return Math.Min(score, 0.85); // Cap C# score to reflect inherent limitations
    }

    /// <summary>
    /// Calculates score based on unique ID presence and diversity.
    /// Groups markers by role with relative weights. Score is normalized by
    /// the total weight of groups that have markers present, so programs are
    /// not penalized for groups they don't use (e.g., a purely functional
    /// program won't lose points for lacking §CL{ markers).
    /// </summary>
    private static double CalculateIdPreservationScore(string source)
    {
        var earnedWeight = 0.0;
        var applicableWeight = 0.0;

        // Module-level IDs (base weight: 0.20)
        var moduleIds = ExtractIds(source, @"§M\{([^:]+):");
        if (moduleIds.Count > 0)
        {
            applicableWeight += 0.20;
            if (moduleIds.All(IsUniqueId))
                earnedWeight += 0.20;
        }

        // Type-level IDs (base weight: 0.15)
        var classIds = ExtractIds(source, @"§CL\{([^:]+):");
        var ifaceIds = ExtractIds(source, @"§IFACE\{([^:]+):");
        var structIds = ExtractIds(source, @"§STRUCT\{([^:]+):");
        if (classIds.Count > 0 || ifaceIds.Count > 0 || structIds.Count > 0)
        {
            applicableWeight += 0.15;
            earnedWeight += 0.15;
        }

        // Member-level IDs (base weight: 0.30)
        var functionIds = ExtractIds(source, @"§F\{([^:]+):");
        var asyncFuncIds = ExtractIds(source, @"§AF\{([^:]+):");
        var methodIds = ExtractIds(source, @"§MT\{([^:]+):");
        var ctorIds = ExtractIds(source, @"§CTOR\{([^:]+):");
        var opIds = ExtractIds(source, @"§OP\{([^:]+):");
        if (functionIds.Count > 0 || asyncFuncIds.Count > 0 || methodIds.Count > 0 ||
            ctorIds.Count > 0 || opIds.Count > 0)
        {
            applicableWeight += 0.30;
            earnedWeight += 0.30;
        }

        // Property/Field IDs (base weight: 0.15)
        var propIds = ExtractIds(source, @"§PROP\{([^:]+):");
        var fieldIds = ExtractIds(source, @"§FLD\{([^:]+):");
        if (propIds.Count > 0 || fieldIds.Count > 0)
        {
            applicableWeight += 0.15;
            earnedWeight += 0.15;
        }

        // Control flow IDs (base weight: 0.10)
        var loopIds = ExtractIds(source, @"§LOOP\{([^:]+):");
        var ifIds = ExtractIds(source, @"§IF\{([^:]+):");
        var whileIds = ExtractIds(source, @"§WH\{([^:]+):");
        var forIds = ExtractIds(source, @"§FOR\{([^:]+):");
        if (loopIds.Count > 0 || ifIds.Count > 0 || whileIds.Count > 0 || forIds.Count > 0)
        {
            applicableWeight += 0.10;
            earnedWeight += 0.10;
        }

        // Variable/Binding IDs (base weight: 0.10)
        var variableIds = ExtractIds(source, @"§V\{([^:]+):");
        var bindingIds = ExtractIds(source, @"§B\{([^:]+):");
        if (variableIds.Count > 0 || bindingIds.Count > 0)
        {
            applicableWeight += 0.10;
            earnedWeight += 0.10;
        }

        // Normalize: score relative to applicable groups only
        if (applicableWeight == 0) return 0.0;
        return Math.Min(earnedWeight / applicableWeight, 1.0);
    }

    /// <summary>
    /// Calculates score based on reference structure.
    /// Original weights preserved for backward compatibility; new markers are additive.
    /// </summary>
    private static double CalculateReferenceValidityScore(string source)
    {
        var score = 0.5; // Base score

        // Function calls with proper IDs
        var funcCalls = CountPattern(source, @"§C\{[^\}]+\}");
        if (funcCalls > 0) score += 0.2;

        // Proper closing tags (enable safe boundary detection)
        var closingTags = CountPattern(source, @"§/[A-Z]+\{");
        if (closingTags > 0) score += 0.15;

        // Input/output annotations (typed references)
        var typeAnnotations = CountPattern(source, @"§[IO]\{");
        if (typeAnnotations > 0) score += 0.15;

        // Property/field references (reference-bearing constructs)
        var propRefs = CountPattern(source, @"§PROP\{[^\}]+\}");
        var fieldRefs = CountPattern(source, @"§FLD\{[^\}]+\}");
        if (propRefs > 0 || fieldRefs > 0) score += 0.1;

        // Binding references
        var bindingRefs = CountPattern(source, @"§B\{[^\}]+\}");
        if (bindingRefs > 0) score += 0.05;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Calculates score based on structural clarity for diff minimization.
    /// Checks module, type, function, and member boundary pairs. The 0.50 bonus
    /// budget is normalized by applicable boundary types so programs are not
    /// penalized for boundary types they don't use.
    /// </summary>
    private static double CalculateStructuralClarityScore(string source)
    {
        var earnedBonus = 0.0;
        var applicableBonus = 0.0;

        // Module boundaries
        if (source.Contains("§M{"))
        {
            applicableBonus += 0.125;
            if (source.Contains("§/M{")) earnedBonus += 0.125;
        }

        // Type boundaries (class, interface)
        if (source.Contains("§CL{") || source.Contains("§IFACE{"))
        {
            applicableBonus += 0.125;
            if ((source.Contains("§CL{") && source.Contains("§/CL{")) ||
                (source.Contains("§IFACE{") && source.Contains("§/IFACE{")))
                earnedBonus += 0.125;
        }

        // Function boundaries
        if (source.Contains("§F{"))
        {
            applicableBonus += 0.125;
            if (source.Contains("§/F{")) earnedBonus += 0.125;
        }

        // Member boundaries (method, async function, constructor, operator)
        if (source.Contains("§MT{") || source.Contains("§AF{") ||
            source.Contains("§CTOR{") || source.Contains("§OP{"))
        {
            applicableBonus += 0.125;
            if ((source.Contains("§MT{") && source.Contains("§/MT{")) ||
                (source.Contains("§AF{") && source.Contains("§/AF{")) ||
                (source.Contains("§CTOR{") && source.Contains("§/CTOR{")) ||
                (source.Contains("§OP{") && source.Contains("§/OP{")))
                earnedBonus += 0.125;
        }

        // Normalize bonus to the 0.50 budget, scaled by applicable groups
        var bonusScore = applicableBonus > 0 ? (earnedBonus / applicableBonus) * 0.50 : 0.0;
        return Math.Min(0.50 + bonusScore, 1.0);
    }

    /// <summary>
    /// Calculates score based on explicit semantic information.
    /// </summary>
    private static double CalculateSemanticExplicitnessScore(string source)
    {
        var score = 0.3;

        // Contracts preserve semantic intent during refactoring
        if (source.Contains("§REQ")) score += 0.2;
        if (source.Contains("§ENS")) score += 0.2;
        if (source.Contains("§INV")) score += 0.1;

        // Effect declarations preserve behavior constraints
        if (source.Contains("§E{")) score += 0.2;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// Evaluates Calor refactoring for a specific scenario.
    /// </summary>
    private static double EvaluateCalorRefactoring(
        RefactoringScenario scenario,
        string before,
        string after)
    {
        return scenario switch
        {
            RefactoringScenario.RenameFunction => EvaluateCalorRename(before, after),
            RefactoringScenario.ExtractMethod => EvaluateCalorExtract(before, after),
            RefactoringScenario.MoveFunction => EvaluateCalorMove(before, after),
            RefactoringScenario.ChangeSignature => EvaluateCalorSignatureChange(before, after),
            RefactoringScenario.InlineVariable => EvaluateCalorInline(before, after),
            _ => 0.5
        };
    }

    /// <summary>
    /// Evaluates C# refactoring for a specific scenario.
    /// </summary>
    private static double EvaluateCSharpRefactoring(
        RefactoringScenario scenario,
        string before,
        string after)
    {
        return scenario switch
        {
            RefactoringScenario.RenameFunction => EvaluateCSharpRename(before, after),
            RefactoringScenario.ExtractMethod => EvaluateCSharpExtract(before, after),
            RefactoringScenario.MoveFunction => EvaluateCSharpMove(before, after),
            RefactoringScenario.ChangeSignature => EvaluateCSharpSignatureChange(before, after),
            RefactoringScenario.InlineVariable => EvaluateCSharpInline(before, after),
            _ => 0.5
        };
    }

    private static double EvaluateCalorRename(string before, string after)
    {
        // In Calor, IDs should stay the same even when names change
        var beforeIds = ExtractIds(before, @"§F\{([^:]+):");
        var afterIds = ExtractIds(after, @"§F\{([^:]+):");

        var preserved = beforeIds.Intersect(afterIds).Count();
        return beforeIds.Count > 0 ? (double)preserved / beforeIds.Count : 0.5;
    }

    private static double EvaluateCSharpRename(string before, string after)
    {
        // In C#, rename changes all references - measure how consistent it is
        var diff = CalculateDiffSize(before, after);
        // A good rename should have minimal structural changes
        return Math.Max(0, 1.0 - (diff * 0.01));
    }

    private static double EvaluateCalorExtract(string before, string after)
    {
        // After extract, original IDs should remain, new function gets new ID
        var beforeFuncCount = CountPattern(before, @"§F\{");
        var afterFuncCount = CountPattern(after, @"§F\{");

        // Extract should add exactly one new function
        return afterFuncCount == beforeFuncCount + 1 ? 1.0 : 0.5;
    }

    private static double EvaluateCSharpExtract(string before, string after)
    {
        var beforeMethodCount = CountPattern(before, @"\b(public|private|protected)\s+\w+\s+\w+\s*\(");
        var afterMethodCount = CountPattern(after, @"\b(public|private|protected)\s+\w+\s+\w+\s*\(");

        return afterMethodCount == beforeMethodCount + 1 ? 0.8 : 0.4;
    }

    private static double EvaluateCalorMove(string before, string after)
    {
        // Moving a function should preserve its ID
        var beforeIds = ExtractIds(before, @"§F\{([^:]+):");
        var afterIds = ExtractIds(after, @"§F\{([^:]+):");

        var preserved = beforeIds.Intersect(afterIds).Count();
        return beforeIds.Count > 0 ? (double)preserved / beforeIds.Count : 0.5;
    }

    private static double EvaluateCSharpMove(string before, string after)
    {
        // In C#, moving requires updating all qualified references
        var diff = CalculateDiffSize(before, after);
        return Math.Max(0.3, 1.0 - (diff * 0.02));
    }

    private static double EvaluateCalorSignatureChange(string before, string after)
    {
        // Signature change keeps ID, updates callers
        var beforeIds = ExtractIds(before, @"§F\{([^:]+):");
        var afterIds = ExtractIds(after, @"§F\{([^:]+):");

        return beforeIds.SetEquals(afterIds) ? 1.0 : 0.6;
    }

    private static double EvaluateCSharpSignatureChange(string before, string after)
    {
        // C# signature changes cascade through all callers
        var diff = CalculateDiffSize(before, after);
        return Math.Max(0.2, 1.0 - (diff * 0.015));
    }

    private static double EvaluateCalorInline(string before, string after)
    {
        // Inlining should reduce variable count but maintain readability
        var beforeVars = CountPattern(before, @"§V\{");
        var afterVars = CountPattern(after, @"§V\{");

        return afterVars < beforeVars ? 0.9 : 0.6;
    }

    private static double EvaluateCSharpInline(string before, string after)
    {
        var beforeVars = CountPattern(before, @"\b(var|int|string|double)\s+\w+\s*=");
        var afterVars = CountPattern(after, @"\b(var|int|string|double)\s+\w+\s*=");

        return afterVars < beforeVars ? 0.8 : 0.5;
    }

    private static int CountPreservedIds(string before, string after)
    {
        var beforeIds = ExtractIds(before, @"§[A-Z]+\{([^:]+):");
        var afterIds = ExtractIds(after, @"§[A-Z]+\{([^:]+):");
        return beforeIds.Intersect(afterIds).Count();
    }

    private static int CountPreservedNames(string before, string after)
    {
        var beforeNames = ExtractNames(before);
        var afterNames = ExtractNames(after);
        return beforeNames.Intersect(afterNames).Count();
    }

    private static HashSet<string> ExtractIds(string source, string pattern)
    {
        var matches = Regex.Matches(source, pattern);
        return matches.Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static HashSet<string> ExtractNames(string source)
    {
        // Extract method/variable names from C#
        var methodPattern = @"\b(public|private|protected|internal)\s+(?:static\s+)?(\w+)\s+(\w+)\s*\(";
        var varPattern = @"\b(?:var|int|string|double|bool)\s+(\w+)\s*[;=]";

        var names = new HashSet<string>();

        foreach (Match m in Regex.Matches(source, methodPattern))
            names.Add(m.Groups[3].Value);

        foreach (Match m in Regex.Matches(source, varPattern))
            names.Add(m.Groups[1].Value);

        return names;
    }

    private static bool IsUniqueId(string id)
    {
        // Check if ID looks like a unique identifier
        // Supports: test IDs (f001, m001), named IDs (mod_main), and ULID-style IDs (f_01J5X7K9M2NPQRSTABWXYZ12)
        // ULID format: prefix + underscore + 26 chars of Crockford Base32 (excludes I, L, O, U)
        return Regex.IsMatch(id, @"^[a-z]+[0-9]+$|^[a-z_]+$|^[a-z]+_[0-9A-HJKMNP-TV-Z]{26}$", RegexOptions.IgnoreCase);
    }

    private static int CalculateDiffSize(string before, string after)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(before, after);

        return diff.Lines.Count(l => l.Type != ChangeType.Unchanged);
    }

    private static Dictionary<string, object> AnalyzeCalorIds(EvaluationContext context)
    {
        var source = context.CalorSource;
        return new Dictionary<string, object>
        {
            ["moduleIds"] = ExtractIds(source, @"§M\{([^:]+):").ToList(),
            ["functionIds"] = ExtractIds(source, @"§F\{([^:]+):").ToList(),
            ["variableIds"] = ExtractIds(source, @"§V\{([^:]+):").ToList(),
            ["hasClosingTags"] = source.Contains("§/"),
            ["idDensity"] = CountPattern(source, @"§[A-Z]+\{") / Math.Max(1.0, source.Split('\n').Length)
        };
    }

    private static Dictionary<string, object> AnalyzeCSharpReferences(EvaluationContext context)
    {
        var source = context.CSharpSource;
        return new Dictionary<string, object>
        {
            ["methodNames"] = ExtractNames(source).ToList(),
            ["hasNamespaces"] = source.Contains("namespace"),
            ["braceNestingDepth"] = source.Count(c => c == '{'),
            ["referenceFragility"] = CalculateReferenceFragility(source)
        };
    }

    private static double CalculateReferenceFragility(string source)
    {
        // Higher fragility = more things that can break during refactoring
        var fragility = 0.0;

        // String literals with method names (fragile to renames)
        fragility += CountPattern(source, @"""[^""]*[A-Z][a-z]+[^""]*""") * 0.1;

        // Reflection usage
        fragility += CountPattern(source, @"typeof\(|GetType\(\)") * 0.2;

        // Dynamic usage
        fragility += CountPattern(source, @"\bdynamic\b") * 0.3;

        return Math.Min(fragility, 1.0);
    }

    private static List<string> GetSupportedRefactoringScenarios()
    {
        return Enum.GetNames<RefactoringScenario>().ToList();
    }

    private static int CountPattern(string source, string pattern)
    {
        return Regex.Matches(source, pattern).Count;
    }
}

/// <summary>
/// Types of refactoring operations to test.
/// </summary>
public enum RefactoringScenario
{
    /// <summary>Rename a function while preserving its ID.</summary>
    RenameFunction,

    /// <summary>Extract code into a new function.</summary>
    ExtractMethod,

    /// <summary>Move a function to a different module.</summary>
    MoveFunction,

    /// <summary>Change function parameters/return type.</summary>
    ChangeSignature,

    /// <summary>Inline a variable.</summary>
    InlineVariable
}

/// <summary>
/// Result of evaluating a refactoring operation.
/// </summary>
public class RefactoringResult
{
    public RefactoringScenario Scenario { get; init; }
    public double CalorScore { get; init; }
    public double CSharpScore { get; init; }
    public int CalorPreservedIds { get; init; }
    public int CSharpPreservedNames { get; init; }
    public int CalorDiffSize { get; init; }
    public int CSharpDiffSize { get; init; }

    public double AdvantageRatio => CSharpScore > 0 ? CalorScore / CSharpScore : 1.0;
    public bool CalorWins => CalorScore > CSharpScore;
}
