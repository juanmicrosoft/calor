using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Calor.Compiler.Ids;
using Calor.Evaluation.Core;

namespace Calor.Evaluation.Metrics;

/// <summary>
/// Category 8: Refactoring Stability Calculator
/// Measures how well unique IDs preserve references during code transformations.
///
/// Calor's stable declaration IDs (kept in the project sidecar, <c>.calor/ids.json</c>) are
/// hypothesized to survive refactoring operations better than C#'s name-based references.
/// IDs are read and carried across edits by the compiler's identity store; see CalorIdentities.
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
        var ids = CalorIdentities.Of(context);
        var score = 0.0;

        // ID Preservation potential (30% weight)
        var idScore = CalculateIdPreservationScore(source, ids);
        score += idScore * 0.30;

        // Reference validity potential (25% weight)
        var referenceScore = CalculateReferenceValidityScore(source);
        score += referenceScore * 0.25;

        // Diff minimization potential (20% weight) - based on structural clarity
        var structureScore = CalculateStructuralClarityScore(source, ids);
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
    /// Calculates score based on stable ID presence and diversity.
    /// Groups declarations by role with relative weights. A group applies when the source
    /// has that kind of construct, and earns its weight when those constructs carry sidecar
    /// IDs. The score is normalized by the applicable weight, so programs are not penalized
    /// for groups they don't use (e.g., a purely functional program has no type group).
    /// Native Calor gives IDs to modules, types, callables, properties and fields; loops,
    /// branches and local bindings have none, so those groups apply but earn nothing.
    /// </summary>
    private static double CalculateIdPreservationScore(string source, IReadOnlyList<CalorDeclarationId> ids)
    {
        var earnedWeight = 0.0;
        var applicableWeight = 0.0;

        void Group(double weight, bool applicable, bool earned)
        {
            if (!applicable) return;
            applicableWeight += weight;
            if (earned) earnedWeight += weight;
        }

        // Module-level IDs (0.20), type-level (0.15), member-level (0.30), property/field (0.15):
        // the sidecar assigns every such declaration a unique ID.
        Group(0.20, ids.Any(d => d.Kind == IdKind.Module), earned: true);
        Group(0.15, ids.Any(d => CalorIdentities.IsType(d.Kind)), earned: true);
        Group(0.30, ids.Any(d => CalorIdentities.IsCallable(d.Kind)), earned: true);
        Group(0.15, ids.Any(d => CalorIdentities.IsMemberData(d.Kind)), earned: true);

        // Control flow (0.10) and local bindings (0.10) carry no IDs in native Calor.
        Group(0.10, CalorSyntax.If.IsMatch(source) || CalorSyntax.Loop.IsMatch(source), earned: false);
        Group(0.10, CalorSyntax.Binding.IsMatch(source), earned: false);

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

        // Function calls
        if (CalorSyntax.Call.IsMatch(source)) score += 0.2;

        // Closing tags: native source has none (blocks end by indentation), so no bonus.

        // Input/output annotations (typed references)
        if (CalorSyntax.HasTypedParameters(source) || CalorSyntax.TypedOutput.IsMatch(source)) score += 0.15;

        // Property/field declarations (reference-bearing constructs)
        if (PropertyOrField.IsMatch(source)) score += 0.1;

        // Binding references
        if (CalorSyntax.Binding.IsMatch(source)) score += 0.05;

        return Math.Min(score, 1.0);
    }

    private static readonly Regex PropertyOrField = new(@"^[ \t]*(?:property|field)\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Calculates score based on structural clarity for diff minimization.
    /// Checks module, type, function, and member boundary pairs. The 0.50 bonus
    /// budget is normalized by applicable boundary types so programs are not
    /// penalized for boundary types they don't use.
    /// </summary>
    private static double CalculateStructuralClarityScore(string source, IReadOnlyList<CalorDeclarationId> ids)
    {
        var earnedBonus = 0.0;
        var applicableBonus = 0.0;

        // Every structural opener is terminated by a dedent. The "earned" signal is that
        // there is at least one indented body line; a single file-level regex approximates
        // it, since the calculators are heuristic, not parser-based.
        var hasIndentedBody = Regex.IsMatch(source, @"^[ \t]+\S", RegexOptions.Multiline);

        void Boundary(bool present)
        {
            if (!present) return;
            applicableBonus += 0.125;
            if (hasIndentedBody) earnedBonus += 0.125;
        }

        Boundary(ids.Any(d => d.Kind == IdKind.Module));                                   // Module
        Boundary(ids.Any(d => d.Kind is IdKind.Class or IdKind.Interface));                // Type
        Boundary(ids.Any(d => d.Kind == IdKind.Function));                                 // Function
        Boundary(ids.Any(d => d.Kind is IdKind.Method or IdKind.Constructor or IdKind.OperatorOverload)); // Member

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
        if (CalorSyntax.Precondition.IsMatch(source)) score += 0.2;
        if (CalorSyntax.Ensures.IsMatch(source)) score += 0.2;
        if (CalorSyntax.Invariant.IsMatch(source)) score += 0.1;

        // Effect declarations preserve behavior constraints
        if (CalorSyntax.EffectRow.IsMatch(source)) score += 0.2;

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
        var (beforeIds, afterIds) = CalorIdentities.Track(before, after);
        var callables = beforeIds.Count(d => CalorIdentities.IsCallable(d.Kind));
        var preserved = CalorIdentities.Preserved(beforeIds, afterIds, CalorIdentities.IsCallable);
        return callables > 0 ? (double)preserved / callables : 0.5;
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
        var beforeFuncCount = CalorSyntax.Function.Matches(before).Count;
        var afterFuncCount = CalorSyntax.Function.Matches(after).Count;

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
        var (beforeIds, afterIds) = CalorIdentities.Track(before, after);
        var callables = beforeIds.Count(d => CalorIdentities.IsCallable(d.Kind));
        var preserved = CalorIdentities.Preserved(beforeIds, afterIds, CalorIdentities.IsCallable);
        return callables > 0 ? (double)preserved / callables : 0.5;
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
        var (beforeIds, afterIds) = CalorIdentities.Track(before, after);
        var beforeSet = beforeIds.Where(d => CalorIdentities.IsCallable(d.Kind)).Select(d => d.Id).ToHashSet();
        var afterSet = afterIds.Where(d => CalorIdentities.IsCallable(d.Kind)).Select(d => d.Id).ToHashSet();
        return beforeSet.Count > 0 && beforeSet.SetEquals(afterSet) ? 1.0 : 0.6;
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
        var beforeVars = CalorSyntax.Binding.Matches(before).Count;
        var afterVars = CalorSyntax.Binding.Matches(after).Count;

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
        var (beforeIds, afterIds) = CalorIdentities.Track(before, after);
        return CalorIdentities.Preserved(beforeIds, afterIds);
    }

    private static int CountPreservedNames(string before, string after)
    {
        var beforeNames = ExtractNames(before);
        var afterNames = ExtractNames(after);
        return beforeNames.Intersect(afterNames).Count();
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

    private static int CalculateDiffSize(string before, string after)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(before, after);

        return diff.Lines.Count(l => l.Type != ChangeType.Unchanged);
    }

    private static Dictionary<string, object> AnalyzeCalorIds(EvaluationContext context)
    {
        var source = context.CalorSource;
        var ids = CalorIdentities.Of(context);
        return new Dictionary<string, object>
        {
            ["moduleIds"] = ids.Where(d => d.Kind == IdKind.Module).Select(d => d.Id).ToList(),
            ["functionIds"] = ids.Where(d => CalorIdentities.IsCallable(d.Kind)).Select(d => d.Id).ToList(),
            ["variableIds"] = new List<string>(), // local bindings carry no IDs in native Calor
            ["hasClosingTags"] = false,
            ["idDensity"] = ids.Count / Math.Max(1.0, source.Split('\n').Length)
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
