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

    /// <summary>
    /// After-only fixtures: the R2 and alpha-equivalence evidence. They have no
    /// <c>before/</c> counterpart (their BEFORE is the row-less middleware and
    /// A2 respectively), so they sit outside <see cref="PairedArtifacts"/> — but
    /// they are cited by name in <c>spike-verdict.json</c>'s R2 evidence, so P31
    /// must cover them or they could be deleted with every test green.
    /// </summary>
    private static readonly string[] AfterOnlyArtifacts =
    [
        "A2-broadening", "A3-middleware-alpha", "A3-middleware-broadening",
    ];

    /// <summary>
    /// The after-only fixtures that EMIT. <c>A3-middleware-broadening</c> is
    /// rejected with Calor0421 — being rejected is its whole point — so it has
    /// no <c>.g.cs.txt</c> and its diagnostic list is the evidence.
    /// </summary>
    private static readonly string[] AfterOnlyArtifactsThatEmit =
    [
        "A2-broadening", "A3-middleware-alpha",
    ];

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
            var measured = CompareCommittedEmittedCSharp(artifact);
            var recorded = gCodegen.GetProperty(artifact);

            Assert.Equal(measured.StrictEqual, recorded.GetProperty("strictBytesEqual").GetBoolean());
            Assert.Equal(
                measured.StrictEqual ? "PASS" : "PASS-MODULO-LINE-DIRECTIVES",
                recorded.GetProperty("verdict").GetString());

            // Both byte counts are recomputed and compared to the record. The
            // single field these replaced was a length delta that read 0 for A2
            // while 7 bytes differed, and nothing asserted it.
            Assert.Equal(
                measured.StrictDiffBytes,
                recorded.GetProperty("strictDiffBytes").GetInt32());
            Assert.Equal(
                measured.NonLineDirectiveDiffBytes,
                recorded.GetProperty("nonLineDirectiveDiffBytes").GetInt32());

            Assert.Equal(0, measured.NonLineDirectiveDiffBytes);
            Assert.Equal(0, measured.NonLineDifferences);
        }

        Assert.Equal("PASS", gCodegen.GetProperty("overall").GetString());

        // --- recorded: R1, R2, R3 ---------------------------------------------
        // R1 is recorded ONLY until E2 gives us a compiler that parses a row.
        // The deferral is asserted, not assumed, so nobody can quietly leave it
        // recorded once E2 has landed.
        var ramp = root.GetProperty("ramp");
        var r1 = ramp.GetProperty("R1");
        Assert.True(r1.GetProperty("recordedNotRecomputed").GetBoolean());

        // The EXACT string, not a substring. `Contains("E2")` was satisfied by
        // anything mentioning E2 at all, so it did not actually pin the
        // deferral — once E2 lands, this assertion is what forces the field (and
        // this test's R1 leg) to be rewritten rather than quietly left alone.
        Assert.Equal(
            "P27 once E2 lands; recorded until then",
            r1.GetProperty("recomputedBy").GetString());
        Assert.False(string.IsNullOrWhiteSpace(r1.GetProperty("recomputeDeferralReason").GetString()));

        // §7.5's precondition is part of R1's claim and was dropped from the
        // record until review round 1. Without it R1 reads as a claim about the
        // resolution ceiling rather than about the four combinators.
        Assert.Contains(
            "when every participating row is concrete and every callee resolves",
            r1.GetProperty("claim").GetString() ?? "",
            StringComparison.Ordinal);

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
        // SEVEN, not three. The first record listed three because it was
        // assembled from P29's failure message, which reports the FIRST
        // difference per script and hides the rest; review round 1 caught it by
        // diffing every script in full. The count and the case list must agree,
        // and the obligation must name the number, so a future edit cannot drop
        // one silently.
        var divergences = root.GetProperty("transcriptDivergences");
        var cases = divergences.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(7, divergences.GetProperty("count").GetInt32());
        Assert.Equal(7, cases.Length);
        Assert.Contains("SEVEN", divergences.GetProperty("e2Obligation").GetString() ?? "",
            StringComparison.Ordinal);

        foreach (var divergence in cases)
        {
            foreach (var field in new[] { "script", "case", "committed", "withPrototype", "verdict" })
            {
                Assert.False(string.IsNullOrWhiteSpace(divergence.GetProperty(field).GetString()),
                    $"A transcript divergence has an empty '{field}'. Each one is an E2 "
                    + "obligation; an unlabelled row is not one.");
            }
        }

        // Every divergence must name a script the harness actually runs.
        var scripts = new[] { "run.py", "run2.py", "run3.py", "facts.py", "facts2.py", "compile53.py" };
        foreach (var divergence in cases)
        {
            Assert.Contains(divergence.GetProperty("script").GetString(), scripts);
        }
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
            var measured = CompareCommittedEmittedCSharp(artifact);
            if (measured.NonLineDifferences != 0 || measured.NonLineDirectiveDiffBytes != 0)
            {
                failures.Add(
                    $"{artifact}: {measured.NonLineDifferences} emitted C# line(s) and "
                    + $"{measured.NonLineDirectiveDiffBytes} byte(s) differ outside #line "
                    + "directives — a row changed codegen.");
            }
            else if (!measured.StrictEqual)
            {
                // Allowed, and named: the only moving bytes are source positions.
                // A2 is the one artifact whose rows sit on their own added lines;
                // its 7 differing bytes are all digits inside #line directives.
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

        // The after-only fixtures carry R2 and the alpha-equivalence proof. They
        // were outside this loop, so `after/A3-middleware-alpha.g.cs.txt` and
        // `after/A2-broadening.g.cs.txt` could be deleted with every test green
        // — which is exactly the hole P31 exists to close.
        foreach (var artifact in AfterOnlyArtifacts)
        {
            foreach (var extension in new[] { ".calr", ".diagnostics.txt" })
            {
                var path = Path.Combine(spike, "after", artifact + extension);
                if (!File.Exists(path)) missing.Add($"after/{artifact}{extension}: missing");
                else if (new FileInfo(path).Length == 0) missing.Add($"after/{artifact}{extension}: empty");
            }
        }

        foreach (var artifact in AfterOnlyArtifactsThatEmit)
        {
            var path = Path.Combine(spike, "after", artifact + ".g.cs.txt");
            if (!File.Exists(path)) missing.Add($"after/{artifact}.g.cs.txt: missing");
            else if (new FileInfo(path).Length == 0) missing.Add($"after/{artifact}.g.cs.txt: empty");
        }

        // A3-middleware-broadening must NOT emit: being rejected with Calor0421
        // is the evidence. If it starts emitting, R2's rejection half has gone.
        Assert.False(
            File.Exists(Path.Combine(spike, "after", "A3-middleware-broadening.g.cs.txt")),
            "after/A3-middleware-broadening.g.cs.txt exists, but that fixture is R2's REJECTION "
            + "case — it is supposed to fail with Calor0421 and emit nothing.");

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
    /// Compares an artifact's committed before/after emitted C#.
    ///
    /// <para>Returns THREE numbers, because one is not enough to be honest about
    /// A2. <c>StrictDiffBytes</c> counts differing bytes with no normalisation
    /// whatever; <c>NonLineDirectiveDiffBytes</c> counts them again after the
    /// line NUMBER inside a <c>#line</c> directive is normalised away; and
    /// <c>NonLineDifferences</c> is the same thing counted in lines.</para>
    ///
    /// <para>An earlier version of the verdict recorded a single
    /// <c>diffBytes</c> that was really a LENGTH delta. For A2 it read 0 while 7
    /// bytes differed, and no test read the field — so a wrong number sat in the
    /// record looking like a measurement. Both counts are now asserted for every
    /// artifact.</para>
    /// </summary>
    private static (bool StrictEqual, int StrictDiffBytes, int NonLineDirectiveDiffBytes,
        int NonLineDifferences) CompareCommittedEmittedCSharp(string artifact)
    {
        var spike = SpikeDirectory();
        var before = Path.Combine(spike, "before", artifact + ".g.cs.txt");
        var after = Path.Combine(spike, "after", artifact + ".g.cs.txt");

        Assert.True(File.Exists(before), $"Missing before/{artifact}.g.cs.txt");
        Assert.True(File.Exists(after), $"Missing after/{artifact}.g.cs.txt");

        var beforeBytes = File.ReadAllBytes(before);
        var afterBytes = File.ReadAllBytes(after);
        var strictEqual = beforeBytes.SequenceEqual(afterBytes);
        var strictDiffBytes = CountDifferingBytes(beforeBytes, afterBytes);

        var beforeLines = NormalizeLineDirectives(File.ReadAllLines(before));
        var afterLines = NormalizeLineDirectives(File.ReadAllLines(after));
        var differences = Math.Abs(beforeLines.Length - afterLines.Length)
            + beforeLines.Zip(afterLines).Count(pair => !string.Equals(
                pair.First, pair.Second, StringComparison.Ordinal));

        var nonLineDirectiveDiffBytes = CountDifferingBytes(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", beforeLines)),
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", afterLines)));

        return (strictEqual, strictDiffBytes, nonLineDirectiveDiffBytes, differences);
    }

    /// <summary>
    /// Counts positions at which two byte sequences differ, plus the length
    /// delta — the <c>cmp -l | wc -l</c> reading, NOT a length comparison.
    /// </summary>
    private static int CountDifferingBytes(byte[] left, byte[] right)
        => left.Zip(right).Count(pair => pair.First != pair.Second)
           + Math.Abs(left.Length - right.Length);

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

    /// <summary>
    /// The compiler the pinned invocation runs. When both configurations are
    /// built (a dev box), the most recently written <c>calor.dll</c> wins, so a
    /// frozen baseline is never adjudicated against a stale build of the other
    /// configuration. CI builds one configuration only.
    /// </summary>
    private static string CompilerDll()
    {
        var root = RepositoryRoot();
        var newest = new[] { "Debug", "Release" }
            .Select(configuration => Path.Combine(
                root, "src", "Calor.Compiler", "bin", configuration, "net10.0", "calor.dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is not null) return newest;

        throw new InvalidOperationException(
            "calor.dll not found under src/Calor.Compiler/bin/{Debug,Release}/net10.0/. "
            + "Run: dotnet build src/Calor.Compiler");
    }

    /// <summary>
    /// The five fixtures annex entry A-1.11 froze as PP-E1's denominator, with
    /// the git blob SHA-1 each is pinned to in the §A.2 row. The row is
    /// byte-frozen by <c>scripts/check-annex-freeze.py</c> and can never be
    /// edited, so a fixture edit would otherwise leave the row pointing at
    /// content that no longer exists, silently. This is that pin.
    /// </summary>
    private static readonly (string Fixture, string BlobSha)[] PpE1FrozenFixtures =
    [
        ("A2", "93ecdf1605c4e220313c1dd76b3291d3a79bb705"),
        ("A3-map", "0885b3dd40fcff28c51de72860d47a32db60bf8c"),
        ("A3-match", "c1ce75179ff0ab0b80bd74e2e7f6709ffb542bfe"),
        ("A3-middleware", "e5ee81e24abcf38f9111407d8e5c635a482a7ed2"),
        ("A3-callback", "05ddc23d342e8652ae59be242d29dd0b8a3ca5c4"),
        // The L5-A2 mutant is itself a committed artifact (A-1.11, honest timing).
        ("A2-broadening", "f975f2824464af2531d53e889ef79e1fe5a363e4"),
    ];

    /// <summary>
    /// PP-E1 (annex A-1.11) freezes its fixture set by blob SHA. Recompute the
    /// git object id of each frozen fixture and compare: an edit to any of them
    /// turns this red, which is the only mechanical link between the frozen
    /// annex row and the bytes it names.
    /// </summary>
    [Fact]
    public void PpE1FixtureBlobShasMatchTheFrozenAnnexRow()
    {
        foreach (var (fixture, expected) in PpE1FrozenFixtures)
        {
            var path = Path.Combine(SpikeDirectory(), "after", fixture + ".calr");
            Assert.True(File.Exists(path), $"PP-E1 fixture missing: {path}");

            var actual = GitBlobSha1(path);
            Assert.True(
                string.Equals(actual, expected, StringComparison.Ordinal),
                $"PP-E1 fixture '{fixture}' has blob SHA {actual}, but annex A-1.11's frozen "
                + $"§A.2 row names {expected}. The row is append-only and cannot be corrected: "
                + "either restore the fixture, or register the new content in a NEW annex entry "
                + "that supersedes the cell (docs/plans/agent-native-gates.md §A.3).");
        }
    }

    /// <summary>Git's object id for a file: SHA-1 over "blob {length}\0{bytes}".</summary>
    private static string GitBlobSha1(string path)
    {
        var content = File.ReadAllBytes(path);
        var header = System.Text.Encoding.ASCII.GetBytes(
            $"blob {content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}\0");
        var payload = new byte[header.Length + content.Length];
        header.CopyTo(payload, 0);
        content.CopyTo(payload, header.Length);
#pragma warning disable CA5350 // git object ids are SHA-1 by definition; not a security use
        var digest = System.Security.Cryptography.SHA1.HashData(payload);
#pragma warning restore CA5350
        return Convert.ToHexStringLower(digest);
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

    // ========================================================================
    // v0.15 E3 slice a — PP-E1 leg A's NEGATIVE CONTROL, the half this slice can
    // honestly assert.
    //
    // The gate row (docs/plans/agent-native-gates.md, A-1.11) freezes five
    // fixtures and bars "any of {Calor0405, Calor0420, Calor0421, Calor0424}
    // anywhere in a control compile". Calor0424 and Calor0425 are E3's ONLY new
    // emissions, so this asserts they are absent from all five — which is the
    // clause of the control that E3 can put at risk and therefore the clause E3
    // owes.
    //
    // What this test deliberately does NOT assert is the control's full
    // per-fixture multiset. That is asserted separately, by
    // PpE1NegativeControls_MatchA1111Baselines_PreE4 below.
    //
    // THE BASELINE A-1.11 FROZE IS SUPERSEDED. A-1.11's leg-A negative control
    // read "A2 = 1x Calor0410 at (23,9) + 3x Calor0411; the four A3 = exit 0
    // with zero diagnostics". That cell carried two defects, both found by the
    // E3a review (PR #1103, round 1, F1/F3) and both corrected by annex
    // sub-entry A-1.11.1 (2026-08-26):
    //
    //   * A2's multiset was transcribed from after/A2.diagnostics.txt, whose
    //     header records `# emit args: --permissive-effects` -- a flag the same
    //     gate row FORBIDS in this probe. It was never the pinned invocation's
    //     output, so the control could not have passed on any compiler.
    //   * all five baselines came from the spike's throwaway, unmerged
    //     prototype, and the four A3 fixtures draw Calor0418 at each invocation
    //     on the shipping compiler, because invoking a row-less value is still
    //     Calor0418 until E4 -- the gate row says so itself ("Calor0425 ... is
    //     E4's and owns all five L7 cells").
    //
    // A-1.11.1 re-freezes A2 under the pinned invocation and registers the
    // pre-E4 A3 counts; the four A3 fixtures' "exit 0, zero diagnostics" (A-1.11's
    // words, verbatim) STANDS as the post-E4 expectation. Until E4 merges, PP-E1
    // leg A is a MISS under A-1.11's own-goal clause if adjudicated;
    // adjudication is at the 0.15.0 release commit.
    //
    // A separate E2b defect on this fixture -- A2 drew 2x Calor0405, because
    // slice b's P6 check read `RequestHandlerDelegate<TResponse>`, a
    // §CSHARP-declared delegate rather than a §DEL, as not function-typed --
    // was fixed in PR #1103 (review round 1, F2): delegate declarations inside
    // interop text are collected, and Calor0405 fails OPEN via
    // TypeIdentity.IsProvablyNonFunctionType. Pinned by
    // PpE1NegativeControl_A2DrawsNoCalor0405_AfterF2 below.
    // ========================================================================

    /// <summary>The five §12.1 fixtures PP-E1 leg A freezes, by path.</summary>
    private static readonly string[] PpE1ControlFixtures =
    [
        "A2", "A3-map", "A3-match", "A3-middleware", "A3-callback",
    ];

    [Fact]
    public void PpE1NegativeControl_NoEffectRowDiagnosticOnAnyUnmutatedFixture()
    {
        var failures = new List<string>();

        foreach (var fixture in PpE1ControlFixtures)
        {
            var path = Path.Combine(SpikeDirectory(), "after", fixture + ".calr");
            Assert.True(File.Exists(path), $"PP-E1 control fixture missing: {path}");

            var source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            var diagnostics = new Compiler.Diagnostics.DiagnosticBag();
            var module = new Compiler.Parsing.Parser(
                new Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(),
                diagnostics).Parse();
            new Compiler.Binding.Binder(diagnostics, fixture + ".calr").Bind(module);
            if (!diagnostics.HasErrors)
            {
                // The pinned invocation is the CLI default: enforcement on,
                // UnknownCallPolicy.Strict, NO --permissive-effects. The gate row
                // forbids the flag here, because it waives Calor0425 and would
                // satisfy every L7 cell for free.
                new Compiler.Effects.EffectEnforcementPass(diagnostics).Enforce(module);
            }

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.EffectRowMismatch
                    || diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.EffectRowUnknown
                    || diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.OverrideEffectVariance
                    || diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InterfaceEffectVariance)
                {
                    failures.Add($"{fixture}: {diagnostic.Code} — {diagnostic.Message}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "PP-E1 leg A's negative control is FROZEN: an unmutated fixture must draw no "
            + "row-family diagnostic. E3's emission put one there, which means the L5/L7 "
            + "detection cells can no longer discriminate the feature under test. STOP and "
            + "report before pushing — do not regenerate the baseline.\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// v0.15 E3 slice b — the FULL frozen multiset of the four A3 combinator
    /// fixtures, asserted exactly, under the pinned invocation.
    ///
    /// <para>Slice a could only assert the absence of row-family diagnostics
    /// (above). Slice b implements site 6, which is the mechanism these four
    /// fixtures exist to exercise, so the honest pin is the whole multiset: the
    /// PRE-E4 state is <b>exactly Calor0418 once per invocation of a row-less
    /// value, and nothing else</b>. Anything that this slice adds or removes on
    /// these five files is a change to PP-E1's frozen control, and the gate row
    /// says STOP rather than regenerate.</para>
    ///
    /// <para><b>What E4 owes.</b> Replace Calor0418 and every count below goes to
    /// zero, which is the frozen A-1.11 baseline (exit 0, zero diagnostics)
    /// restored. Until then PP-E1 leg A is a MISS under the own-goal clause,
    /// stated here rather than papered over.</para>
    /// </summary>
    [Theory]
    [InlineData("A3-callback", 1, 0)]
    [InlineData("A3-map", 1, 0)]
    [InlineData("A3-match", 2, 0)]
    [InlineData("A3-middleware", 2, 0)]
    // The alpha-equivalence fixture: `eff e` on the interface member, `eff f` on
    // the implementation. Zero Calor0421 is §7.5's R2, and it now holds because
    // sites 4/5 compare binders by ORDINAL — not, as in slice a, because they
    // never compared them at all.
    [InlineData("A3-middleware-alpha", 1, 0)]
    // The negative control for the same mechanism: same shape, impl row {e, cw}
    // against interface row {e}. The ORDINARY fits relation rejects it.
    [InlineData("A3-middleware-broadening", 1, 1)]
    public void A3Fixtures_AreExactlyCalor0418PerInvocation(
        string fixture, int expected0418, int expected0421)
    {
        var path = Path.Combine(SpikeDirectory(), "after", fixture + ".calr");
        Assert.True(File.Exists(path), $"A3 fixture missing: {path}");

        var source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        // Driven through Program.Compile, NOT through a hand-built
        // Parser/Binder/EffectEnforcementPass chain. That distinction is
        // load-bearing and was found the hard way: binding A3-map and A3-match
        // directly reports Calor0202 ("'Double' is a function, not a variable")
        // on their method-group arguments, which the real pipeline does not, so
        // the hand-built chain SKIPS the effect pass on exactly the two fixtures
        // that exercise rank-1 instantiation. `PpE1NegativeControl_*` above has
        // that shape and is therefore vacuous for those two; this pin does not
        // inherit it. UnsafeTranspileOnly stops at C# emission, which removes
        // only Calor1002 relative to the pinned CLI invocation.
        var result = Compiler.Program.Compile(
            source,
            fixture + ".calr",
            new Compiler.CompilationOptions { UnsafeTranspileOnly = true });

        var byCode = result.Diagnostics
            .GroupBy(d => d.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        if (expected0418 > 0)
            expected[Compiler.Diagnostics.DiagnosticCode.DelegateInvocation] = expected0418;
        if (expected0421 > 0)
            expected[Compiler.Diagnostics.DiagnosticCode.InterfaceEffectVariance] = expected0421;

        Assert.Equal(
            string.Join(", ", expected.OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => $"{e.Key}x{e.Value}")),
            string.Join(", ", byCode.OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => $"{e.Key}x{e.Value}")));
    }

    /// <summary>
    /// The corrected PP-E1 leg-A negative-control baselines, from annex
    /// sub-entry <b>A-1.11.1</b> (2026-08-26), measured under the PINNED
    /// invocation (no flags) at <c>main</c> =
    /// <c>9119397e979dfcab3606ee382b16afbdec4b136a</c>. Key is the fixture; the
    /// value is the expected process exit code plus the sorted multiset of
    /// <c>severity Calor####@line,column</c>. Severity and exit code are part
    /// of the pin because they are exactly what the forbidden flag changes on
    /// the A3 fixtures: the same Calor0418s, demoted from error to warning, and
    /// exit 0 instead of 1. A code-and-position multiset alone cannot tell the
    /// pinned invocation from the forbidden one.
    ///
    /// <para><b>E4 MUST UPDATE THIS TABLE.</b> Every entry here contains at
    /// least one Calor0418, and Calor0418 is precisely what E4 replaces. A-1.11.1
    /// registers the post-E4 expectation: A2 becomes
    /// <c>Calor0410@23,9 + Calor0411@26,24 + Calor0411@28,19</c> (the Calor0418
    /// at (27,27) disappears), and the four A3 fixtures return to "exit 0, zero
    /// diagnostics", which is the baseline A-1.11 froze, verbatim, and which
    /// A-1.11.1 leaves standing. When E4 lands, this test goes red; the E4
    /// PR flips it to those post-E4 multisets. Do NOT regenerate it to whatever
    /// the compiler happens to emit — A-1.11.1 exists because a baseline was
    /// once recorded that way.</para>
    /// </summary>
    private static readonly (string Fixture, int ExitCode, string[] Expected)[] PpE1PreE4Baselines =
    [
        ("A2", 1,
        [
            "error Calor0410@23,9",
            "error Calor0418@27,27",
            "warning Calor0411@26,24",
            "warning Calor0411@28,19",
        ]),
        ("A3-map", 1, ["error Calor0418@7,22"]),
        ("A3-match", 1, ["error Calor0418@5,10", "error Calor0418@6,8"]),
        ("A3-middleware", 1, ["error Calor0418@4,19", "error Calor0418@5,20"]),
        ("A3-callback", 1, ["error Calor0418@6,7"]),
    ];

    /// <summary>
    /// PP-E1 leg A's negative control, as CORRECTED by annex sub-entry
    /// <b>A-1.11.1</b>: the full per-fixture diagnostic multiset of every
    /// unmutated fixture under the pinned invocation, in its PRE-E4 state.
    ///
    /// <para>A-1.11's own A2 baseline is superseded and cannot be asserted here:
    /// it was recorded with <c>--permissive-effects</c>, which that row forbids,
    /// so it was never reproducible. See the block comment above and
    /// <c>docs/plans/agent-native-gates.md</c> §A.3 entry A-1.11.1.</para>
    /// </summary>
    [Fact]
    public void PpE1NegativeControls_MatchA1111Baselines_PreE4()
    {
        var failures = new List<string>();

        foreach (var (fixture, expectedExitCode, expected) in PpE1PreE4Baselines)
        {
            var (exitCode, actual) = CompileControlFixture(fixture);

            if (exitCode != expectedExitCode)
            {
                failures.Add($"{fixture}: expected exit {expectedExitCode} but got exit {exitCode}");
            }

            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{fixture}: expected [{string.Join(", ", expected)}] "
                    + $"but got [{string.Join(", ", actual)}]");
            }
        }

        Assert.True(failures.Count == 0,
            "PP-E1 leg A's negative control is FROZEN by annex sub-entry A-1.11.1 "
            + "(docs/plans/agent-native-gates.md §A.3). These are the PRE-E4 multisets. "
            + "If you are landing E4: this test is SUPPOSED to go red — replace each entry "
            + "with A-1.11.1's registered POST-E4 multiset (A2 loses its Calor0418 at (27,27) and "
            + "stays exit 1; the four A3 fixtures return to exit 0 with zero diagnostics). "
            + "If you are NOT landing E4: "
            + "STOP and report — do not regenerate the baseline, which is the exact mistake "
            + "A-1.11.1 was written to correct.\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// Compiles one control fixture with the PINNED invocation A-1.11 froze —
    /// <c>dotnet &lt;calor.dll&gt; -i &lt;source&gt; -o &lt;scratch&gt;</c> with
    /// <b>no flags</b>. <c>--permissive-effects</c> is deliberately NOT passed:
    /// the gate row forbids it in this probe, and A-1.11.1 exists because the
    /// superseded baseline was recorded with it. Returns the process exit code
    /// and the diagnostic multiset as sorted <c>severity Calor####@line,column</c>
    /// strings.
    ///
    /// <para>This shells out rather than driving the passes in-process on
    /// purpose. The pinned invocation IS the CLI pipeline, and the in-process
    /// shortcut measures something else: it reports binder diagnostics the CLI
    /// filters and, where those are errors, skips the effect pass entirely, so
    /// A3-map and A3-match come back with no Calor0418 at all.</para>
    /// </summary>
    private static (int ExitCode, string[] Diagnostics) CompileControlFixture(string fixture)
    {
        var source = Path.Combine(SpikeDirectory(), "after", fixture + ".calr");
        Assert.True(File.Exists(source), $"PP-E1 control fixture missing: {source}");

        var output = Path.Combine(Path.GetTempPath(), $"calor-ppe1-{Guid.NewGuid():N}.g.cs");
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

            using var process = Process.Start(start);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            string[] diagnostics = [.. DiagnosticLine.Matches(stdout + "\n" + stderr)
                .Select(m => $"{m.Groups[2].Value} {m.Groups[3].Value}@{m.Groups[1].Value}")
                .OrderBy(s => s, StringComparer.Ordinal)];
            return (process.ExitCode, diagnostics);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>`…/A2.calr(26,24): warning Calor0411: …` → `26,24`, `warning`, `Calor0411`.</summary>
    private static readonly System.Text.RegularExpressions.Regex DiagnosticLine = new(
        @"\((\d+,\d+)\): (error|warning|info) (Calor\d+):",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    [Fact]
    public void PpE1NegativeControl_A2DrawsNoCalor0405_AfterF2()
    {
        // PP-E1's row bars Calor0405 "anywhere in a control compile". A2 drew
        // TWO — from E2 slice b's P6 check, on `RequestHandlerDelegate<TResponse>`,
        // a delegate declared inside a §CSHARP block that the binder's
        // delegate-name collection never saw. Review round 1 (F2) fixed both
        // halves: the collection now reads interop text, and Calor0405 fires only
        // where the type is PROVABLY non-function.
        //
        // Kept as an explicit pin rather than folded into the sweep above,
        // because it is the clause of PP-E1 this branch had to repair, and a
        // regression in either half must name A2.
        var path = Path.Combine(SpikeDirectory(), "after", "A2.calr");
        var source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var diagnostics = new Compiler.Diagnostics.DiagnosticBag();
        var module = new Compiler.Parsing.Parser(
            new Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(),
            diagnostics).Parse();
        new Compiler.Binding.Binder(diagnostics, "A2.calr").Bind(module);

        Assert.DoesNotContain(diagnostics,
            d => d.Code == Compiler.Diagnostics.DiagnosticCode.EffectRowMisplaced);
    }
}
