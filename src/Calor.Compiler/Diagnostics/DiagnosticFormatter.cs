using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Diagnostics;

/// <summary>Authored text summaries; unknown message shapes remain lossless, not truncated.</summary>
internal static class DiagnosticText
{
    internal static string OneLine(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            result.Append(character switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ when char.IsControl(character) || character is '\u2028' or '\u2029'
                    => $"\\u{(int)character:x4}",
                _ => character.ToString()
            });
        }
        return result.ToString();
    }

    internal static string ShortMessage(Diagnostic diagnostic)
    {
        var message = diagnostic.Message;
        if (Rules.TryGetValue(diagnostic.Code, out var rule))
        {
            message = System.Text.RegularExpressions.Regex.Replace(
                message, rule.Pattern, rule.Summary,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant
                    | System.Text.RegularExpressions.RegexOptions.NonBacktracking,
                TimeSpan.FromSeconds(1));
        }
        return OneLine(message);
    }

    internal static string? Explanation(string code) =>
        Rules.TryGetValue(code, out var rule) ? rule.Explanation : null;

    private sealed record Rule(string Pattern, string Summary, string Explanation);

    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        [DiagnosticCode.LegacyCloserForm] = new(
            "^Legacy structural closing tag '([^']+)' was removed in Phase 4d and is no longer accepted\\. A block now ends at the dedent of its body, so delete the closer line -- the indentation already marks where the block ends\\.$",
            "Remove legacy closer '$1'; blocks end at dedent",
            "Structural closing tags are not accepted. Blocks end when their bodies dedent. Remove the closer, not its surrounding code or comments. The diagnostic carries a safe deletion patch; call and block-lambda closers remain valid."),
        [DiagnosticCode.MalformedFunctionHeader] = new(
            "^'([^']+)' header has three fields \\(([^)]+)\\), but §F/§AF headers take at most \\{name:visibility\\}\\. The second field '([^']+)' is a return type -- declare it in the signature as '\\(\\.\\.\\.\\) -> [^']+', not in the header\\. A return type belongs in the inline signature, not the header\\.$",
            "'$1' header $2: move return type '$3' to the signature",
            "Function headers have at most {name:visibility}. The return type belongs in the inline signature, for example §F{Add:pub} () -> i32. A structured patch moves the return type out of a malformed three-field header."),
        [DiagnosticCode.TabIndentation] = new(
            "^Leading whitespace uses tabs on ([0-9]+) line\\(s\\)\\. Calor indentation is canonically 2 spaces per level\\.$",
            "Tab indentation on $1 line(s); use 2 spaces per level",
            "Canonical indentation is 2 spaces per nesting level. A tab counts as one column in the parser. Consistently tab-only indentation carries a safe expansion patch. Mixing tabs with space-indented lines can change block structure, so expansion remains a suggestion in that case."),
        [DiagnosticCode.NonStandardIndentWidth] = new(
            "^Indentation step is not 2 spaces on ([0-9]+) line\\(s\\)\\. Calor indentation is canonically 2 spaces per level\\.$",
            "Noncanonical indentation on $1 line(s); use 2 spaces per level",
            "The parser uses relative indentation levels, but canonical source uses 2 spaces per level. Space-only source with consistent nesting carries a safe patch that retains those nesting levels. Mixed or mismatched indentation requires an explicit decision."),
        [DiagnosticCode.EffectRowUnknown] = new(
            "^(.+) has effect row (.+) and (.+) declares row (.+), so it cannot be decided whether (?:the row fits|the callee's effects fit)\\. State a row on both sides, or compile with --permissive-effects\\.$",
            "$1 row $2 vs $3 row $4: compatibility unknown",
            "An effect row states what a function value may do. An unknown row cannot be checked against another row; an assumed row is not a proof. State rows on both sides or resolve the unavailable declaration. --permissive-effects explicitly waives unknown-row checking, not known effect violations; --strict-effects promotes the warning to an error. Choosing an effect policy is not a deterministic fix."),
        [DiagnosticCode.UnknownExternalCall] = new(
            "^Unknown (?:external call to|call target) '([^']+)'(?:\\. Add effect declaration in a \\.calor-effects\\.json manifest\\.| - assuming worst-case effects\\. Consider adding to manifest\\.|: not an internal function, parameter, binding, or field visible to the effect pass\\. If this is a delegate-typed member, declare it where this module can see it with an effect row \\(§E\\{…\\} on the same line as its type\\); otherwise add the callee to a \\.calor-effects\\.json manifest\\.)$",
            "Unknown effects for call '$1'; declare its effect contract",
            "The effect pass cannot resolve this call's effects. It charges worst-case effects rather than assuming purity. Supply a .calor-effects.json manifest for an external member, or make a function-valued declaration and its effect row visible to the module. Selecting effects is a policy decision, not a safe automatic fix."),
        [DiagnosticCode.UndeclaredPublicFunction] = new(
            "^Public function '([^']+)' in module '([^']+)' has no effect declaration\\. Cross-module callers cannot verify effect safety\\. Add §E\\{\\.\\.\\.\\} to declare effects\\.$",
            "Public function '$2.$1' has no effect declaration",
            "Public functions need an explicit §E effect declaration so cross-module callers can verify their effects. §E{} declares purity; nonempty declarations must include the effects the function can perform. The compiler does not choose this API contract automatically."),
        [DiagnosticCode.AmbiguousCallContinuation] = new(
            "^Closer-less call '([^']+)' already took one inline argument; a second positional argument is ambiguous\\. Use the explicit form '([^']+)' instead\\.$",
            "Ambiguous second argument to '$1'; use '$2'",
            "A closer-less call accepts at most one inline argument. Multiple arguments require explicit §A markers and §/C. The intended argument boundaries are ambiguous, so no automatic patch is applied.")
    };
}

