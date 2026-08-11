using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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

    [Theory]
    [MemberData(nameof(MultilineProtectedTokenSources))]
    public void MultilineTokenInteriors_AreProtectedByLexerSpans(
        string name,
        string source,
        string protectedText)
    {
        var result = new CalorFormatter().FormatSource(source, $"{name}.calr");

        Assert.True(result.Success, $"{name}: {string.Join("\n", result.Errors)}");
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);
        Assert.NotEqual(source, result.Formatted);
        Assert.Contains(protectedText, result.Formatted, StringComparison.Ordinal);
        Assert.Equal(
            SemanticTokenHash(source, $"{name}.calr"),
            SemanticTokenHash(result.Formatted, $"{name}.calr"));
    }

    public static IEnumerable<object[]> MultilineProtectedTokenSources()
    {
        yield return
        [
            "multiline-string",
            "§M{m001:MultilineString}   \n" +
            "  §F{f001:Main:pub} () -> void\n" +
            "    §E{cw}\n" +
            "    §P \"\"\"\n" +
            "      first line   \n" +
            "        // string content   \n" +
            "      last line\t\n" +
            "    \"\"\"\n",
            "      first line   \n        // string content   \n      last line\t"
        ];
        yield return
        [
            "multiline-raw-expression",
            "§M{m001:MultilineExpression}   \n" +
            "  §F{f001:Get:pub} () -> i32\n" +
            "    §R §CS{\n" +
            "      1 +   \n" +
            "        2\t\n" +
            "    }\n",
            "\n      1 +   \n        2\t\n    }"
        ];
        yield return
        [
            "multiline-raw-block",
            "§M{m001:MultilineRaw}   \n" +
                        "  §F{f001:Main:pub} () -> void\n" +
                        "    §E{cw}\n" +
                        "    §RAW\n" +
                        "var rawKeep = \"keep\";   \n" +
                        "Console.WriteLine(rawKeep);\n" +
                        "    §/RAW\n",
                        "var rawKeep = \"keep\";   \nConsole.WriteLine(rawKeep);"
        ];
        yield return
        [
            "multiline-interop-block",
            "§M{m001:MultilineInterop}   \n" +
            "  §CSHARP{\n" +
            "public static class InteropKeep\n" +
            "{\n" +
            "    public static string Value = \"keep\";   \n" +
            "}\n" +
            "}§/CSHARP\n",
            "public static class InteropKeep\n{\n    public static string Value = \"keep\";   \n}"
        ];
    }

    [Fact]
    public void CrOnlyInput_FormatsWithoutLineDesynchronization()
    {
        const string source =
            "§M{m001:CrOnly}\r" +
            "    §F{f001:Main:pub} () -> void   \r" +
            "        §E{cw}\r" +
            "        §P \"ok\"   \r";

        var result = new CalorFormatter().FormatSource(source, "cr-only.calr");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.False(result.UsedConservativeFallback, result.ConservativeFallbackReason);
        Assert.Equal(
            "§M{m001:CrOnly}\r" +
            "  §F{f001:Main:pub} () -> void\r" +
            "    §E{cw}\r" +
            "    §P \"ok\"\r",
            result.Formatted);
    }

    [Fact]
    public void MixedLoneCrInput_PreservesEveryTerminatorAndRemainsIdempotent()
    {
        const string source =
            "§M{m001:MixedCr}\r\n" +
            "    §F{f001:Main:pub} () -> void   \r" +
            "        §E{cw}\n" +
            "        §P \"ok\"   \r\n";
        var formatter = new CalorFormatter();

        var once = formatter.FormatSource(source, "mixed-cr.calr");
        var twice = formatter.FormatSource(once.Formatted, "mixed-cr.calr");

        Assert.True(once.Success, string.Join("\n", once.Errors));
        Assert.True(twice.Success, string.Join("\n", twice.Errors));
        Assert.False(once.UsedConservativeFallback, once.ConservativeFallbackReason);
        Assert.Equal(once.Formatted, twice.Formatted);
        Assert.True(LosslessSourceDocument.HasEquivalentLineShape(
            source,
            once.Formatted,
            out var error), error);
        Assert.Contains("\r\n", once.Formatted, StringComparison.Ordinal);
        Assert.Contains("\r", once.Formatted, StringComparison.Ordinal);
        Assert.Contains("\n", once.Formatted, StringComparison.Ordinal);
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
    public async Task SafeWrite_ResolvesSymlinkTargetWithoutReplacingLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var target = Path.Combine(_testDirectory, "symlink-target.calr");
        var link = Path.Combine(_testDirectory, "symlink.calr");
        const string source =
            "§M{m001:Symlink}\n" +
            "    §F{f001:Main:pub} () -> void   \n";
        await File.WriteAllTextAsync(target, source);
        File.CreateSymbolicLink(link, target);

        var snapshot = await SourceFileSnapshot.ReadAsync(link);
        var formatter = new CalorFormatter();
        var result = formatter.FormatSource(snapshot.Text, link);
        Assert.True(result.Success, string.Join("\n", result.Errors));

        await SafeSourceFile.WriteFormattedAsync(snapshot, result.Formatted, formatter);

        Assert.NotNull(File.ResolveLinkTarget(link, returnFinalTarget: false));
        Assert.Equal(result.Formatted, await File.ReadAllTextAsync(target));
        Assert.Equal(result.Formatted, await File.ReadAllTextAsync(link));
    }

    [Fact]
    public async Task SafeWrite_RejectsMultipleHardLinksWithoutChangingEitherName()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_testDirectory, "hardlink-source.calr");
        var alias = Path.Combine(_testDirectory, "hardlink-alias.calr");
        const string source =
            "§M{m001:HardLink}\n" +
            "    §F{f001:Main:pub} () -> void   \n";
        await File.WriteAllTextAsync(path, source);
        Assert.Equal(0, CreateHardLink(path, alias));
        Assert.True(NativeFileLinks.TryGetLinkCount(path) > 1);

        var snapshot = await SourceFileSnapshot.ReadAsync(path);
        var formatter = new CalorFormatter();
        var result = formatter.FormatSource(snapshot.Text, path);
        Assert.True(result.Success, string.Join("\n", result.Errors));

        var error = await Assert.ThrowsAsync<IOException>(
            () => SafeSourceFile.WriteFormattedAsync(
                snapshot,
                result.Formatted,
                formatter));

        Assert.Contains("multiple hard links", error.Message, StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
        Assert.Equal(source, await File.ReadAllTextAsync(alias));
        Assert.Empty(Directory.EnumerateFiles(_testDirectory, "*.format.tmp"));
    }

    [Fact]
    public void LinuxX64LinkCountDecoder_ReadsUnsigned64BitValueAtOffset16()
    {
        var buffer = new byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16), 7);

        Assert.Equal(
            7,
            NativeFileLinks.DecodeLinuxLinkCount(buffer, Architecture.X64));
    }

    [Theory]
    [InlineData(Architecture.Arm64, false)]
    [InlineData(Architecture.RiscV64, false)]
    [InlineData(Architecture.LoongArch64, false)]
    [InlineData(Architecture.Ppc64le, false)]
    [InlineData(Architecture.S390x, true)]
    public void LinuxGeneric64BitLinkCountDecoder_ReadsUnsigned32BitValueAtOffset20(
        Architecture architecture,
        bool bigEndian)
    {
        var buffer = new byte[24];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(20), 11);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), 11);
        }

        Assert.Equal(
            11,
            NativeFileLinks.DecodeLinuxLinkCount(buffer, architecture));
    }

    [Fact]
    public void LinuxLinkCountDecoder_RejectsUnsupportedArchitectures()
    {
        var error = Assert.Throws<IOException>(
            () => NativeFileLinks.DecodeLinuxLinkCount(
                new byte[24],
                Architecture.X86));

        Assert.Contains("unsupported Linux architecture", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxLinkCountDecoder_WrapsOverflowAsIOException()
    {
        var buffer = new byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(
            buffer.AsSpan(16),
            (ulong)int.MaxValue + 1);

        var error = Assert.Throws<IOException>(
            () => NativeFileLinks.DecodeLinuxLinkCount(buffer, Architecture.X64));

        Assert.IsType<OverflowException>(error.InnerException);
    }

    [Fact]
    public void SafeWrite_RejectsUnknownHardLinkCount()
    {
        var error = Assert.Throws<IOException>(
            () => SafeSourceFile.EnsureKnownSingleLinkCount(null));

        Assert.Contains("Could not determine", error.Message, StringComparison.Ordinal);
        Assert.Contains("refusing replacement", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SafeWrite_TemporaryFileIsRestrictiveAndFinalModeIsPreserved()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_testDirectory, "mode.calr");
        const string source =
            "§M{m001:Mode}\n" +
            "    §F{f001:Main:pub} () -> void   \n";
        await File.WriteAllTextAsync(path, source);
        var originalMode = UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead
            | UnixFileMode.OtherRead;
        File.SetUnixFileMode(path, originalMode);

        var snapshot = await SourceFileSnapshot.ReadAsync(path);
        var formatter = new CalorFormatter();
        var result = formatter.FormatSource(snapshot.Text, path);
        Assert.True(result.Success, string.Join("\n", result.Errors));

        UnixFileMode? temporaryMode = null;
        await SafeSourceFile.WriteFormattedAsync(
            snapshot,
            result.Formatted,
            formatter,
            _ =>
            {
                var temporary = Assert.Single(
                    Directory.EnumerateFiles(_testDirectory, "*.format.tmp"));
#pragma warning disable CA1416 // Guarded by the Windows return above.
                temporaryMode = File.GetUnixFileMode(temporary);
#pragma warning restore CA1416
                return Task.CompletedTask;
            });

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            temporaryMode);
#pragma warning disable CA1416 // Guarded by the Windows return above.
        Assert.Equal(originalMode, File.GetUnixFileMode(path));
#pragma warning restore CA1416
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
    public void CheckedInCalorCorpus_MatchesBaselineAndExercisesSafeTransformations()
    {
        var repoRoot = CliTestHarness.FindRepoRoot();
        var files = GetTrackedCalorFiles(repoRoot);
        using var baselineDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repoRoot,
            "tests",
            "TestData",
            "Formatting",
            "formatter-corpus-baseline.json")));
        var baseline = baselineDocument.RootElement;
        Assert.Equal(
            baseline.GetProperty("trackedFileCount").GetInt32(),
            files.Length);

        var expectedParseFailures = ReadPathSet(
            baseline.GetProperty("parseFailurePaths"));
        var fallbacks = baseline.GetProperty("conservativeFallbacks");
        var expectedSemanticFallbacks = ReadPathSet(
            fallbacks.GetProperty("semanticErrors"));
        var expectedGeneratedFallbacks = ReadPathSet(
            fallbacks.GetProperty("generatedCSharpErrors"));

        var formatter = new CalorFormatter();
        var actualParseFailures = new HashSet<string>(StringComparer.Ordinal);
        var actualSemanticFallbacks = new HashSet<string>(StringComparer.Ordinal);
        var actualGeneratedFallbacks = new HashSet<string>(StringComparer.Ordinal);
        var successfulTransformations = 0;
        var commentProbes = 0;

        foreach (var relativePath in files)
        {
            var path = Path.Combine(repoRoot, relativePath);
            var source = File.ReadAllText(path);
            var classification = formatter.FormatSource(source, path);
            if (!classification.Success)
            {
                actualParseFailures.Add(relativePath);
                continue;
            }
            if (classification.UsedConservativeFallback)
            {
                Assert.Equal(source, classification.Formatted);
                Assert.NotNull(classification.ConservativeFallbackReason);
                if (classification.ConservativeFallbackReason.StartsWith(
                        "Source has semantic errors",
                        StringComparison.Ordinal))
                {
                    actualSemanticFallbacks.Add(relativePath);
                }
                else if (classification.ConservativeFallbackReason.StartsWith(
                             "Source does not generate Roslyn-clean C#",
                             StringComparison.Ordinal))
                {
                    actualGeneratedFallbacks.Add(relativePath);
                }
                else
                {
                    Assert.Fail(
                        $"{relativePath}: unreviewed conservative fallback reason: " +
                        classification.ConservativeFallbackReason);
                }
                continue;
            }

            var (probedSource, usedCommentProbe) = InjectFormattingProbe(source, path);
            if (usedCommentProbe)
            {
                commentProbes++;
            }

            var once = formatter.FormatSource(probedSource, path);
            Assert.True(once.Success, $"{relativePath}: {string.Join("; ", once.Errors)}");
            Assert.False(
                once.UsedConservativeFallback,
                $"{relativePath}: {once.ConservativeFallbackReason}");
            Assert.NotEqual(probedSource, once.Formatted);

            var before = Program.Compile(probedSource, path);
            var after = Program.Compile(once.Formatted, path);
            Assert.False(
                before.HasErrors,
                $"{relativePath} before: {string.Join("; ", before.Diagnostics.Errors)}");
            Assert.False(
                after.HasErrors,
                $"{relativePath} after: {string.Join("; ", after.Diagnostics.Errors)}");
            Assert.Equal(
                SemanticTokenHash(probedSource, path),
                SemanticTokenHash(once.Formatted, path));
            Assert.Equal(before.GeneratedCode, after.GeneratedCode);
            Assert.True(
                GeneratedCSharpCompiler.Validate(after.GeneratedCode).CompilationSuccess,
                $"{relativePath}: formatted output did not generate Roslyn-clean C#.");
            Assert.Equal(
                PublicApi(before.GeneratedCode),
                PublicApi(after.GeneratedCode));

            var twice = formatter.FormatSource(once.Formatted, path);
            Assert.True(twice.Success, $"{relativePath}: {string.Join("; ", twice.Errors)}");
            Assert.False(twice.UsedConservativeFallback, twice.ConservativeFallbackReason);
            Assert.Equal(once.Formatted, twice.Formatted);
            successfulTransformations++;
        }

        Assert.Equal(
            expectedParseFailures.Order(StringComparer.Ordinal),
            actualParseFailures.Order(StringComparer.Ordinal));
        Assert.Equal(
            expectedSemanticFallbacks.Order(StringComparer.Ordinal),
            actualSemanticFallbacks.Order(StringComparer.Ordinal));
        Assert.Equal(
            expectedGeneratedFallbacks.Order(StringComparer.Ordinal),
            actualGeneratedFallbacks.Order(StringComparer.Ordinal));
        Assert.Equal(
            baseline.GetProperty("successfulTransformationCount").GetInt32(),
            successfulTransformations);
        Assert.Equal(successfulTransformations, commentProbes);
    }

    private static string[] GetTrackedCalorFiles(string repoRoot)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("*.calr");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"git ls-files failed with exit {process.ExitCode}: {error}");

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<string> ReadPathSet(JsonElement array) =>
        array.EnumerateArray()
            .Select(element => element.GetString()
                ?? throw new InvalidDataException("Corpus baseline path is null."))
            .ToHashSet(StringComparer.Ordinal);

    private static (string Source, bool UsedCommentProbe) InjectFormattingProbe(
        string source,
        string path)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : source.Contains('\n')
                ? "\n"
                : source.Contains('\r')
                    ? "\r"
                    : "\n";
        var commentProbe = "// lossless formatter corpus probe"
            + newline
            + source;
        if (Parses(commentProbe, path)
            && TryInjectTrailingWhitespace(commentProbe, path, out var probedWithComment))
        {
            return (probedWithComment, true);
        }

        if (TryInjectTrailingWhitespace(source, path, out var probedSource))
        {
            return (probedSource, false);
        }

        throw new InvalidDataException(
            $"{path}: could not inject a parseable formatting probe.");
    }

    private static bool TryInjectTrailingWhitespace(
        string source,
        string path,
        out string candidate)
    {
        var lineEnds = new List<(int Offset, int Line)>();
        var line = 1;
        for (var offset = 0; offset <= source.Length; offset++)
        {
            if (offset < source.Length
                && source[offset] is not '\r' and not '\n')
            {
                continue;
            }

            lineEnds.Add((offset, line));
            if (offset < source.Length
                && source[offset] == '\r'
                && offset + 1 < source.Length
                && source[offset + 1] == '\n')
            {
                offset++;
            }
            line++;
        }

        foreach (var lineEnd in lineEnds.AsEnumerable().Reverse())
        {
            candidate = source.Insert(lineEnd.Offset, " \t");
            if (LosslessSourceDocument
                    .GetTrimmableTrailingWhitespaceLines(candidate)
                    .Contains(lineEnd.Line)
                && Parses(candidate, path))
            {
                return true;
            }
        }

        candidate = source;
        return false;
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

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLink(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);
}
