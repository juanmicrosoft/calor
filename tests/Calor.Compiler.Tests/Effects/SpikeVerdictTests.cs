using System.Diagnostics;
using System.Text.Json;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 §4.1 term 1 — the emitter spike's verdict, pinned.
///
/// <para>The effect-rows design doc (§12) freezes three artifacts (A1, A2, A3),
/// a machine-readable verdict (<c>spike-verdict.json</c>) and three pins. This
/// file is those pins.</para>
///
/// <para><b>What these tests can and cannot verify ON MAIN.</b> The spike's
/// AFTER forms carry effect rows, which main's compiler does not parse — the
/// prototype that produced them is a throwaway on branch
/// <c>spike/effect-rows-emitter</c> and is deliberately NOT merged. So the pins
/// split cleanly, and the split is stated rather than hidden:</para>
///
/// <list type="bullet">
///   <item><b>Recomputed today.</b> Every <c>before/</c> artifact. P28 re-emits
///   <c>before/A1.calr</c> and <c>before/A2.calr</c> with the CURRENT compiler
///   and asserts the emitted C# is byte-identical to the committed
///   <c>before/*.g.cs</c>. A1 is the regression module: it has no
///   function-typed position, so this leg is exactly the question "did the row
///   feature move codegen for a row-less program?".</item>
///   <item><b>Compared today.</b> The G-CODEGEN verdict itself, by diffing the
///   committed <c>before/</c> and <c>after/</c> <c>.g.cs</c> pairs under the
///   <c>#line</c>-normalisation rule below. That comparison needs no compiler
///   at all, so it is a real assertion on main, not a deferral.</item>
///   <item><b>Recorded until E2.</b> The <c>after/</c> artifacts and the R1
///   leg. Design-doc §12.3 has P27 recompute R1 by compiling each A3 fixture;
///   that needs a compiler with rows. Until E2 lands, R1 is recorded exactly as
///   R2 and R3 are, and <c>spike-verdict.json</c> says so in
///   <c>ramp.R1.recomputedBy</c>. <b>Not</b> a Skip and <b>not</b> a fake pass:
///   the tests still assert the recorded evidence exists, is well-formed, and
///   says what the verdict claims it says.</item>
/// </list>
///
/// <para>Design doc §13.2 pins <b>P27</b>, <b>P28</b> and <b>P31</b>. Freeze
/// point: this spike PR.</para>
/// </summary>
public sealed class SpikeVerdictTests
{
    /// <summary>The artifacts whose before/after pair G-CODEGEN compares.</summary>
    private static readonly string[] PairedArtifacts =
    [
        "A1", "A2", "A3-callback", "A3-map", "A3-match", "A3-middleware",
    ];

    /// <summary>
    /// The artifacts G-CODEGEN is BLOCKING for (§12.4): A1 and A2. These are
    /// also the two whose <c>before/</c> side P28 re-emits, because they are the
    /// ones the design doc names.
    /// </summary>
    private static readonly string[] BlockingArtifacts = ["A1", "A2"];

    /// <summary>The four A3 combinator fixtures R1 is adjudicated over (§7.5).</summary>
    private static readonly string[] R1Fixtures =
    [
        "A3-callback", "A3-map", "A3-match", "A3-middleware",
    ];

    /// <summary>Codes R1 requires to be absent from every A3 fixture's AFTER form.</summary>
    private static readonly string[] R1ForbiddenCodes = ["Calor0404", "Calor0424", "Calor0425"];