/// <summary>
/// Formats diagnostics for output.
/// </summary>
public interface IDiagnosticFormatter
{
    string Format(IEnumerable<Diagnostic> diagnostics);
    string Format(DiagnosticBag diagnostics);
    string ContentType { get; }
}

/// <summary>
/// Formats diagnostics as plain text (default).
/// </summary>
public sealed class TextDiagnosticFormatter : IDiagnosticFormatter
{
    public string ContentType => "text/plain";

    public string Format(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));
    }

    public string Format(DiagnosticBag diagnostics)
    {
        // Text format just uses the diagnostic messages
        return Format((IEnumerable<Diagnostic>)diagnostics);
    }
}

/// <summary>
/// Formats diagnostics as JSON for machine processing.
/// </summary>
public sealed class JsonDiagnosticFormatter : IDiagnosticFormatter
{
    /// <summary>
    /// Envelope schema version (loop plan D1.1). 1.1 added per-diagnostic
    /// <c>declarationId</c> (nearest enclosing declaration ID, null when IDs are
    /// absent) and <c>verification</c> (choke-point proof outcome with the closed
    /// status vocabulary and the structured counterexample model on refuted).
    /// 2.0 (guarantees plan D-G2.4): the verification status vocabulary grows to
    /// proven|refuted|assumed|unknown|timeout|unsupported|unavailable, and the
    /// payload gains <c>vacuous</c> (true on vacuous proofs — never elidable) and
    /// <c>assumptions</c> (the named set an assumed proof is conditional on).
    /// The plan drafted this as "v1.2", but the schema doc's own versioning rule
    /// makes the status vocabulary CLOSED — adding a status is a major bump, so
    /// this is 2.0 with a CHANGELOG migration note. Everything else is additive.
    /// </summary>
    public const string SchemaVersion = "2.0";

    public string ContentType => "application/json";

    /// <summary>
    /// Optional span→declaration-ID resolver; when set, each diagnostic entry
    /// carries the ID of its nearest enclosing declaration.
    /// </summary>
    public Ids.DeclarationIdResolver? DeclarationIds { get; set; }

    private static readonly JsonSerializerOptions s_indentedOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions s_compactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly JsonSerializerOptions _options;

    /// <summary>Indented single-document formatter (default; compile/lint output).</summary>
    public JsonDiagnosticFormatter() : this(writeIndented: true)
    {
    }

