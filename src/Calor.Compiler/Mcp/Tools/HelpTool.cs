using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Init;
using Calor.Compiler.Migration;
using Calor.Compiler.SelfTest;
using Calor.Compiler.Telemetry;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Consolidated MCP tool for Calor syntax, feature support, error explanations, and working examples.
/// Merges: SyntaxLookupTool, SyntaxHelpTool, FeatureSupportTool, ExplainErrorTool, GetExampleTool.
/// </summary>
public sealed class HelpTool : McpToolBase
{
    // ── Lazy-loaded resources ────────────────────────────────────────
    private static readonly Lazy<SyntaxDocumentation?> SyntaxDoc = new(LoadSyntaxDocumentation);
    private static Lazy<string> ReferenceContent = new(LoadReferenceContent);
    private static readonly Lazy<List<ErrorPattern>> ErrorPatterns = new(LoadErrorPatterns);

    /// <summary>
    /// Environment variable to override the generated reference path.
    /// </summary>
    public const string ReferenceFilePathEnvVar = "CALOR_REFERENCE_FILE";

    /// <summary>
    /// Resets the cached reference content. Used for testing environment variable overrides.
    /// </summary>
    internal static void ResetCacheForTesting()
    {
        ReferenceContent = new Lazy<string>(LoadReferenceContent);
    }

    public override string Name => "calor_help";

