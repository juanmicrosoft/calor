using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

internal static class RawFrameAssertions
{
    internal static Token[] GetFrames(string? source, TokenKind kind = TokenKind.CSharpInterop)
    {
        Assert.NotNull(source);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAll();
        Assert.Empty(diagnostics.Errors);
        var frames = tokens.Where(token => token.Kind == kind).ToArray();
        Assert.All(frames, token => Assert.StartsWith("raw ", token.Text));
        return frames;
    }

    internal static void AssertFramed(string? source, TokenKind kind = TokenKind.CSharpInterop, bool present = true)
    {
        var frames = GetFrames(source, kind);
        if (present)
            Assert.NotEmpty(frames);
        else
            Assert.Empty(frames);
    }
}