    /// <summary>
    /// With <paramref name="writeIndented"/> false the document is compact — no
    /// embedded newlines — as required by NDJSON stream producers that emit one
    /// document per line (<c>calor watch --format json</c>).
    /// </summary>
    public JsonDiagnosticFormatter(bool writeIndented)
    {
        _options = writeIndented ? s_indentedOptions : s_compactOptions;
    }

    public string Format(IEnumerable<Diagnostic> diagnostics)
    {
        var output = new DiagnosticOutput
        {
            Version = SchemaVersion,
            Diagnostics = diagnostics.Select(d => DiagnosticEnvelope.Build(d, DeclarationIds)).ToList(),
            Summary = DiagnosticEnvelope.Summarize(diagnostics)
        };

        return JsonSerializer.Serialize(output, _options);
    }

    public string Format(DiagnosticBag diagnostics)
    {
        var output = new DiagnosticOutput
        {
            Version = SchemaVersion,
            Diagnostics = DiagnosticEnvelope.Build(diagnostics, DeclarationIds),
            Summary = DiagnosticEnvelope.Summarize(diagnostics)
        };

        return JsonSerializer.Serialize(output, _options);
    }

    private sealed class DiagnosticOutput
    {
        public required string Version { get; init; }
        public required List<EnvelopeDiagnostic> Diagnostics { get; init; }
        public required EnvelopeSummary Summary { get; init; }
    }
}

/// <summary>
/// Formats diagnostics as SARIF (Static Analysis Results Interchange Format).
/// This is the standard format for static analysis tools.
/// </summary>
public sealed class SarifDiagnosticFormatter : IDiagnosticFormatter
{
    public string ContentType => "application/sarif+json";

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _toolName;
    private readonly Func<string, string>? _ruleDescriptionProvider;
    private readonly Func<string, string>? _ruleHelpUriProvider;

    /// <summary>
    /// Optional span→declaration-ID resolver; when set, each result's property
    /// bag carries the ID of its nearest enclosing declaration.
    /// </summary>
    public Ids.DeclarationIdResolver? DeclarationIds { get; set; }

    /// <summary>
    /// Builds the SARIF property bag mirroring the JSON envelope's declarationId
    /// and verification fields; null when neither applies.
    /// </summary>
    private Dictionary<string, object>? BuildProperties(Diagnostic d)
    {
        var declarationId = DeclarationIds?.Resolve(d);
        if (declarationId == null && d.Verification == null)
            return null;

        var properties = new Dictionary<string, object>();
        if (declarationId != null)
            properties["declarationId"] = declarationId;

        if (d.Verification != null)
        {
            var verification = new Dictionary<string, object> { ["status"] = d.Verification.StatusName };
            if (d.Verification.Reason != null)
                verification["reason"] = d.Verification.Reason;
            if (d.Verification.IsVacuous)
                verification["vacuous"] = true;
            if (d.Verification.Assumptions.Count > 0)
                verification["assumptions"] = d.Verification.Assumptions.ToList();
            if (d.Verification.Counterexample != null)
            {
                verification["counterexample"] = new Dictionary<string, object>
                {
                    ["rendered"] = d.Verification.Counterexample.Render(),
                    ["bindings"] = d.Verification.Counterexample.Bindings
                        .Select(b => new Dictionary<string, string> { ["name"] = b.Name, ["value"] = b.Value })
                        .ToList()
                };
            }
            properties["verification"] = verification;
        }

        return properties;
    }

    /// <summary>
    /// Creates the default SARIF formatter for compiler diagnostics
    /// (tool name "calor", rule metadata from <see cref="DiagnosticCode"/>).
    /// </summary>
    public SarifDiagnosticFormatter() : this("calor")
    {
    }

