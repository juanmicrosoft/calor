using System.Text.Json;
using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// CLI tests for <c>calor verify --weakening-check</c> (guarantees plan G5,
/// gates doc Annex A-1.3 instrumentation item 5): the M-G4 mechanical
/// contract-weakening check over a frozen/final source pair. Structural
/// verdicts (deleted contracts, renamed declaration, signature change,
/// invocation errors) are solver-independent and asserted unconditionally;
/// solver-backed verdicts tolerate the Z3-less-CI "solver unavailable"
/// indeterminate per the A-1.3 rule (indeterminate is never a weakened
/// verdict).
/// </summary>
public class WeakeningCheckCliTests : IDisposable
{
    private readonly string _tempDir;

    private const string FrozenSource = """
        §M{Quotes}
          §F{QuoteWithSurcharge:pub} (i32:baseAmount, i32:surcharge, i32:cap) -> i32
            §S (&& (<= result cap) (>= result 0))
            §B{total:i32} (+ baseAmount surcharge)
            §IF (> total cap)
              §R cap
            §R total
        """;

    public WeakeningCheckCliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-weakening-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteFile(string name, string content)
    {
        var directory = Path.Combine(_tempDir, Path.GetFileNameWithoutExtension(name));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.calr");
        File.WriteAllText(path, content);
        return path;
    }

    private (int ExitCode, JsonElement? Json, string StdOut, string StdErr) RunCheck(
        string frozenPath, string finalPath, string? declarationId = null, bool preserveIdentity = true)
    {
        var state = new DeclarationIdentityStore(Path.GetDirectoryName(frozenPath)!).Sync([frozenPath]);
        declarationId ??= Assert.Single(state.Declarations, declaration => declaration.Kind == IdKind.Function).Id;
        if (preserveIdentity)
            DeclarationIdentityStore.CopyToSnapshot(new Dictionary<string, string> { [frozenPath] = finalPath });
        var (exit, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir, "verify", frozenPath, finalPath, "--weakening-check", declarationId);
        JsonElement? json = null;
        var line = stdOut.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith('{'));
        if (line != null)
        {
            json = JsonDocument.Parse(line).RootElement.Clone();
            Assert.Equal(declarationId, json.Value.GetProperty("declaration").GetString());
        }
        return (exit, json, stdOut, stdErr);
    }

    private static bool SolverUnavailable(JsonElement json)
        => json.GetProperty("reason").GetString() == "solver unavailable";

    // #780: wide literals retain their executable i64 width, so weakening
    // checks can compare them without the old BV32 truncation.
    [Fact]
    public void WideLiteral_WeakeningIsDeterminate()
    {
        const string bigFrozen = """
            module Big Big block
              func B (long:a) → long
                !
                => ((result >= 0) && (result <= 4000000000L))
                return a
            """;
        var frozen = WriteFile("frozen.calr", bigFrozen);
        var final_ = WriteFile("final.calr", bigFrozen.Replace("4000000000", "8000000000"));

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.False(json.Value.GetProperty("indeterminate").GetBoolean());
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
    }

    [Fact]
    public void WrongFileCount_InvocationError()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);

        var (exit, _, stdErr) = CliTestHarness.RunCli(
            _tempDir, "verify", frozen, "--weakening-check", "f003");

        Assert.Equal(2, exit);
        Assert.Contains("exactly two files", stdErr);
    }
}