    /// <summary>
    /// P27 — the verdict is read off the file, and every part a machine can
    /// check on main IS checked rather than trusted.
    /// </summary>
    [Fact]
    public void SpikeVerdictMatchesRecomputation()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(VerdictPath()));
        var root = document.RootElement;

        // --- shape ------------------------------------------------------------
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        AssertCommitSha(root.GetProperty("measuredCommit").GetString(), "measuredCommit");

        // The prototype must be identified by branch AND commit, so a reviewer
        // can fetch the code that produced the AFTER artifacts.
        var prototype = root.GetProperty("prototype");
        Assert.True(prototype.GetProperty("throwaway").GetBoolean(),
            "The spike prototype is not an implementation. Recording it as anything else "
            + "invites it to be merged as E2.");
        AssertCommitSha(prototype.GetProperty("commit").GetString(), "prototype.commit");
        Assert.False(string.IsNullOrWhiteSpace(prototype.GetProperty("branch").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(prototype.GetProperty("note").GetString()));
        Assert.Equal(0, prototype.GetProperty("newFilesUnderSrc").GetInt32());

        // --- compared: the gCodegen block (this IS P28's committed-pair leg) ---
        var gCodegen = root.GetProperty("gCodegen");
        foreach (var artifact in PairedArtifacts)
        {
            var (strictEqual, nonLineDifferences) = CompareCommittedEmittedCSharp(artifact);
            var recorded = gCodegen.GetProperty(artifact);

            Assert.Equal(strictEqual, recorded.GetProperty("strictBytesEqual").GetBoolean());
            Assert.Equal(
                strictEqual ? "PASS" : "PASS-MODULO-LINE-DIRECTIVES",
                recorded.GetProperty("verdict").GetString());
            Assert.Equal(0, nonLineDifferences);
        }

        Assert.Equal("PASS", gCodegen.GetProperty("overall").GetString());

        // --- recorded: R1, R2, R3 ---------------------------------------------
        // R1 is recorded ONLY until E2 gives us a compiler that parses a row.
        // The deferral is asserted, not assumed, so nobody can quietly leave it
        // recorded once E2 has landed.
        var ramp = root.GetProperty("ramp");
        var r1 = ramp.GetProperty("R1");
        Assert.True(r1.GetProperty("recordedNotRecomputed").GetBoolean());
        Assert.Contains("E2", r1.GetProperty("recomputedBy").GetString() ?? "",
            StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(r1.GetProperty("recomputeDeferralReason").GetString()));

        // …but the RECORDED evidence is still read and checked against the claim.
        foreach (var fixture in R1Fixtures)
        {
            var diagnostics = ReadDiagnostics(
                Path.Combine(SpikeDirectory(), "after", fixture + ".diagnostics.txt"));

            foreach (var code in R1ForbiddenCodes)
            {
                Assert.False(
                    diagnostics.Any(line => line.Contains(code, StringComparison.Ordinal)),
                    $"R1 claims the AFTER form of '{fixture}' compiles with zero {code}, but its "
                    + "recorded diagnostic list contains one.");
            }
        }

        foreach (var leg in new[] { "R1", "R2", "R3" })
        {
            var block = ramp.GetProperty(leg);
            Assert.False(string.IsNullOrWhiteSpace(block.GetProperty("claim").GetString()));
            Assert.Contains(block.GetProperty("verdict").GetString(), new[] { "PASS", "FAIL" });
        }

        // R2 and R3 are judgements about whether a carve-out was needed and
        // whether the solve stayed one-line. A test cannot re-derive either, and
        // §12.3 says so plainly rather than implying a machine adjudicates them.
        foreach (var leg in new[] { "R2", "R3" })
        {
            var block = ramp.GetProperty(leg);
            Assert.True(block.GetProperty("recordedNotRecomputed").GetBoolean());
            Assert.NotEmpty(block.GetProperty("evidence").EnumerateArray());
        }

        // Every evidence string that names a file under the spike directory must
        // name one that EXISTS. This is what stops the recorded legs from
        // degenerating into prose.
        AssertEvidencePathsExist(ramp);

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
        AssertEvidencePathsExist(spelling);

        // The three transcript divergences the prototype causes are recorded
        // with the exact E2 obligation, so E2 cannot regenerate more than these.
        var divergences = root.GetProperty("transcriptDivergences");
        Assert.Contains("X6a", divergences.GetProperty("e2Obligation").GetString() ?? "",
            StringComparison.Ordinal);
        Assert.Equal(3, divergences.GetProperty("cases").EnumerateArray().Count());
    }

    /// <summary>
    /// P28 — the pin G-CODEGEN never had. Rows are a CHECKING feature, so adding
    /// one must not move a byte of emitted C#. §9's "0 <c>.cs</c> goldens" and
    /// §8.5's "the semantics stamp does not change" both rest on this being true
    /// rather than assumed. If it fails, E2 does not ship — monomorphic or not.
    ///
    /// <para>Two legs. <b>Leg 1 recomputes</b>: A1's and A2's <c>before/</c>
    /// sources are re-emitted with the CURRENT compiler and diffed against the
    /// committed <c>before/*.g.cs</c>. On main that answers "is the recorded
    /// BEFORE still what this compiler produces?" — and once E2 lands it becomes
    /// "did the row feature move codegen for a row-less program?", which is the
    /// question A1 exists to ask. <b>Leg 2 compares</b> the committed
    /// before/after pairs, which needs no compiler.</para>
    ///
    /// <para>A <c>#line N "&lt;source&gt;.calr"</c> directive tracks the SOURCE,
    /// so it necessarily moves when the author adds an annotation line. That is
    /// not a codegen difference. The test reports both readings rather than
    /// picking the flattering one: A2 is the one artifact whose rows sit on
    /// their own added lines, and it is byte-identical everywhere else.</para>
    /// </summary>
    [Fact]
    public void GCodegen_BeforeAfterEmittedCSharpIsByteIdentical()
    {
        var failures = new List<string>();

        // Leg 1 — recompute the BEFORE side with this compiler.
        foreach (var artifact in BlockingArtifacts)
        {
            var committed = Path.Combine(SpikeDirectory(), "before", artifact + ".g.cs.txt");
            Assert.True(File.Exists(committed), $"Missing before/{artifact}.g.cs");

            var reEmitted = ReEmit(Path.Combine(SpikeDirectory(), "before", artifact + ".calr"));
            if (reEmitted == null)
            {
                failures.Add(
                    $"{artifact}: before/{artifact}.calr did not emit with this compiler, so the "
                    + "committed BEFORE artifact cannot be recomputed.");
                continue;
            }

            if (!string.Equals(Normalize(File.ReadAllText(committed)), Normalize(reEmitted),
                    StringComparison.Ordinal))
            {
                failures.Add(
                    $"{artifact}: re-emitting before/{artifact}.calr with this compiler no longer "
                    + $"reproduces the committed before/{artifact}.g.cs. Either codegen changed — "
                    + "in which case G-CODEGEN's baseline moved and the spike verdict must be "
                    + "re-measured — or the artifact was hand-edited.");
            }
        }

        // Leg 2 — compare the committed before/after pairs.
        foreach (var artifact in PairedArtifacts)
        {
            var (strictEqual, nonLineDifferences) = CompareCommittedEmittedCSharp(artifact);
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
                foreach (var extension in new[] { ".calr", ".g.cs.txt", ".diagnostics.txt" })
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
            "The spike's frozen artifacts are incomplete. Regenerate the BEFORE side with "
            + "`python3 docs/design/spikes/effect-rows/experiments/spike_artifacts.py`; the AFTER "
            + "side needs the prototype branch and CALOR_WRITE_SPIKE_AFTER=1.\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The A2 corpus subject itself. A2's Calor is committed under
    /// <c>docs/design/spikes/effect-rows/</c>, so the three pins above run on a
    /// bare clone; only this leg re-reads the corpus file the design doc pins,
    /// so a submodule bump that changes it is caught. Skips cleanly without
    /// submodules — the <c>BinderIncompleteRatchetTests</c> pattern, registered
    /// in <c>eng/test-manifest.json</c>.
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
    /// Compiles <paramref name="source"/> with the compiler this test run built
    /// and returns the emitted C#, normalised the way
    /// <c>spike_artifacts.py</c> normalises it (absolute paths out, the source
    /// path replaced by a stable placeholder). Returns null when nothing was
    /// emitted.
    /// </summary>
    private static string? ReEmit(string source)
    {
        var output = Path.Combine(Path.GetTempPath(),
            $"calor-spike-{Guid.NewGuid():N}.g.cs");

        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = RepositoryRoot(),
            };
            start.ArgumentList.Add(CompilerDll());
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(source);
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(output);
            // The BEFORE artifacts are row-less programs that today's compiler
            // rejects at the invocation of a function-typed value (Calor0418).
            // The waiver is what a converted file needs today, and it is what
            // spike_artifacts.py used to produce the committed bytes.
            start.ArgumentList.Add("--permissive-effects");

            using var process = Process.Start(start);
            Assert.NotNull(process);
            process!.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!File.Exists(output)) return null;

            var root = RepositoryRoot();
            return File.ReadAllText(output)
                .Replace(Path.GetFullPath(source), "<source>.calr", StringComparison.Ordinal)
                .Replace(root + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)
                .Replace(root + "/", "", StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>
    /// Compares an artifact's committed before/after emitted C#. Returns whether
    /// the two are byte-identical, and how many lines differ once the line
    /// NUMBER inside a <c>#line</c> directive is normalised away.
    /// </summary>
    private static (bool StrictEqual, int NonLineDifferences) CompareCommittedEmittedCSharp(
        string artifact)
    {
        var spike = SpikeDirectory();
        var before = Path.Combine(spike, "before", artifact + ".g.cs.txt");
        var after = Path.Combine(spike, "after", artifact + ".g.cs.txt");

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

    /// <summary>
    /// Every evidence string that looks like a path under the spike directory
    /// must name a file that exists — otherwise a "recorded" leg is just prose.
    /// </summary>
    private static void AssertEvidencePathsExist(JsonElement element)
    {
        foreach (var text in Strings(element))
        {
            foreach (var token in text.Split([' ', ',', ';', ':', '\n'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (!token.StartsWith("before/", StringComparison.Ordinal)
                    && !token.StartsWith("after/", StringComparison.Ordinal))
                {
                    continue;
                }

                var candidate = token.TrimEnd('.', ')', ',');
                // Prose says things like "the before/ and after/ .calr"; only a
                // token that actually names a FILE is a path claim.
                if (!Path.HasExtension(candidate)) continue;

                Assert.True(
                    File.Exists(Path.Combine(SpikeDirectory(), candidate)),
                    $"spike-verdict.json cites '{candidate}' as evidence, but no such artifact "
                    + "is committed.");
            }
        }
    }

    private static IEnumerable<string> Strings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString() ?? "";
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var text in Strings(item)) yield return text;
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var text in Strings(property.Value)) yield return text;
                }

                break;
        }
    }

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

    private static void AssertCommitSha(string? sha, string field)
        => Assert.True(sha is { Length: 40 } && sha.All(Uri.IsHexDigit),
            $"{field} must be a 40-hex commit SHA, was '{sha}'. It records WHERE the measurement "
            + "came from; like the two existing ledgers it is shape-checked, not compared to HEAD.");

    private static string VerdictPath()
    {
        var path = Path.Combine(SpikeDirectory(), "spike-verdict.json");
        Assert.True(File.Exists(path),
            $"Missing the spike's machine-readable verdict: {path}. "
            + "Design doc §12.3 replaces Draft v1's prose README with this file.");
        return path;
    }

    private static string CompilerDll()
    {
        var root = RepositoryRoot();
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(
                root, "src", "Calor.Compiler", "bin", configuration, "net10.0", "calor.dll");
            if (File.Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException(
            "calor.dll not found under src/Calor.Compiler/bin/{Debug,Release}/net10.0/. "
            + "Run: dotnet build src/Calor.Compiler");
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

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
}