    /// <summary>
    /// Creates a SARIF formatter for a specific tool surface. Commands that emit
    /// non-compiler rule IDs (e.g. <c>calor assess</c>) supply their own tool name
    /// and rule metadata providers instead of maintaining a duplicate SARIF object model.
    /// </summary>
    /// <param name="toolName">SARIF tool.driver.name (e.g. "calor-assess").</param>
    /// <param name="ruleDescriptionProvider">Maps a rule ID to its short description; null uses compiler defaults.</param>
    /// <param name="ruleHelpUriProvider">Maps a rule ID to its help URI; null uses compiler defaults.</param>
    public SarifDiagnosticFormatter(
        string toolName,
        Func<string, string>? ruleDescriptionProvider = null,
        Func<string, string>? ruleHelpUriProvider = null)
    {
        _toolName = toolName;
        _ruleDescriptionProvider = ruleDescriptionProvider;
        _ruleHelpUriProvider = ruleHelpUriProvider;
    }

    public string Format(IEnumerable<Diagnostic> diagnostics)
    {
        var sarif = new SarifLog
        {
            Schema = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            Version = "2.1.0",
            Runs = new List<SarifRun>
            {
                new SarifRun
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifToolDriver
                        {
                            Name = _toolName,
                            Version = "1.0.0",
                            InformationUri = "https://github.com/calor-lang/calor",
                            Rules = GetRules(diagnostics)
                        }
                    },
                    Results = diagnostics.Select(d => new SarifResult
                    {
                        RuleId = d.Code,
                        Level = d.Severity switch
                        {
                            DiagnosticSeverity.Error => "error",
                            DiagnosticSeverity.Warning => "warning",
                            _ => "note"
                        },
                        Message = new SarifMessage { Text = d.Message },
                        Locations = new List<SarifLocation>
                        {
                            new SarifLocation
                            {
                                PhysicalLocation = new SarifPhysicalLocation
                                {
                                    ArtifactLocation = new SarifArtifactLocation
                                    {
                                        Uri = d.FilePath != null ? new Uri(d.FilePath, UriKind.RelativeOrAbsolute).ToString() : null
                                    },
                                    Region = new SarifRegion
                                    {
                                        StartLine = d.Span.Line,
                                        StartColumn = d.Span.Column,
                                        EndColumn = d.Span.Column + d.Span.Length
                                    }
                                }
                            }
                        },
                        Fixes = null, // Fix suggestions not yet implemented
                        Properties = BuildProperties(d)
                    }).ToList()
                }
            }
        };

        return JsonSerializer.Serialize(sarif, s_options);
    }

    public string Format(DiagnosticBag diagnostics)
    {
        // Build lookup from DiagnosticsWithFixes to populate fix info
        // Include message in key to differentiate between different constructs at same location
        var fixLookup = diagnostics.DiagnosticsWithFixes
            .GroupBy(dwf => (dwf.FilePath, dwf.Span.Line, dwf.Span.Column, dwf.Code, dwf.Message))
            .ToDictionary(g => g.Key, g => g.First());

        var results = new List<SarifResult>();
        foreach (var d in diagnostics)
        {
            List<SarifFix>? fixes = null;

            // Check if this diagnostic has an associated fix
            var key = (d.FilePath, d.Span.Line, d.Span.Column, d.Code, d.Message);
            if (fixLookup.TryGetValue(key, out var diagnosticWithFix))
            {
                fixes = new List<SarifFix>
                {
                    new SarifFix
                    {
                        Description = new SarifMessage { Text = diagnosticWithFix.Fix.Description },
                        ArtifactChanges = diagnosticWithFix.Fix.Edits.Select(e => new SarifArtifactChange
                        {
                            ArtifactLocation = new SarifArtifactLocation
                            {
                                Uri = e.FilePath
                            },
                            Replacements = new List<SarifReplacement>
                            {
                                new SarifReplacement
                                {
                                    DeletedRegion = new SarifRegion
                                    {
                                        StartLine = e.StartLine,
                                        StartColumn = e.StartColumn,
                                        EndLine = e.EndLine,
                                        EndColumn = e.EndColumn
                                    },
                                    InsertedContent = new SarifContent { Text = e.NewText }
                                }
                            }
                        }).ToList()
                    }
                };
            }

            results.Add(new SarifResult
            {
                RuleId = d.Code,
                Level = d.Severity switch
                {
                    DiagnosticSeverity.Error => "error",
                    DiagnosticSeverity.Warning => "warning",
                    _ => "note"
                },
                Message = new SarifMessage { Text = d.Message },
                Locations = new List<SarifLocation>
                {
                    new SarifLocation
                    {
                        PhysicalLocation = new SarifPhysicalLocation
                        {
                            ArtifactLocation = new SarifArtifactLocation
                            {
                                Uri = d.FilePath != null ? new Uri(d.FilePath, UriKind.RelativeOrAbsolute).ToString() : null
                            },
                            Region = new SarifRegion
                            {
                                StartLine = d.Span.Line,
                                StartColumn = d.Span.Column,
                                EndColumn = d.Span.Column + d.Span.Length
                            }
                        }
                    }
                },
                Fixes = fixes,
                Properties = BuildProperties(d)
            });
        }

        var sarif = new SarifLog
        {
            Schema = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            Version = "2.1.0",
            Runs = new List<SarifRun>
            {
                new SarifRun
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifToolDriver
                        {
                            Name = _toolName,
                            Version = "1.0.0",
                            InformationUri = "https://github.com/calor-lang/calor",
                            Rules = GetRules(diagnostics)
                        }
                    },
                    Results = results
                }
            }
        };

        return JsonSerializer.Serialize(sarif, s_options);
    }

    private List<SarifRule> GetRules(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics
            .Select(d => d.Code)
            .Distinct()
            .Select(code => new SarifRule
            {
                Id = code,
                ShortDescription = new SarifMessage
                {
                    Text = _ruleDescriptionProvider?.Invoke(code) ?? GetRuleDescription(code)
                },
                HelpUri = _ruleHelpUriProvider?.Invoke(code)
                    ?? $"https://calor-lang.org/docs/diagnostics/{code}"
            })
            .ToList();
    }

    private static string GetRuleDescription(string code) => code switch
    {
        DiagnosticCode.UnexpectedCharacter => "Unexpected character in source",
        DiagnosticCode.UnknownSectionMarker => "Unknown section marker",
        DiagnosticCode.InvalidSectionOperator => "Invalid section operator",
        DiagnosticCode.UnterminatedString => "Unterminated string literal",
        DiagnosticCode.UnexpectedToken => "Unexpected token",
        DiagnosticCode.TypeMismatch => "Type mismatch",
        DiagnosticCode.UndefinedReference => "Undefined reference",
        DiagnosticCode.DuplicateFunctionSignature => "Duplicate function signature",
        DiagnosticCode.BindReassignsImmutable => "Cannot rebind immutable symbol",
        DiagnosticCode.WriteToReadOnlyLocal => "Write to an immutable binding or loop variable",
        DiagnosticCode.UnknownVisibility => "Unknown visibility word",
        DiagnosticCode.TypeFirstBindingHeader => "Ambiguous type-first binding header",
        DiagnosticCode.AmbiguousOverload => "Ambiguous overload",
        DiagnosticCode.NoMatchingOverload => "No matching overload",
        DiagnosticCode.InheritanceCycle => "Inheritance cycle",
        DiagnosticCode.NonExhaustiveMatch => "Non-exhaustive match expression",
        DiagnosticCode.UnreachablePattern => "Unreachable pattern",
        DiagnosticCode.DuplicatePattern => "Duplicate pattern",
        DiagnosticCode.MissingDocComment => "Missing documentation comment",
        DiagnosticCode.BreakingChangeWithoutMarker => "Breaking change without marker",
        DiagnosticCode.CodeGenSyntaxError => "Generated C# code contains syntax errors",
        DiagnosticCode.CodeGenCompilationError => "Generated C# failed Roslyn compilation",
        DiagnosticCode.UnterminatedCSharpInteropBlock => "Unterminated C# interop block",
        DiagnosticCode.CSharpInteropBlockPreserved => "C# code preserved in interop block",
        DiagnosticCode.LintTrailingWhitespace => "Line has trailing whitespace",
        DiagnosticCode.LintNonAbbreviatedId => "Reserved legacy ID-format lint diagnostic",
        DiagnosticCode.LintFileNotFound => "Lint input file not found",
        DiagnosticCode.LintUnsupportedFileType => "Lint input file is not a .calr file",
        DiagnosticCode.LintProcessingError => "Unexpected error while linting a file",
        DiagnosticCode.FormatConservativeFallback => "Source left unchanged by safe formatting fallback",
        DiagnosticCode.CliInputNotFound => "Input file not found",
        DiagnosticCode.CliUsageError => "Invalid command-line argument combination",
        DiagnosticCode.CliInternalError => "Unhandled compiler error",
        _ => "Calor compiler diagnostic"
    };

    // SARIF data structures
    private sealed class SarifLog
    {
        [JsonPropertyName("$schema")]
        public required string Schema { get; init; }
        public required string Version { get; init; }
        public required List<SarifRun> Runs { get; init; }
    }

    private sealed class SarifRun
    {
        public required SarifTool Tool { get; init; }
        public required List<SarifResult> Results { get; init; }
    }

    private sealed class SarifTool
    {
        public required SarifToolDriver Driver { get; init; }
    }

    private sealed class SarifToolDriver
    {
        public required string Name { get; init; }
        public required string Version { get; init; }
        public string? InformationUri { get; init; }
        public required List<SarifRule> Rules { get; init; }
    }

    private sealed class SarifRule
    {
        public required string Id { get; init; }
        public required SarifMessage ShortDescription { get; init; }
        public string? HelpUri { get; init; }
    }

    private sealed class SarifResult
    {
        public required string RuleId { get; init; }
        public required string Level { get; init; }
        public required SarifMessage Message { get; init; }
        public required List<SarifLocation> Locations { get; init; }
        public List<SarifFix>? Fixes { get; init; }

        /// <summary>SARIF property bag: envelope schema v1 declarationId + verification payload.</summary>
        public Dictionary<string, object>? Properties { get; init; }
    }

    private sealed class SarifMessage
    {
        public required string Text { get; init; }
    }

    private sealed class SarifLocation
    {
        public required SarifPhysicalLocation PhysicalLocation { get; init; }
    }

    private sealed class SarifPhysicalLocation
    {
        public required SarifArtifactLocation ArtifactLocation { get; init; }
        public required SarifRegion Region { get; init; }
    }

    private sealed class SarifArtifactLocation
    {
        public string? Uri { get; init; }
    }

    private sealed class SarifRegion
    {
        public int StartLine { get; init; }
        public int StartColumn { get; init; }
        public int? EndLine { get; init; }
        public int? EndColumn { get; init; }
    }

    private sealed class SarifFix
    {
        public required SarifMessage Description { get; init; }
        public required List<SarifArtifactChange> ArtifactChanges { get; init; }
    }

    private sealed class SarifArtifactChange
    {
        public required SarifArtifactLocation ArtifactLocation { get; init; }
        public required List<SarifReplacement> Replacements { get; init; }
    }

    private sealed class SarifReplacement
    {
        public required SarifRegion DeletedRegion { get; init; }
        public required SarifContent InsertedContent { get; init; }
    }

    private sealed class SarifContent
    {
        public required string Text { get; init; }
    }
}

/// <summary>
/// Factory for creating diagnostic formatters.
/// </summary>
public static class DiagnosticFormatterFactory
{
    public static IDiagnosticFormatter Create(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => new JsonDiagnosticFormatter(),
            "sarif" => new SarifDiagnosticFormatter(),
            "text" or _ => new TextDiagnosticFormatter()
        };
    }

    /// <summary>
    /// Creates a formatter with a span→declaration-ID resolver attached, so JSON
    /// and SARIF output carry each diagnostic's enclosing declaration ID.
    /// </summary>
    public static IDiagnosticFormatter Create(string format, Ids.DeclarationIdResolver? declarationIds)
    {
        var formatter = Create(format);
        switch (formatter)
        {
            case JsonDiagnosticFormatter json:
                json.DeclarationIds = declarationIds;
                break;
            case SarifDiagnosticFormatter sarif:
                sarif.DeclarationIds = declarationIds;
                break;
        }
        return formatter;
    }
}
