using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// MCP tool: calor_refine
/// Work with Calor's refinement type system — obligations, bounds checking,
/// diagnostics, guards, fix suggestions, and type inference.
/// </summary>
public sealed class RefineTool : McpToolBase
{
    public override string Name => "calor_refine";

    public override string Description =>
        "Work with Calor's refinement type system — obligations, bounds checking, " +
        "diagnostics, guards, fix suggestions, and type inference";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["obligations", "bounds", "diagnose", "guards", "fixes", "types"],
                    "description": "Refinement action. obligations=generate proof obligations, bounds=verify array bounds, diagnose=diagnose type failures, guards=discover guards, fixes=suggest fix templates, types=suggest refined types"
                },
                "source": {
                    "type": "string",
                    "description": "Calor source code to analyze"
                },
                "timeout": {
                    "type": "integer",
                    "default": 5000,
                    "description": "Z3 solver timeout per obligation in milliseconds (obligations action)"
                },
                "function_id": {
                    "type": "string",
                    "description": "Optional: filter results to a specific function ID"
                },
                "obligation_id": {
                    "type": "string",
                    "description": "Optional: specific obligation ID to target (guards, fixes actions)"
                },
                "policy": {
                    "type": "string",
                    "enum": ["default", "strict", "permissive"],
                    "default": "default",
                    "description": "Obligation policy: default, strict (all errors), permissive (all guards) (diagnose action)"
                }
            },
            "required": ["action", "source"]
        ,

        "additionalProperties": false

        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var action = GetString(arguments, "action");
        if (string.IsNullOrEmpty(action))
            return Task.FromResult(McpToolResult.Error("Missing required parameter: action"));

        var source = GetString(arguments, "source");
        if (string.IsNullOrEmpty(source))
            return Task.FromResult(McpToolResult.Error("Missing required parameter: source"));

        return action switch
        {
            "obligations" => HandleObligations(arguments, source, cancellationToken),
            "bounds" => HandleBounds(arguments, source, cancellationToken),
            "diagnose" => HandleDiagnose(arguments, source, cancellationToken),
            "guards" => HandleGuards(arguments, source, cancellationToken),
            "fixes" => HandleFixes(arguments, source, cancellationToken),
            "types" => HandleTypes(arguments, source, cancellationToken),
            _ => Task.FromResult(McpToolResult.Error($"Unknown action: {action}. Expected: obligations, bounds, diagnose, guards, fixes, types"))
        };
    }

    // ───── obligations ─────

    private Task<McpToolResult> HandleObligations(JsonElement? arguments, string source, CancellationToken cancellationToken)
    {
        var timeout = (uint)GetInt(arguments, "timeout", 5000);
        var functionId = GetString(arguments, "function_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                VerificationTimeoutMs = timeout,
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, "mcp_obligations.calr", options);

            var tracker = options.ObligationResults;
            if (tracker == null)
            {
                return Task.FromResult(McpToolResult.Json(new ObligationsOutput
                {
                    Success = true,
                    Summary = new ObligationsSummaryOutput(),
                    Obligations = new List<ObligationOutput>(),
                    CompilationErrors = result.Diagnostics.Errors.Select(d => d.Message).ToList()
                }));
            }

            var obligations = functionId != null
                ? tracker.GetByFunction(functionId)
                : tracker.Obligations;

            var summary = tracker.GetSummary();

            var output = new ObligationsOutput
            {
                Success = !result.Diagnostics.HasErrors && summary.Failed == 0,
                Summary = new ObligationsSummaryOutput
                {
                    Total = summary.Total,
                    Discharged = summary.Discharged,
                    Failed = summary.Failed,
                    Timeout = summary.Timeout,
                    Boundary = summary.Boundary,
                    Pending = summary.Pending,
                    Unsupported = summary.Unsupported
                },
                Obligations = obligations.Select(o => new ObligationOutput
                {
                    Id = o.Id,
                    Kind = o.Kind.ToString(),
                    FunctionId = o.FunctionId,
                    Description = o.Description,
                    Status = o.Status.ToString(),
                    Line = o.Span.Line,
                    Column = o.Span.Column,
                    Counterexample = o.CounterexampleDescription,
                    SuggestedFix = o.SuggestedFix,
                    SolverDurationMs = o.SolverDuration?.TotalMilliseconds
                }).ToList(),
                CompilationErrors = result.Diagnostics.Errors.Select(d => d.Message).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output, isError: !output.Success));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Obligation verification failed: {ex.Message}"));
        }
    }

    // ───── bounds ─────

    private Task<McpToolResult> HandleBounds(JsonElement? arguments, string source, CancellationToken cancellationToken)
    {
        var functionId = GetString(arguments, "function_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            Program.Compile(source, "mcp_bounds.calr", options);

            var tracker = options.ObligationResults;

            var accessSites = new List<AccessSiteOutput>();

            if (tracker != null)
            {
                var obligations = tracker.Obligations
                    .Where(o => o.Kind == ObligationKind.IndexBounds);

                if (!string.IsNullOrEmpty(functionId))
                    obligations = obligations.Where(o => o.FunctionId == functionId);

                foreach (var obl in obligations)
                {
                    accessSites.Add(new AccessSiteOutput
                    {
                        ObligationId = obl.Id,
                        FunctionId = obl.FunctionId,
                        ArrayName = obl.ParameterName ?? "unknown",
                        Status = obl.Status.ToString(),
                        Description = obl.Description,
                        Counterexample = obl.CounterexampleDescription,
                        SuggestedFix = obl.SuggestedFix,
                        SolverDurationMs = obl.SolverDuration?.TotalMilliseconds
                    });
                }
            }

            var indexBoundsCount = tracker?.Obligations.Count(o => o.Kind == ObligationKind.IndexBounds) ?? 0;
            var indexBoundsDischarged = tracker?.Obligations.Count(o => o.Kind == ObligationKind.IndexBounds && o.Status == ObligationStatus.Discharged) ?? 0;
            var indexBoundsFailed = tracker?.Obligations.Count(o => o.Kind == ObligationKind.IndexBounds && o.Status == ObligationStatus.Failed) ?? 0;

            return Task.FromResult(McpToolResult.Json(new BoundsCheckOutput
            {
                Safe = indexBoundsFailed == 0,
                TotalAccessSites = indexBoundsCount,
                Discharged = indexBoundsDischarged,
                Failed = indexBoundsFailed,
                AccessSites = accessSites
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Bounds check failed: {ex.Message}"));
        }
    }

    // ───── diagnose ─────

    private Task<McpToolResult> HandleDiagnose(JsonElement? arguments, string source, CancellationToken cancellationToken)
    {
        var policyName = GetString(arguments, "policy") ?? "default";

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, "mcp_diagnose.calr", options);

            var tracker = options.ObligationResults;

            var typeSuggestions = new List<TypeSuggestion>();
            if (result.Ast != null)
            {
                var typeSuggester = new TypeSuggester();
                typeSuggestions = typeSuggester.Suggest(result.Ast);
            }

            var guardDiscovery = new GuardDiscovery();
            var guards = tracker != null
                ? guardDiscovery.DiscoverGuards(tracker)
                : new List<DiscoveredGuard>();

            var policy = policyName switch
            {
                "strict" => ObligationPolicy.Strict,
                "permissive" => ObligationPolicy.Permissive,
                _ => ObligationPolicy.Default
            };

            var patches = new List<PatchOutput>();

            if (tracker != null)
            {
                foreach (var obl in tracker.Obligations)
                {
                    var action = policy.GetAction(obl.Status);
                    if (action == ObligationAction.Ignore)
                        continue;

                    var oblGuards = guards
                        .Where(g => g.ObligationId == obl.Id)
                        .ToList();

                    foreach (var guard in oblGuards)
                    {
                        patches.Add(new PatchOutput
                        {
                            ObligationId = obl.Id,
                            ObligationStatus = obl.Status.ToString(),
                            PolicyAction = action.ToString(),
                            PatchKind = guard.InsertionKind,
                            CalorCode = guard.CalorExpression,
                            Description = guard.Description,
                            Confidence = guard.Confidence,
                            DischargesObligations = new List<string> { obl.Id }
                        });
                    }
                }
            }

            foreach (var ts in typeSuggestions)
            {
                patches.Add(new PatchOutput
                {
                    ObligationId = null,
                    ObligationStatus = null,
                    PolicyAction = "suggest",
                    PatchKind = "refine_parameter",
                    CalorCode = ts.CalorSyntax,
                    Description = ts.Reason,
                    Confidence = ts.Confidence,
                    DischargesObligations = new List<string>()
                });
            }

            var summary = tracker?.GetSummary();

            return Task.FromResult(McpToolResult.Json(new DiagnoseOutput
            {
                Success = summary == null || summary.Failed == 0,
                Policy = policyName,
                Summary = summary != null ? new DiagnoseSummaryOutput
                {
                    Total = summary.Total,
                    Discharged = summary.Discharged,
                    Failed = summary.Failed,
                    Timeout = summary.Timeout,
                    Boundary = summary.Boundary
                } : null,
                Patches = patches,
                CompilationErrors = result.Diagnostics.Errors.Select(d => d.Message).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Refinement diagnosis failed: {ex.Message}"));
        }
    }

    // ───── guards ─────

    private Task<McpToolResult> HandleGuards(JsonElement? arguments, string source, CancellationToken cancellationToken)
    {
        var obligationId = GetString(arguments, "obligation_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            var result = Program.Compile(source, "mcp_guards.calr", options);

            var tracker = options.ObligationResults;
            if (tracker == null)
            {
                return Task.FromResult(McpToolResult.Json(new GuardDiscoveryOutput
                {
                    Success = true,
                    Guards = new List<GuardOutput>()
                }));
            }

            var discovery = new GuardDiscovery();
            var guards = obligationId != null
                ? tracker.Obligations
                    .Where(o => o.Id == obligationId)
                    .SelectMany(o => discovery.DiscoverForObligation(o))
                    .ToList()
                : discovery.DiscoverGuards(tracker);

            if (result.Ast != null && result.Ast.Functions.Count > 0)
            {
                var allParams = result.Ast.Functions
                    .SelectMany(f => f.Parameters.Select(p => (p.Name, p.TypeName)))
                    .Distinct()
                    .ToList();
                discovery.ValidateWithZ3(guards, tracker.Obligations, allParams);
            }

            return Task.FromResult(McpToolResult.Json(new GuardDiscoveryOutput
            {
                Success = true,
                Guards = guards.Select(g => new GuardOutput
                {
                    ObligationId = g.ObligationId,
                    Description = g.Description,
                    CalorExpression = g.CalorExpression,
                    InsertionKind = g.InsertionKind,
                    Confidence = g.Confidence,
                    Validated = g.Validated
                }).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Guard discovery failed: {ex.Message}"));
        }
    }

    // ───── fixes ─────

    private Task<McpToolResult> HandleFixes(JsonElement? arguments, string source, CancellationToken cancellationToken)
    {
        var obligationId = GetString(arguments, "obligation_id");

        try
        {
            var options = new CompilationOptions
            {
                VerifyRefinements = true,
                EnableTypeChecking = true,
                CancellationToken = cancellationToken
            };

            Program.Compile(source, "mcp_suggest.calr", options);

            var tracker = options.ObligationResults;
            if (tracker == null)
            {
                return Task.FromResult(McpToolResult.Json(new SuggestFixesOutput
                {
                    Success = true,
                    Fixes = new List<FixSuggestion>()
                }));
            }

            var targetObligations = obligationId != null
                ? tracker.Obligations.Where(o => o.Id == obligationId).ToList()
                : tracker.GetFailed();

            var fixes = new List<FixSuggestion>();

            foreach (var obl in targetObligations)
            {
                fixes.AddRange(GenerateFixSuggestions(obl));
            }

            return Task.FromResult(McpToolResult.Json(new SuggestFixesOutput
            {
                Success = true,
                Fixes = fixes
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Fix suggestion failed: {ex.Message}"));
        }
    }

    // ───── types ─────

    private Task<McpToolResult> HandleTypes(JsonElement? arguments, string source, CancellationToken cancellationToken)
    {
        var functionId = GetString(arguments, "function_id");

        try
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(source, diagnostics);
            var tokens = lexer.TokenizeAll();
            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors)
            {
                return Task.FromResult(McpToolResult.Json(new TypeSuggestionOutput
                {
                    Success = false,
                    Suggestions = new List<SuggestionOutput>(),
                    Errors = diagnostics.Errors.Select(d => d.Message).ToList()
                }, isError: true));
            }

            var suggester = new TypeSuggester();
            var suggestions = suggester.Suggest(module);

            if (functionId != null)
                suggestions = suggestions.Where(s => s.FunctionId == functionId).ToList();

            return Task.FromResult(McpToolResult.Json(new TypeSuggestionOutput
            {
                Success = true,
                Suggestions = suggestions.Select(s => new SuggestionOutput
                {
                    FunctionId = s.FunctionId,
                    ParameterName = s.ParameterName,
                    CurrentType = s.CurrentType,
                    SuggestedPredicate = s.SuggestedPredicate,
                    Reason = s.Reason,
                    Confidence = s.Confidence,
                    CalorSyntax = s.CalorSyntax
                }).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Type suggestion failed: {ex.Message}"));
        }
    }

    // ───── Fix generation helpers ─────

    private static List<FixSuggestion> GenerateFixSuggestions(Obligation obligation)
    {
        var fixes = new List<FixSuggestion>();
        var paramName = ExtractParameterName(obligation.Description);

        switch (obligation.Kind)
        {
            case ObligationKind.RefinementEntry:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "add_precondition",
                    Description = $"Add a §Q precondition to require callers satisfy the refinement constraint",
                    Confidence = "high",
                    Template = paramName != null
                        ? $"§Q (>= {paramName} INT:0)"
                        : "§Q (condition)"
                });
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "add_guard",
                    Description = "Add a runtime guard at the start of the function body",
                    Confidence = "medium",
                    Template = paramName != null
                        ? $"§IF{{g1}} (< {paramName} INT:0) → §R (err \"Invalid {paramName}\")"
                        : "§IF{g1} (not condition) → §R (err \"constraint violated\")"
                });
                break;

            case ObligationKind.ProofObligation:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "strengthen_precondition",
                    Description = "Add or strengthen preconditions so the proof obligation can be discharged",
                    Confidence = "high",
                    Template = "§Q (condition-that-implies-proof)"
                });
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "add_guard",
                    Description = "Wrap the code path with a guard that ensures the condition",
                    Confidence = "medium",
                    Template = "§IF{g1} (not condition) → §R (err \"condition not met\")"
                });
                break;

            case ObligationKind.Subtype:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "refine_parameter",
                    Description = "Change the source parameter to use the refined type",
                    Confidence = "high",
                    Template = "§I{RefinedType:param}"
                });
                break;

            default:
                fixes.Add(new FixSuggestion
                {
                    ObligationId = obligation.Id,
                    Strategy = "mark_unsafe",
                    Description = "Suppress this obligation (not recommended — obligation remains unverified)",
                    Confidence = "low",
                    Template = "// SUPPRESS: " + obligation.Id
                });
                break;
        }

        return fixes;
    }

    private static string? ExtractParameterName(string description)
    {
        var start = description.IndexOf('\'');
        var end = description.IndexOf('\'', start + 1);
        if (start >= 0 && end > start)
            return description[(start + 1)..end];
        return null;
    }

    // ───── Output DTOs ─────

    // obligations
    private sealed class ObligationsOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("summary")]
        public required ObligationsSummaryOutput Summary { get; init; }

        [JsonPropertyName("obligations")]
        public required List<ObligationOutput> Obligations { get; init; }

        [JsonPropertyName("compilationErrors")]
        public required List<string> CompilationErrors { get; init; }
    }

    private sealed class ObligationsSummaryOutput
    {
        [JsonPropertyName("total")]
        public int Total { get; init; }

        [JsonPropertyName("discharged")]
        public int Discharged { get; init; }

        [JsonPropertyName("failed")]
        public int Failed { get; init; }

        [JsonPropertyName("timeout")]
        public int Timeout { get; init; }

        [JsonPropertyName("boundary")]
        public int Boundary { get; init; }

        [JsonPropertyName("pending")]
        public int Pending { get; init; }

        [JsonPropertyName("unsupported")]
        public int Unsupported { get; init; }
    }

    private sealed class ObligationOutput
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("function_id")]
        public required string FunctionId { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("column")]
        public int Column { get; init; }

        [JsonPropertyName("counterexample")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Counterexample { get; init; }

        [JsonPropertyName("suggested_fix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SuggestedFix { get; init; }

        [JsonPropertyName("solver_duration_ms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? SolverDurationMs { get; init; }
    }

    // bounds
    private sealed class BoundsCheckOutput
    {
        [JsonPropertyName("safe")]
        public bool Safe { get; init; }

        [JsonPropertyName("total_access_sites")]
        public int TotalAccessSites { get; init; }

        [JsonPropertyName("discharged")]
        public int Discharged { get; init; }

        [JsonPropertyName("failed")]
        public int Failed { get; init; }

        [JsonPropertyName("access_sites")]
        public required List<AccessSiteOutput> AccessSites { get; init; }
    }

    private sealed class AccessSiteOutput
    {
        [JsonPropertyName("obligation_id")]
        public required string ObligationId { get; init; }

        [JsonPropertyName("function_id")]
        public required string FunctionId { get; init; }

        [JsonPropertyName("array_name")]
        public required string ArrayName { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("counterexample")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Counterexample { get; init; }

        [JsonPropertyName("suggested_fix")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SuggestedFix { get; init; }

        [JsonPropertyName("solver_duration_ms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? SolverDurationMs { get; init; }
    }

    // diagnose
    private sealed class DiagnoseOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("policy")]
        public required string Policy { get; init; }

        [JsonPropertyName("summary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DiagnoseSummaryOutput? Summary { get; init; }

        [JsonPropertyName("patches")]
        public required List<PatchOutput> Patches { get; init; }

        [JsonPropertyName("compilation_errors")]
        public required List<string> CompilationErrors { get; init; }
    }

    private sealed class DiagnoseSummaryOutput
    {
        [JsonPropertyName("total")]
        public int Total { get; init; }

        [JsonPropertyName("discharged")]
        public int Discharged { get; init; }

        [JsonPropertyName("failed")]
        public int Failed { get; init; }

        [JsonPropertyName("timeout")]
        public int Timeout { get; init; }

        [JsonPropertyName("boundary")]
        public int Boundary { get; init; }
    }

    private sealed class PatchOutput
    {
        [JsonPropertyName("obligation_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ObligationId { get; init; }

        [JsonPropertyName("obligation_status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ObligationStatus { get; init; }

        [JsonPropertyName("policy_action")]
        public required string PolicyAction { get; init; }

        [JsonPropertyName("patch_kind")]
        public required string PatchKind { get; init; }

        [JsonPropertyName("calor_code")]
        public required string CalorCode { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("discharges_obligations")]
        public required List<string> DischargesObligations { get; init; }
    }

    // guards
    private sealed class GuardDiscoveryOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("guards")]
        public required List<GuardOutput> Guards { get; init; }
    }

    private sealed class GuardOutput
    {
        [JsonPropertyName("obligation_id")]
        public required string ObligationId { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("calor_expression")]
        public required string CalorExpression { get; init; }

        [JsonPropertyName("insertion_kind")]
        public required string InsertionKind { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("validated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Validated { get; init; }
    }

    // fixes
    private sealed class SuggestFixesOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("fixes")]
        public required List<FixSuggestion> Fixes { get; init; }
    }

    private sealed class FixSuggestion
    {
        [JsonPropertyName("obligation_id")]
        public required string ObligationId { get; init; }

        [JsonPropertyName("strategy")]
        public required string Strategy { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("template")]
        public required string Template { get; init; }
    }

    // types
    private sealed class TypeSuggestionOutput
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("suggestions")]
        public required List<SuggestionOutput> Suggestions { get; init; }

        [JsonPropertyName("errors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Errors { get; init; }
    }

    private sealed class SuggestionOutput
    {
        [JsonPropertyName("function_id")]
        public required string FunctionId { get; init; }

        [JsonPropertyName("parameter_name")]
        public required string ParameterName { get; init; }

        [JsonPropertyName("current_type")]
        public required string CurrentType { get; init; }

        [JsonPropertyName("suggested_predicate")]
        public required string SuggestedPredicate { get; init; }

        [JsonPropertyName("reason")]
        public required string Reason { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("calor_syntax")]
        public required string CalorSyntax { get; init; }
    }
}
