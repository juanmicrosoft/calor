using System.Text.Json;
using System.Text.Json.Serialization;
using Opal.Compiler.Parsing;

namespace Opal.Compiler.Diagnostics;

/// <summary>
/// Formats diagnostics for output.
/// </summary>
public interface IDiagnosticFormatter
{
    string Format(IEnumerable<Diagnostic> diagnostics);
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
}

/// <summary>
/// Formats diagnostics as JSON for machine processing.
/// </summary>
public sealed class JsonDiagnosticFormatter : IDiagnosticFormatter
{
    public string ContentType => "application/json";

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format(IEnumerable<Diagnostic> diagnostics)
    {
        var output = new DiagnosticOutput
        {
            Version = "1.0",
            Diagnostics = diagnostics.Select(d => new DiagnosticEntry
            {
                Code = d.Code,
                Message = d.Message,
                Severity = d.Severity.ToString().ToLower(),
                Location = new LocationInfo
                {
                    File = d.FilePath,
                    Line = d.Span.Line,
                    Column = d.Span.Column,
                    Length = d.Span.Length
                },
                Fix = null // Fix suggestions not yet implemented in base Diagnostic
            }).ToList(),
            Summary = new SummaryInfo
            {
                Total = diagnostics.Count(),
                Errors = diagnostics.Count(d => d.IsError),
                Warnings = diagnostics.Count(d => d.IsWarning),
                Info = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Info)
            }
        };

        return JsonSerializer.Serialize(output, s_options);
    }

    private sealed class DiagnosticOutput
    {
        public required string Version { get; init; }
        public required List<DiagnosticEntry> Diagnostics { get; init; }
        public required SummaryInfo Summary { get; init; }
    }

    private sealed class DiagnosticEntry
    {
        public required string Code { get; init; }
        public required string Message { get; init; }
        public required string Severity { get; init; }
        public required LocationInfo Location { get; init; }
        public FixInfo? Fix { get; init; }
    }

    private sealed class LocationInfo
    {
        public string? File { get; init; }
        public int Line { get; init; }
        public int Column { get; init; }
        public int Length { get; init; }
    }

    private sealed class FixInfo
    {
        public required string Description { get; init; }
        public required List<EditInfo> Edits { get; init; }
    }

    private sealed class EditInfo
    {
        public required string FilePath { get; init; }
        public int StartLine { get; init; }
        public int StartColumn { get; init; }
        public int EndLine { get; init; }
        public int EndColumn { get; init; }
        public required string NewText { get; init; }
    }

    private sealed class SummaryInfo
    {
        public int Total { get; init; }
        public int Errors { get; init; }
        public int Warnings { get; init; }
        public int Info { get; init; }
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
                            Name = "opalc",
                            Version = "1.0.0",
                            InformationUri = "https://github.com/opal-lang/opal",
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
                        Fixes = null // Fix suggestions not yet implemented
                    }).ToList()
                }
            }
        };

        return JsonSerializer.Serialize(sarif, s_options);
    }

    private static List<SarifRule> GetRules(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics
            .Select(d => d.Code)
            .Distinct()
            .Select(code => new SarifRule
            {
                Id = code,
                ShortDescription = new SarifMessage { Text = GetRuleDescription(code) },
                HelpUri = $"https://opal-lang.org/docs/diagnostics/{code}"
            })
            .ToList();
    }

    private static string GetRuleDescription(string code) => code switch
    {
        DiagnosticCode.UnexpectedCharacter => "Unexpected character in source",
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
        _ => "OPAL compiler diagnostic"
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
}
