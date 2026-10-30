using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ActiveSyntaxInputTests
{
    public static IEnumerable<object[]> RegisteredInputs()
        => ActiveSyntaxInputs.OriginalPaths.Order(StringComparer.Ordinal)
            .Select(path => new object[] { path });

    [Fact]
    public void ExecutableRegistry_IdentifiesEveryScriptAndTranscriptWithoutParsingThemAsCalor()
    {
        var root = CliTestHarness.FindRepoRoot();
        var activeRoot = Path.Combine(root, ActiveSyntaxInputs.RelativeRoot);
        var registrations = ActiveSyntaxInputs.ExecutableRegistrations.Values.ToArray();
        Assert.NotEmpty(registrations);
        foreach (var extension in new[] { ".py", ".txt" })
        {
            var mirrors = Directory.EnumerateFiles(activeRoot, "*" + extension, SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(activeRoot, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal);
            var registered = registrations.Select(entry => extension == ".py"
                ? entry.OriginalPath : entry.TranscriptPath).Order(StringComparer.Ordinal);
            Assert.Equal(registered, mirrors);
        }

        foreach (var registration in registrations)
        {
            Assert.Equal(".py", Path.GetExtension(registration.OriginalPath));
            Assert.Equal(
                Path.Combine(Path.GetDirectoryName(registration.OriginalPath)!, "transcripts",
                    Path.ChangeExtension(Path.GetFileName(registration.OriginalPath), ".txt")).Replace('\\', '/'),
                registration.TranscriptPath);
            Assert.True(File.Exists(Path.Combine(root, registration.OriginalPath)));
            Assert.True(File.Exists(Path.Combine(root, registration.TranscriptPath)));
            Assert.DoesNotContain(registration.OriginalPath, ActiveSyntaxInputs.OriginalPaths);
            var (script, transcript) = ActiveSyntaxInputs.ForCurrentExecutable(root, registration.OriginalPath);
            Assert.Equal(Path.Combine(activeRoot, registration.OriginalPath), script);
            Assert.Equal(Path.Combine(activeRoot, registration.TranscriptPath), transcript);
            Assert.Throws<InvalidDataException>(() =>
                ActiveSyntaxInputs.ForCurrentCompiler(root, registration.OriginalPath));
        }
    }

    [Fact]
    public void ExecutableResolver_RejectsUnregisteredAndAliasedPaths()
    {
        var root = CliTestHarness.FindRepoRoot();
        var original = ActiveSyntaxInputs.ExecutableRegistrations.Keys.Order(StringComparer.Ordinal).First();
        foreach (var path in new[]
        {
            "./" + original, Path.Combine(root, original), ActiveSyntaxInputs.RelativeRoot + original,
            "samples/FizzBuzz/fizzbuzz.calr", "docs/unregistered.py", "../outside.py"
        })
        {
            Assert.Throws<ArgumentException>(() => ActiveSyntaxInputs.ForCurrentExecutable(root, path));
        }
    }

    [Fact]
    public void RequiredExecutableMirrorMissing_DoesNotRunTheHistoricalOriginal()
    {
        var root = Path.Combine(CliTestHarness.FindRepoRoot(), "artifacts", $"missing-active-{Guid.NewGuid():N}");
        var original = ActiveSyntaxInputs.ExecutableRegistrations.Keys.Order(StringComparer.Ordinal).First();
        var exception = Assert.Throws<FileNotFoundException>(() =>
            ActiveSyntaxInputs.ForCurrentExecutable(root, original));
        Assert.Equal(Path.Combine(root, ActiveSyntaxInputs.RelativeRoot, original), exception.FileName);
    }

    [Fact]
    public void Registry_IdentifiesEveryMirrorWithoutAddingCorpusSubjects()
    {
        var root = CliTestHarness.FindRepoRoot();
        var mirrors = Directory.EnumerateFiles(
                Path.Combine(root, ActiveSyntaxInputs.RelativeRoot), "*.calr", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(
                Path.Combine(root, ActiveSyntaxInputs.RelativeRoot), path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        Assert.NotEmpty(ActiveSyntaxInputs.OriginalPaths);
        Assert.Equal(ActiveSyntaxInputs.OriginalPaths.Order(StringComparer.Ordinal), mirrors);
        foreach (var original in ActiveSyntaxInputs.OriginalPaths)
        {
            Assert.True(File.Exists(Path.Combine(root, original)), $"Original input missing: {original}");
            var active = ActiveSyntaxInputs.ForCurrentCompiler(root, original);
            Assert.NotEqual(Path.Combine(root, original), active);
            Assert.True(ActiveSyntaxInputs.IsActiveCopy(Path.GetRelativePath(root, active).Replace('\\', '/')));
        }
    }

    [Theory]
    [MemberData(nameof(RegisteredInputs))]
    public void ActiveInput_ParsesWithTheCurrentCompiler(string original)
    {
        var path = ActiveSyntaxInputs.ForCurrentCompiler(CliTestHarness.FindRepoRoot(), original);
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(File.ReadAllText(path), diagnostics);
        new Parser(lexer.TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors,
            $"{path} is an active semantic input, not a historical raw replay:\n"
            + string.Join("\n", diagnostics.Errors));
    }

    [Theory]
    [InlineData("bench/phase0-agent-native/epochs/guarantees-probe-001/W5A-002-inventory-log/calor-v10-verify/run-1/final-src/Inventory.calr")]
    [InlineData("bench/phase0-agent-native/epochs/guarantees-probe-001/W5A-002-inventory-log/calor-v10-verify/run-2/final-src/Inventory.calr")]
    [InlineData("bench/phase0-agent-native/epochs/guarantees-probe-001/W5A-002-inventory-log/calor-v10-verify/run-3/final-src/Inventory.calr")]
    [InlineData("bench/phase0-agent-native/epochs/w5-parity-001/N1-001-string-utils/calor+treatment/run-1/final-src/TextUtils.calr")]
    [InlineData("bench/phase0-agent-native/epochs/w5-parity-001/N1-001-string-utils/calor+treatment/run-2/final-src/TextUtils.calr")]
    public void MigratedControlFlowInput_CompilesWithoutOptOuts(string original)
    {
        var active = ActiveSyntaxInputs.ForCurrentCompiler(CliTestHarness.FindRepoRoot(), original);
        var result = Program.Compile(File.ReadAllText(active), active, new CompilationOptions());
        Assert.False(result.HasErrors, $"{active}:\n" + string.Join("\n", result.Diagnostics.Errors));
    }

    [Fact]
    public void RequiredMirrorMissing_DoesNotFallBackToHistoricalInput()
    {
        var root = Path.Combine(Path.GetTempPath(), $"calor-active-input-{Guid.NewGuid():N}");
        var original = ActiveSyntaxInputs.OriginalPaths.Order(StringComparer.Ordinal).First();
        var exception = Assert.Throws<FileNotFoundException>(
            () => ActiveSyntaxInputs.ForCurrentCompiler(root, original));
        Assert.Equal(Path.Combine(root, ActiveSyntaxInputs.RelativeRoot, original), exception.FileName);
    }

    [Fact]
    public void OrdinaryActiveSource_IsNotTranslatedOrRemapped()
    {
        var root = CliTestHarness.FindRepoRoot();
        const string source = "samples/FizzBuzz/fizzbuzz.calr";
        Assert.Equal(Path.Combine(root, source), ActiveSyntaxInputs.ForCurrentCompiler(root, source));
        Assert.Throws<ArgumentException>(() =>
            ActiveSyntaxInputs.ForCurrentCompiler(root, "../outside.calr"));
        Assert.Throws<ArgumentException>(() =>
            ActiveSyntaxInputs.ForCurrentCompiler(root, ActiveSyntaxInputs.RelativeRoot + source));
    }
}
