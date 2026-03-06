using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Init;
using Calor.Compiler.Migration;
using Calor.Compiler.SelfTest;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Mcp.Tools;

/// <summary>
/// Consolidated MCP tool for Calor syntax, feature support, error explanations, and working examples.
/// Merges: SyntaxLookupTool, SyntaxHelpTool, FeatureSupportTool, ExplainErrorTool, GetExampleTool.
/// </summary>
public sealed class HelpTool : McpToolBase
{
    // ── Lazy-loaded resources ────────────────────────────────────────
    private static readonly Lazy<SyntaxDocumentation?> SyntaxDoc = new(LoadSyntaxDocumentation);
    private static Lazy<string> SkillContent = new(LoadSkillContent);
    private static readonly Lazy<List<ErrorPattern>> ErrorPatterns = new(LoadErrorPatterns);

    /// <summary>
    /// Environment variable to override the skill file path.
    /// </summary>
    public const string SkillFilePathEnvVar = "CALOR_SKILL_FILE";

    /// <summary>
    /// Resets the cached skill content. Used for testing environment variable overrides.
    /// </summary>
    internal static void ResetCacheForTesting()
    {
        SkillContent = new Lazy<string>(LoadSkillContent);
    }

    public override string Name => "calor_help";

    public override string Description =>
        "Get help with Calor syntax, feature support, error explanations, or working examples";

