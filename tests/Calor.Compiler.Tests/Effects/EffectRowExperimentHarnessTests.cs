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
/// <para>This test closes it. It re-runs the compiler-behavior scripts and compares the
/// output byte-for-byte against the transcripts committed beside them. A drift is
/// a red test naming the script, not a stale document.</para>
///
/// <para><b>Regenerating.</b> After an intentional compiler change, review the diff,
/// update the design doc's quoted output, and commit both in the same PR.
/// Setting <c>CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS=1</c> makes this test
/// rewrite the transcripts instead of asserting — the same discipline as
/// <c>CALOR_REGENERATE_S5_LEDGER</c> for the metadata ledger.</para>
///
/// <para>The source census and <c>o53/baseline.json</c> are historical measurements,
/// not current behavior contracts. They are not regenerated or compared by this test.</para>
/// </summary>
public sealed class EffectRowExperimentHarnessTests
{
    private static readonly string[] Scripts =
        ["run.py", "run2.py", "run3.py"];

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
            + "\n\nRegenerate this test's behavior transcripts with "
            + "CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS=1.");
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
