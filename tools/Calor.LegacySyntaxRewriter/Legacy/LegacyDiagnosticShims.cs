using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.LegacySyntax;

/// <summary>
/// The vendored pre-cutover parser reports unexpected tokens with its own
/// <see cref="TokenKind"/>; today's <see cref="DiagnosticBag"/> only accepts the
/// current lexer's kinds. These overloads keep the vendored code unchanged.
/// </summary>
internal static class LegacyDiagnosticShims
{
    public static void ReportUnexpectedToken(this DiagnosticBag bag, TextSpan span, TokenKind expected, TokenKind actual)
        => bag.ReportError(span, DiagnosticCode.UnexpectedToken, $"Expected {expected} but found {actual}");

    public static void ReportUnexpectedToken(this DiagnosticBag bag, TextSpan span, string expected, TokenKind actual)
        => bag.ReportError(span, DiagnosticCode.UnexpectedToken, $"Expected {expected} but found {actual}");
}
