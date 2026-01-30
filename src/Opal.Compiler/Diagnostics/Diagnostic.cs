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
    public const string InvalidOperator = "OPAL0106";

    // Semantic errors (OPAL0200-0299)
    public const string UndefinedReference = "OPAL0200";
    public const string DuplicateDefinition = "OPAL0201";
    public const string TypeMismatch = "OPAL0202";
    public const string InvalidReference = "OPAL0203";

    // Contract errors (OPAL0300-0399)
    public const string InvalidPrecondition = "OPAL0300";
    public const string InvalidPostcondition = "OPAL0301";
    public const string ContractViolation = "OPAL0302";

    // Effect errors (OPAL0400-0499)
    public const string UndeclaredEffect = "OPAL0400";
    public const string UnusedEffectDeclaration = "OPAL0401";
    public const string EffectMismatch = "OPAL0402";

    // Pattern matching errors (OPAL0500-0599)
    public const string NonExhaustiveMatch = "OPAL0500";
    public const string UnreachablePattern = "OPAL0501";
    public const string DuplicatePattern = "OPAL0502";
    public const string InvalidPatternForType = "OPAL0503";

    // API strictness errors (OPAL0600-0699)
    public const string BreakingChangeWithoutMarker = "OPAL0600";
    public const string MissingDocComment = "OPAL0601";
    public const string PublicApiChanged = "OPAL0602";
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

/// <summary>
/// A diagnostic with an associated suggested fix.
/// </summary>
public sealed class DiagnosticWithFix
{
    public string Code { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public DiagnosticSeverity Severity { get; }
    public string? FilePath { get; }
    public SuggestedFix Fix { get; }

    public DiagnosticWithFix(
        string code,
        string message,
        TextSpan span,
        SuggestedFix fix,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string? filePath = null)
    {
        Code = code;
        Message = message;
        Span = span;
        Severity = severity;
        FilePath = filePath;
        Fix = fix ?? throw new ArgumentNullException(nameof(fix));
    }

    public bool IsError => Severity == DiagnosticSeverity.Error;
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;
}

/// <summary>
/// A suggested fix for a diagnostic.
/// </summary>
public sealed class SuggestedFix
{
    /// <summary>
    /// Description of what the fix does.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The edits to apply to fix the issue.
    /// </summary>
    public IReadOnlyList<TextEdit> Edits { get; }

    public SuggestedFix(string description, IReadOnlyList<TextEdit> edits)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Edits = edits ?? throw new ArgumentNullException(nameof(edits));
    }

    public SuggestedFix(string description, TextEdit edit)
        : this(description, new[] { edit })
    {
    }
}

/// <summary>
/// A text edit to apply as part of a fix.
/// </summary>
public sealed class TextEdit
{
    public string FilePath { get; }
    public int StartLine { get; }
    public int StartColumn { get; }
    public int EndLine { get; }
    public int EndColumn { get; }
    public string NewText { get; }

    public TextEdit(string filePath, int startLine, int startColumn, int endLine, int endColumn, string newText)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        NewText = newText ?? throw new ArgumentNullException(nameof(newText));
    }

    /// <summary>
    /// Create an insertion edit.
    /// </summary>
    public static TextEdit Insert(string filePath, int line, int column, string text)
        => new(filePath, line, column, line, column, text);

    /// <summary>
    /// Create a replacement edit.
    /// </summary>
    public static TextEdit Replace(string filePath, int startLine, int startColumn, int endLine, int endColumn, string text)
        => new(filePath, startLine, startColumn, endLine, endColumn, text);
}
