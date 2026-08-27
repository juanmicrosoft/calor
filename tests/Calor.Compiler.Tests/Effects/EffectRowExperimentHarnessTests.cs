using System.Diagnostics;
using System.Text;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 §4.1 — the effect-rows design doc's experiment harness, pinned.
///
/// <para><c>docs/design/effect-rows-in-the-type-system.md</c> quotes ~40 executed
/// compiler outputs (the X/Y/Z cases) as its evidence that a syntax or diagnostic
/// claim about TODAY's compiler is true. Round 2 of the design-doc critique found
/// that those outputs were reproducible but <b>unobserved</b>: nothing re-ran them,
/// so a compiler change — a Calor0410 reword, <c>§FLD</c> starting to accept
/// <c>§E</c>, the Calor0420 permissive demotion being removed — would silently make
/// the design doc false while every test stayed green. That is the exact failure
/// mode the doc criticises Draft v1 for.</para>
///
/// <para>This test closes it. It re-runs every harness script and compares the
/// output byte-for-byte against the transcripts committed beside them. A drift is
/// a red test naming the script, not a stale document.</para>
///
/// <para><b>Regenerating.</b> After an intentional compiler change, run
/// <c>python3 docs/design/spikes/effect-rows/experiments/regenerate-transcripts.py</c>,
/// review the diff, update the design doc's quoted output, and commit both in the
/// same PR. Setting <c>CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS=1</c> makes this test
/// rewrite the transcripts instead of asserting — the same discipline as
/// <c>CALOR_REGENERATE_S5_LEDGER</c> for the metadata ledger.</para>
///
/// <para>Design doc: §13.2 pin <b>P29</b> (transcripts) and <b>P30</b>
/// (<c>o53/baseline.json</c> shape). Freeze point: before E2 merges.</para>
/// </summary>
public sealed class EffectRowExperimentHarnessTests
{
    private static readonly string[] Scripts =
        ["run.py", "run2.py", "run3.py", "facts.py", "facts2.py", "compile53.py"];

    /// <summary>
    /// P29 — every quoted experiment still produces the output the design doc quotes.
    /// </summary>
    [Fact]
    public void ExperimentTranscripts_MatchARerun()
    {
        var experiments = ExperimentsDirectory();
        var regenerate =
            Environment.GetEnvironmentVariable("CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS") == "1";

        var drifted = new List<string>();
        foreach (var script in Scripts)
        {
            var transcript = Path.Combine(experiments, "transcripts",
                Path.ChangeExtension(script, ".txt"));
            var actual = RunScript(experiments, script);

            if (regenerate)
            {
                File.WriteAllText(transcript, actual);
                continue;
            }

            Assert.True(File.Exists(transcript),
                $"Missing transcript for {script}. Regenerate with " +
                "CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS=1.");

            var expected = File.ReadAllText(transcript);
            if (!string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal))
            {
                drifted.Add($"{script}: {FirstDifference(Normalize(expected), Normalize(actual))}");
            }
        }

