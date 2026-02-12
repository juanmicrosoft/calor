using Calor.Compiler.Diagnostics;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using CalorDiagnostic = Calor.Compiler.Diagnostics.Diagnostic;
using CalorSeverity = Calor.Compiler.Diagnostics.DiagnosticSeverity;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace Calor.LanguageServer.Utilities;

/// <summary>
/// Converts Calor compiler diagnostics to LSP diagnostics.
/// </summary>
public static class DiagnosticConverter
{
    /// <summary>
    /// Convert a Calor diagnostic to an LSP diagnostic.
    /// </summary>
    public static LspDiagnostic ToLspDiagnostic(CalorDiagnostic diagnostic, string source)
    {
        return new LspDiagnostic
        {
            Range = PositionConverter.ToLspRange(diagnostic.Span, source),
            Severity = ToLspSeverity(diagnostic.Severity),
            Code = diagnostic.Code,
            Source = "calor",
            Message = diagnostic.Message
        };
    }

    /// <summary>
    /// Convert a Calor diagnostic to an LSP diagnostic using single-line range.
    /// </summary>
    public static LspDiagnostic ToLspDiagnosticSingleLine(CalorDiagnostic diagnostic)
    {
        return new LspDiagnostic
        {
            Range = PositionConverter.ToLspRangeSingleLine(diagnostic.Span),
            Severity = ToLspSeverity(diagnostic.Severity),
            Code = diagnostic.Code,
            Source = "calor",
            Message = diagnostic.Message
        };
    }

    /// <summary>
    /// Convert a DiagnosticWithFix to an LSP diagnostic.
    /// The fix information is stored in the diagnostic's Data field for later use.
    /// </summary>
    public static LspDiagnostic ToLspDiagnostic(DiagnosticWithFix diagnostic, string source)
    {
        return new LspDiagnostic
        {
            Range = PositionConverter.ToLspRange(diagnostic.Span, source),
            Severity = ToLspSeverity(diagnostic.Severity),
            Code = diagnostic.Code,
            Source = "calor",
            Message = diagnostic.Message,
            // Store fix description in data for code action handler
            Data = diagnostic.Fix.Description
        };
    }

    /// <summary>
    /// Convert Calor severity to LSP severity.
    /// </summary>
    public static LspSeverity ToLspSeverity(CalorSeverity severity)
    {
        return severity switch
        {
            CalorSeverity.Error => LspSeverity.Error,
            CalorSeverity.Warning => LspSeverity.Warning,
            CalorSeverity.Info => LspSeverity.Information,
            _ => LspSeverity.Information
        };
    }

    /// <summary>
    /// Convert a collection of Calor diagnostics to LSP diagnostics.
    /// </summary>
    public static IEnumerable<LspDiagnostic> ToLspDiagnostics(
        IEnumerable<CalorDiagnostic> diagnostics,
        string source)
    {
        return diagnostics.Select(d => ToLspDiagnostic(d, source));
    }

    /// <summary>
    /// Convert a DiagnosticBag to LSP diagnostics.
    /// </summary>
    public static IEnumerable<LspDiagnostic> ToLspDiagnostics(
        DiagnosticBag bag,
        string source)
    {
        return bag.Select(d => ToLspDiagnostic(d, source));
    }
}
