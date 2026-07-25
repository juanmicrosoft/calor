using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool for previewing the effects of an edit before committing it.
/// Compares original and modified source to detect what would break.
/// </summary>
public sealed class EditPreviewTool : McpToolBase
{
    public override string Name => "calor_edit_preview";

    public override string Description =>
        "Preview the effects of an edit before committing it. " +
        "Given original and modified Calor source, reports compilation errors, " +
        "contract violations, effect inconsistencies, and dangling references. " +
        "Returns a verdict: safe, safe_with_warnings, or breaking.";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };


    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "originalSource": {
                    "type": "string",
                    "description": "The original Calor source code before the edit"
                },
                "modifiedSource": {
                    "type": "string",
                    "description": "The modified Calor source code after the edit"
                },
                "checks": {
                    "type": "array",
                    "items": { "type": "string", "enum": ["compile", "contracts", "effects", "references"] },
                    "description": "Which checks to run (default: all)"
                }
            },
            "required": ["originalSource", "modifiedSource"]
        ,

        "additionalProperties": false

        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var originalSource = GetString(arguments, "originalSource");
        var modifiedSource = GetString(arguments, "modifiedSource");

        if (string.IsNullOrEmpty(originalSource) || string.IsNullOrEmpty(modifiedSource))
            return Task.FromResult(McpToolResult.Error("Both 'originalSource' and 'modifiedSource' are required"));

        // Parse checks array
        var runAll = true;
        var runCompile = true;
        var runContracts = true;
        var runEffects = true;
        var runReferences = true;

        if (arguments != null && arguments.Value.TryGetProperty("checks", out var checksElement) && checksElement.ValueKind == JsonValueKind.Array)
        {
            runAll = false;
            runCompile = false;
            runContracts = false;
            runEffects = false;
            runReferences = false;
            foreach (var check in checksElement.EnumerateArray())
            {
                var val = check.GetString();
                switch (val)
                {
                    case "compile": runCompile = true; break;
                    case "contracts": runContracts = true; break;
                    case "effects": runEffects = true; break;
                    case "references": runReferences = true; break;
                }
            }
        }

        if (runAll)
        {
            runCompile = true;
            runContracts = true;
            runEffects = true;
            runReferences = true;
        }

        // Parse both versions
        var originalParse = CalorSourceHelper.Parse(originalSource);
        var modifiedParse = CalorSourceHelper.Parse(modifiedSource);

        // Edit summary
        var editSummary = ComputeEditSummary(originalSource, modifiedSource, originalParse, modifiedParse);

        // Compilation check
        var compilationResult = new CompilationCheckResult { Checked = runCompile };
        if (runCompile)
        {
            compilationResult.OriginalCompiles = originalParse.IsSuccess;
            compilationResult.ModifiedCompiles = modifiedParse.IsSuccess;
            compilationResult.Errors = modifiedParse.ToEnvelopeDiagnostics();
        }

        // Contract verification
        var contractResult = new ContractCheckResult { Checked = runContracts };
        if (runContracts && modifiedParse.IsSuccess)
        {
            CheckContracts(originalParse, modifiedParse, contractResult);
        }

        // Effect analysis
        var effectResult = new EffectCheckResult { Checked = runEffects };
        if (runEffects && modifiedParse.IsSuccess)
        {
            CheckEffects(modifiedParse, effectResult);
        }

        // Reference integrity
        var referenceResult = new ReferenceCheckResult { Checked = runReferences };
        if (runReferences && modifiedParse.IsSuccess && originalParse.IsSuccess)
        {
            CheckReferences(originalParse, modifiedParse, referenceResult);
        }

        // Determine verdict
        var verdict = DetermineVerdict(compilationResult, contractResult, effectResult, referenceResult);

        // Recommendations
        var recommendations = GenerateRecommendations(compilationResult, contractResult, effectResult, referenceResult);

        return Task.FromResult(McpToolResult.Json(new EditPreviewOutput
        {
            Success = true,
            EditSummary = editSummary,
            CompilationResult = compilationResult,
            ContractVerification = contractResult,
            EffectAnalysis = effectResult,
            ReferenceIntegrity = referenceResult,
            OverallVerdict = verdict,
            Recommendations = recommendations
        }));
    }

    internal static EditSummaryInfo ComputeEditSummary(string original, string modified,
        ParseResult originalParse, ParseResult modifiedParse)
    {
        var origLines = original.Split('\n');
        var modLines = modified.Split('\n');

        var linesAdded = Math.Max(0, modLines.Length - origLines.Length);
        var linesRemoved = Math.Max(0, origLines.Length - modLines.Length);
        var linesModified = 0;
        for (int i = 0; i < Math.Min(origLines.Length, modLines.Length); i++)
        {
            if (origLines[i] != modLines[i]) linesModified++;
        }

        var symbolsAdded = new List<string>();
        var symbolsRemoved = new List<string>();

        // Partial ASTs are good enough for a surface-level symbol diff — a
        // declaration the recovery elided just shows up as removed.
        if (originalParse.Ast != null && modifiedParse.Ast != null)
        {
            var origIds = CollectSymbolIds(originalParse.Ast);
            var modIds = CollectSymbolIds(modifiedParse.Ast);

            symbolsAdded = modIds.Except(origIds).ToList();
            symbolsRemoved = origIds.Except(modIds).ToList();
        }

        return new EditSummaryInfo
        {
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
            LinesModified = linesModified,
            SymbolsAdded = symbolsAdded,
            SymbolsRemoved = symbolsRemoved
        };
    }

    internal static HashSet<string> CollectSymbolIds(ModuleNode ast)
    {
        var ids = new HashSet<string>();
        foreach (var func in ast.Functions) ids.Add(func.Id);
        foreach (var cls in ast.Classes)
        {
            ids.Add(cls.Name);
            foreach (var method in cls.Methods) ids.Add($"{cls.Name}.{method.Id}");
        }
        foreach (var iface in ast.Interfaces) ids.Add(iface.Name);
        foreach (var enumDef in ast.Enums) ids.Add(enumDef.Name);
        return ids;
    }

    internal static void CheckContracts(ParseResult originalParse, ParseResult modifiedParse, ContractCheckResult result)
    {
        if (!originalParse.IsSuccess || !modifiedParse.IsSuccess) return;

        var origContracts = CountContracts(originalParse.Ast!);
        var modContracts = CountContracts(modifiedParse.Ast!);

        result.OriginalContractCount = origContracts;
        result.ModifiedContractCount = modContracts;

        if (modContracts < origContracts)
        {
            result.Issues.Add($"Contract count decreased from {origContracts} to {modContracts} — contracts may have been accidentally removed");
        }

        // Check for functions that lost contracts
        var origFuncContracts = GetFunctionContracts(originalParse.Ast!);
        var modFuncContracts = GetFunctionContracts(modifiedParse.Ast!);

        foreach (var (funcId, count) in origFuncContracts)
        {
            if (modFuncContracts.TryGetValue(funcId, out var newCount) && newCount < count)
            {
                result.Issues.Add($"Function '{funcId}' lost contracts: {count} → {newCount}");
            }
        }
    }

    private static int CountContracts(ModuleNode ast)
    {
        var count = 0;
        foreach (var func in ast.Functions)
        {
            count += func.Preconditions.Count + func.Postconditions.Count;
        }
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                count += method.Preconditions.Count + method.Postconditions.Count;
            }
        }
        return count;
    }

    private static Dictionary<string, int> GetFunctionContracts(ModuleNode ast)
    {
        var result = new Dictionary<string, int>();
        foreach (var func in ast.Functions)
        {
            var count = func.Preconditions.Count + func.Postconditions.Count;
            if (count > 0) result[func.Id] = count;
        }
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var count = method.Preconditions.Count + method.Postconditions.Count;
                if (count > 0) result[$"{cls.Name}.{method.Id}"] = count;
            }
        }
        return result;
    }

    internal static void CheckEffects(ParseResult modifiedParse, EffectCheckResult result)
    {
        if (!modifiedParse.IsSuccess) return;

        // Run effect enforcement on modified source
        var diagnostics = new DiagnosticBag();
        var pass = new EffectEnforcementPass(diagnostics, policy: UnknownCallPolicy.Permissive);
        pass.Enforce(modifiedParse.Ast!);

        result.EffectViolations = diagnostics.Errors
            .Where(e => e.Code == DiagnosticCode.ForbiddenEffect.ToString() || e.Message.Contains("effect"))
            .Select(e => e.Message)
            .ToList();

        result.HasViolations = result.EffectViolations.Count > 0;
    }

    internal static void CheckReferences(ParseResult originalParse, ParseResult modifiedParse, ReferenceCheckResult result)
    {
        // Functions are checked against actual call targets in the modified
        // AST — declarations are referenced by NAME at call sites (§C{add}),
        // not by declaration id (f001), so an id-substring scan would both
        // miss real dangling calls and flag ids that only appear in strings.
        var removedFunctions = CollectFunctionNames(originalParse.Ast!)
            .Except(CollectFunctionNames(modifiedParse.Ast!))
            .ToList();
        if (removedFunctions.Count > 0)
        {
            var callTargets = CollectCallTargets(modifiedParse.Ast!);
            foreach (var name in removedFunctions)
            {
                if (callTargets.Contains(name))
                {
                    result.DanglingReferences.Add($"Function '{name}' was removed but is still called");
                }
            }
        }

        // Types have no call-graph equivalent; use a whole-word text match
        // (still a heuristic, but bounded — no substring hits inside longer
        // identifiers).
        var removedTypes = CollectTypeNames(originalParse.Ast!)
            .Except(CollectTypeNames(modifiedParse.Ast!))
            .ToList();
        foreach (var name in removedTypes)
        {
            if (ContainsWholeWord(modifiedParse.Source!, name))
            {
                result.DanglingReferences.Add($"Type '{name}' was removed but is still referenced");
            }
        }

        result.HasDanglingReferences = result.DanglingReferences.Count > 0;
    }

    /// <summary>Top-level function names — what call sites actually reference.</summary>
    internal static HashSet<string> CollectFunctionNames(ModuleNode ast)
        => ast.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Class, interface, and enum names.</summary>
    internal static HashSet<string> CollectTypeNames(ModuleNode ast)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cls in ast.Classes) names.Add(cls.Name);
        foreach (var iface in ast.Interfaces) names.Add(iface.Name);
        foreach (var enumDef in ast.Enums) names.Add(enumDef.Name);
        return names;
    }

    /// <summary>Every call-target name in the module, as written at the call sites.</summary>
    internal static HashSet<string> CollectCallTargets(ModuleNode ast)
        => Analysis.CallGraphAnalysis.Build(ast).ForwardGraph.Values
            .SelectMany(calls => calls.Select(c => c.Callee))
            .ToHashSet(StringComparer.Ordinal);

    internal static bool ContainsWholeWord(string source, string word)
        => Regex.IsMatch(source, $@"\b{Regex.Escape(word)}\b");

    internal static string DetermineVerdict(CompilationCheckResult compile, ContractCheckResult contracts,
        EffectCheckResult effects, ReferenceCheckResult references)
    {
        if (compile.Checked && !compile.ModifiedCompiles)
            return "breaking";

        if (references.Checked && references.HasDanglingReferences)
            return "breaking";

        if (effects.Checked && effects.HasViolations)
            return "breaking";

        if (contracts.Checked && contracts.Issues.Count > 0)
            return "safe_with_warnings";

        return "safe";
    }

    internal static List<string> GenerateRecommendations(CompilationCheckResult compile, ContractCheckResult contracts,
        EffectCheckResult effects, ReferenceCheckResult references)
    {
        var recs = new List<string>();

        if (compile.Checked && !compile.ModifiedCompiles)
            recs.Add("Fix compilation errors before applying this edit");

        if (contracts.Checked && contracts.Issues.Count > 0)
            recs.Add("Review contract changes — ensure postconditions still hold");

        if (effects.Checked && effects.HasViolations)
            recs.Add("Update effect declarations to match actual effects in modified code");

        if (references.Checked && references.HasDanglingReferences)
            recs.Add("Update or remove references to deleted symbols");

        if (recs.Count == 0)
            recs.Add("Edit looks safe to apply");

        return recs;
    }

    private sealed class EditPreviewOutput
    {
        [JsonPropertyName("success")] public bool Success { get; init; }
        [JsonPropertyName("editSummary")] public EditSummaryInfo? EditSummary { get; init; }
        [JsonPropertyName("compilationResult")] public CompilationCheckResult? CompilationResult { get; init; }
        [JsonPropertyName("contractVerification")] public ContractCheckResult? ContractVerification { get; init; }
        [JsonPropertyName("effectAnalysis")] public EffectCheckResult? EffectAnalysis { get; init; }
        [JsonPropertyName("referenceIntegrity")] public ReferenceCheckResult? ReferenceIntegrity { get; init; }
        [JsonPropertyName("overallVerdict")] public string? OverallVerdict { get; init; }
        [JsonPropertyName("recommendations")] public List<string> Recommendations { get; init; } = new();
    }

    internal sealed class EditSummaryInfo
    {
        [JsonPropertyName("linesAdded")] public int LinesAdded { get; init; }
        [JsonPropertyName("linesRemoved")] public int LinesRemoved { get; init; }
        [JsonPropertyName("linesModified")] public int LinesModified { get; init; }
        [JsonPropertyName("symbolsAdded")] public List<string> SymbolsAdded { get; init; } = new();
        [JsonPropertyName("symbolsRemoved")] public List<string> SymbolsRemoved { get; init; } = new();
    }

    internal sealed class CompilationCheckResult
    {
        [JsonPropertyName("checked")] public bool Checked { get; set; }
        [JsonPropertyName("originalCompiles")] public bool OriginalCompiles { get; set; }
        [JsonPropertyName("modifiedCompiles")] public bool ModifiedCompiles { get; set; }
        /// <summary>Envelope schema v1.1 diagnostic entries for parse errors of the modified source.</summary>
        [JsonPropertyName("errors")] public List<EnvelopeDiagnostic> Errors { get; set; } = new();
    }

    internal sealed class ContractCheckResult
    {
        [JsonPropertyName("checked")] public bool Checked { get; set; }
        [JsonPropertyName("originalContractCount")] public int OriginalContractCount { get; set; }
        [JsonPropertyName("modifiedContractCount")] public int ModifiedContractCount { get; set; }
        [JsonPropertyName("issues")] public List<string> Issues { get; set; } = new();
    }

    internal sealed class EffectCheckResult
    {
        [JsonPropertyName("checked")] public bool Checked { get; set; }
        [JsonPropertyName("hasViolations")] public bool HasViolations { get; set; }
        [JsonPropertyName("effectViolations")] public List<string> EffectViolations { get; set; } = new();
    }

    internal sealed class ReferenceCheckResult
    {
        [JsonPropertyName("checked")] public bool Checked { get; set; }
        [JsonPropertyName("hasDanglingReferences")] public bool HasDanglingReferences { get; set; }
        [JsonPropertyName("danglingReferences")] public List<string> DanglingReferences { get; set; } = new();
    }
}
