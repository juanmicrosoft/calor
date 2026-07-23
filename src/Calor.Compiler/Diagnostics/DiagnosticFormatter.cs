using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Diagnostics;

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
    /// Envelope schema version (loop plan D1.1). 1.1 adds per-diagnostic
    /// <c>declarationId</c> (nearest enclosing declaration ID, null when IDs are
    /// absent) and <c>verification</c> (choke-point proof outcome with the closed
    /// proven|refuted|unknown|timeout|unsupported status vocabulary and the
    /// structured counterexample model on refuted).
    /// </summary>
    public const string SchemaVersion = "1.1";

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
            .GroupBy(dwf => (dwf.Span.Line, dwf.Span.Column, dwf.Code, dwf.Message))
            .ToDictionary(g => g.Key, g => g.First());

        var results = new List<SarifResult>();
        foreach (var d in diagnostics)
        {
            List<SarifFix>? fixes = null;

            // Check if this diagnostic has an associated fix
            var key = (d.Span.Line, d.Span.Column, d.Code, d.Message);
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
        DiagnosticCode.MismatchedId => "Mismatched construct IDs",
        DiagnosticCode.TypeMismatch => "Type mismatch",
        DiagnosticCode.UndefinedReference => "Undefined reference",
        DiagnosticCode.NonExhaustiveMatch => "Non-exhaustive match expression",
        DiagnosticCode.UnreachablePattern => "Unreachable pattern",
        DiagnosticCode.DuplicatePattern => "Duplicate pattern",
        DiagnosticCode.MissingDocComment => "Missing documentation comment",
        DiagnosticCode.BreakingChangeWithoutMarker => "Breaking change without marker",
        DiagnosticCode.CodeGenSyntaxError => "Generated C# code contains syntax errors",
        DiagnosticCode.UnterminatedCSharpInteropBlock => "Unterminated C# interop block",
        DiagnosticCode.CSharpInteropBlockPreserved => "C# code preserved in interop block",
        DiagnosticCode.LintTrailingWhitespace => "Line has trailing whitespace",
        DiagnosticCode.LintNonAbbreviatedId => "Construct ID is not in abbreviated form",
        DiagnosticCode.LintFileNotFound => "Lint input file not found",
        DiagnosticCode.LintUnsupportedFileType => "Lint input file is not a .calr file",
        DiagnosticCode.LintProcessingError => "Unexpected error while linting a file",
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
