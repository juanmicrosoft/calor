using System.Text.Json;
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
/// <c>.calr</c> files by invoking <c>calor</c> on them, and re-checks the stdout report envelope
/// schema.
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
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
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
    public void MigrateCli_OnFixtureProject_ProducesCompilableCalrFiles()
    {
        // Arrange: copy fixture project into an isolated temp dir so the test never mutates
        // the source tree and so it is detached from the outer Directory.Packages.props (which
        // enables locked-mode central package management that a bare fixture cannot satisfy).
        var fixtureSource = FindFixtureSource();
        var fixtureCopy = Path.Combine(_tempDir, "MigrateFixture");
        CopyDirectory(fixtureSource, fixtureCopy);

        var csFiles = Directory.GetFiles(fixtureCopy, "*.cs");
        Assert.NotEmpty(csFiles);

        // Act 1: run `calor migrate` on the fixture directory. --skip-verify avoids depending
        // on Z3 native assets in this test lane; --skip-analyze keeps the test focused on the
        // conversion + envelope produced by the CLI's final phase.
        var reportPath = Path.Combine(_tempDir, "migrate-report.json");
        var (migrateExit, migrateStdOut, migrateStdErr) = CliTestHarness.RunCli(
            _tempDir,
            "migrate", fixtureCopy,
            "--report", reportPath,
            "--skip-verify",
            "--skip-analyze");

        Assert.True(migrateExit == 0,
            $"calor migrate failed (exit {migrateExit}).\nSTDOUT:\n{migrateStdOut}\nSTDERR:\n{migrateStdErr}");

        // Assert: .calr files were produced next to the original .cs files.
        var calrFiles = Directory.GetFiles(fixtureCopy, "*.calr", SearchOption.AllDirectories);
        Assert.NotEmpty(calrFiles);
        Assert.True(calrFiles.Length >= csFiles.Length - 1,
            $"Expected at least {csFiles.Length - 1} .calr files, got {calrFiles.Length}. STDOUT:\n{migrateStdOut}");

        // Assert: the .json report is envelope-wrapped per schema 2.0 (same shape asserted by
        // MigrateReportEnvelopeTests).
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
        Assert.True(summary.GetProperty("totalFiles").GetInt32() >= csFiles.Length);
        Assert.True(data.TryGetProperty("fileResults", out var fileResults));
        Assert.Equal(JsonValueKind.Array, fileResults.ValueKind);

        // Act 2: verify the produced .calr files actually compile end-to-end by invoking
        // `calor --input` on each one. We use --no-enforce-effects and --no-type-check to
        // isolate this test from strictness defaults orthogonal to migrate's contract.
        var compileArgs = new List<string>();
        foreach (var calr in calrFiles)
        {
            compileArgs.Add("--input");
            compileArgs.Add(calr);
        }
        compileArgs.Add("--no-enforce-effects");
        compileArgs.Add("--no-type-check");

        var (compileExit, compileStdOut, compileStdErr) = CliTestHarness.RunCli(
            _tempDir, compileArgs.ToArray());

        Assert.True(compileExit == 0,
            $"calor compile of migrated .calr files failed (exit {compileExit}).\n" +
            $"Files: {string.Join(", ", calrFiles.Select(Path.GetFileName))}\n" +
            $"STDOUT:\n{compileStdOut}\nSTDERR:\n{compileStdErr}");
    }
}
