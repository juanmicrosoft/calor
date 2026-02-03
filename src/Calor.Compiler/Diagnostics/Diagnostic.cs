using Calor.Compiler.Parsing;

namespace Calor.Compiler.Diagnostics;

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
/// Standard diagnostic codes for the Calor compiler.
/// </summary>
public static class DiagnosticCode
{
    // Lexer errors (Calor0001-0099)
    public const string UnexpectedCharacter = "Calor0001";
    public const string UnterminatedString = "Calor0002";
    public const string InvalidTypedLiteral = "Calor0003";
    public const string InvalidEscapeSequence = "Calor0004";

    // Parser errors (Calor0100-0199)
    public const string UnexpectedToken = "Calor0100";
    public const string MismatchedId = "Calor0101";
    public const string MissingRequiredAttribute = "Calor0102";
    public const string ExpectedKeyword = "Calor0103";
    public const string ExpectedExpression = "Calor0104";
    public const string ExpectedClosingTag = "Calor0105";
    public const string InvalidOperator = "Calor0106";

    // Semantic errors (Calor0200-0299)
    public const string UndefinedReference = "Calor0200";
    public const string DuplicateDefinition = "Calor0201";
    public const string TypeMismatch = "Calor0202";
    public const string InvalidReference = "Calor0203";

    // Contract errors (Calor0300-0399)
    public const string InvalidPrecondition = "Calor0300";
    public const string InvalidPostcondition = "Calor0301";
    public const string ContractViolation = "Calor0302";

    // Effect errors (Calor0400-0499)
    public const string UndeclaredEffect = "Calor0400";
    public const string UnusedEffectDeclaration = "Calor0401";
    public const string EffectMismatch = "Calor0402";

    // Pattern matching errors (Calor0500-0599)
    public const string NonExhaustiveMatch = "Calor0500";
    public const string UnreachablePattern = "Calor0501";
    public const string DuplicatePattern = "Calor0502";
    public const string InvalidPatternForType = "Calor0503";

    // API strictness errors (Calor0600-0699)
    public const string BreakingChangeWithoutMarker = "Calor0600";
    public const string MissingDocComment = "Calor0601";
    public const string PublicApiChanged = "Calor0602";
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