    public override McpToolAnnotations? Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string GetInputSchemaJson() => """
        {
            "type": "object",
            "properties": {
                "action": {
                    "type": "string",
                    "enum": ["syntax", "features", "error", "example"],
                    "description": "Help topic. syntax=C# to Calor syntax mapping (fuzzy match), features=feature support registry, error=explain error code/message, example=load working Calor examples",
                    "default": "syntax"
                },
                "query": {
                    "type": "string",
                    "description": "C# construct or feature topic to look up (e.g., 'object instantiation', 'for loop', 'async', 'overview'). Used with action=syntax."
                },
                "feature": {
                    "type": "string",
                    "description": "Query a specific feature by name (e.g., 'named-argument', 'with-expression'). Used with action=features."
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

        // If query provided, use SyntaxLookupTool fuzzy matching
        if (!string.IsNullOrWhiteSpace(query))
        {
            return HandleSyntaxLookup(query);
        }

        // Fall back to feature parameter for SyntaxHelpTool topic-based help
        var feature = GetString(arguments, "feature");
        if (!string.IsNullOrEmpty(feature))
        {
            return HandleSyntaxHelp(feature);
        }

        return Task.FromResult(McpToolResult.Error("Missing required parameter: query"));
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
            var availableConstructs = doc.Constructs
                .Select(c => c.CSharpConstruct)
                .OrderBy(c => c)
                .ToList();

            return Task.FromResult(McpToolResult.Json(new LookupResult
            {
                Found = false,
                Query = query,
                Message = $"No matches found for '{query}'",
                AvailableConstructs = availableConstructs
            }));
        }

        var bestMatch = matches[0];
        var result = new LookupResult
        {
            Found = true,
            Query = query,
            Construct = bestMatch.CSharpConstruct,
            CalorSyntax = bestMatch.CalorSyntax,
            Description = bestMatch.Description,
            Examples = bestMatch.Examples.Select(e => new SyntaxExampleOutput
            {
                CSharp = e.CSharp,
                Calor = e.Calor
            }).ToList(),
            OtherMatches = matches.Skip(1).Take(3).Select(m => m.CSharpConstruct).ToList()
        };

        return Task.FromResult(McpToolResult.Json(result));
    }

    private static List<SyntaxConstruct> FindConstructMatches(SyntaxDocumentation doc, string query)
    {
        var queryLower = query.ToLowerInvariant();
        var queryTerms = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

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
            .Select(s => s.Construct)
            .ToList();
    }

    private static int CalculateMatchScore(SyntaxConstruct construct, string queryLower, string[] queryTerms)
    {
        var score = 0;

        // Exact match on construct name (highest priority)
        if (construct.CSharpConstruct.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        // Construct name contains query
        if (construct.CSharpConstruct.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }

        // Query contains construct name
        if (queryLower.Contains(construct.CSharpConstruct.ToLowerInvariant()))
        {
            score += 400;
        }

        // Keyword matches
        foreach (var keyword in construct.Keywords)
        {
            var keywordLower = keyword.ToLowerInvariant();

            // Exact keyword match
            if (queryTerms.Contains(keywordLower))
            {
                score += 100;
            }
            // Query contains keyword
            else if (queryLower.Contains(keywordLower))
            {
                score += 50;
            }
            // Keyword contains query term
            else if (queryTerms.Any(t => keywordLower.Contains(t)))
            {
                score += 25;
            }
        }

        // Description matches (lower priority)
        foreach (var term in queryTerms)
        {
            if (construct.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }

        // Calor syntax tag match
        if (queryLower.StartsWith("§") && construct.CalorSyntax.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        return score;
    }

    #endregion

    #region SyntaxHelp (topic-based documentation)

    private static readonly Dictionary<string, string[]> FeatureAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["async"] = ["async", "await", "§AF", "§AMT", "§AWAIT", "§ASYNC"],
        ["contracts"] = ["contract", "§Q", "§S", "precondition", "postcondition", "requires", "ensures"],
        ["effects"] = ["effect", "§E", "side effect", "cw", "cr", "fs:"],
        ["loops"] = ["loop", "for", "for loop", "foreach", "while", "§L{", "§WH{", "§DO{", "§BK", "§CN"],
        ["conditionals"] = ["if", "if statement", "if-else", "else", "conditional", "§IF{", "§EI", "§EL", "ternary"],
        ["functions"] = ["function", "§F{", "§I{", "§O{", "§R", "return", "parameter"],
        ["classes"] = ["class", "§CL{", "§EXT{", "§IMPL{", "inheritance", "interface"],
        ["generics"] = ["generic", "<T>", "§WHERE", "type parameter", "constraint"],
        ["collections"] = ["list", "dict", "array", "§LIST{", "§DICT{", "§ARR", "§IDX"],
        ["patterns"] = ["pattern", "match", "switch", "§W{", "§K", "§SW{", "is pattern", "combinator", "relational pattern", "positional", "property pattern"],
        ["exceptions"] = ["try", "catch", "throw", "exception", "§TR{", "§CA", "§TH"],
        ["lambdas"] = ["lambda", "delegate", "§LAM{", "§DEL{", "nested delegate"],
        ["strings"] = ["string", "str", "concat", "substr", "interpolation"],
        ["types"] = ["type", "i32", "i64", "f32", "f64", "bool", "void"],
        ["records"] = ["record", "§D{", "union", "§T{", "§V{"],
        ["enums"] = ["enum", "§EN{", "§ENUM{"],
        ["constructors"] = ["constructor", "§CTOR{", "§BASE", "§THIS"],
        ["properties"] = ["property", "§PROP{", "§GET", "§SET", "field", "§FLD{"],
        ["structs"] = ["struct", "§ST{", "value type"],
        ["operators"] = ["operator", "§OP{", "overload", "arithmetic"],
        ["nullable"] = ["nullable", "null", "§?", "§??", "§?.", "null check", "null coalescing", "null conditional", "null-conditional", "null-coalescing", "coalesce"],
        ["linq"] = ["linq", "query", "select", "where", "orderby"],
        ["events"] = ["event", "§EV{", "§EVT{", "§EADD", "§EREM", "add accessor", "remove accessor", "event handler"],
        ["using"] = ["using", "§USE{", "dispose", "IDisposable"],
        ["modifiers"] = ["static", "abstract", "sealed", "virtual", "override", "readonly", "partial"],
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

    private Task<McpToolResult> HandleSyntaxHelp(string feature)
    {
        try
        {
            var content = SkillContent.Value;
            if (string.IsNullOrEmpty(content))
            {
                return Task.FromResult(McpToolResult.Error("Syntax documentation not available"));
            }

            var resolvedCategory = ResolveCategory(feature);

            // Handle overview request — return curated language reference
            if (resolvedCategory == "overview")
            {
                return Task.FromResult(HandleOverview(feature, content));
            }

            var sections = ExtractRelevantSections(content, feature);

            // Track telemetry
            if (CalorTelemetry.IsInitialized)
            {
                var matchedSectionNames = sections.Count > 0
                    ? string.Join(";", sections.Select(s => s.Title).Take(5))
                    : null;
                CalorTelemetry.Instance.TrackSyntaxHelpQuery(
                    feature, resolvedCategory, sections.Count, matchedSectionNames);
            }

            if (sections.Count == 0)
            {
                var availableFeatures = string.Join(", ", FeatureAliases.Keys.OrderBy(k => k));
                return Task.FromResult(McpToolResult.Text(
                    $"No documentation found for '{feature}'. Available features: {availableFeatures}"));
            }

            // Truncate if total content exceeds budget to prevent token overflow
            const int maxContentChars = 15_000;
            var totalChars = sections.Sum(s => s.Content.Length);
            List<string>? omittedTitles = null;

            if (totalChars > maxContentChars)
            {
                var kept = new List<SyntaxSection>();
                var budget = 0;
                omittedTitles = new List<string>();

                foreach (var section in sections)
                {
                    if (budget + section.Content.Length <= maxContentChars || kept.Count == 0)
                    {
                        kept.Add(section);
                        budget += section.Content.Length;
                    }
                    else
                    {
                        omittedTitles.Add(section.Title);
                    }
                }

                // Add a tip section listing omitted titles
                if (omittedTitles.Count > 0)
                {
                    kept.Add(new SyntaxSection
                    {
                        Title = "Truncated — additional sections available",
                        Content = $"Response truncated ({totalChars} chars). " +
                                  $"Query these sections individually for full content:\n" +
                                  string.Join("\n", omittedTitles.Select(t => $"- {t}"))
                    });
                }

                sections = kept;
            }

            var output = new SyntaxHelpOutput
            {
                Feature = feature,
                Sections = sections,
                AvailableFeatures = FeatureAliases.Keys.OrderBy(k => k).ToList()
            };

            return Task.FromResult(McpToolResult.Json(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResult.Error($"Failed to get syntax help: {ex.Message}"));
        }
    }

    private static McpToolResult HandleOverview(string feature, string content)
    {
        // Extract the "Supported C# Features — Quick Reference" table and
        // "Syntax Quick Reference" section from the skills doc for a curated overview
        var overviewSections = new List<SyntaxSection>();

        // 1. Extract the quick reference table
        var headerPattern = new Regex(@"^## (.+)$", RegexOptions.Multiline);
        var matches = headerPattern.Matches(content);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var title = match.Groups[1].Value.Trim();
            var startIndex = match.Index;
            var endIndex = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
            var sectionContent = content[startIndex..endIndex].Trim();

            if (title.Contains("Quick Reference", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Core Syntax", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("Properties", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("Indexers", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Structure Tags", StringComparison.OrdinalIgnoreCase))
            {
                overviewSections.Add(new SyntaxSection
                {
                    Title = title,
                    Content = sectionContent
                });
            }
        }

        // 2. Add a compact syntax cheatsheet as the first section
        overviewSections.Insert(0, new SyntaxSection
        {
            Title = "Calor Language Overview",
            Content = """
                ## Calor Language Overview