    public override string Description =>
        "Get help with Calor syntax through one or two closest complete examples, conversion feature support, error explanations, or the example catalog";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["syntax", "features", "error", "example"],
                    "description": "syntax=closest complete canonical examples (at most two), features=conversion support, error=explain diagnostic, example=existing example catalog",
                    "default": "syntax"
                },
                "query": {
                    "type": "string",
                    "description": "Syntax topic or task (e.g., 'for loop', 'async', 'dictionary'). overview gives a concise overview; reference gives the full reference. Used with action=syntax."
                },
                "feature": {
                    "type": "string",
                    "description": "Alias for query with action=syntax; conversion feature name with action=features."
                },
                "supportLevel": {
                    "type": "string",
                    "enum": ["Full", "Partial", "NotSupported", "ManualRequired"],
                    "description": "List all features at a specific support level. Used with action=features."
                },
                "error": {
                    "type": "string",
                    "description": "The error code (e.g., 'CALOR0042') or error message text to explain. Used with action=error."
                },
                "name": {
                    "type": "string",
                    "description": "Name or keyword to search for (e.g., 'foreach', 'async', 'generic', 'hello_world'). Used with action=example."
                },
                "list": {
                    "type": "boolean",
                    "description": "If true, list all available examples with descriptions. Used with action=example.",
                    "default": false
                }
            },
            "additionalProperties": false
        }
        """;

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var action = GetString(arguments, "action") ?? "syntax";

        return action switch
        {
            "syntax" => HandleSyntax(arguments),
            "features" => HandleFeatures(arguments),
            "error" => HandleError(arguments),
            "example" => HandleExample(arguments),
            _ => Task.FromResult(McpToolResult.Error(
                $"Unknown action '{action}'. Valid actions: syntax, features, error, example"))
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // ── action: "syntax" ── SyntaxLookupTool + SyntaxHelpTool logic ──
    // ═══════════════════════════════════════════════════════════════════

    private Task<McpToolResult> HandleSyntax(JsonElement? arguments)
    {
        var query = GetString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
            query = GetString(arguments, "feature");
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(McpToolResult.Error("Missing required parameter: query"));
        query = query.Trim();
        if (query.Equals("reference", StringComparison.OrdinalIgnoreCase) || ResolveCategory(query) == "overview")
            return HandleSyntaxHelp(query);
        return HandleSyntaxLookup(query);
    }

    #region SyntaxLookup (fuzzy C# → Calor mapping)

    private Task<McpToolResult> HandleSyntaxLookup(string query)
    {
        var doc = SyntaxDoc.Value;
        if (doc == null)
        {
            return Task.FromResult(McpToolResult.Error("Syntax documentation not available"));
        }

        var matches = FindConstructMatches(doc, query);
        if (matches.Count == 0)
        {
            if (CalorTelemetry.IsInitialized)
                CalorTelemetry.Instance.TrackSyntaxHelpQuery(query, ResolveCategory(query), 0, null);
            var availableConstructs = doc.Constructs
                .Select(c => c.CSharpConstruct)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(McpToolResult.Json(new LookupResult
            {
                Found = false,
                Query = query,
                Feature = query,
                Examples = [],
                Message = $"No canonical executable example matches '{query}'. Try a specific syntax topic; use action=features for conversion capabilities.",
                AvailableConstructs = availableConstructs,
                AvailableFeatures = FeatureAliases.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList()
            }));
        }

        var bestMatch = matches[0];
        var candidates = bestMatch.Examples
            .Select((example, index) => (Example: example, Index: index,
                ProgramId: bestMatch.Programs.ElementAtOrDefault(index),
                Score: ExampleMatchScore(example.Calor, bestMatch.Programs.ElementAtOrDefault(index) ?? "", query)))
            .Where(e => e.ProgramId != null && e.Example.Calor.StartsWith("§M{", StringComparison.Ordinal)
                && e.Example.Calor.Contains('\n'))
            .ToList();
        var hasPreciseExample = candidates.Any(c => c.Score > 0);
        var examples = candidates
            .Where(e => !hasPreciseExample || e.Score > 0)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Index)
            .DistinctBy(e => e.Example.Calor, StringComparer.Ordinal)
            .Take(2)
            .Select(e => new SyntaxExampleOutput
            {
                ProgramId = e.ProgramId!,
                CSharp = e.Example.CSharp,
                Calor = e.Example.Calor
            }).ToList();
        var result = new LookupResult
        {
            Found = examples.Count > 0,
            Query = query,
            Feature = query,
            Construct = bestMatch.CSharpConstruct,
            CalorSyntax = bestMatch.CalorSyntax,
            Description = bestMatch.Description,
            Examples = examples,
            Message = examples.Count == 0
                ? "This topic has no canonical executable example. Use action=features for conversion capabilities; no unrelated example was substituted."
                : null,
            OtherMatches = matches.Skip(1).Take(3).Select(m => m.CSharpConstruct).ToList(),
            AvailableFeatures = FeatureAliases.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList()
        };
        if (CalorTelemetry.IsInitialized)
            CalorTelemetry.Instance.TrackSyntaxHelpQuery(query, ResolveCategory(query),
                examples.Count, examples.Count > 0 ? bestMatch.CSharpConstruct : null);

        return Task.FromResult(McpToolResult.Json(result));
    }

    private static List<SyntaxConstruct> FindConstructMatches(SyntaxDocumentation doc, string query)
    {
        // Only category names expand to their primary alias. Precise topics such
        // as foreach or postcondition must not broaden to their entire category.
        var topic = FeatureAliases.TryGetValue(query, out var aliases) ? aliases[0] : query;
        var queryLower = NormalizeQuery(topic);
        var queryTerms = QueryTerms(queryLower);

        var scored = new List<(SyntaxConstruct Construct, int Score)>();

        foreach (var construct in doc.Constructs)
        {
            var score = CalculateMatchScore(construct, queryLower, queryTerms);
            if (score > 0)
            {
                scored.Add((construct, score));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Construct.Id, StringComparer.Ordinal)
            .Select(s => s.Construct)
            .ToList();
    }

    private static int CalculateMatchScore(SyntaxConstruct construct, string queryLower, string[] queryTerms)
    {
        var name = NormalizeQuery(construct.CSharpConstruct);
        var keywords = construct.Keywords.Select(NormalizeQuery).Distinct(StringComparer.Ordinal).ToList();
        if (name == queryLower) return 10_000;
        if (keywords.Contains(queryLower)) return 9_000 + (ContainsTerm(name, queryLower) ? 100 : 0);
        if (queryLower.StartsWith('§') && construct.CalorSyntax.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
            return 8_000;

        var score = keywords.Where(k => ContainsTerm(queryLower, k) && QueryTerms(k).Any(queryTerms.Contains))
            .Sum(k => 100 + k.Length);
        score += queryTerms.Count(t => ContainsTerm(name, t) || keywords.Any(k => ContainsTerm(k, t))) * 50;
        if (ContainsTerm(queryLower, name)) score += 500;
        if (score == 0 && !queryTerms.Any(t => t.Length > 2
            && ContainsTerm(construct.Description, t) && construct.Examples.Any(e => ContainsTerm(e.Calor, t))))
            return 0;
        // Program IDs and actual code help distinguish precise built-ins from
        // broad topic names; prose alone never admits an unrelated result.
        score += construct.Examples.Select((e, i) =>
            ExampleMatchScore(e.Calor, construct.Programs.ElementAtOrDefault(i) ?? "", queryLower)).DefaultIfEmpty().Max();
        return score;
    }

    private static string NormalizeQuery(string query)
        => Regex.Replace(query.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");

    private static string[] QueryTerms(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return terms.Length == 1 ? terms : terms.Where(t => t is not
            ("a" or "an" or "the" or "how" or "do" or "i" or "to" or "in" or "for" or "with" or "please" or "show" or "me")).ToArray();
    }

    private static bool ContainsTerm(string text, string term)
        => term.Length > 0 && Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int ExampleMatchScore(string source, string programId, string query)
        => QueryTerms(NormalizeQuery(query)).Count(t => t.Length > 1
            && (ContainsTerm(source, t) || ContainsTerm(programId.Replace('_', ' '), t))) * 20;

    #endregion

    #region SyntaxHelp (explicit overview and reference)

    private static readonly Dictionary<string, string[]> FeatureAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["async"] = ["async", "await", "§AF", "§AMT", "§AWAIT", "§ASYNC"],
        ["contracts"] = ["contract", "§Q", "§S", "precondition", "postcondition", "requires", "ensures"],
        ["effects"] = ["effect", "§E", "side effect", "cw", "cr", "fs:"],
        ["loops"] = ["loop", "for", "for loop", "foreach", "while", "§L{", "§WH{", "§DO{", "§BK", "§CN"],
        ["conditionals"] = ["if", "if statement", "if-else", "else", "conditional", "§IF{", "§EI", "§EL", "ternary"],
        ["functions"] = ["function", "§F{", "§I{", "§O{", "§R", "return", "parameter", "extension method", "params", "default parameter", "ref", "out", "in"],
        ["classes"] = ["class", "§CL{", "§EXT{", "§IMPL{", "inheritance", "interface", "static class"],
        ["generics"] = ["generic", "<T>", "§WHERE", "type parameter", "constraint"],
        ["collections"] = ["list", "dict", "array", "§LIST{", "§DICT{", "§ARR", "§IDX"],
        ["patterns"] = ["pattern", "match", "switch", "§W{", "§K", "is pattern", "combinator", "relational pattern", "positional", "property pattern"],
        ["exceptions"] = ["try", "catch", "throw", "exception", "§TR{", "§CA", "§TH"],
        ["lambdas"] = ["lambda", "delegate", "§LAM{", "§DEL{", "nested delegate"],
        ["strings"] = ["string", "str", "concat", "substr", "interpolation"],
        ["types"] = ["type", "i32", "i64", "f32", "f64", "bool", "void"],
        ["records"] = ["record", "§D{", "union", "§T{", "§V{"],
        ["enums"] = ["enum", "§EN{"],
        ["constructors"] = ["constructor", "§CTOR{", "§BASE", "§THIS"],
        ["properties"] = ["property", "§PROP{", "§GET", "§SET", "field", "§FLD{"],
        ["structs"] = ["struct", "§ST{", "value type"],
        ["operators"] = ["operator", "§OP{", "overload", "arithmetic"],
        ["nullable"] = ["nullable", "null", "§?", "§??", "§?.", "null check", "null coalescing", "null conditional", "null-conditional", "null-coalescing", "coalesce"],
        ["linq"] = ["linq", "query", "select", "where", "orderby"],
        ["events"] = ["event", "§EV{", "§EVT{", "§EADD", "§EREM", "add accessor", "remove accessor", "event handler"],
        ["using"] = ["using", "§USE{", "dispose", "IDisposable"],
        ["modifiers"] = ["static", "abstract", "sealed", "virtual", "override", "readonly", "partial", "volatile"],
        ["indexers"] = ["indexer", "§IXER{", "this[]", "this[int", "this[string"],
        ["yield"] = ["yield", "iterator", "IEnumerable"],
        ["tuples"] = ["tuple", "value tuple", "(,)", "pair", "triple", "deconstruct", "tuple literal", "tuple type"],
        ["preprocessor"] = ["preprocessor", "#if", "#else", "#endif", "§PP", "§PPE", "conditional compilation"],
        ["synchronization"] = ["lock", "sync", "§SYNC", "monitor", "thread safety"],
        ["ranges"] = ["range", "slice", "..", "§RANGE", "§^", "index from end", "span", "array slice"],
        ["goto"] = ["goto", "goto case", "goto default", "§GOTO", "jump", "label", "fallthrough"],
        ["limitations"] = ["limitation", "unsupported", "not supported", "workaround", "migration", "known issues", "pragma", "#pragma"],
        ["overview"] = ["overview", "all", "summary", "syntax", "reference", "cheatsheet", "cheat sheet"],
    };

    internal static IReadOnlyCollection<string> TelemetryCategories => FeatureAliases.Keys;

    private Task<McpToolResult> HandleSyntaxHelp(string feature)
    {
        try
        {
            if (!feature.Equals("reference", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(HandleOverview(feature));
            return Task.FromResult(McpToolResult.Json(new SyntaxHelpOutput
            {
                Feature = feature,
                Sections = [new SyntaxSection { Title = "Complete Calor Reference", Content = ReferenceContent.Value }],
                AvailableFeatures = FeatureAliases.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to get syntax help: {ex.Message}"));
        }
    }

    private static McpToolResult HandleOverview(string feature)
    {
        var overview = new SyntaxSection
        {
            Title = "Calor Language Overview",
            Content = EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.overview.md")
        };
        if (CalorTelemetry.IsInitialized)
            CalorTelemetry.Instance.TrackSyntaxHelpQuery(feature, "overview", 1, overview.Title);
        return McpToolResult.Json(new SyntaxHelpOutput
        {
            Feature = feature,
            Sections = [overview],
            AvailableFeatures = FeatureAliases.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList()
        });
    }

    private static string? ResolveCategory(string feature)
    {
        foreach (var (key, aliases) in FeatureAliases)
        {
            if (key.Equals(feature, StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(a => a.Equals(feature, StringComparison.OrdinalIgnoreCase)))
            {
                return key;
            }
        }
        return null;
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // ── action: "features" ── FeatureSupportTool logic ───────────────
    // ═══════════════════════════════════════════════════════════════════

    private Task<McpToolResult> HandleFeatures(JsonElement? arguments)
    {
        var feature = GetString(arguments, "feature");
        var supportLevelStr = GetString(arguments, "supportLevel");

        try
        {
            // Mode 1: Query a specific feature
            if (!string.IsNullOrEmpty(feature))
            {
                var info = FeatureSupport.GetFeatureInfo(feature);
                if (info == null)
                {
                    return Task.FromResult(McpToolResult.Text(
                        $"Feature '{feature}' not found in registry. Use calor_help with action=features and no feature param for a summary of all features."));
                }

                var result = new FeatureQueryResult
                {
                    Feature = info.Name,
                    Support = info.Support.ToString(),
                    Description = info.Description,
                    Workaround = info.Workaround
                };

                return Task.FromResult(McpToolResult.Json(result));
            }

            // Mode 2: List features by support level
            if (!string.IsNullOrEmpty(supportLevelStr))
            {
                if (!Enum.TryParse<SupportLevel>(supportLevelStr, ignoreCase: true, out var level))
                {
                    return Task.FromResult(McpToolResult.Error(
                        $"Invalid support level '{supportLevelStr}'. Valid values: Full, Partial, NotSupported, ManualRequired"));
                }

                var features = FeatureSupport.GetFeaturesBySupport(level)
                    .Select(f => new FeatureListItem
                    {
                        Feature = f.Name,
                        Description = f.Description,
                        Workaround = f.Workaround
                    })
                    .OrderBy(f => f.Feature)
                    .ToList();

                var result = new FeatureListResult
                {
                    SupportLevel = level.ToString(),
                    Count = features.Count,
                    Features = features
                };

                return Task.FromResult(McpToolResult.Json(result));
            }

            // Mode 3: Summary (no arguments)
            var allFeatures = FeatureSupport.GetAllFeatures().ToList();
            var summary = new FeatureSummaryResult
            {
                TotalFeatures = allFeatures.Count,
                FullCount = allFeatures.Count(f => f.Support == SupportLevel.Full),
                PartialCount = allFeatures.Count(f => f.Support == SupportLevel.Partial),
                NotSupportedCount = allFeatures.Count(f => f.Support == SupportLevel.NotSupported),
                ManualRequiredCount = allFeatures.Count(f => f.Support == SupportLevel.ManualRequired),
                FullFeatures = allFeatures.Where(f => f.Support == SupportLevel.Full)
                    .Select(f => f.Name).OrderBy(n => n).ToList(),
                PartialFeatures = allFeatures.Where(f => f.Support == SupportLevel.Partial)
                    .Select(f => f.Name).OrderBy(n => n).ToList(),
                NotSupportedFeatures = allFeatures.Where(f => f.Support == SupportLevel.NotSupported)
                    .Select(f => f.Name).OrderBy(n => n).ToList(),
                ManualRequiredFeatures = allFeatures.Where(f => f.Support == SupportLevel.ManualRequired)
                    .Select(f => f.Name).OrderBy(n => n).ToList()
            };

            return Task.FromResult(McpToolResult.Json(summary));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to query feature support: {ex.Message}"));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ── action: "error" ── ExplainErrorTool logic ────────────────────
    // ═══════════════════════════════════════════════════════════════════

    private Task<McpToolResult> HandleError(JsonElement? arguments)
    {
        var error = GetString(arguments, "error");
        if (string.IsNullOrEmpty(error))
        {
            return Task.FromResult(McpToolResult.Error("Missing required parameter: error"));
        }

        try
        {
            var patterns = ErrorPatterns.Value;
            var matchList = FindMatchingErrorPatterns(patterns, error);
            var explanation = DiagnosticText.Explanation(error);

            if (explanation != null)
            {
                return Task.FromResult(McpToolResult.Json(new
                {
                    query = error,
                    explanation,
                    matches = matchList.Select(p => new ErrorExplanation
                    {
                        Id = p.Id,
                        Title = p.Title,
                        Description = p.Description,
                        WrongExample = p.WrongExample,
                        CorrectExample = p.CorrectExample,
                        Explanation = p.Explanation
                    }).ToList()
                }));
            }

            if (matchList.Count == 0)
            {
                return Task.FromResult(McpToolResult.Text(
                    $"No known common mistake pattern matches '{error}'. " +
                    "Check the Calor syntax help for the relevant feature using calor_help with action=syntax."));
            }

            var output = new ExplainErrorOutput
            {
                Query = error,
                Matches = matchList.Select(p => new ErrorExplanation
                {
                    Id = p.Id,
                    Title = p.Title,
                    Description = p.Description,
                    WrongExample = p.WrongExample,
                    CorrectExample = p.CorrectExample,
                    Explanation = p.Explanation
                }).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to explain error: {ex.Message}"));
        }
    }

    private static List<ErrorPattern> FindMatchingErrorPatterns(List<ErrorPattern> patterns, string error)
    {
        var normalizedError = error.ToLowerInvariant();
        var matchList = new List<ErrorPattern>();

        foreach (var pattern in patterns)
        {
            // Check error codes
            if (pattern.MatchCodes.Any(code =>
                error.Contains(code, StringComparison.OrdinalIgnoreCase)))
            {
                matchList.Add(pattern);
                continue;
            }

            // Check message patterns
            if (pattern.MatchPatterns.Any(mp =>
                normalizedError.Contains(mp.ToLowerInvariant())))
            {
                matchList.Add(pattern);
            }
        }

        return matchList;
    }

    // ═══════════════════════════════════════════════════════════════════
    // ── action: "example" ── GetExampleTool logic ────────────────────
    // ═══════════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, string> ScenarioDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["01_hello_world"] = "Basic hello world program with Console.WriteLine",
        ["02_fizzbuzz"] = "FizzBuzz implementation with loops and conditionals",
        ["03_contracts"] = "Function contracts with requires/ensures clauses",
        ["04_option_result"] = "Option and Result types for safe error handling",
        ["05_skill_syntax"] = "Skill (class) syntax with methods and properties",
        ["06_pattern_matching"] = "Pattern matching with match expressions",
        ["07_collections"] = "Collection types and operations (lists, arrays)",
        ["07_quantifiers"] = "Quantifier expressions (forall, exists) for contracts",
        ["08_contract_inheritance_z3"] = "Contract inheritance with Z3 verification",
        ["09_codegen_bugfixes"] = "Miscellaneous codegen edge cases and bug fixes",
    };

    private Task<McpToolResult> HandleExample(JsonElement? arguments)
    {
        try
        {
            var listMode = GetBool(arguments, "list");
            var nameFilter = GetString(arguments, "name");

            var scenarios = SelfTestRunner.LoadScenarios();

            if (listMode)
            {
                return Task.FromResult(McpToolResult.Json(new ExampleListOutput
                {
                    Examples = scenarios.Select(s => new ExampleSummary
                    {
                        Name = s.Name,
                        Description = GetScenarioDescription(s.Name)
                    }).ToList()
                }));
            }

            if (string.IsNullOrWhiteSpace(nameFilter))
            {
                return Task.FromResult(McpToolResult.Error(
                    "Provide 'name' to search for an example, or set 'list' to true to see all available examples."));
            }

            // Exact match first
            var match = scenarios.FirstOrDefault(s =>
                s.Name.Equals(nameFilter, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return Task.FromResult(McpToolResult.Json(new ExampleOutput
                {
                    Name = match.Name,
                    Description = GetScenarioDescription(match.Name),
                    CalorSource = match.Input,
                    ExpectedCSharp = match.ExpectedOutput
                }));
            }

            // Keyword search: match against name and description
            var keyword = nameFilter.ToLowerInvariant();
            var matchingScenarios = scenarios
                .Where(s => s.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || GetScenarioDescription(s.Name).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingScenarios.Count == 1)
            {
                var single = matchingScenarios[0];
                return Task.FromResult(McpToolResult.Json(new ExampleOutput
                {
                    Name = single.Name,
                    Description = GetScenarioDescription(single.Name),
                    CalorSource = single.Input,
                    ExpectedCSharp = single.ExpectedOutput
                }));
            }

            if (matchingScenarios.Count > 1)
            {
                return Task.FromResult(McpToolResult.Json(new ExampleListOutput
                {
                    Examples = matchingScenarios.Select(s => new ExampleSummary
                    {
                        Name = s.Name,
                        Description = GetScenarioDescription(s.Name)
                    }).ToList()
                }));
            }

            // No match — suggest closest names
            var available = scenarios.Select(s => new ExampleSummary
            {
                Name = s.Name,
                Description = GetScenarioDescription(s.Name)
            }).ToList();

            return Task.FromResult(McpToolResult.Json(new NoMatchOutput
            {
                Error = $"No example found matching '{nameFilter}'.",
                AvailableExamples = available
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to load examples: {ex.Message}"));
        }
    }

    private static string GetScenarioDescription(string scenarioName)
    {
        return ScenarioDescriptions.TryGetValue(scenarioName, out var desc)
            ? desc
            : FormatNameAsDescription(scenarioName);
    }

    private static string FormatNameAsDescription(string name)
    {
        // Strip leading number prefix like "01_" and convert underscores to spaces
        var trimmed = name;
        if (trimmed.Length > 3 && char.IsDigit(trimmed[0]) && char.IsDigit(trimmed[1]) && trimmed[2] == '_')
            trimmed = trimmed[3..];
        return trimmed.Replace('_', ' ');
    }

    // ═══════════════════════════════════════════════════════════════════
    // ── Resource Loading ─────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static SyntaxDocumentation LoadSyntaxDocumentation() =>
        JsonSerializer.Deserialize<SyntaxDocumentation>(
            EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.calor-syntax-documentation.json"), JsonOptions)
        ?? throw new InvalidDataException("Packaged syntax documentation is empty.");

    private static string LoadReferenceContent()
    {
        var path = Environment.GetEnvironmentVariable(ReferenceFilePathEnvVar);
        if (!string.IsNullOrEmpty(path))
            return File.ReadAllText(path);
        return EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.current-language-reference.md");
    }

    private static List<ErrorPattern> LoadErrorPatterns()
    {
        try
        {
            var json = EmbeddedResourceHelper.ReadResource(
                "Calor.Compiler.Resources.error-suggestions.json");
            var doc = JsonDocument.Parse(json);
            var patterns = new List<ErrorPattern>();

            foreach (var item in doc.RootElement.GetProperty("patterns").EnumerateArray())
            {
                patterns.Add(new ErrorPattern
                {
                    Id = item.GetProperty("id").GetString() ?? "",
                    Title = item.GetProperty("title").GetString() ?? "",
                    Description = item.GetProperty("description").GetString() ?? "",
                    WrongExample = item.GetProperty("wrongExample").GetString() ?? "",
                    CorrectExample = item.GetProperty("correctExample").GetString() ?? "",
                    Explanation = item.GetProperty("explanation").GetString() ?? "",
                    MatchPatterns = item.GetProperty("matchPatterns")
                        .EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .ToList(),
                    MatchCodes = item.GetProperty("matchCodes")
                        .EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .ToList()
                });
            }

            return patterns;
        }
        catch
        {
            return new List<ErrorPattern>();
        }
    }

    private static string? FindProjectRoot()
    {
        // Start from current directory and base directory
        var startDirs = new[]
        {
            Directory.GetCurrentDirectory(),
            AppDomain.CurrentDomain.BaseDirectory
        };

        foreach (var startDir in startDirs)
        {
            var dir = startDir;
            for (var i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "Calor.sln")) ||
                    Directory.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }

                var parent = Directory.GetParent(dir);
                dir = parent?.FullName;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════
    // ── JSON Models ──────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════════

    #region SyntaxLookup Models

    private sealed class SyntaxDocumentation
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("constructs")]
        public List<SyntaxConstruct> Constructs { get; set; } = new();

        [JsonPropertyName("tags")]
        public Dictionary<string, TagInfo> Tags { get; set; } = new();
    }

    private sealed class SyntaxConstruct
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("csharpConstruct")]
        public string CSharpConstruct { get; set; } = "";

        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();

        [JsonPropertyName("calorSyntax")]
        public string CalorSyntax { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("examples")]
        public List<SyntaxExample> Examples { get; set; } = new();

        [JsonPropertyName("programs")]
        public List<string> Programs { get; set; } = new();
    }

    private sealed class SyntaxExample
    {
        [JsonPropertyName("csharp")]
        public string CSharp { get; set; } = "";

        [JsonPropertyName("calor")]
        public string Calor { get; set; } = "";
    }

    private sealed class TagInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("syntax")]
        public string Syntax { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("csharpEquivalent")]
        public string CSharpEquivalent { get; set; } = "";
    }

    private sealed class LookupResult
    {
        [JsonPropertyName("found")]
        public bool Found { get; init; }

        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("examples")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SyntaxExampleOutput>? Examples { get; init; }

        [JsonPropertyName("construct")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Construct { get; init; }

        [JsonPropertyName("calorSyntax")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalorSyntax { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; init; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; init; }

        [JsonPropertyName("otherMatches")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? OtherMatches { get; init; }

        [JsonPropertyName("availableConstructs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? AvailableConstructs { get; init; }

        [JsonPropertyName("availableFeatures")]
        public required List<string> AvailableFeatures { get; init; }
    }

    private sealed class SyntaxExampleOutput
    {
        [JsonPropertyName("programId")]
        public required string ProgramId { get; init; }

        [JsonPropertyName("csharp")]
        public required string CSharp { get; init; }

        [JsonPropertyName("calor")]
        public required string Calor { get; init; }
    }

    #endregion

    #region SyntaxHelp Models

    private sealed class SyntaxHelpOutput
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("sections")]
        public required List<SyntaxSection> Sections { get; init; }

        [JsonPropertyName("availableFeatures")]
        public required List<string> AvailableFeatures { get; init; }
    }

    private sealed class SyntaxSection
    {
        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    #endregion

    #region FeatureSupport Models

    private sealed class FeatureQueryResult
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("support")]
        public required string Support { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; init; }

        [JsonPropertyName("workaround")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Workaround { get; init; }
    }

    private sealed class FeatureListResult
    {
        [JsonPropertyName("supportLevel")]
        public required string SupportLevel { get; init; }

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("features")]
        public required List<FeatureListItem> Features { get; init; }
    }

    private sealed class FeatureListItem
    {
        [JsonPropertyName("feature")]
        public required string Feature { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; init; }

        [JsonPropertyName("workaround")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Workaround { get; init; }
    }

    private sealed class FeatureSummaryResult
    {
        [JsonPropertyName("totalFeatures")]
        public int TotalFeatures { get; init; }

        [JsonPropertyName("fullCount")]
        public int FullCount { get; init; }

        [JsonPropertyName("partialCount")]
        public int PartialCount { get; init; }

        [JsonPropertyName("notSupportedCount")]
        public int NotSupportedCount { get; init; }

        [JsonPropertyName("manualRequiredCount")]
        public int ManualRequiredCount { get; init; }

        [JsonPropertyName("fullFeatures")]
        public required List<string> FullFeatures { get; init; }

        [JsonPropertyName("partialFeatures")]
        public required List<string> PartialFeatures { get; init; }

        [JsonPropertyName("notSupportedFeatures")]
        public required List<string> NotSupportedFeatures { get; init; }

        [JsonPropertyName("manualRequiredFeatures")]
        public required List<string> ManualRequiredFeatures { get; init; }
    }

    #endregion

    #region ExplainError Models

    private sealed class ErrorPattern
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string WrongExample { get; init; }
        public required string CorrectExample { get; init; }
        public required string Explanation { get; init; }
        public required List<string> MatchPatterns { get; init; }
        public required List<string> MatchCodes { get; init; }
    }

    private sealed class ExplainErrorOutput
    {
        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("matches")]
        public required List<ErrorExplanation> Matches { get; init; }
    }

    private sealed class ErrorExplanation
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("wrongExample")]
        public required string WrongExample { get; init; }

        [JsonPropertyName("correctExample")]
        public required string CorrectExample { get; init; }

        [JsonPropertyName("explanation")]
        public required string Explanation { get; init; }
    }

    #endregion

    #region GetExample Models

    private sealed class ExampleListOutput
    {
        [JsonPropertyName("examples")]
        public required List<ExampleSummary> Examples { get; init; }
    }

    private sealed class ExampleSummary
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }
    }

    private sealed class ExampleOutput
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("calorSource")]
        public required string CalorSource { get; init; }

        [JsonPropertyName("expectedCSharp")]
        public required string ExpectedCSharp { get; init; }
    }

    private sealed class NoMatchOutput
    {
        [JsonPropertyName("error")]
        public required string Error { get; init; }

        [JsonPropertyName("availableExamples")]
        public required List<ExampleSummary> AvailableExamples { get; init; }
    }

    #endregion
}
