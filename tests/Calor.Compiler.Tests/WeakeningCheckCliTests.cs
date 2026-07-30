using System.Text.Json;
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
        §M{m001:Quotes}
          §F{f003:QuoteWithSurcharge:pub} (i32:baseAmount, i32:surcharge, i32:cap) -> i32
            §S (&& (<= result cap) (>= result 0))
            §B{total:i32} (+ baseAmount surcharge)
            §IF{if1} (> total cap)
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
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private (int ExitCode, JsonElement? Json, string StdOut, string StdErr) RunCheck(
        string frozenPath, string finalPath, string declarationId)
    {
        var (exit, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir, "verify", frozenPath, finalPath, "--weakening-check", declarationId);
        JsonElement? json = null;
        var line = stdOut.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith('{'));
        if (line != null)
        {
            json = JsonDocument.Parse(line).RootElement.Clone();
        }
        return (exit, json, stdOut, stdErr);
    }

    private static bool SolverUnavailable(JsonElement json)
        => json.GetProperty("reason").GetString() == "solver unavailable";

    [Fact]
    public void IdenticalContracts_NotWeakened()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource);

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.False(json.Value.GetProperty("weakened").GetBoolean());
        Assert.False(json.Value.GetProperty("indeterminate").GetBoolean());
        Assert.Equal("Proven", json.Value.GetProperty("forward").GetString());
        Assert.Equal("Proven", json.Value.GetProperty("backward").GetString());
    }

    [Fact]
    public void DroppedConjunct_Weakened()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr",
            FrozenSource.Replace("§S (&& (<= result cap) (>= result 0))", "§S (<= result cap)"));

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.Equal("Proven", json.Value.GetProperty("forward").GetString());
        Assert.Equal("Disproven", json.Value.GetProperty("backward").GetString());
    }

    [Fact]
    public void LiteralBoundRelaxed_Weakened()
    {
        var frozen = WriteFile("frozen.calr",
            FrozenSource.Replace("§S (&& (<= result cap) (>= result 0))", "§S (<= result 100)"));
        var final_ = WriteFile("final.calr",
            FrozenSource.Replace("§S (&& (<= result cap) (>= result 0))", "§S (<= result 200)"));

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
    }

    [Fact]
    public void StrengthenedContract_NotWeakened()
    {
        var frozen = WriteFile("frozen.calr",
            FrozenSource.Replace("§S (&& (<= result cap) (>= result 0))", "§S (<= result cap)"));
        var final_ = WriteFile("final.calr", FrozenSource);

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.False(json.Value.GetProperty("weakened").GetBoolean());
        Assert.False(json.Value.GetProperty("indeterminate").GetBoolean());
    }

    [Fact]
    public void FinalContractSetEmpty_WeakenedByRule()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr",
            string.Join('\n', FrozenSource.Split('\n').Where(l => !l.Contains("§S "))));

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        // Solver-independent by-rule verdict — asserted even on Z3-less CI.
        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.False(json.Value.GetProperty("indeterminate").GetBoolean());
    }

    [Fact]
    public void DeclarationRenamed_WeakenedByRule()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource.Replace("f003", "f999"));

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.Contains("renamed or removed", json.Value.GetProperty("reason").GetString());
    }

    [Fact]
    public void SignatureChanged_WeakenedByRule()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource.Replace("i32:cap", "i64:cap"));

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.Contains("signature changed", json.Value.GetProperty("reason").GetString());
    }

    [Fact]
    public void UnparseableFinal_WeakenedByRule()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", "§M{m001:Quotes}\n  §F{f003:Broken:pub} (i32:a) -> i32\n    §R (+ a\n");

        var (exit, json, _, _) = RunCheck(frozen, final_, "f003");

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
    }

    [Fact]
    public void DeclarationMissingFromFrozen_InvocationError()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource);

        var (exit, _, _, stdErr) = RunCheck(frozen, final_, "nonexistent");

        Assert.Equal(2, exit);
        Assert.Contains("not found in frozen", stdErr);
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
