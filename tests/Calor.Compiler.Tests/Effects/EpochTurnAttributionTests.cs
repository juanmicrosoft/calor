using System.Diagnostics;
using System.Text.Json;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.16 S1 step 3 / W4 — turn-gap attribution (roadmap §2.1, §3.1 W4, §5 gate 12).
/// The instrument is <c>bench/phase0-agent-native/ppe1-turn-attribution.py</c>; the
/// committed artifact is <c>bench/phase0-agent-native/ppe1-turn-attribution.json</c>,
/// compared here by exact equality to a fresh recomputation over the archive.
///
/// <para><b>Denominator</b> (gate 12): every entry under
/// <c>bench/phase0-agent-native/epochs/</c> is either analyzed (the PP-E1 / PP-W5
/// <c>result.json</c> shape) or skipped by name with a reason.
/// <b>Discriminating pin</b> (gate 12, W4): delete one archived run and the
/// recomputation no longer equals the committed file.</para>
///
/// <para>The arithmetic itself (N:S1.1 census, N:S1.2 medians, both permutation
/// statistics, the sensitivity line) is pinned number-by-number in
/// <c>bench/phase0-agent-native/tests/test_ppe1_turn_attribution.py</c>; this
/// class is the C#-lane observation of the committed bytes, the denominator,
/// the mutation, and the <c>--transcripts</c> mode.</para>
/// </summary>
public class EpochTurnAttributionTests
{
    private const string ScriptRelativePath = "bench/phase0-agent-native/ppe1-turn-attribution.py";
    private const string ArtifactRelativePath = "bench/phase0-agent-native/ppe1-turn-attribution.json";
    private const string EpochsRelativePath = "bench/phase0-agent-native/epochs";
    private const string TranscriptFixturesRelativePath = "bench/phase0-agent-native/tests/fixtures/transcripts";

    private static string RepoRoot() => PpE1Probe.RepositoryRoot();

    private static string Rel(string relative)
        => Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Committed()
        => File.ReadAllText(Rel(ArtifactRelativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void CommittedAttributionEqualsFreshRecomputation()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "calor-turn-attribution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var outPath = Path.Combine(tmp, "recomputed.json");
            var (exit, log) = RunScript("--out", outPath);
            Assert.True(exit == 0, "ppe1-turn-attribution.py failed:\n" + log);

            var recomputed = File.ReadAllText(outPath).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.True(recomputed == Committed(),
                "bench/phase0-agent-native/ppe1-turn-attribution.json is stale or the archive changed. "
                + "Regenerate with `python3 " + ScriptRelativePath + "` and name the cause of every delta "
                + "in the PR body (gate 12: the denominator is every archived epoch).");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void CommittedAttributionCoversEveryEntryUnderEpochs()
    {
        using var document = JsonDocument.Parse(Committed());
        var root = document.RootElement;
        var analyzed = root.GetProperty("analyzedEpochs").EnumerateArray().Select(e => e.GetString()!).ToList();
        var skipped = root.GetProperty("skipped").EnumerateArray()
            .Select(e => (Name: e.GetProperty("epoch").GetString()!, Reason: e.GetProperty("reason").GetString()!))
            .ToList();

        var entries = Directory.EnumerateFileSystemEntries(Rel(EpochsRelativePath))
            .Select(Path.GetFileName)
            .Select(n => n!)
            .Order(StringComparer.Ordinal)
            .ToList();
        var listed = analyzed.Concat(skipped.Select(s => s.Name)).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(entries, listed);
        Assert.Equal(entries.Count, root.GetProperty("entries").GetInt32());
        Assert.All(skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason), s.Name + " skipped without a reason"));
        Assert.Equal(["e1-rows-parity-001", "w5-parity-001", "w5-parity-002"], analyzed);

        // Every analyzed run is in the per-turn table: none archived a transcript yet (W1),
        // and none is silently dropped.
        foreach (var epoch in root.GetProperty("epochs").EnumerateArray())
        {
            var perTurn = epoch.GetProperty("perTurn");
            Assert.Equal(epoch.GetProperty("runs").GetInt32(), perTurn.GetProperty("runs").GetInt32());
            Assert.Equal(0, perTurn.GetProperty("withTranscript").GetInt32());
            Assert.Equal(epoch.GetProperty("runs").GetInt32(), perTurn.GetProperty("noTranscript").GetArrayLength());
        }
    }

