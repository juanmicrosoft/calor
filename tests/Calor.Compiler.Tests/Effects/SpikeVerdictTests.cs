using System.Text.Json;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 §4.1 term 1 — the emitter spike's verdict, pinned.
///
/// <para>The effect-rows design doc (§12) freezes three artifacts (A1, A2, A3),
/// a machine-readable verdict (<c>spike-verdict.json</c>) and three pins. This
/// file is those pins:</para>
///
/// <list type="bullet">
///   <item><b>P27</b> <c>SpikeVerdictMatchesRecomputation</c> — recomputes the
///   <c>gCodegen</c> block (via P28) and the <c>ramp.R1</c> leg; shape-checks
///   <c>schemaVersion</c> and <c>measuredCommit</c>; asserts R2 and R3 are
///   present and well-formed but <b>does not re-derive them</b>. Both are
///   judgements about whether a carve-out was needed and whether the solve
///   stayed one-line; a test cannot re-derive either, and saying so is better
///   than implying a machine adjudicates them (§12.3).</item>
///   <item><b>P28</b> <c>GCodegen_BeforeAfterEmittedCSharpIsByteIdentical</c> —
///   the feature-wide blocking gate. Re-emits every artifact's <c>before/</c>
///   and <c>after/</c> <c>.calr</c> and diffs the <c>.g.cs</c>.</item>
///   <item><b>P31</b> <c>SpikeArtifactManifestIsComplete</c> — for every
///   artifact named in the verdict, the source, the emitted C# and the
///   diagnostic list exist, are non-empty, and the diagnostic list parses.
///   P27 alone would pass with every artifact missing (§12.3).</item>
/// </list>
///
/// <para><b>Submodules.</b> A2 is derived from
/// <c>bench/corpus/MediatR/</c>, which <c>git clone</c> does not init. Its
/// <i>sources</i> are committed under <c>docs/design/spikes/effect-rows/</c>,
/// so the A2 legs of all three pins run on a bare clone; only the leg that
/// re-reads the corpus file itself skips, and that skip is registered in
/// <c>eng/test-manifest.json</c>.</para>
/// </summary>
public sealed class SpikeVerdictTests
{
    /// <summary>The artifacts whose before/after pair G-CODEGEN compares.</summary>
    private static readonly string[] PairedArtifacts =
    [
        "A1", "A2", "A3-callback", "A3-map", "A3-match", "A3-middleware",
    ];

    /// <summary>The four A3 combinator fixtures R1 is adjudicated over (§7.5).</summary>
    private static readonly string[] R1Fixtures =
    [
        "A3-callback", "A3-map", "A3-match", "A3-middleware",
    ];

    /// <summary>Codes R1 requires to be absent from every A3 fixture's AFTER form.</summary>
    private static readonly string[] R1ForbiddenCodes = ["Calor0404", "Calor0424", "Calor0425"];

