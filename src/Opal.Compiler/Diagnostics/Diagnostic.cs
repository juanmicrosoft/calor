using Opal.Compiler.Parsing;

namespace Opal.Compiler.Diagnostics;

/// <summary>
/// Diagnostic severity levels.
/// </summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// Standard diagnostic codes for the OPAL compiler.
/// </summary>
public static class DiagnosticCode
{
    // Lexer errors (OPAL0001-0099)
    public const string UnexpectedCharacter = "OPAL0001";
    public const string UnterminatedString = "OPAL0002";
    public const string InvalidTypedLiteral = "OPAL0003";
    public const string InvalidEscapeSequence = "OPAL0004";

    // Parser errors (OPAL0100-0199)
    public const string UnexpectedToken = "OPAL0100";
    public const string MismatchedId = "OPAL0101";
    public const string MissingRequiredAttribute = "OPAL0102";
    public const string ExpectedKeyword = "OPAL0103";
    public const string ExpectedExpression = "OPAL0104";
    public const string ExpectedClosingTag = "OPAL0105";

    // Semantic errors (OPAL0200-0299)
    public const string UndefinedReference = "OPAL0200";
    public const string DuplicateDefinition = "OPAL0201";
    public const string TypeMismatch = "OPAL0202";
}

/// <summary>
/// Represents a compiler diagnostic (error, warning, or info).
/// </summary>
public sealed class Diagnostic
{
    public string Code { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticSeverity Severity { get; }
    public string? FilePath { get; }

    public Diagnostic(
        string code,
        string message,
        TextSpan span,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string? filePath = null)
    {
        Code = code;
        Message = message;
        Span = span;
        Severity = severity;
        FilePath = filePath;
    }

    public bool IsError => Severity == DiagnosticSeverity.Error;
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;

    public override string ToString()
    {
        var location = FilePath != null
            ? $"{FilePath}({Span.Line},{Span.Column})"
            : $"({Span.Line},{Span.Column})";

        var severityText = Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            _ => "unknown"
        };

        return $"{location}: {severityText} {Code}: {Message}";
    }
}
