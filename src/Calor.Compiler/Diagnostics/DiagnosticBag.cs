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
    private Binding.BindingDiagnosticContext? _bindingContext;

    internal IDisposable EnterBindingContext()
    {
        var scope = new BindingScope(this, _bindingContext);
        _bindingContext = Binding.BindingDiagnosticContext.General;
        return scope;
    }

    private sealed class BindingScope(DiagnosticBag bag, Binding.BindingDiagnosticContext? previous) : IDisposable
    {
        public void Dispose() => bag._bindingContext = previous;
    }

    public int Count => _diagnostics.Count;
    public bool HasErrors => _diagnostics.Any(d => d.IsError);

    internal (int Diagnostics, int Fixes) CreateCheckpoint()
        => (_diagnostics.Count, _diagnosticsWithFixes.Count);

    internal void RestoreCheckpoint((int Diagnostics, int Fixes) checkpoint)
    {
        if (_diagnostics.Count > checkpoint.Diagnostics)
            _diagnostics.RemoveRange(checkpoint.Diagnostics, _diagnostics.Count - checkpoint.Diagnostics);
        if (_diagnosticsWithFixes.Count > checkpoint.Fixes)
            _diagnosticsWithFixes.RemoveRange(checkpoint.Fixes, _diagnosticsWithFixes.Count - checkpoint.Fixes);
    }

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
        => Report(span, code, message, severity, bindingContext: null);

    public void Report(TextSpan span, string code, string message,
        DiagnosticSeverity severity, Binding.BindingDiagnosticContext? bindingContext)
    {
        _diagnostics.Add(new Diagnostic(code, message, span, severity, _currentFilePath)
        {
            BindingContext = bindingContext ?? _bindingContext
        });
    }

    /// <summary>
    /// Adds an already-constructed diagnostic (e.g. from cross-module passes
    /// that produce <see cref="Diagnostic"/> instances directly).
    /// </summary>
    public void Add(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Reports a contract-verification diagnostic carrying the choke-point proof
    /// outcome as its envelope verification payload.
    /// </summary>
    public void ReportVerification(TextSpan span, string code, string message,
        DiagnosticSeverity severity, Verification.ProofOutcome outcome)
    {
        _diagnostics.Add(new Diagnostic(code, message, span, severity, _currentFilePath)
        {
            Verification = outcome,
            BindingContext = _bindingContext
        });
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

    public void ReportUnsignedNegativeLiteral(TextSpan span)
        => ReportError(span, DiagnosticCode.UnsignedNegativeLiteral,
            "Unsigned integer literals cannot be negative");

    public void ReportInterpolatedUtf8Literal(TextSpan span)
        => ReportError(span, DiagnosticCode.InterpolatedUtf8Literal,
            "Interpolated UTF-8 string literals are not supported; use a non-interpolated u8 literal or encode the formatted string explicitly");

    public void ReportSignedIntegerLiteralOverflow(TextSpan span)
        => ReportError(span, DiagnosticCode.SignedIntegerLiteralOverflow,
            "Explicit signed 64-bit integer literal is outside the Int64 range");

    // Parser diagnostics
    public void ReportUnexpectedToken(TextSpan span, TokenKind expected, TokenKind actual)
        => ReportError(span, DiagnosticCode.UnexpectedToken,
            $"Expected {expected} but found {actual}");

    public void ReportUnexpectedToken(TextSpan span, string expected, TokenKind actual)
        => ReportError(span, DiagnosticCode.UnexpectedToken,
            $"Expected {expected} but found {actual}");

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

    /// <summary>
    /// Calor0150 — A closer-less <c>§C{target}</c> call took one inline
    /// primary expression as its argument and then encountered a second
    /// expression-start token on the same call. Per RFC v0.6 call-closer-
    /// elision §3.2 case B this is ambiguous; the user should switch to
    /// the explicit form <c>§C{target} §A arg1 §A arg2 §/C</c>.
    /// </summary>
    public void ReportAmbiguousCallContinuation(TextSpan span, string target)
        => ReportError(span, DiagnosticCode.AmbiguousCallContinuation,
            $"Closer-less call '§C{{{target}}}' already took one inline argument; " +
            "a second positional argument is ambiguous. " +
            $"Use the explicit form '§C{{{target}}} §A arg1 §A arg2 §/C' instead.");

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
        _diagnostics.Add(new Diagnostic(code, message, span, severity, _currentFilePath)
        {
            BindingContext = _bindingContext
        });
        // Also add to fix list for code actions
        _diagnosticsWithFixes.Add(new DiagnosticWithFix(code, message, span, fix, severity, _currentFilePath)
        {
            BindingContext = _bindingContext
        });
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
