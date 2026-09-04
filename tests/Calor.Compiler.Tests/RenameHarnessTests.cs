using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Rename gate (roadmap §2.5 gate 4).
///
/// Two claims, and the second is the one that matters: edits target exact
/// identifier tokens, and the renamed program still *behaves* the same. Compile
/// success is not the oracle — a capturing or colliding rename compiles
/// perfectly well and quietly means something else. So every applying case is
/// executed before and after the rename and the results are compared.
///
/// Corpus and its registration rules: tests/TestData/RenameScripts/README.md.
/// </summary>
public sealed class RenameHarnessTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // #1150: generated assemblies go into collectible contexts, unloaded below.
    private readonly CollectibleAssemblyLoader _assemblies = new();

    public void Dispose()
    {
        // #1150: unload the generated assemblies this test loaded.
        _assemblies.Dispose();

        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    public static TheoryData<string> RegisteredCases()
    {
        var data = new TheoryData<string>();
        foreach (var directory in EnumerateCaseDirectories())
            data.Add(Path.GetFileName(directory));
        return data;
    }

    [Theory]
    [MemberData(nameof(RegisteredCases))]
    public void RenameSurvivesApplyRecompileAndRun(string caseName)
    {
        var script = LoadScript(caseName);
        var workspace = CreateTempDir();
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in Directory.GetFiles(script.Directory, "*.calr"))
        {
            var path = Path.Combine(workspace, Path.GetFileName(source));
            File.Copy(source, path);
            originals[path] = File.ReadAllText(path);
        }

        var index = ProjectSymbolIndex.Build(originals.Keys, out var skipped);
        Assert.True(
            skipped.Count == 0,
            $"{caseName}: files failed to parse or bind: {string.Join(", ", skipped)}");

        var targetPath = Path.Combine(workspace, script.TargetFile);
        var offset = NthOccurrence(originals[targetPath], script.Marker, script.Occurrence);
        var occurrence = index.Resolve(targetPath, offset);
        Assert.True(
            occurrence != null,
            $"{caseName}: no symbol at occurrence {script.Occurrence} of "
                + $"'{script.Marker}' in {script.TargetFile}");

        var result = RenameEngine.Rename(index, occurrence!.SymbolId, script.NewName);

        if (!script.Applies)
        {
            Assert.Equal(script.ExpectedRefusal, result.Refusal.ToString());
            Assert.Empty(result.Edits);
            return;
        }

        Assert.Equal(RenameRefusal.None, result.Refusal);
        Assert.NotEmpty(result.Edits);

        // Every edit must land on exactly the old name — the "exact identifier
        // token" half of the gate, checked against the text rather than trusted
        // from the index.
        foreach (var edit in result.Edits)
        {
            Assert.Equal(
                result.OldName,
                originals[edit.FilePath].Substring(edit.Span.Start, edit.Span.Length));
        }

        var before = Execute(originals, script.Entry!, caseName + " (before)");
        var updated = RenameEngine.Apply(originals, result.Edits);
        foreach (var (path, text) in updated)
            File.WriteAllText(path, text);

        // Recompile: the renamed sources must still parse and bind cleanly.
        ProjectSymbolIndex.Build(updated.Keys, out var skippedAfter);
        Assert.True(
            skippedAfter.Count == 0,
            $"{caseName}: renamed sources no longer parse or bind: "
                + string.Join(", ", skippedAfter));

        // ...and run: the behaviour oracle.
        var after = Execute(updated, script.Entry!, caseName + " (after)");
        Assert.Equal(before, after);
        Assert.Equal(script.ExpectedResult, after);

        // Behaviour alone cannot catch over-renaming: rewriting every occurrence
        // of a name consistently preserves behaviour too. Cases where that is
        // the risk register what must survive untouched.
        foreach (var preserved in script.Preserved)
        {
            var text = updated[Path.Combine(workspace, preserved.File)];
            Assert.Equal(
                preserved.Count,
                CountOccurrences(text, preserved.Text));
        }
    }

    [Theory]
    [MemberData(nameof(RegisteredCases))]
    public void ApplyingCasesActuallyChangeTheSources(string caseName)
    {
        // Anti-vacuity: an "applies" case that produced no textual change would
        // pass the behaviour comparison trivially, since it would be comparing a
        // program against itself.
        var script = LoadScript(caseName);
        if (!script.Applies)
            return;

        var workspace = CreateTempDir();
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in Directory.GetFiles(script.Directory, "*.calr"))
        {
            var path = Path.Combine(workspace, Path.GetFileName(source));
            File.Copy(source, path);
            originals[path] = File.ReadAllText(path);
        }

        var index = ProjectSymbolIndex.Build(originals.Keys, out _);
        var targetPath = Path.Combine(workspace, script.TargetFile);
        var occurrence = index.Resolve(
            targetPath,
            NthOccurrence(originals[targetPath], script.Marker, script.Occurrence));
        var result = RenameEngine.Rename(index, occurrence!.SymbolId, script.NewName);
        var updated = RenameEngine.Apply(originals, result.Edits);

        Assert.NotEqual(originals[targetPath], updated[targetPath]);
        Assert.Contains(script.NewName, updated[targetPath], StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteredCaseIdsAreStable()
    {
        // The denominator is pinned: dropping a case to make the gate pass has
        // to appear in the diff.
        Assert.Equal(
            new[]
            {
                "RN-01-function-across-files",
                "RN-02-shadowed-local",
                "RN-03-parameter-vs-field",
                "RN-04-capture-refused",
                "RN-05-overload-exact",
                "RN-06-module-split-refused",
            },
            EnumerateCaseDirectories().Select(Path.GetFileName).ToArray());
    }

    // --- oracle ------------------------------------------------------------

    /// <summary>
    /// Compiles the workspace the way <c>calor run</c> does — including the
    /// cross-module qualification map, without which a cross-file call does not
    /// resolve — then loads the result and invokes the entry method.
    /// </summary>
    // #1150: instance, because the assembly loads into this instance's context.
    private int Execute(
        IReadOnlyDictionary<string, string> sources,
        string entryMethod,
        string label)
    {
        var files = sources.Keys
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FileInfo(path))
            .ToList();

        // The driver is used directly rather than through
        // ExecutionWorkspace.CompileSources so a failure can say *why*: a gate
        // that reports only "compilation failed" cannot be acted on.
        var sink = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var generated = new List<(string Name, string Code)>();
        var result = CompilationDriver.CompileAll(
            files,
            file => new CompilationOptions
            {
                EnforceEffects = true,
                UnknownCallPolicy = Calor.Compiler.Effects.UnknownCallPolicy.Strict,
                ProjectDirectory = Path.GetDirectoryName(file.FullName),
            },
            crossModuleEnforcement: true,
            crossModulePolicy: Calor.Compiler.Effects.UnknownCallPolicy.Strict,
            onCompiled: (file, compiled) => generated.Add(
                (Path.GetFileNameWithoutExtension(file.Name) + ".g.cs", compiled.GeneratedCode)),
            diagnosticSink: sink);

        Assert.False(
            result.AnyErrors,
            $"{label}: Calor compilation failed:\n"
                + string.Join(
                    "\n",
                    sink.Where(d => d.Severity
                            == Calor.Compiler.Diagnostics.DiagnosticSeverity.Error)
                        .Select(d => $"  {d.Code}: {d.Message}")));

        var trees = generated
            .Select(unit => CSharpSyntaxTree.ParseText(unit.Code, path: unit.Name))
            .ToArray();
        var name = "RenameOracle_" + Guid.NewGuid().ToString("N")[..8];
        var compilation = CSharpCompilation.Create(
            name,
            trees,
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            $"{label}: generated C# does not compile:\n"
                + string.Join(
                    "\n",
                    emit.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString())));

        var assembly = _assemblies.Load(stream.ToArray(), name);
        var method = assembly.GetTypes()
            .Select(type => type.GetMethod(
                entryMethod,
                BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(candidate => candidate != null);
        Assert.True(method != null, $"{label}: no public static '{entryMethod}' found");

        try
        {
            return (int)method!.Invoke(null, null)!;
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException != null)
        {
            throw invocation.InnerException;
        }
    }

    private static IEnumerable<MetadataReference> PlatformReferences()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return trusted
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    // --- corpus ------------------------------------------------------------

    private sealed record RenameCase(
        string Directory,
        string TargetFile,
        string Marker,
        int Occurrence,
        string NewName,
        bool Applies,
        string? ExpectedRefusal,
        string? Entry,
        int ExpectedResult,
        IReadOnlyList<PreservedText> Preserved);

    private sealed record PreservedText(string File, string Text, int Count);

    private static RenameCase LoadScript(string caseName)
    {
        var directory = Path.Combine(CorpusRoot, caseName);
        using var json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, "script.json")));
        var root = json.RootElement;
        var target = root.GetProperty("target");
        var expect = root.GetProperty("expect").GetString()!;
        Assert.Contains(expect, new[] { "applies", "refuses" });

        var applies = expect == "applies";
        return new RenameCase(
            directory,
            target.GetProperty("file").GetString()!,
            target.GetProperty("marker").GetString()!,
            target.GetProperty("occurrence").GetInt32(),
            root.GetProperty("newName").GetString()!,
            applies,
            applies ? null : root.GetProperty("expectedRefusal").GetString(),
            applies ? root.GetProperty("entry").GetString() : null,
            applies ? root.GetProperty("expectedResult").GetInt32() : 0,
            root.TryGetProperty("preserved", out var preserved)
                ? preserved.EnumerateArray()
                    .Select(entry => new PreservedText(
                        entry.GetProperty("file").GetString()!,
                        entry.GetProperty("text").GetString()!,
                        entry.GetProperty("count").GetInt32()))
                    .ToArray()
                : Array.Empty<PreservedText>());
    }

    /// <summary>
    /// The nth WHOLE-identifier occurrence of the marker. Substring matching
    /// would let a marker of "Pick" select the class "Picker" that happens to
    /// appear earlier, silently pointing the case at a different symbol than the
    /// one it claims to test.
    /// </summary>
    private static int NthOccurrence(string source, string marker, int occurrence)
    {
        var found = -1;
        var index = -1;
        while (found < occurrence)
        {
            index = source.IndexOf(marker, index + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"marker '{marker}' occurrence {occurrence} not found");

            var beforeIsIdentifier = index > 0 && IsIdentifierChar(source[index - 1]);
            var afterIndex = index + marker.Length;
            var afterIsIdentifier = afterIndex < source.Length
                && IsIdentifierChar(source[afterIndex]);
            if (!beforeIsIdentifier && !afterIsIdentifier)
                found++;
        }

        return index;
    }

    private static bool IsIdentifierChar(char character) =>
        char.IsLetterOrDigit(character) || character == '_';

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            var beforeOk = index == 0 || !IsIdentifierChar(source[index - 1]);
            var afterIndex = index + value.Length;
            var afterOk = afterIndex >= source.Length || !IsIdentifierChar(source[afterIndex]);
            if (beforeOk && afterOk)
                count++;
        }
        return count;
    }

    private static string[] EnumerateCaseDirectories() =>
        Directory.GetDirectories(CorpusRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string CorpusRoot =>
        Path.Combine(CliTestHarness.FindRepoRoot(), "tests", "TestData", "RenameScripts");

    private string CreateTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "calor-rename-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