                Calor is a DSL that compiles to C# on .NET 10. All constructs use section markers (§).

                ### Core Tags
                ```
                §M{id:Name}              Module (close: §/M{id})
                §F{id:name:vis}           Function (close: §/F{id})
                §B{name:type}             Immutable binding
                §B{~name:type}            Mutable binding
                §I{type:name}             Parameter
                §O{type}                  Return type
                §R expr                   Return statement
                §C{obj.Method} §A arg §/C Method call with argument
                ```

                ### Control Flow
                ```
                §IF{id} (cond) ... §EI (cond2) ... §EL ... §/I{id}   If/ElseIf/Else
                §L{id:var:from:to:step} ... §/L{id}                  For loop
                §L{id:item:collection} ... §/L{id}                   Foreach loop
                §WH{id} (cond) ... §/WH{id}                          While loop
                §W{expr} §K{pattern} result §/W                      Match/Switch
                §GOTO{label}                                          Goto label
                §GOTO{CASE:expr}                                      Goto case expr
                §GOTO{DEFAULT}                                        Goto default
                ```

                ### Types and Classes
                ```
                §CL{id:Name:vis}          Class (close: §/CL{id})
                §IFACE{id:Name}           Interface (close: §/IFACE{id})
                §MT{id:name:vis}          Method (close: §/MT{id})
                §CTOR{id:vis}             Constructor (close: §/CTOR{id})
                §PROP{id:Name:type:vis}   Property (close: §/PROP{id})
                §IXER{id:type:vis}        Indexer (close: §/IXER{id})
                §FLD{type:name:vis}       Field
                §EXT{BaseClass}           Extends
                §IMPL{Interface}          Implements
                §EN{id:Name}              Enum (close: §/EN{id})
                ```