    /// <summary>
    /// Gate 12's pin, executed as a mutation: an epochs root identical to the archive
    /// except for one deleted run recomputes to something other than the committed file.
    /// </summary>
    [Fact]
    public void DeletingOneArchivedRun_RecomputationDiffersFromCommitted()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "calor-turn-attribution-mut-" + Guid.NewGuid().ToString("N"));
        var epochsRoot = Path.Combine(tmp, "epochs");
        Directory.CreateDirectory(epochsRoot);
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(Rel(EpochsRelativePath)))
            {
                var name = Path.GetFileName(entry);
                var target = Path.Combine(epochsRoot, name);
                if (name == "e1-rows-parity-001")
                {
                    CopyDirectory(entry, target);
                }
                else if (Directory.Exists(entry))
                {
                    Directory.CreateSymbolicLink(target, entry);
                }
                else
                {
                    File.Copy(entry, target);
                }
            }

            var victim = Path.Combine(epochsRoot, "e1-rows-parity-001", "N1-003-csv-row", "calor+v0.14.3", "run-2");
            Assert.True(Directory.Exists(victim), "the archived run this pin deletes is missing: " + victim);
            Directory.Delete(victim, recursive: true);

            var outPath = Path.Combine(tmp, "mutated.json");
            var (exit, log) = RunScript("--epochs-root", epochsRoot, "--out", outPath);
            Assert.True(exit == 0, "ppe1-turn-attribution.py failed on the mutated root:\n" + log);

            var mutated = File.ReadAllText(outPath).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.NotEqual(Committed(), mutated);

            using var document = JsonDocument.Parse(mutated);
            var e1 = document.RootElement.GetProperty("epochs").EnumerateArray()
                .Single(e => e.GetProperty("epoch").GetString() == "e1-rows-parity-001");
            Assert.Equal(39, e1.GetProperty("runs").GetInt32());
            Assert.Equal(19, e1.GetProperty("validRuns").GetProperty("control").GetInt32());
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    /// <summary>
    /// W4's per-turn table (built now, populated once W1 archives transcripts): the
    /// <c>--transcripts</c> mode tabulates <c>turns.assistantMessages</c> and the six
    /// tool classes per run and per arm, lists runs without a transcript as
    /// <c>noTranscript</c>, and renders markdown.
    /// </summary>
    [Fact]
    public void TranscriptsMode_TabulatesFixturesAndListsRunsWithoutTranscript()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "calor-turn-attribution-tx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var outPath = Path.Combine(tmp, "table.json");
            var mdPath = Path.Combine(tmp, "table.md");
            var (exit, log) = RunScript("--transcripts", Rel(TranscriptFixturesRelativePath), "--out", outPath, "--markdown", mdPath);
            Assert.True(exit == 0, "ppe1-turn-attribution.py --transcripts failed:\n" + log);

            using var document = JsonDocument.Parse(File.ReadAllText(outPath));
            var table = document.RootElement;
            Assert.Equal(4, table.GetProperty("runs").GetInt32());
            Assert.Equal(3, table.GetProperty("withTranscript").GetInt32());
            Assert.Equal(new[] { "W-001-demo/calor+control/run-2" },
                table.GetProperty("noTranscript").EnumerateArray().Select(e => e.GetString()!).ToArray());

            var basic = table.GetProperty("byRun").EnumerateArray()
                .Single(r => r.GetProperty("directory").GetString() == "W-001-demo/calor+treatment/run-1");
            Assert.Equal(7, basic.GetProperty("turns").GetProperty("assistantMessages").GetInt32());
            var tools = basic.GetProperty("toolCalls");
            Assert.Equal((1, 2, 3, 1, 2, 1), (
                tools.GetProperty("Read").GetInt32(),
                tools.GetProperty("Grep").GetInt32(),
                tools.GetProperty("Bash-build").GetInt32(),
                tools.GetProperty("Bash-other").GetInt32(),
                tools.GetProperty("Edit").GetInt32(),
                tools.GetProperty("other").GetInt32()));

            var subagent = table.GetProperty("byRun").EnumerateArray()
                .Single(r => r.GetProperty("directory").GetString() == "W-001-demo/calor+treatment/run-2");
            Assert.Equal(6, subagent.GetProperty("turns").GetProperty("assistantMessages").GetInt32());
            Assert.Equal(3, subagent.GetProperty("turns").GetProperty("subagentMessages").GetInt32());

            var markdown = File.ReadAllText(mdPath);
            Assert.Contains("| W-001-demo/calor+control/run-2 | noTranscript |", markdown, StringComparison.Ordinal);
            Assert.Contains("| calor+treatment | 2 | 2 | 13 | 3 |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    private static (int Exit, string Log) RunScript(params string[] args)
    {
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Rel(ScriptRelativePath));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["LC_ALL"] = "C";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";

        using var process = Process.Start(start);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
