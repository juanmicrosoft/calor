using System.Collections;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Diagnostics;

/// <summary>
/// A collection of diagnostics accumulated during compilation.
/// </summary>
public sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<DiagnosticWithFix> _diagnosticsWithFixes = [];
    private string? _currentFilePath;

    public int Count => _diagnostics.Count;
    public bool HasErrors => _diagnostics.Any(d => d.IsError);

    /// <summary>
    /// Diagnostics that have associated fixes.
    /// </summary>
    public IReadOnlyList<DiagnosticWithFix> DiagnosticsWithFixes => _diagnosticsWithFixes;

    public IReadOnlyList<Diagnostic> Errors
        => _diagnostics.Where(d => d.IsError).ToList();

    public IReadOnlyList<Diagnostic> Warnings
        => _diagnostics.Where(d => d.IsWarning).ToList();

    /// <summary>
    /// Gets the current file path for diagnostics.
    /// </summary>
    public string? CurrentFilePath => _currentFilePath;

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

    // Diagnostics with fixes
    /// <summary>
    /// Reports a diagnostic with an associated suggested fix.
    /// </summary>
    public void ReportWithFix(TextSpan span, string code, string message, SuggestedFix fix,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        // Add to regular diagnostics for normal display
        _diagnostics.Add(new Diagnostic(code, message, span, severity, _currentFilePath));
        // Also add to fix list for code actions
        _diagnosticsWithFixes.Add(new DiagnosticWithFix(code, message, span, fix, severity, _currentFilePath));
    }

    /// <summary>
    /// Reports an error diagnostic with an associated suggested fix.
    /// </summary>
    public void ReportErrorWithFix(TextSpan span, string code, string message, SuggestedFix fix)
        => ReportWithFix(span, code, message, fix, DiagnosticSeverity.Error);

    /// <summary>
    /// Reports a warning diagnostic with an associated suggested fix.
    /// </summary>
    public void ReportWarningWithFix(TextSpan span, string code, string message, SuggestedFix fix)
        => ReportWithFix(span, code, message, fix, DiagnosticSeverity.Warning);

    /// <summary>
    /// Reports a mismatched ID error with a fix to correct the closing tag ID.
    /// </summary>
    public void ReportMismatchedIdWithFix(TextSpan span, string openTag, string openId,
        string closeTag, string closeId)
    {
        var message = $"{closeTag} id '{closeId}' does not match {openTag} id '{openId}'";
        var filePath = _currentFilePath ?? "";

        // If closeId is empty, the closing tag is completely missing - suggest inserting it
        if (string.IsNullOrEmpty(closeId))
        {
            var closingTag = GetClosingTagSyntax(openTag, openId);
            var fix = new SuggestedFix(
                $"Insert missing closing tag '{closingTag}'",
                TextEdit.Insert(filePath, span.Line, span.Column, closingTag + "\n"));

            ReportErrorWithFix(span, DiagnosticCode.MismatchedId, message, fix);
            return;
        }

        // Create fix to replace the wrong ID with the correct one
        // The edit position is calculated based on where the ID appears in the close tag
        // Format: §/TAG{closeId} - the ID starts at column + prefix length
        // Prefix is "§/TAG{" where TAG length varies
        var tagLetters = GetClosingTagLetters(openTag);
        var prefixLength = 3 + tagLetters.Length; // § (1) + / (1) + TAG + { (1)
        var idStartColumn = span.Column + prefixLength;

        var idFix = new SuggestedFix(
            $"Change '{closeId}' to '{openId}'",
            TextEdit.Replace(filePath, span.Line, idStartColumn, span.Line, idStartColumn + closeId.Length, openId));

        ReportErrorWithFix(span, DiagnosticCode.MismatchedId, message, idFix);
    }

    /// <summary>
    /// Gets the closing tag letters for an opening tag name.
    /// </summary>
    private static string GetClosingTagLetters(string openTag)
    {
        return openTag switch
        {
            "MODULE" or "M" => "M",
            "FUNC" or "F" => "F",
            "AF" => "AF",
            "CLASS" or "CL" => "CL",
            "METHOD" or "MT" => "MT",
            "IFACE" => "IFACE",
            "FOR" or "L" => "L",
            "WHILE" or "W" => "W",
            "DO" => "DO",
            "IF" => "IF",
            "MATCH" or "MA" => "MA",
            "TRY" or "TR" => "TR",
            "LAM" => "LAM",
            "PROP" or "PR" => "PR",
            "CTOR" or "CT" => "CT",
            "DECISION" or "DC" => "DC",
            "EN" => "EN",
            "EXT" => "EXT",
            "ARR" => "ARR",
            "EACH" => "EACH",
            "LIST" => "LIST",
            "DICT" => "DICT",
            "HSET" => "HSET",
            "EACHKV" => "EACHKV",
            "AMT" => "AMT",
            "DEL" => "DEL",
            _ => openTag // fallback to the tag name itself
        };
    }

    /// <summary>
    /// Gets the correct closing tag syntax for an opening tag.
    /// </summary>
    private static string GetClosingTagSyntax(string openTag, string id)
    {
        // Map opening tag names to their closing tag syntax
        return openTag switch
        {
            "MODULE" or "M" => $"§/M{{{id}}}",
            "FUNC" or "F" => $"§/F{{{id}}}",
            "AF" => $"§/AF{{{id}}}",
            "CLASS" or "CL" => $"§/CL{{{id}}}",
            "METHOD" or "MT" => $"§/MT{{{id}}}",
            "IFACE" => $"§/IFACE{{{id}}}",
            "FOR" or "L" => $"§/L{{{id}}}",
            "WHILE" or "W" => $"§/W{{{id}}}",
            "DO" => $"§/DO{{{id}}}",
            "IF" => $"§/IF{{{id}}}",
            "MATCH" or "MA" => $"§/MA{{{id}}}",
            "TRY" or "TR" => $"§/TR{{{id}}}",
            "LAM" => $"§/LAM{{{id}}}",
            "PROP" or "PR" => $"§/PR{{{id}}}",
            "CTOR" or "CT" => $"§/CT{{{id}}}",
            "DECISION" or "DC" => $"§/DC{{{id}}}",
            "EN" => $"§/EN{{{id}}}",
            "EXT" => $"§/EXT{{{id}}}",
            _ => $"§/{openTag}{{{id}}}"
        };
    }

    /// <summary>
    /// Reports an expected closing tag error with a fix to insert the closing tag.
    /// </summary>
    public void ReportExpectedClosingTagWithFix(TextSpan span, string openTag,
        string expectedCloseTag, int insertLine, int insertColumn)
    {
        var message = $"Expected '{expectedCloseTag}' to close '{openTag}'";

        // Create fix to insert the missing closing tag
        var filePath = _currentFilePath ?? "";
        var fix = new SuggestedFix(
            $"Insert '{expectedCloseTag}'",
            TextEdit.Insert(filePath, insertLine, insertColumn, $"\n{expectedCloseTag}"));

        ReportErrorWithFix(span, DiagnosticCode.ExpectedClosingTag, message, fix);
    }

    /// <summary>
    /// Reports an error when a function is used where a variable is expected,
    /// with a fix to call the function.
    /// </summary>
    public void ReportNotAVariableWithFix(TextSpan span, string name, bool isFunction)
    {
        if (isFunction)
        {
            var message = $"'{name}' is a function, not a variable. Did you mean to call it?";
            var filePath = _currentFilePath ?? "";

            // Create fix to add parentheses to call the function
            var fix = new SuggestedFix(
                $"Call '{name}()'",
                TextEdit.Replace(filePath, span.Line, span.Column, span.Line, span.Column + name.Length, $"§C{{{name}}} §/C"));

            ReportErrorWithFix(span, DiagnosticCode.TypeMismatch, message, fix);
        }
        else
        {
            ReportError(span, DiagnosticCode.TypeMismatch, $"'{name}' is not a variable");
        }
    }

    /// <summary>
    /// Reports a duplicate definition error with a fix to rename the symbol.
    /// </summary>
    public void ReportDuplicateDefinitionWithFix(TextSpan span, string name, string suggestedName)
    {
        var message = $"'{name}' is already defined. Consider using a different name.";
        var filePath = _currentFilePath ?? "";

        var fix = new SuggestedFix(
            $"Rename to '{suggestedName}'",
            TextEdit.Replace(filePath, span.Line, span.Column, span.Line, span.Column + name.Length, suggestedName));

        ReportErrorWithFix(span, DiagnosticCode.DuplicateDefinition, message, fix);
    }

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        _diagnostics.AddRange(diagnostics);
    }

    public void AddRange(DiagnosticBag other)
    {
        _diagnostics.AddRange(other._diagnostics);
        _diagnosticsWithFixes.AddRange(other._diagnosticsWithFixes);
    }

    public void Clear()
    {
        _diagnostics.Clear();
        _diagnosticsWithFixes.Clear();
    }

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