                ### Properties and Indexers (compact forms)
                ```
                §PROP{id:Name:type:vis:get,set}                      Auto-property
                §IXER{id:type:vis:get,set} (type:param)              Auto-indexer
                §IXER{id:type:vis:get,set} (type:p1, type:p2)        Multi-param indexer
                ```

                ### Contracts and Effects
                ```
                §Q (expr)                 Precondition (requires)
                §S (expr)                 Postcondition (ensures)
                §INV (expr)               Invariant
                §E{effect1,effect2}       Effect declarations
                ```

                ### Async, Exceptions, and Resources
                ```
                §AF{id:name:vis}          Async function
                §AMT{id:name:vis}         Async method
                §AWAIT expr §/AWAIT       Await expression
                §TR{id} ... §CA{Type:var} ... §FI{} ... §/TR{id}   Try/Catch/Finally
                §TH expr                  Throw
                §USE{var}=expr ... §/USE  Using statement
                §YIELD expr               Yield return
                §YBRK                     Yield break
                ```

                ### Lambdas, Delegates, Events
                ```
                §LAM{id:param:type} body §/LAM{id}   Lambda expression
                §DEL{id:Name:vis}                     Delegate type (also inside §CL)
                §EVT{id:Name:vis:type}                Event
                §EADD body §/EADD                     Event add accessor
                §EREM body §/EREM                     Event remove accessor
                §OP{id:operator:vis}                  Operator overload
                ```

                ### Tuples and Null Operators
                ```
                (Type1, Type2)            Tuple type
                (expr1, expr2)            Tuple literal
                (?? left right)           Null-coalescing (left ?? right)
                (?. target Member)        Null-conditional (target?.Member)
                ```

                ### Collections
                ```
                §LIST{id:type}            List<T>
                §DICT{id:ktype:vtype}     Dictionary<K,V>
                §HSET{id:type}            HashSet<T>
                §IDX coll index           Element access
                §PUSH coll item           Add to list/set
                §PUT dict key value       Add/update dict entry
                §RANGE start end          Range: start..end (for slicing)
                §^ n                      Index from end: ^n
                ```

                ### Preprocessor and Interop
                ```
                §PP{CONDITION} ... §PPE ... §/PP{CONDITION}   #if/#else/#endif
                §CSHARP{code}§/CSHARP                         Raw C# passthrough (member-level)
                §RAW ... §/RAW                                Raw C# block
                §UNSAFE{id} ... §/UNSAFE{id}                  Unsafe block
                ```

                ### Typed Literals
                ```
                INT:42    STR:"hello"    BOOL:true    FLOAT:3.14    DECIMAL:18.00
                ```

                ### Visibility: pub, priv, prot, int, protint
                ### Modifiers: stat, abs, virt, over, seal, req, partial

                **Key rule**: If a C# construct has a Calor tag, use it natively. Only use §CSHARP for constructs without native support.

