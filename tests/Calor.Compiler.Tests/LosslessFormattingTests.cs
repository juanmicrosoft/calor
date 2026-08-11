using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class LosslessFormattingTests : IDisposable
{
    private readonly string _testDirectory;

    public LosslessFormattingTests()
    {
        _testDirectory = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".calor-test-artifacts",
            $"format-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void User001Reproduction_PreservesDeclarationReferenceAndComment()
    {
        const string source = """
            §M{m001:Test}
                §F{f001:Main:pub} () -> void
                    §E{cw}
                    §B{user001} STR:"secret"
                    // This comment must survive.
                    §P user001
            """;

        var result = new CalorFormatter().FormatSource(source, "user001.calr");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);
        Assert.Contains("§B{user001}", result.Formatted);
        Assert.Contains("§P user001", result.Formatted);
        Assert.Contains("// This comment must survive.", result.Formatted);
        Assert.DoesNotContain("§B{user1}", result.Formatted);
        Assert.NotEqual(source, result.Formatted);
    }

    [Fact]
    public void ZeroPaddedSemanticText_IsPreservedInEveryContext()
    {
        const string source = """
            §M{m001:Test001}
              §CL{c001:Type001:pub}
                §FLD{str:field001:priv}
                §CTOR{ctor001:pub}
                §MT{mt001:method001:pub} (Type001:arg001) -> str
                  §R field001
              §F{f001:call001:pub} () -> void
                §E{cw}
                §B{user001:Type001} §NEW{Type001} §/NEW
                §C{user001.method001} user001
                §P "user001 Type001 field001 method001 ctor001"
                §RAW
            var raw001 = "user001 Type001.field001.method001";
                §/RAW
            """;

        var result = new CalorFormatter().FormatSource(source, "identifiers.calr");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);
        foreach (var text in new[]
                 {
                     "m001", "Test001", "c001", "Type001", "field001",
                     "ctor001", "mt001", "method001", "arg001", "f001",
                     "call001", "user001", "user001.method001",
                     "var raw001 = \"user001 Type001.field001.method001\";"
                 })
        {
            Assert.Contains(text, result.Formatted, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CommentsDocCommentsAndBlankLineIntent_SurviveMajorBlocks()
    {
        const string source = """
            // before module
            §M{m001:Comments}
                /// function docs
                §F{f001:Main:pub} () -> void
                    §E{cw}
                    // before if
                    §IF{i001} true
                        // inside if
                        §P "if"
                    // between blocks

                    §L{l001:i:0:1:1}
                        // inside loop
                        §P i
                    // after loop
                // after function
                §CL{c001:Holder:pub}
                    // inside class
                    §FLD{str:value001:priv}
                // after class
            // after module
            """;

        var result = new CalorFormatter().FormatSource(source, "comments.calr");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);
        foreach (var comment in new[]
                 {
                     "// before module", "/// function docs", "// before if",
                     "// inside if", "// between blocks", "// inside loop",
                     "// after loop", "// after function", "// inside class",
                     "// after class", "// after module"
                 })
        {
            Assert.Equal(1, Count(result.Formatted, comment));
        }
        Assert.Contains("// between blocks\n\n", result.Formatted, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AdditionalMajorBlockSources))]
    public void CommentsBeforeInsideAndAfter_AdditionalMajorBlocksArePreserved(
        string name,
        string source)
    {
        var result = new CalorFormatter().FormatSource(source, $"{name}.calr");

        Assert.True(result.Success, $"{name}: {string.Join("\n", result.Errors)}");
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);
        Assert.Equal(1, Count(result.Formatted, $"// before {name}"));
        Assert.Equal(1, Count(result.Formatted, $"// inside {name}"));
        Assert.Equal(1, Count(result.Formatted, $"// after {name}"));
    }

    public static IEnumerable<object[]> AdditionalMajorBlockSources()
    {
        yield return
        [
            "while",
            """
            §M{m001:WhileComments}
                §F{f001:Main:pub} () -> void
                    §E{cw}
                    // before while
                    §WH{w001} false
                        // inside while
                        §P "inside"
                    // after while
                    §P "after"
            """
        ];
        yield return
        [
            "foreach",
            """
            §M{m001:ForeachComments}
                §F{f001:Main:pub} (i32[]:items001) -> void
                    §E{cw}
                    // before foreach
                    §EACH{e001:item001:i32} items001
                        // inside foreach
                        §P item001
                    // after foreach
                    §P "after"
            """
        ];
        yield return
        [
            "try",
            """
            §M{m001:TryComments}
                §F{f001:Main:pub} () -> void
                    §E{cw}
                    // before try
                    §TR{t001}
                        // inside try
                        §P "inside"
                    §FI
                        §P "finally"
                    // after try
                    §P "after"
            """
        ];
        yield return
        [
            "match",
            """
            §M{m001:MatchComments}
                §F{f001:Main:pub} (i32:value001) -> void
                    §E{cw}
                    // before match
                    §W{w001} value001
                        §K 1
                            // inside match
                            §P "one"
                        §K _
                            §P "other"
                    // after match
                    §P "after"
            """
        ];
        yield return
        [
            "interface",
            """
            §M{m001:InterfaceComments}
                // before interface
                §IFACE{i001:IThing001}
                    // inside interface
                    §MT{mt001:Run001} () -> void
                // after interface
                §F{f001:Main:pub} () -> void
            """
        ];
    }

    [Fact]
    public void StringsInlineCommentsAndRawCSharp_AreByteForBytePreserved()
    {
        const string rawLine = "  var user001 = \"// not a Calor comment\";   ";
        var source = string.Join(
            "\n",
            "§M{m001:Raw}",
            "    §F{f001:Main:pub} () -> void",
            "        §E{cw}",
            "        §P \"user001 // literal\"   // inline comment   ",
            "        §RAW",
            rawLine,
            "\tConsole.WriteLine(user001);",
            "        §/RAW",
            "");

        var result = new CalorFormatter().FormatSource(source, "raw.calr");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);
        Assert.Contains("\"user001 // literal\"   // inline comment   ", result.Formatted);
        Assert.Contains($"\n{rawLine}\n", result.Formatted, StringComparison.Ordinal);
        Assert.Contains("\n\tConsole.WriteLine(user001);\n", result.Formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void LintTrailingWhitespaceClassification_MatchesLosslessFormatter()
    {
        var source = string.Join(
            "\n",
            "§M{m001:Trivia}   ",
            "  // comment whitespace is content   ",
            "  §RAW",
            "var value001 = 1;   ",
            "  §/RAW",
            "");

        var lines = LosslessSourceDocument.GetTrimmableTrailingWhitespaceLines(source);

        Assert.Contains(1, lines);
        Assert.DoesNotContain(2, lines);
        Assert.DoesNotContain(4, lines);
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    public async Task SafeWrite_PreservesBomEncodingAndMixedNewlines(string encodingName)
    {
        var path = Path.Combine(_testDirectory, $"{encodingName}.calr");
        const string source =
            "§M{m001:Encoding}\r\n" +
            "    §F{f001:Main:pub} () -> void   \n" +
            "        §E{cw}\r\n" +
            "        §P \"é\" \r\n";
        var encoding = encodingName == "utf8-bom"
            ? (Encoding)new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        await File.WriteAllBytesAsync(
            path,
            encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());

        var snapshot = await SourceFileSnapshot.ReadAsync(path);
        var formatter = new CalorFormatter();
        var result = formatter.FormatSource(snapshot.Text, path);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);

        await SafeSourceFile.WriteFormattedAsync(snapshot, result.Formatted, formatter);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.AsSpan().StartsWith(encoding.GetPreamble()));
        var rewritten = await SourceFileSnapshot.ReadAsync(path);
        Assert.Contains("\r\n", rewritten.Text, StringComparison.Ordinal);
        Assert.Contains("void\n", rewritten.Text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", rewritten.Text, StringComparison.Ordinal);
        Assert.Contains("§F{f001:Main:pub}", rewritten.Text, StringComparison.Ordinal);
        Assert.Contains("§P \"é\"", rewritten.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureBeforeAtomicReplace_LeavesOriginalBytesUntouched()
    {
        var path = Path.Combine(_testDirectory, "atomic.calr");
        var original = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("§M{m001:Atomic}   \r\n"))
            .ToArray();
        await File.WriteAllBytesAsync(path, original);

        var snapshot = await SourceFileSnapshot.ReadAsync(path);
        var formatter = new CalorFormatter();
        var result = formatter.FormatSource(snapshot.Text, path);
        Assert.True(result.Success, string.Join("\n", result.Errors));

        await Assert.ThrowsAsync<InjectedFormatFailure>(() =>
            SafeSourceFile.WriteFormattedAsync(
                snapshot,
                result.Formatted,
                formatter,
                _ => throw new InjectedFormatFailure()));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(_testDirectory, "*.format.tmp"));
    }

    [Fact]
    public void SemanticHashGeneratedCSharpAndPublicApi_AreEquivalent()
    {
        const string source = """
            §M{m001:Api}
                §F{f001:Add001:pub} (i32:user001, i32:value001) -> i32
                    §R (+ user001 value001)
            """;
        var formatter = new CalorFormatter();
        var formatted = formatter.FormatSource(source, "api.calr");
        Assert.True(formatted.Success, string.Join("\n", formatted.Errors));

        var before = Program.Compile(source, "api.calr");
        var after = Program.Compile(formatted.Formatted, "api.calr");
        Assert.False(before.HasErrors, string.Join("\n", before.Diagnostics.Errors));
        Assert.False(after.HasErrors, string.Join("\n", after.Diagnostics.Errors));
        Assert.Equal(
            SemanticTokenHash(source, "api.calr"),
            SemanticTokenHash(formatted.Formatted, "api.calr"));
        Assert.Equal(Sha256(before.GeneratedCode), Sha256(after.GeneratedCode));
        Assert.Equal(PublicApi(before.GeneratedCode), PublicApi(after.GeneratedCode));
        Assert.True(
            GeneratedCSharpCompiler.Validate(after.GeneratedCode).CompilationSuccess);

        var syntax = CSharpSyntaxTree.ParseText(after.GeneratedCode);
        Assert.DoesNotContain(
            syntax.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void SemanticallyUnsupportedInput_UsesDocumentedConservativeFallback()
    {
        const string source = """
            §M{m001:Unsupported}
                §F{f001:Main:pub} () -> void
                    §P "missing effect declaration"
            """;

        var result = new CalorFormatter().FormatSource(source, "unsupported.calr");

        Assert.True(result.Success);
        Assert.True(result.UsedConservativeFallback);
        Assert.NotNull(result.ConservativeFallbackReason);
        Assert.Equal(source, result.Formatted);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.FormatConservativeFallback);
    }

    [Fact]
    public void CheckedInParseableCalorCorpus_IsIdempotentAndSemanticallyEquivalent()
    {
        var repoRoot = CliTestHarness.FindRepoRoot();
        var files = Directory.EnumerateFiles(repoRoot, "*.calr", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}.calor-test-artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var formatter = new CalorFormatter();
        var parseable = 0;

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            if (!Parses(source, path))
            {
                continue;
            }

            parseable++;
            var once = formatter.FormatSource(source, path);
            Assert.True(once.Success, $"{path}: {string.Join("; ", once.Errors)}");
            var twice = formatter.FormatSource(once.Formatted, path);
            Assert.True(twice.Success, $"{path}: {string.Join("; ", twice.Errors)}");
            Assert.Equal(once.Formatted, twice.Formatted);
        }

        Assert.True(parseable > 0);
    }

    private static bool Parses(string source, string path)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(path);
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        if (!diagnostics.HasErrors)
        {
            new Parser(tokens, diagnostics).Parse();
        }
        return !diagnostics.HasErrors;
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string SemanticTokenHash(string source, string path)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(path);
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        var canonical = string.Join(
            "\n",
            tokens.Select(token => $"{(int)token.Kind}:{token.Text}"));
        return Sha256(canonical);
    }

    private static string[] PublicApi(string generatedCode)
    {
        var root = CSharpSyntaxTree.ParseText(generatedCode).GetRoot();
        return root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>()
            .Where(member => member.Modifiers.Any(
                modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
            .Select(member => member.NormalizeWhitespace().ToFullString())
            .OrderBy(member => member, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class InjectedFormatFailure : Exception;
}