    /// <summary>
    /// P27 — the verdict is read off the file, and the parts a machine CAN
    /// re-derive are re-derived rather than trusted.
    /// </summary>
    [Fact]
    public void SpikeVerdictMatchesRecomputation()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(VerdictPath()));
        var root = document.RootElement;

        // --- shape only, per §12.3 -------------------------------------------
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

        var sha = root.GetProperty("measuredCommit").GetString();
        Assert.True(sha is { Length: 40 } && sha.All(Uri.IsHexDigit),
            $"measuredCommit must be a 40-hex commit SHA, was '{sha}'. "
            + "It records WHEN the spike was measured; like the two existing ledgers it is "
            + "shape-checked, not compared to HEAD.");

        // --- recomputed: the R1 leg ------------------------------------------
        var ramp = root.GetProperty("ramp");
        foreach (var fixture in R1Fixtures)
        {
            var diagnostics = ReadDiagnostics(Path.Combine(SpikeDirectory(), "after", fixture + ".diagnostics.txt"));

            foreach (var code in R1ForbiddenCodes)
            {
                Assert.False(
                    diagnostics.Any(line => line.Contains(code, StringComparison.Ordinal)),
                    $"R1 requires the AFTER form of '{fixture}' to compile with zero {code}, "
                    + $"but its diagnostic list contains one. Recomputed from "
                    + $"after/{fixture}.diagnostics.txt; the verdict claims "
                    + $"'{ramp.GetProperty("R1").GetProperty("verdict").GetString()}'.");
            }
        }

        Assert.Equal("PASS", ramp.GetProperty("R1").GetProperty("verdict").GetString());

        // --- recomputed: the gCodegen block (this IS P28) ---------------------
        var gCodegen = root.GetProperty("gCodegen");
        foreach (var artifact in PairedArtifacts)
        {
            var (strictEqual, nonLineDifferences) = CompareEmittedCSharp(artifact);
            var recorded = gCodegen.GetProperty(artifact);

            Assert.Equal(strictEqual, recorded.GetProperty("strictBytesEqual").GetBoolean());
            Assert.Equal(
                strictEqual ? "PASS" : "PASS-MODULO-LINE-DIRECTIVES",
                recorded.GetProperty("verdict").GetString());
            Assert.Equal(0, nonLineDifferences);
        }

        Assert.Equal("PASS", gCodegen.GetProperty("overall").GetString());

        // --- recorded, NOT recomputed: R2 and R3 ------------------------------
        foreach (var leg in new[] { "R2", "R3" })
        {
            var block = ramp.GetProperty(leg);
            Assert.True(block.GetProperty("recordedNotRecomputed").GetBoolean(),
                $"{leg} is a judgement the spike PR's diff carries, not one a test re-derives. "
                + "Marking it recomputed would be a claim this file cannot honour.");
            Assert.False(string.IsNullOrWhiteSpace(block.GetProperty("claim").GetString()));
            Assert.NotEmpty(block.GetProperty("evidence").EnumerateArray());
            Assert.Contains(block.GetProperty("verdict").GetString(), new[] { "PASS", "FAIL" });
        }

        // ramp.verdict = VALIDATED iff R1 ∧ R2 ∧ R3.
        var validated = new[] { "R1", "R2", "R3" }
            .All(leg => ramp.GetProperty(leg).GetProperty("verdict").GetString() == "PASS");
        Assert.Equal(
            validated ? "VALIDATED" : "NOT-VALIDATED",
            ramp.GetProperty("verdict").GetString());

        // The A3 middleware spelling is the open Major §12.1 says the spike must
        // decide BEFORE freezing A3. Recorded either way, never left blank.
        var spelling = root.GetProperty("a3MiddlewareSpelling");
        Assert.Contains(
            spelling.GetProperty("decision").GetString(),
            new[] { "MEMBER-LEVEL", "CLASS-OR-INTERFACE-LEVEL" });
        Assert.NotEmpty(spelling.GetProperty("evidence").EnumerateArray());
    }

    /// <summary>
    /// P28 — the pin G-CODEGEN never had. Rows are a CHECKING feature, so
    /// adding one must not move a byte of emitted C#. §9's "0 <c>.cs</c>
    /// goldens" and §8.5's "the semantics stamp does not change" both rest on
    /// this being true rather than assumed. If it fails, E2 does not ship —
    /// monomorphic or not.
    ///
    /// <para>A <c>#line N "&lt;source&gt;.calr"</c> directive tracks the SOURCE,
    /// so it necessarily moves when the author adds an annotation line. That is
    /// not a codegen difference, and the test reports the two readings
    /// separately rather than picking the flattering one.</para>
    /// </summary>
    [Fact]
    public void GCodegen_BeforeAfterEmittedCSharpIsByteIdentical()
    {
        var failures = new List<string>();

        foreach (var artifact in PairedArtifacts)
        {
            var (strictEqual, nonLineDifferences) = CompareEmittedCSharp(artifact);
            if (nonLineDifferences != 0)
            {
                failures.Add(
                    $"{artifact}: {nonLineDifferences} emitted C# line(s) differ outside #line "
                    + "directives — a row changed codegen.");
            }
            else if (!strictEqual)
            {
                // Allowed, and named: the only moving bytes are source positions.
                Assert.Equal("A2", artifact);
            }
        }

        Assert.True(failures.Count == 0,
            "G-CODEGEN is a feature-wide BLOCKING gate (design doc §12.2): if a row moves the "
            + "emitted C#, effect rows do not ship.\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// P31 — the presence check. P27 reads one JSON file and would pass with
    /// every <c>.calr</c> and <c>.g.cs</c> missing, which is why v2's decline of
    /// this test was unsound.
    /// </summary>
    [Fact]
    public void SpikeArtifactManifestIsComplete()
    {
        var spike = SpikeDirectory();
        var missing = new List<string>();

        foreach (var artifact in PairedArtifacts)
        {
            foreach (var side in new[] { "before", "after" })
            {
                foreach (var extension in new[] { ".calr", ".g.cs", ".diagnostics.txt" })
                {
                    var path = Path.Combine(spike, side, artifact + extension);
                    if (!File.Exists(path))
                    {
                        missing.Add($"{side}/{artifact}{extension}: missing");
                    }
                    else if (new FileInfo(path).Length == 0)
                    {
                        missing.Add($"{side}/{artifact}{extension}: empty");
                    }
                }

                // The diagnostic list must PARSE, not merely exist: a header
                // stating the exit code, whether the artifact emitted, and how
                // many diagnostics there were, followed by that many lines.
                var diagnosticsPath = Path.Combine(spike, side, artifact + ".diagnostics.txt");
                if (!File.Exists(diagnosticsPath)) continue;

                var lines = File.ReadAllLines(diagnosticsPath);
                if (!lines.Any(l => l.StartsWith("# exit: ", StringComparison.Ordinal))
                    || !lines.Any(l => l.StartsWith("# emitted: ", StringComparison.Ordinal))
                    || !lines.Any(l => l.StartsWith("# diagnostics: ", StringComparison.Ordinal)))
                {
                    missing.Add($"{side}/{artifact}.diagnostics.txt: header does not parse");
                    continue;
                }

                var declared = int.Parse(
                    lines.First(l => l.StartsWith("# diagnostics: ", StringComparison.Ordinal))
                        ["# diagnostics: ".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
                var actual = ReadDiagnostics(diagnosticsPath).Count;
                if (declared != actual)
                {
                    missing.Add(
                        $"{side}/{artifact}.diagnostics.txt: header says {declared} diagnostics, "
                        + $"file holds {actual}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "The spike's frozen artifacts are incomplete. Regenerate with "
            + "`python3 docs/design/spikes/effect-rows/experiments/spike_artifacts.py`.\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The A2 corpus subject itself. Committed under
    /// <c>docs/design/spikes/effect-rows/</c>, this leg re-reads the file the
    /// design doc pins so a submodule bump that changes it is caught. Skips
    /// cleanly without submodules — the <c>BinderIncompleteRatchetTests</c>
    /// pattern, registered in <c>eng/test-manifest.json</c>.
    /// </summary>
    [SkippableFact]
    public void A2CorpusSubject_IsThePinnedTwentyNineLineFile()
    {
        var subject = Path.Combine(
            RepositoryRoot(),
            "bench", "corpus", "MediatR", "src", "MediatR", "Pipeline",
            "RequestPreProcessorBehavior.cs");

        Skip.IfNot(File.Exists(subject), "corpus submodules not initialized");

        // 29 by `grep -c ''` / `awk 'END{print NR}'`; `wc -l` reports 28 because
        // the final line is unterminated (design doc §12.1, §14.1).
        var text = File.ReadAllText(subject);
        var lineCount = text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0);
        Assert.Equal(29, lineCount);

        Assert.Contains("RequestHandlerDelegate<TResponse> next", text, StringComparison.Ordinal);
        Assert.Contains("IPipelineBehavior<TRequest, TResponse>", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Compares an artifact's before/after emitted C#. Returns whether the two
    /// are byte-identical, and how many lines differ once the line NUMBER inside
    /// a <c>#line</c> directive is normalised away.
    /// </summary>
    private static (bool StrictEqual, int NonLineDifferences) CompareEmittedCSharp(string artifact)
    {
        var spike = SpikeDirectory();
        var before = Path.Combine(spike, "before", artifact + ".g.cs");
        var after = Path.Combine(spike, "after", artifact + ".g.cs");

        Assert.True(File.Exists(before), $"Missing before/{artifact}.g.cs");
        Assert.True(File.Exists(after), $"Missing after/{artifact}.g.cs");

        var strictEqual = File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after));

        var beforeLines = NormalizeLineDirectives(File.ReadAllLines(before));
        var afterLines = NormalizeLineDirectives(File.ReadAllLines(after));
        var differences = Math.Abs(beforeLines.Length - afterLines.Length)
            + beforeLines.Zip(afterLines).Count(pair => !string.Equals(
                pair.First, pair.Second, StringComparison.Ordinal));

        return (strictEqual, differences);
    }

    private static string[] NormalizeLineDirectives(string[] lines)
        => lines
            .Select(line => System.Text.RegularExpressions.Regex.Replace(
                line.TrimEnd(),
                @"^(\s*#line )\d+( "".*"")$",
                "$1N$2"))
            .ToArray();

    /// <summary>Reads a diagnostics file's diagnostic lines, skipping its header.</summary>
    private static List<string> ReadDiagnostics(string path)
    {
        Assert.True(File.Exists(path), $"Missing diagnostic list: {path}");

        return File.ReadAllLines(path)
            .Where(line => !line.StartsWith('#'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => line.Contains("): ", StringComparison.Ordinal))
            .ToList();
    }

    private static string VerdictPath()
    {
        var path = Path.Combine(SpikeDirectory(), "spike-verdict.json");
        Assert.True(File.Exists(path),
            $"Missing the spike's machine-readable verdict: {path}. "
            + "Design doc §12.3 replaces Draft v1's prose README with this file.");
        return path;
    }

    private static string SpikeDirectory()
        => Path.Combine(RepositoryRoot(), "docs", "design", "spikes", "effect-rows");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found above {AppContext.BaseDirectory}.");
    }
}