                Query a specific feature for detailed syntax and examples (e.g., 'async', 'indexers', 'contracts').
                """
        });

        // Track telemetry
        if (CalorTelemetry.IsInitialized)
        {
            var matchedNames = string.Join(";", overviewSections.Select(s => s.Title).Take(5));
            CalorTelemetry.Instance.TrackSyntaxHelpQuery(
                feature, "overview", overviewSections.Count, matchedNames);
        }

        var output = new SyntaxHelpOutput
        {
            Feature = feature,
            Sections = overviewSections,
            AvailableFeatures = FeatureAliases.Keys.OrderBy(k => k).ToList()
        };

        return McpToolResult.Json(output);
    }

    private static List<SyntaxSection> ExtractRelevantSections(string content, string feature)
    {
        var sections = new List<SyntaxSection>();
        var searchTerms = GetSearchTerms(feature);

        // Split content by ## headers
        var headerPattern = new Regex(@"^## (.+)$", RegexOptions.Multiline);
        var matches = headerPattern.Matches(content);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var title = match.Groups[1].Value.Trim();
            var startIndex = match.Index;
            var endIndex = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
            var sectionContent = content[startIndex..endIndex].Trim();

            // Check if this section is relevant
            if (IsSectionRelevant(title, sectionContent, searchTerms))
            {
                sections.Add(new SyntaxSection
                {
                    Title = title,
                    Content = sectionContent
                });
            }
        }

        // If no sections found by header, try to find template sections
        if (sections.Count == 0)
        {
            var templatePattern = new Regex(@"### Template: (.+?)\n(```calor\n[\s\S]+?```)", RegexOptions.Multiline);
            var templateMatches = templatePattern.Matches(content);

            foreach (Match match in templateMatches)
            {
                var title = match.Groups[1].Value.Trim();
                var templateContent = match.Groups[2].Value.Trim();

                if (searchTerms.Any(term =>
                    title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    IsContentMatch(templateContent, term)))
                {
                    sections.Add(new SyntaxSection
                    {
                        Title = $"Template: {title}",
                        Content = templateContent
                    });
                }
            }
        }

        return sections;
    }

    private static string[] GetSearchTerms(string feature)
    {
        // Check for direct alias match
        foreach (var (key, aliases) in FeatureAliases)
        {
            if (key.Equals(feature, StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(a => a.Equals(feature, StringComparison.OrdinalIgnoreCase)))
            {
                return aliases;
            }
        }

        // Fall back to the feature itself as search term
        return [feature];
    }

    private static bool IsSectionRelevant(string title, string content, string[] searchTerms)
    {
        return searchTerms.Any(term =>
            title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            IsContentMatch(content, term));
    }

    /// <summary>
    /// Matches a search term against section content. Short plain-text terms (≤ 3 chars)
    /// use word-boundary matching to prevent false positives like "for" in "information"
    /// or "if" in "specific". Calor tokens and longer terms use substring matching.
    /// </summary>
    private static bool IsContentMatch(string text, string term)
    {
        // Calor-specific tokens (§, {, <, [) are already very specific — use substring matching
        if (term.Contains('§') || term.Contains('{') || term.Contains('<') || term.Contains('['))
        {
            return text.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        // Short terms (≤ 3 chars) use word-boundary matching to avoid false positives
        if (term.Length <= 3)
        {
            return Regex.IsMatch(text, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase);
        }

        // Longer terms are specific enough for substring matching
        return text.Contains(term, StringComparison.OrdinalIgnoreCase);
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

    private static SyntaxDocumentation? LoadSyntaxDocumentation()
    {
        try
        {
            // Try embedded resource first
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Calor.Compiler.Resources.calor-syntax-documentation.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<SyntaxDocumentation>(json, JsonOptions);
            }

            // Fall back to file system (for development)
            var projectRoot = FindProjectRoot();
            if (projectRoot != null)
            {
                var filePath = Path.Combine(projectRoot, "src", "Calor.Compiler", "Resources", "calor-syntax-documentation.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<SyntaxDocumentation>(json, JsonOptions);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string LoadSkillContent()
    {
        // 1. Check environment variable first (highest priority)
        var envPath = Environment.GetEnvironmentVariable(SkillFilePathEnvVar);
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            try
            {
                return File.ReadAllText(envPath);
            }
            catch
            {
                // Fall through to other methods
            }
        }

        // 2. Look for project root by walking up directories
        var projectRoot = FindProjectRoot();
        if (projectRoot != null)
        {
            var skillPath = Path.Combine(projectRoot, "tests", "Calor.Evaluation", "Skills", "calor-language-skills.md");
            if (File.Exists(skillPath))
            {
                try
                {
                    return File.ReadAllText(skillPath);
                }
                catch
                {
                    // Fall through
                }
            }
        }

        // 3. Try NuGet tool installation path
        var nugetSkillPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Skills", "calor-language-skills.md");
        if (File.Exists(nugetSkillPath))
        {
            try
            {
                return File.ReadAllText(nugetSkillPath);
            }
            catch
            {
                // Fall through
            }
        }

        // 4. Fall back to embedded resource (always available in NuGet tool installations)
        try
        {
            return EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.calor-language-skills.md");
        }
        catch
        {
            // Resource not found
        }

        return "";
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

        [JsonPropertyName("construct")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Construct { get; init; }

        [JsonPropertyName("calorSyntax")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalorSyntax { get; init; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; init; }

        [JsonPropertyName("examples")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SyntaxExampleOutput>? Examples { get; init; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; init; }

        [JsonPropertyName("otherMatches")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? OtherMatches { get; init; }

        [JsonPropertyName("availableConstructs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? AvailableConstructs { get; init; }
    }

    private sealed class SyntaxExampleOutput
    {
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
