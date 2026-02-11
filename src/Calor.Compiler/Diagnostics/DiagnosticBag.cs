using System.Collections;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Diagnostics;

/// <summary>
/// A collection of diagnostics accumulated during compilation.
/// </summary>
public sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];
    private string? _currentFilePath;

    public int Count => _diagnostics.Count;
    public bool HasErrors => _diagnostics.Any(d => d.IsError);

    public IReadOnlyList<Diagnostic> Errors
        => _diagnostics.Where(d => d.IsError).ToList();

    public IReadOnlyList<Diagnostic> Warnings
        => _diagnostics.Where(d => d.IsWarning).ToList();

    public void SetFilePath(string? filePath)
    {
        _currentFilePath = filePath;
    }

    public void Report(TextSpan span, string code, string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        _diagnostics.Add(new Diagnostic(code, message, span, severity, _currentFilePath));
    }

    public void ReportError(TextSpan span, string code, string message)
        => Report(span, code, message, DiagnosticSeverity.Error);

    public void ReportWarning(TextSpan span, string code, string message)
        => Report(span, code, message, DiagnosticSeverity.Warning);

    public void ReportInfo(TextSpan span, string code, string message)
        => Report(span, code, message, DiagnosticSeverity.Info);

    // Lexer diagnostics
    public void ReportUnexpectedCharacter(TextSpan span, char character)
        => ReportError(span, DiagnosticCode.UnexpectedCharacter,
            $"Unexpected character '{character}'");

    public void ReportUnterminatedString(TextSpan span)
        => ReportError(span, DiagnosticCode.UnterminatedString,
            "Unterminated string literal");

    public void ReportInvalidTypedLiteral(TextSpan span, string type)
        => ReportError(span, DiagnosticCode.InvalidTypedLiteral,
            $"Invalid {type} literal");

    public void ReportInvalidEscapeSequence(TextSpan span, char character)
        => ReportError(span, DiagnosticCode.InvalidEscapeSequence,
            $"Invalid escape sequence '\\{character}'");

    // Parser diagnostics
    public void ReportUnexpectedToken(TextSpan span, TokenKind expected, TokenKind actual)
        => ReportError(span, DiagnosticCode.UnexpectedToken,
            $"Expected {expected} but found {actual}");

    public void ReportUnexpectedToken(TextSpan span, string expected, TokenKind actual)
        => ReportError(span, DiagnosticCode.UnexpectedToken,
            $"Expected {expected} but found {actual}");

    public void ReportMismatchedId(TextSpan span, string openTag, string openId, string closeTag, string closeId)
        => ReportError(span, DiagnosticCode.MismatchedId,
            $"{closeTag} id '{closeId}' does not match {openTag} id '{openId}'");

    public void ReportMissingRequiredAttribute(TextSpan span, string tagName, string attributeName)
        => ReportError(span, DiagnosticCode.MissingRequiredAttribute,
            $"Missing required attribute '{attributeName}' on {tagName}");

    public void ReportExpectedKeyword(TextSpan span, string keyword)
        => ReportError(span, DiagnosticCode.ExpectedKeyword,
            $"Expected keyword '{keyword}'");

    public void ReportExpectedExpression(TextSpan span)
        => ReportError(span, DiagnosticCode.ExpectedExpression,
            "Expected expression");

    public void ReportExpectedClosingTag(TextSpan span, string openTag, string expectedCloseTag)
        => ReportError(span, DiagnosticCode.ExpectedClosingTag,
            $"Expected '{expectedCloseTag}' to close '{openTag}'");

    // Semantic diagnostics
    public void ReportMissingExtensionSelf(TextSpan span, string methodName, string enumName)
        => ReportError(span, DiagnosticCode.MissingExtensionSelf,
            $"Extension method '{methodName}' must have a parameter of type '{enumName}'");

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        _diagnostics.AddRange(diagnostics);
    }

    public void Clear()
    {
        _diagnostics.Clear();
    }

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
