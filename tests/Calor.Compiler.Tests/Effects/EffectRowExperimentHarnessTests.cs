using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
/// output byte-for-byte against explicitly registered active transcript mirrors. The scripts
/// render structured diagnostics in the archived layout, preserving full messages
/// without making the old layout a contract for today's default text output.
/// Original scripts and transcripts remain historical inputs. Active copies use ID-free
/// source and replace retired Option/Result probes with nullable/throw probes.</para>
///
/// <para><b>Regenerating.</b> After an intentional compiler change, review the diff,
/// update only the registered current copies, not the historical design doc's quoted output.
/// Setting <c>CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS=1</c> makes this test
/// rewrite only the active transcripts instead of asserting — the same discipline as
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
    /// P29 — every current probe still produces its explicitly registered output.
    /// </summary>
    [Fact]
    public void ExperimentTranscripts_MatchARerun()
    {
        var experiments = ExperimentsDirectory();
        var root = RepositoryRoot();
        var regenerate =
            Environment.GetEnvironmentVariable("CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS") == "1";

        var drifted = new List<string>();
        foreach (var script in Scripts)
        {
            var original = Path.GetRelativePath(root, Path.Combine(experiments, script)).Replace('\\', '/');
            var (activeScript, transcript) = ActiveSyntaxInputs.ForCurrentExecutable(root, original);
            var actual = RunScript(experiments, activeScript);

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
            "The registered current effect-row probes no longer reproduce. "
            + "Either the compiler changed (review the active scripts and transcripts together) "
            + "or the harness broke.\n  "
            + string.Join("\n  ", drifted)
            + "\n\nRegenerate only this test's active behavior transcripts with "
            + "CALOR_REGENERATE_EXPERIMENT_TRANSCRIPTS=1.");
    }

    [Fact]
    public void ActiveTranscripts_RetainHistoricalCasesAndRegisteredCurrentDifferences()
    {
        var replacements = new Dictionary<string, string[]>
        {
            ["run.py"] = ["X4-fallible-type-suffix"],
            ["run2.py"] = ["Y2a-fallible-brace-form", "Y2b-fallible-in-B"],
            ["run3.py"] = ["Z11-fallible-brace-form-full-output"]
        };
        var root = RepositoryRoot();
        foreach (var script in Scripts)
        {
            var original = Path.Combine(ExperimentsDirectory(), script);
            var relative = Path.GetRelativePath(root, original).Replace('\\', '/');
            var (_, activeTranscript) = ActiveSyntaxInputs.ForCurrentExecutable(root, relative);
            var historical = TranscriptCases(File.ReadAllText(Path.Combine(
                ExperimentsDirectory(), "transcripts", Path.ChangeExtension(script, ".txt"))));
            var active = TranscriptCases(File.ReadAllText(activeTranscript));
            Assert.NotEmpty(historical);
            Assert.Equal(historical.Keys.Order(StringComparer.Ordinal), active.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, expected) in historical)
            {
                if (replacements[script].Contains(name, StringComparer.Ordinal))
                    Assert.NotEqual(expected, active[name]);
                else if (script == "run.py" && name == "X3b-bang-sigil-mixed")
                {
                    const string historicalMessage = "Unknown effect code '! e'.";
                    const string currentMessage = "Unknown effect code '!e'.";
                    Assert.Contains(historicalMessage, expected);
                    Assert.Equal(ProjectSourceIdentity(
                        expected.Replace(historicalMessage, currentMessage, StringComparison.Ordinal)), active[name]);
                }
                else if (script == "run3.py" && name is "Z8b-LAM-typeparam-list" or "Z10-E-inside-call-arguments")
                {
                    var projected = ProjectSourceIdentity(expected);
                    var (before, after) = name == "Z8b-LAM-typeparam-list"
                        ? (
                            "Z8b-LAM-typeparam-list.calr(4,18): error Calor0100: Expected statement but found Identifier\n"
                                + "Z8b-LAM-typeparam-list.calr(4,19): error Calor0100: Expected statement but found Greater",
                            "Z8b-LAM-typeparam-list.calr(4,21): error Calor0100: Expected operand after '>' but found Return\n"
                                + "Z8b-LAM-typeparam-list.calr(4,18): error Calor0100: A native expression statement must be an invocation.")
                        : (
                            "Z10-E-inside-call-arguments.calr(7,31): error Calor0100: Expected statement but found Identifier",
                            "Z10-E-inside-call-arguments.calr(7,31): error Calor0100: A native expression statement must be an invocation.");
                    Assert.Contains("exit: 1", projected, StringComparison.Ordinal);
                    Assert.Contains(before, projected, StringComparison.Ordinal);
                    Assert.Equal(projected.Replace(before, after, StringComparison.Ordinal), active[name]);
                }
                else
                    Assert.Equal(ProjectSourceIdentity(expected), active[name]);
            }
        }
    }

    private static string ProjectSourceIdentity(string transcript)
    {
        // Expected-output metadata only: preserve every exit, diagnostic code/message and C# byte.
        var separator = "\n" + new string('-', 78) + "\n";
        var parts = transcript.Split(separator);
        Assert.Equal(3, parts.Length);
        var originalLines = parts[1].Split('\n');
        static string Project(string text)
        {
            text = Regex.Replace(text, @"§(M|F|CL|MT|IFACE|DEL)\{(?:m|f|c|mt|i|d)\d+:", "§$1{");
            text = Regex.Replace(text, @"§LAM\{lam\d+:", "§LAM{");
            text = Regex.Replace(text, @"§LAM\{lam\d+\}", "§LAM{}");
            return Regex.Replace(text, @"§/LAM\{lam\d+\}", "§/LAM");
        }
        parts[1] = string.Join("\n", originalLines.Select(Project));
        parts[2] = Regex.Replace(parts[2], @"(?<file>\S+\.calr)\((?<line>\d+),(?<column>\d+)\)", match =>
        {
            var line = int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var column = int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(line, 1, originalLines.Length);
            Assert.InRange(column, 1, originalLines[line - 1].Length + 1);
            var projectedColumn = Project(originalLines[line - 1][..(column - 1)]).Length + 1;
            return $"{match.Groups["file"].Value}({line},{projectedColumn})";
        });
        return string.Join(separator, parts);
    }

    private static Dictionary<string, string> TranscriptCases(string text)
    {
        text = Normalize(text);
        var matches = Regex.Matches(text, @"(?m)^CASE (\S+)");
        return matches.Select((match, index) => (
            Name: match.Groups[1].Value,
            Body: text[match.Index..(index + 1 < matches.Count ? matches[index + 1].Index : text.Length)]))
            .ToDictionary(entry => entry.Name, entry => entry.Body, StringComparer.Ordinal);
    }

    private static string RunScript(string experiments, string script)
    {
        var work = Path.Combine(RepositoryRoot(), "src", "Calor.Compiler", "bin",
            "effect-row-experiments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var start = new ProcessStartInfo("python3")
        {
            WorkingDirectory = experiments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        start.Environment["CALOR_DLL"] = CompilerDll();
        start.Environment["CALOR_EXPERIMENT_ORIGINAL_DIRECTORY"] = experiments;
        start.Environment["CALOR_EXPERIMENT_WORKDIR"] = work;
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";

        try
        {
            using var process = Process.Start(start);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"Experiment script failed: {script}\n{stderr}");
            return stdout + stderr;
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
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
