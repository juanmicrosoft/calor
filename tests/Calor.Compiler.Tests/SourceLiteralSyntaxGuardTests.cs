using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1603 / #1613: compiler and tool messages, hints, examples and fix patches must teach
/// the 0.30 native syntax. A string literal anywhere under <c>src/</c> that contains a
/// retired <c>§</c> marker fails here unless its file is listed below with the reason.
/// <c>§</c> followed by a digit is a document section reference ("§4.5"), not syntax.
/// Comments are not scanned.
/// </summary>
public sealed class SourceLiteralSyntaxGuardTests
{
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        // Each retired marker the lexer rejects is named next to its native replacement.
        ["src/Calor.Compiler/Parsing/Lexer.cs"] = "retired-marker diagnostics name the marker the user wrote",
        // calor self-check docs scans documentation for retired markers.
        ["src/Calor.Compiler/SelfCheck/DocDriftChecker.cs"] = "scanner for retired markers in docs",
        // Content-stable assumption identifiers: verification envelopes carry these exact
        // strings and assumption-set hashes key on them.
        ["src/Calor.Compiler/Verification/Z3/Z3Verifier.cs"] = "content-stable assumption identifiers",
    };

    [Fact]
    public void SourceStringLiteralsUseNativeSyntax()
    {
        var root = CliTestHarness.FindRepoRoot();
        var findings = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)
                || Allowed.ContainsKey(relative))
                continue;
            var text = File.ReadAllText(path);
            if (!text.Contains('§'))
                continue;
            var tree = CSharpSyntaxTree.ParseText(text);
            foreach (var token in tree.GetRoot().DescendantTokens())
            {
                // A bare "§" or "§/" is detection code (checking for a retired marker), not teaching.
                if (!IsStringToken(token) || token.ValueText is "§" or "§/" || !ContainsRetiredMarker(token.ValueText))
                    continue;
                var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                findings.Add($"{relative}:{line}: {Shorten(token.Text)}");
            }
        }

        Assert.True(findings.Count == 0,
            "String literals teach retired § syntax (rewrite them in native syntax):\n" + string.Join("\n", findings));
    }

    [Fact]
    public void AllowListedFilesStillExist()
    {
        var root = CliTestHarness.FindRepoRoot();
        Assert.All(Allowed.Keys, relative => Assert.True(File.Exists(Path.Combine(root, relative)), relative));
    }

    private static bool IsStringToken(SyntaxToken token) => token.Kind() is
        SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken
        or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
        or SyntaxKind.Utf8StringLiteralToken;

    private static bool ContainsRetiredMarker(string text)
    {
        for (var i = text.IndexOf('§'); i >= 0; i = text.IndexOf('§', i + 1))
        {
            if (i + 1 >= text.Length || !char.IsDigit(text[i + 1]))
                return true;
        }
        return false;
    }

    private static string Shorten(string text) => text.Length <= 120 ? text : text[..117] + "...";
}
