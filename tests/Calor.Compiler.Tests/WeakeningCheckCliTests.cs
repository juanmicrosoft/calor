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

    [Fact]
    public void IdenticalContracts_NotWeakened()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource);

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.False(json.Value.GetProperty("weakened").GetBoolean());
        Assert.False(json.Value.GetProperty("indeterminate").GetBoolean());
        Assert.Equal("Proven", json.Value.GetProperty("forward").GetString());
        Assert.Equal("Proven", json.Value.GetProperty("backward").GetString());
        Assert.True(json.Value.GetProperty("intactOrStrengthened").GetBoolean());
    }

    [Fact]
    public void DroppedConjunct_Weakened()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr",
            FrozenSource.Replace("§S (&& (<= result cap) (>= result 0))", "§S (<= result cap)"));

        var (exit, json, _, _) = RunCheck(frozen, final_);

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

        var (exit, json, _, _) = RunCheck(frozen, final_);

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

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.False(json.Value.GetProperty("weakened").GetBoolean());
        Assert.False(json.Value.GetProperty("indeterminate").GetBoolean());
        Assert.True(json.Value.GetProperty("intactOrStrengthened").GetBoolean());
    }

    // #826 review C2: adding a §Q where the frozen contract has none is the
    // canonical prover-appeasement move (restrict inputs until the refutation
    // disappears, body unchanged) — it MUST score weakened.
    [Fact]
    public void AddedPrecondition_Weakened()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource.Replace(
            "§S (&& (<= result cap) (>= result 0))",
            "§Q (<= (+ baseAmount surcharge) cap)\n    §S (&& (<= result cap) (>= result 0))"));

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.Equal("Disproven", json.Value.GetProperty("qForward").GetString());
        Assert.Equal("Proven", json.Value.GetProperty("qBackward").GetString());
    }

    // #826 review C2: reclassifying a §S as §Q deletes the guarantee — the
    // §S leg sees an emptied set and must score weakened.
    [Fact]
    public void PostconditionReclassifiedAsPrecondition_Weakened()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource.Replace(
            "§S (&& (<= result cap) (>= result 0))",
            "§Q (&& (<= result cap) (>= result 0))"));

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
    }

    // #826 review M3: a gutted, incomparable contract is not weakened under
    // the one-way asymmetry rule, but it is NOT intact-or-strengthened —
    // PP-G3 leg-b adjudicates on this field, not on !weakened.
    [Fact]
    public void GuttedIncomparableContract_NotWeakenedButNotIntact()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource.Replace(
            "§S (&& (<= result cap) (>= result 0))", "§S (>= result 5)"));

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return; // Z3-less CI
        Assert.False(json.Value.GetProperty("weakened").GetBoolean());
        Assert.False(json.Value.GetProperty("intactOrStrengthened").GetBoolean());
    }

    // #780: wide literals retain their executable i64 width, so weakening
    // checks can compare them without the old BV32 truncation.
    [Fact]
    public void WideLiteral_WeakeningIsDeterminate()
    {
        const string bigFrozen = """
            §M{Big}
              §F{B:pub} (i64:a) -> i64
                §S (&& (>= result 0) (<= result 4000000000))
                §R a
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
    public void FinalContractSetEmpty_WeakenedByRule()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr",
            string.Join('\n', FrozenSource.Split('\n').Where(l => !l.Contains("§S "))));

        var (exit, json, _, _) = RunCheck(frozen, final_);

        // Solver-independent by-rule verdict — asserted even on Z3-less CI.
        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.False(json.Value.GetProperty("indeterminate").GetBoolean());
    }

    [Fact]
    public void DeclarationIdentityReplaced_WeakenedByRule()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource);

        var (exit, json, _, _) = RunCheck(frozen, final_, preserveIdentity: false);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.Contains("renamed or removed", json.Value.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdentityContinuousSnapshot_IgnoresParserOrderAndNameChanges(bool rename)
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var finalSource = rename
            ? FrozenSource.Replace("QuoteWithSurcharge", "RenamedQuote", StringComparison.Ordinal)
            : FrozenSource.Replace("  §F{QuoteWithSurcharge", """
                  §F{Earlier:priv} () -> void
                    §E{}
                  §F{QuoteWithSurcharge
                """, StringComparison.Ordinal);
        var final_ = WriteFile("final.calr", finalSource);

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        if (SolverUnavailable(json.Value)) return;
        Assert.False(json.Value.GetProperty("weakened").GetBoolean());
        Assert.True(json.Value.GetProperty("intactOrStrengthened").GetBoolean());
    }

    [Fact]
    public void ParserLocalIdentity_IsNotADeclarationSelector()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource);
        var module = Program.Compile(FrozenSource).Ast!;
        var localId = Assert.Single(module.Functions).LocalId;

        var (exit, json, _, stdErr) = RunCheck(frozen, final_, localId);

        Assert.Equal(2, exit);
        Assert.Null(json);
        Assert.Contains("not found in frozen", stdErr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptSnapshotIdentity_IsAnInvocationError(bool corruptFrozen)
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource);
        var state = new DeclarationIdentityStore(Path.GetDirectoryName(frozen)!).Sync([frozen]);
        var id = Assert.Single(state.Declarations, declaration => declaration.Kind == IdKind.Function).Id;
        DeclarationIdentityStore.CopyToSnapshot(new Dictionary<string, string> { [frozen] = final_ });
        var sidecar = DeclarationIdentityStore.Find(corruptFrozen ? frozen : final_)!.SidecarPath;
        File.WriteAllText(sidecar, "{ broken");

        var (exit, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir, "verify", frozen, final_, "--weakening-check", id);

        Assert.Equal(2, exit);
        Assert.True(string.IsNullOrWhiteSpace(stdOut), stdOut);
        Assert.Contains("declaration identity unavailable", stdErr);
        Assert.Equal("{ broken", File.ReadAllText(sidecar));
    }

    [Fact]
    public void AmbiguousSnapshotIdentity_IsAnInvocationErrorInsteadOfAGuess()
    {
        var frozenSource = FrozenSource + "\n"
            + FrozenSource[FrozenSource.IndexOf("  §F{", StringComparison.Ordinal)..]
                .Replace("QuoteWithSurcharge", "OtherQuote", StringComparison.Ordinal);
        var frozen = WriteFile("frozen.calr", frozenSource);
        var final_ = WriteFile("final.calr",
            frozenSource.Replace("QuoteWithSurcharge", "RenamedOne", StringComparison.Ordinal)
                .Replace("OtherQuote", "RenamedTwo", StringComparison.Ordinal));
        var state = new DeclarationIdentityStore(Path.GetDirectoryName(frozen)!).Sync([frozen]);
        var id = Assert.Single(state.Declarations, declaration => declaration.Name == "QuoteWithSurcharge").Id;
        DeclarationIdentityStore.CopyToSnapshot(new Dictionary<string, string> { [frozen] = final_ });

        var (exit, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir, "verify", frozen, final_, "--weakening-check", id);

        Assert.Equal(2, exit);
        Assert.True(string.IsNullOrWhiteSpace(stdOut), stdOut);
        Assert.Contains("declaration identity unavailable", stdErr);
        Assert.Contains("ambiguous", stdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignatureChanged_WeakenedByRule()
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", FrozenSource.Replace("i32:cap", "i64:cap"));

        var (exit, json, _, _) = RunCheck(frozen, final_);

        Assert.Equal(0, exit);
        Assert.NotNull(json);
        Assert.True(json.Value.GetProperty("weakened").GetBoolean());
        Assert.Contains("signature changed", json.Value.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnparseableFinal_WeakenedByRule(bool fatalLexerError)
    {
        var frozen = WriteFile("frozen.calr", FrozenSource);
        var final_ = WriteFile("final.calr", fatalLexerError
            ? "§M{Quotes}\n  §F{Broken:pub} () -> str\n    §R \"unterminated"
            : "§M{Quotes}\n  §F{Broken:pub} (i32:a) -> i32\n    §R (+ a\n");

        var (exit, json, _, _) = RunCheck(frozen, final_);

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
