using Calor.Compiler.Ids;

namespace Calor.Compiler.Diagnostics;

/// <summary>
/// The one diagnostic entry shape every Calor surface emits (envelope schema v1,
/// loop plan D1.1/D1.3). CLI commands serialize these via
/// <see cref="JsonDiagnosticFormatter"/>; MCP tools embed them directly in their
/// result DTOs. Serialized camelCase with null fields omitted on every surface.
/// </summary>
public sealed class EnvelopeDiagnostic
{
    public required string Code { get; init; }
    public required string Message { get; init; }

    /// <summary>Lowercase severity: error | warning | info.</summary>
    public required string Severity { get; init; }

    public required EnvelopeLocation Location { get; init; }

    /// <summary>
    /// ID of the nearest enclosing declaration (the language's addressing
    /// primitive, e.g. <c>f001</c>); null when IDs are absent or unresolvable.
    /// </summary>
    public string? DeclarationId { get; init; }

    /// <summary>Verification payload for contract diagnostics; null otherwise.</summary>
    public EnvelopeVerification? Verification { get; init; }

    public string? Suggestion { get; set; }
    public EnvelopeFix? Fix { get; set; }
}

public sealed class EnvelopeLocation
{
    public string? File { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int Length { get; init; }
}

/// <summary>
/// Verification payload: the choke-point proof outcome. Status is the closed
/// vocabulary proven | refuted | unknown | timeout | unsupported; refuted
/// carries the concrete solver model when one was produced.
/// </summary>
public sealed class EnvelopeVerification
{
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public EnvelopeCounterexample? Counterexample { get; init; }
}

public sealed class EnvelopeCounterexample
{
    public required string Rendered { get; init; }
    public required List<EnvelopeBinding> Bindings { get; init; }
}

public sealed class EnvelopeBinding
{
    public required string Name { get; init; }
    public required string Value { get; init; }
}

public sealed class EnvelopeFix
{
    public required string Description { get; init; }
    public required List<EnvelopeEdit> Edits { get; init; }
}

public sealed class EnvelopeEdit
{
    public required string FilePath { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public required string NewText { get; init; }
}

public sealed class EnvelopeSummary
{
    public int Total { get; init; }
    public int Errors { get; init; }
    public int Warnings { get; init; }
    public int Info { get; init; }
}

/// <summary>Builders shared by every envelope-emitting surface.</summary>
public static class DiagnosticEnvelope
{
    /// <summary>Builds the envelope entry for one diagnostic.</summary>
    public static EnvelopeDiagnostic Build(
        Diagnostic d,
        DeclarationIdResolver? declarationIds = null,
        SuggestedFix? fix = null)
    {
        var entry = new EnvelopeDiagnostic
        {
            Code = d.Code,
            Message = d.Message,
            Severity = d.Severity.ToString().ToLowerInvariant(),
            Location = new EnvelopeLocation
            {
                File = d.FilePath,
                Line = d.Span.Line,
                Column = d.Span.Column,
                Length = d.Span.Length
            },
            DeclarationId = declarationIds?.Resolve(d),
            Verification = BuildVerification(d.Verification)
        };

        if (fix != null)
        {
            entry.Suggestion = fix.Description;
            entry.Fix = new EnvelopeFix
            {
                Description = fix.Description,
                Edits = fix.Edits.Select(e => new EnvelopeEdit
                {
                    FilePath = e.FilePath,
                    StartLine = e.StartLine,
                    StartColumn = e.StartColumn,
                    EndLine = e.EndLine,
                    EndColumn = e.EndColumn,
                    NewText = e.NewText
                }).ToList()
            };
        }

        return entry;
    }

    /// <summary>
    /// Builds envelope entries for a whole bag, joining fixes the same way the
    /// JSON formatter always has (line, column, code, message).
    /// </summary>
    public static List<EnvelopeDiagnostic> Build(
        DiagnosticBag bag,
        DeclarationIdResolver? declarationIds = null)
    {
        var fixLookup = bag.DiagnosticsWithFixes
            .GroupBy(dwf => (dwf.Span.Line, dwf.Span.Column, dwf.Code, dwf.Message))
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<EnvelopeDiagnostic>();
        foreach (var d in bag)
        {
            fixLookup.TryGetValue((d.Span.Line, d.Span.Column, d.Code, d.Message), out var withFix);
            entries.Add(Build(d, declarationIds, withFix?.Fix));
        }
        return entries;
    }

    public static EnvelopeVerification? BuildVerification(Verification.ProofOutcome? outcome)
    {
        if (outcome == null)
            return null;

        return new EnvelopeVerification
        {
            Status = outcome.StatusName,
            Reason = outcome.Reason,
            Counterexample = outcome.Counterexample == null
                ? null
                : new EnvelopeCounterexample
                {
                    Rendered = outcome.Counterexample.Render(),
                    Bindings = outcome.Counterexample.Bindings
                        .Select(b => new EnvelopeBinding { Name = b.Name, Value = b.Value })
                        .ToList()
                }
        };
    }

    public static EnvelopeSummary Summarize(IEnumerable<Diagnostic> diagnostics)
    {
        var list = diagnostics as ICollection<Diagnostic> ?? diagnostics.ToList();
        return new EnvelopeSummary
        {
            Total = list.Count,
            Errors = list.Count(d => d.IsError),
            Warnings = list.Count(d => d.IsWarning),
            Info = list.Count(d => d.Severity == DiagnosticSeverity.Info)
        };
    }
}