        Assert.True(drifted.Count == 0,
            "The effect-rows design doc quotes compiler output that no longer reproduces. "
            + "Either the compiler changed (update the doc AND the transcripts in the same PR) "
            + "or the harness broke.\n  "
            + string.Join("\n  ", drifted)
            + "\n\nRegenerate: python3 docs/design/spikes/effect-rows/experiments/"
            + "regenerate-transcripts.py");
    }

    /// <summary>
    /// P30 — <c>o53/baseline.json</c> is gate 5's named instrument, so it meets the
    /// bar the design doc sets for its own ledgers: a schema version, a full commit
    /// SHA, and the counts §3.2 quotes. (The SHA records when the measurement was
    /// taken; like the demand ledger it is shape-checked, not compared to HEAD.)
    /// </summary>
    [Fact]
    public void O53Baseline_HasLedgerShape_AndTheCountsTheDocQuotes()
    {
        var path = Path.Combine(ExperimentsDirectory(), "o53", "baseline.json");
        Assert.True(File.Exists(path), $"Missing gate-5 baseline ledger: {path}");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

        var sha = root.GetProperty("measuredCommit").GetString();
        Assert.True(sha is { Length: 40 } && sha.All(Uri.IsHexDigit),
            $"measuredCommit must be a 40-hex commit SHA, was '{sha}'.");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("scope").GetString()));

        // The four numbers §3.2 and §9 quote — 23 / 54 / 1 / 22 when the doc measured
        // them. Changing any of them changes the design doc's "zero corpus occurrences
        // whose meaning changes" argument, so a move is named, never absorbed. PP-E1
        // leg B (epoch e1-rows-parity-001, design §13.5) archived 40 agent-written
        // final-src/*.calr; exactly one of them (N1-003 / calor+0.15.0 / run-5 /
        // CsvRow.calr) writes the canonical two-line §O/§E form, three times, and
        // compiles green — the form whose meaning the line rule does NOT change. Hence
        // 24 / 57 / 2 / 22: one more file, three more occurrences, one more green file,
        // and the 22 already-red files untouched.
        Assert.Equal(24, root.GetProperty("fileCount").GetInt32());
        Assert.Equal(57, root.GetProperty("occurrenceCount").GetInt32());
        Assert.Equal(2, root.GetProperty("compileGreen").GetInt32());
        Assert.Equal(22, root.GetProperty("compileRed").GetInt32());

        var files = root.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(24, files.Length);
        Assert.Equal(
            57,
            files.Sum(entry => entry.GetProperty("twoLineOE").GetInt32()));

        // §3.2's breakdown of the 22 already-red files: 18 bench/mcp + 3
        // benchmarks/security + 1 lint error fixture, none of them effect-related.
        var red = files.Where(entry => entry.GetProperty("exit").GetInt32() != 0).ToArray();
        Assert.Equal(22, red.Length);
        Assert.Equal(18, red.Count(e => FilePath(e).StartsWith("bench/mcp/", StringComparison.Ordinal)));
        Assert.Equal(3, red.Count(e => FilePath(e).StartsWith("benchmarks/", StringComparison.Ordinal)));

        var green = files
            .Where(entry => entry.GetProperty("exit").GetInt32() == 0)
            .Select(FilePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedGreen =
        [
            "bench/phase0-agent-native/epochs/e1-rows-parity-001/N1-003-csv-row/calor+0.15.0/run-5/final-src/CsvRow.calr",
            "tests/E2E/agent-tasks/fixtures/collections-project/Collections.calr",
        ];
        Assert.Equal(expectedGreen, green);

        static string FilePath(System.Text.Json.JsonElement entry)
            => entry.GetProperty("file").GetString() ?? "";
    }

    private static string RunScript(string experiments, string script)
    {
        var start = new ProcessStartInfo("python3", script)
        {
            WorkingDirectory = experiments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["CALOR_DLL"] = CompilerDll();

        using var process = Process.Start(start);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return stdout + stderr;
    }

    /// <summary>
    /// The harness shells out to the real CLI, so it needs the compiler's own
    /// executable output (with its runtimeconfig), not the copy the test project
    /// references. <c>dotnet test</c> builds it as a project dependency, so it is
    /// present whenever this test runs; a missing one is a hard failure rather than
    /// a silent skip, because a skipped evidence pin is how Draft v1's fabricated
    /// quotations survived.
    /// </summary>
    private static string CompilerDll()
    {
        var root = RepositoryRoot();
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = System.IO.Path.Combine(
                root, "src", "Calor.Compiler", "bin", configuration, "net10.0", "calor.dll");
            if (File.Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException(
            "calor.dll not found under src/Calor.Compiler/bin/{Debug,Release}/net10.0/. "
            + "Run: dotnet build src/Calor.Compiler");
    }

    private static string ExperimentsDirectory()
        => System.IO.Path.Combine(
            RepositoryRoot(), "docs", "design", "spikes", "effect-rows", "experiments");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(System.IO.Path.Combine(directory.FullName, ".git"))
                || File.Exists(System.IO.Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found above {AppContext.BaseDirectory}.");
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<end of transcript>";
            var a = i < actualLines.Length ? actualLines[i] : "<end of output>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                return new StringBuilder()
                    .Append("first difference at line ").Append(i + 1)
                    .Append("\n      committed: ").Append(Truncate(e))
                    .Append("\n      re-run:    ").Append(Truncate(a))
                    .ToString();
            }
        }

        return "outputs differ only in trailing whitespace";
    }

    private static string Truncate(string value)
        => value.Length <= 160 ? value : value[..160] + "…";
}
