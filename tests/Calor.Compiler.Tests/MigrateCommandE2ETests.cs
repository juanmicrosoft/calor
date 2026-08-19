using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end tests for the <c>calor migrate</c> CLI (issue #1001).
///
/// Existing coverage (<see cref="MigrateWorkflowTests"/>, <see cref="MigrateReportEnvelopeTests"/>,
/// <see cref="Mcp.MigrateToolTests"/>) exercises plan generation and report envelope schema through
/// internal APIs. This suite runs the actual CLI (via <see cref="CliTestHarness"/>) against a real
/// fixture <c>.csproj</c> from <c>tests/TestData/Projects/MigrateFixture</c>, validates the produced
/// <c>.calr</c> files by (a) inspecting their content for real Calor constructs and (b) invoking
/// <c>calor</c> on them, and re-checks the stdout report envelope schema.
///
/// The fixture deliberately spans a range of C# surfaces (nullable references, async/await, LINQ,
/// pattern matching, custom attributes, generic interfaces) so a migrator that regressed to blanket
/// <c>§CSHARP</c> interop-block passthrough would trip the content assertions rather than sailing
/// through unnoticed.
/// </summary>
public class MigrateCommandE2ETests : IDisposable
{
    private readonly string _tempDir;

    public MigrateCommandE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-migrate-e2e-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort — surfaces on Windows if child process hasn't fully released outputs */ }
        catch (UnauthorizedAccessException) { /* same, file-locked */ }
        GC.SuppressFinalize(this);
    }

    private static string FindFixtureSource()
    {
        var repoRoot = CliTestHarness.FindRepoRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "TestData", "Projects", "MigrateFixture");
        if (!Directory.Exists(fixtureDir))
            throw new DirectoryNotFoundException($"MigrateFixture not found at {fixtureDir}");
        return fixtureDir;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(source))
        {
            CopyDirectory(subDir, Path.Combine(destination, Path.GetFileName(subDir)));
        }
    }

    [Fact]
    public void MigrateCli_OnFixtureProject_ProducesRealCalrAndCompilesEndToEnd()
    {
        // Arrange: copy fixture project into an isolated temp dir so the test never mutates
        // the source tree.
        var fixtureSource = FindFixtureSource();
        var fixtureCopy = Path.Combine(_tempDir, "MigrateFixture");
        CopyDirectory(fixtureSource, fixtureCopy);

        var csFiles = Directory.GetFiles(fixtureCopy, "*.cs", SearchOption.AllDirectories);
        Assert.True(csFiles.Length >= 5,
            $"Fixture is degenerate: only {csFiles.Length} .cs files. The point of this test is a range of C# surfaces (nullable, async, LINQ, patterns, attributes, generics) — see the class doc-comment.");
        var csprojBefore = File.ReadAllText(Path.Combine(fixtureCopy, "MigrateFixture.csproj"));

        // Act 1: run `calor migrate` on the fixture directory. `--skip-verify` avoids a hard
        // dependency on Z3 native assets in this test lane. `--skip-analyze` is intentionally
        // NOT passed — the migrator's analyze phase is exactly what should run against
        // realistic fixtures.
        var reportPath = Path.Combine(_tempDir, "migrate-report.json");
        var (migrateExit, migrateStdOut, migrateStdErr) = CliTestHarness.RunCli(
            _tempDir,
            "migrate", fixtureCopy,
            "--report", reportPath,
            "--skip-verify");

        Assert.True(migrateExit == 0,
            $"calor migrate failed (exit {migrateExit}).\nSTDOUT:\n{migrateStdOut}\nSTDERR:\n{migrateStdErr}");

        // Assert: .calr files were produced, one per .cs source. Strict equality — the migrate
        // contract is one .calr per convertible .cs. A drop-to-zero or an off-by-one would
        // slip past a fuzzy `>=` check.
        var calrFiles = Directory.GetFiles(fixtureCopy, "*.calr", SearchOption.AllDirectories);
        Assert.True(calrFiles.Length == csFiles.Length,
            $"Expected exactly {csFiles.Length} .calr files (one per .cs), got {calrFiles.Length}. STDOUT:\n{migrateStdOut}");

        // Assert content: aggregate counts of executable-member declarations (§MT class methods
        // and §F free functions — the migrator emits §MT for methods inside class bodies) and
        // §CSHARP interop blocks across the whole produced set. A migrator that regressed to
        // blanket-passthrough (every file wrapped in a single §CSHARP block, zero real
        // conversion) would produce §CSHARP-count == file-count and §MT+§F count == 0. This
        // is exactly the "testing the copy loop" failure mode the adversarial review of the
        // first draft flagged.
        int totalMemberDeclarations = 0;
        int totalCSharpBlocks = 0;
        var perFileCsharp = new Dictionary<string, int>();
        foreach (var calr in calrFiles)
        {
            var text = File.ReadAllText(calr);
            var members = Regex.Matches(text, @"§(MT|F|CTOR|PROP|OP|IXER)\{").Count;
            var cs = Regex.Matches(text, @"§CSHARP\{").Count;
            totalMemberDeclarations += members;
            totalCSharpBlocks += cs;
            perFileCsharp[Path.GetFileName(calr)] = cs;
        }
        Assert.True(totalMemberDeclarations >= 5,
            $"Migrator produced very few executable-member declarations ({totalMemberDeclarations}) across {calrFiles.Length} files. Expected at least 5 — the fixture has 8+ methods/constructors total. Per-file §CSHARP block counts: {string.Join(", ", perFileCsharp.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Assert.True(totalCSharpBlocks < calrFiles.Length,
            $"Migrator produced {totalCSharpBlocks} §CSHARP interop blocks across {calrFiles.Length} .calr files — that's >= one wrapper per file, i.e. blanket passthrough. Per-file: {string.Join(", ", perFileCsharp.Select(kv => $"{kv.Key}={kv.Value}"))}");

        // Assert: the .json report is envelope-wrapped per schema 2.0 with populated fileResults.
        Assert.True(File.Exists(reportPath), $"report.json was not written to {reportPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = doc.RootElement.Clone();
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);
        Assert.Equal(JsonDiagnosticFormatter.SchemaVersion, root.GetProperty("version").GetString());
        Assert.Equal("migrate", root.GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("diagnostics").ValueKind);
        var data = root.GetProperty("data");
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.True(data.TryGetProperty("summary", out var summary));
        Assert.True(summary.GetProperty("totalFiles").GetInt32() >= csFiles.Length,
            $"Envelope summary reports {summary.GetProperty("totalFiles").GetInt32()} totalFiles, expected at least {csFiles.Length}.");
        Assert.True(data.TryGetProperty("fileResults", out var fileResults));
        Assert.Equal(JsonValueKind.Array, fileResults.ValueKind);
        Assert.True(fileResults.GetArrayLength() >= csFiles.Length,
            $"Envelope fileResults has {fileResults.GetArrayLength()} entries; expected at least {csFiles.Length} (one per source .cs).");

        // Assert: the .csproj was preserved (migrate contract per R5 acceptance).
        var csprojAfter = File.ReadAllText(Path.Combine(fixtureCopy, "MigrateFixture.csproj"));
        Assert.Equal(csprojBefore, csprojAfter);

        // Act 2: verify the produced .calr files actually compile end-to-end. We keep
        // type-checking enabled (default-on) so a migrator that produced ill-typed code is
        // caught. `--no-enforce-effects` is applied because the migrator cannot in general
        // infer effect declarations from arbitrary C# (the effect system depends on the
        // developer authoring `§E{...}` annotations); expecting migrated code to satisfy
        // strict effect enforcement out of the box would test the migrator's inference,
        // not its structural correctness. Effect-inference regression testing lives
        // elsewhere (`EffectResolverTests`, `Mcp/ImportToolTests`).
        var compileArgs = new List<string>();
        foreach (var calr in calrFiles)
        {
            compileArgs.Add("--input");
            compileArgs.Add(calr);
        }
        compileArgs.Add("--no-enforce-effects");

        var (compileExit, compileStdOut, compileStdErr) = CliTestHarness.RunCli(
            _tempDir, compileArgs.ToArray());

        Assert.True(compileExit == 0,
            $"calor compile of migrated .calr files failed (exit {compileExit}).\n" +
            $"Files: {string.Join(", ", calrFiles.Select(Path.GetFileName))}\n" +
            $"STDOUT:\n{compileStdOut}\nSTDERR:\n{compileStdErr}");
    }
}
