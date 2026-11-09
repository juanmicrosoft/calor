using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 E1 slice 2a, review round 1 finding 2 — the Calor0270 volume ledger.
///
/// <para>The binder marks every receiver it cannot type with
/// <c>UnresolvedBoundType</c>, but only <i>reports</i> Calor0270 for the shapes an
/// author can act on. That split is a judgement call about editor noise, and a
/// judgement call with no instrument drifts. This is the instrument: how many
/// Calor0270 diagnostics the binder emits over the three pinned conversion
/// subjects, and how many converted modules carry at least one.</para>
///
/// <para>The ledger is an <b>exact-equality</b> pin in both directions, per subject
/// and in aggregate — the same discipline as the S5 resolution ledger
/// (<see cref="MetadataBinderCorpusMeasurementTests"/>). A <i>rise</i> means a new
/// unactionable shape started reporting and the editor got noisier; a <i>fall</i>
/// means a shape that authors could act on went silent. Both are decisions, and
/// both require explicit current source-site adjudication, never a rewrite of
/// the immutable historical ledger.</para>
///
/// <para>Historical acceptance replays the original compiler and test:
/// <c>python3 scripts/registered_ledger_historical_replay.py signature</c>.
/// The separate current-volume test retains every exact comparison against its
/// current fixture, plus every original input identity and named diagnostic site.
/// Passing historical replay does not settle a current-volume difference.</para>
///
/// <para>Both sides of the narrowing were measured with this same instrument, by
/// forcing <c>Binder.ShouldReportUnresolvedReceiver</c> to true and re-running:</para>
/// <list type="bullet">
/// <item><b>Reporting every unresolved receiver:</b> 875 diagnostics across 76 of
/// 305 bound modules (MediatR 49/10, Serilog 52/13, FluentValidation 774/53) —
/// dominated by member chains (<c>result.IsValid.X</c>) and converter-synthesized
/// temporaries (<c>_chainNNN</c>), neither of which an author can act on.</item>
/// <item><b>Reporting only the two actionable shapes</b> (the committed ledger):
/// 193 across 38 of 305 — a 78% reduction, and what remains is an inferred local
/// or a declared type string, both of which an explicit type annotation fixes.</item>
/// </list>
///
/// <para>Current acceptance requires all three initialized corpus submodules;
/// missing inputs, failed conversion and failed parsing are failures.</para>
///
/// <para><b>v0.16 K1 — the bind rule, named (roadmap §3.1 K1, §6 row 2).</b> This
/// ledger has <i>no</i> bind guard at all: it binds every module that PARSES
/// (305 of 364), counts the Calor0270 <i>Infos</i> the binder reports, and never
/// asks whether the binder's bag has errors. That is the right rule for what it
/// measures — Calor0270 is emitted BY the binder, so a module whose binding also
/// produced errors still produced these Infos — but it is a DIFFERENT rule from
/// the one the Calor0425 ledger uses (<c>"propagated"</c>: the shipping
/// compiler's filter through <c>BindingDiagnosticPolicy.PropagateCompilationErrors</c>
/// before the stop at <c>Program.cs:829-833</c>). The published "8 Calor0425
/// sites over 99 modules" was a ledger artifact precisely because nobody could
/// tell from the JSON which rule produced the denominator. So each ledger now
/// carries its own <c>BindRule</c> — <c>"parsed"</c> here, <c>"propagated"</c>
/// there — and the two are readable side by side.</para>
///
/// <para>K1 adds that field and <b>nothing else</b>: no number in
/// <c>calor0270-corpus-ledger.json</c> is regenerated, and the field was written
/// into the JSON by hand for exactly that reason.</para>
/// </summary>
public class Calor0270CorpusVolumeTests
{
    /// <summary>
    /// v0.16 K1 — this ledger's rule: bind every module that PARSES, with no
    /// bind guard. Contrast <c>Calor0425CorpusLedgerTests.BindRuleText</c>
    /// (<c>"propagated"</c>).
    /// </summary>
    internal const string BindRuleText = "parsed";

    private const string ScopeText =
        "Calor0270 (SignatureUnresolved) emitted by Binder.Bind over the three A-1.5.3 "
        + "conversion subjects at their pinned submodule commits, converted in-process with "
        + "Lossy/SelectActiveBranchLossy and genuinely empty default preprocessor symbols; "
        + "modules that fail conversion or whose converted output fails to parse are excluded "
        + "from the denominator and counted separately";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string LedgerPath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "calor0270-corpus-ledger.json");

    private static void AssertRegenerationForbidden(string? value) =>
        Assert.True(string.IsNullOrEmpty(value),
            "The original Calor0270 ledger is immutable; CALOR_REGENERATE_CALOR0270_LEDGER is forbidden. "
            + "Replay its original compiler with registered_ledger_historical_replay.py signature; "
            + "adjudicate current source-site differences separately.");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData(" ")]
    public void ImmutableLedger_RejectsEveryNonemptyRegenerationRequest(string? value)
    {
        if (string.IsNullOrEmpty(value))
            AssertRegenerationForbidden(value);
        else
            Assert.Throws<Xunit.Sdk.TrueException>(() => AssertRegenerationForbidden(value));
    }

    [Fact]
    [Trait("Category", "HistoricalReplay")]
    public async Task Calor0270Volume_OriginalCompiler_MatchesImmutableLedger()
    {
        var result = await ExecutionWorkspace.CaptureProcessAsync(
            "python3", [Path.Combine(RepoRoot(), "scripts", "registered_ledger_historical_replay.py"), "signature"],
            RepoRoot(), TimeSpan.FromMinutes(20), CancellationToken.None);
        Assert.True(result.ExitCode == 0 && !result.TimedOut,
            $"Historical Calor0270 signature replay failed (exit {result.ExitCode}).\n{result.Output}");
    }

    [Fact]
    [Trait("Category", "CorpusRequired")]
    public void Calor0270Volume_OverConvertedCorpus_MatchesLedger()
    {
        AssertRegenerationForbidden(Environment.GetEnvironmentVariable("CALOR_REGENERATE_CALOR0270_LEDGER"));
        var expected = ReadCurrentExpectation();
        var actual = MeasureCurrentCorpus();
        AssertCurrentMeasurement(expected, actual);
    }

    private static CurrentExpectation MeasureCurrentCorpus()
    {
        var root = RepoRoot();
        var subjectDirs = new[] { "MediatR", "serilog", "FluentValidation" }
            .Select(subject => (Name: subject,
                Path: Path.Combine(root, "bench", "corpus", subject, "src")))
            .ToList();
        Assert.All(subjectDirs, subject => Assert.True(Directory.Exists(subject.Path),
            $"Required corpus submodule not initialized: {subject.Name}"));

        var perSubject = new List<SubjectVolume>();
        var files = new SortedDictionary<string, FileExpectation>(StringComparer.Ordinal);
        foreach (var subject in subjectDirs)
        {
            perSubject.Add(MeasureSubject(subject.Name, subject.Path, files));
            var last = perSubject[^1];
            Console.WriteLine(
                $"Calor0270-corpus {last.Subject}: {last.Diagnostics} diagnostics across "
                + $"{last.ModulesWithDiagnostics} of {last.ModulesBound} bound modules");
        }

        var measured = new Ledger(
            ScopeText,
            BindRuleText,
            perSubject.Sum(s => s.Diagnostics),
            perSubject.Sum(s => s.ModulesWithDiagnostics),
            perSubject.Sum(s => s.ModulesBound),
            perSubject);

        Console.WriteLine(
            $"Calor0270-corpus aggregate: {measured.AggregateDiagnostics} diagnostics across "
            + $"{measured.AggregateModulesWithDiagnostics} of {measured.AggregateModulesBound} bound modules");

        return new CurrentExpectation(measured, files);
    }

    private static void AssertCurrentMeasurement(CurrentExpectation expectation, CurrentExpectation observation)
    {
        var committed = expectation.Volume;
        var measured = observation.Volume;
        // Anti-vacuity: a ledger that measured nothing would pass every equality
        // below. The corpus binds hundreds of modules; assert the denominator.
        Assert.True(measured.AggregateModulesBound > 250,
            $"Only {measured.AggregateModulesBound} modules bound — the corpus denominator "
            + "collapsed, so the equalities below would be vacuous.");

        Assert.Equal(committed.PerSubject.Count, measured.PerSubject.Count);
        foreach (var (expected, actual) in committed.PerSubject.Zip(measured.PerSubject))
        {
            Assert.Equal(expected.Subject, actual.Subject);
            Assert.True(expected == actual,
                $"Calor0270 volume moved for {actual.Subject}: committed "
                + $"{expected.Diagnostics} diagnostics / {expected.ModulesWithDiagnostics} modules "
                + $"(of {expected.ModulesBound}), measured {actual.Diagnostics} / "
                + $"{actual.ModulesWithDiagnostics} (of {actual.ModulesBound}). A RISE means an "
                + "unactionable receiver shape started reporting and the editor got noisier; a "
                + "FALL means a shape authors could act on went silent. Adjudicate current source-site "
                + "differences explicitly in the current fixture; do not rewrite the immutable historical ledger or treat "
                + "a passing original-compiler replay as current-volume acceptance.");
        }

        Assert.Equal(committed.AggregateDiagnostics, measured.AggregateDiagnostics);
        Assert.Equal(committed.AggregateModulesWithDiagnostics, measured.AggregateModulesWithDiagnostics);
        Assert.Equal(committed.AggregateModulesBound, measured.AggregateModulesBound);
        Assert.Equal(ScopeText, committed.Scope);

        // v0.16 K1 — the rule, in the file, beside the numbers it produced.
        Assert.Equal(BindRuleText, committed.BindRule);
        Assert.Equal(expectation.Files.Keys, observation.Files.Keys);
        foreach (var (file, identity) in expectation.Files)
        {
            var observed = observation.Files[file];
            Assert.True(identity.SourceHash == observed.SourceHash, $"Source input changed: {file}");
            Assert.True(identity.Sites.SequenceEqual(observed.Sites),
                $"Calor0270 named diagnostic sites moved for {file}.\n"
                + $"Expected: {JsonSerializer.Serialize(identity.Sites)}\n"
                + $"Actual: {JsonSerializer.Serialize(observed.Sites)}");
        }
    }

    private static CurrentExpectation ReadCurrentExpectation()
    {
        var expected = JsonSerializer.Deserialize<CurrentExpectation>(File.ReadAllText(Path.Combine(
            RepoRoot(), "tests", "TestData", "Binding", "calor0270-current-expectations.json")))!;
        expected = expected with
        {
            Files = new SortedDictionary<string, FileExpectation>(expected.Files, StringComparer.Ordinal)
        };
        AssertOriginalInputs(expected);
        return expected;
    }

    private static void AssertOriginalInputs(CurrentExpectation expected)
    {
        using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(),
            "bench", "phase0-agent-native", "binder-source-coverage.json")));
        var inputs = original.RootElement.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(364, inputs.Length);
        Assert.Equal(inputs.Select(input => input.Name), expected.Files.Keys);
        foreach (var input in inputs)
            Assert.Equal(input.Value.GetProperty("SourceHash").GetString(), expected.Files[input.Name].SourceHash);
    }

    [Fact]
    public void CurrentExpectation_PinsOriginalInputsAndExactVolumes()
    {
        var expected = ReadCurrentExpectation();
        Assert.Equal(260, expected.Volume.AggregateDiagnostics);
        Assert.Equal(48, expected.Volume.AggregateModulesWithDiagnostics);
        Assert.Equal(364, expected.Volume.AggregateModulesBound);
        Assert.Equal(new[]
        {
            new SubjectVolume("MediatR", 29, 4, 36, 0),
            new SubjectVolume("serilog", 17, 9, 112, 0),
            new SubjectVolume("FluentValidation", 214, 35, 216, 0)
        }, expected.Volume.PerSubject);
        foreach (var subject in expected.Volume.PerSubject)
        {
            var files = expected.Files.Where(pair => pair.Key.StartsWith(subject.Subject + "/",
                StringComparison.Ordinal)).Select(pair => pair.Value).ToArray();
            Assert.Equal(subject.ModulesBound, files.Length);
            Assert.Equal(subject.Diagnostics, files.Sum(file => file.Sites.Count));
            Assert.Equal(subject.ModulesWithDiagnostics, files.Count(file => file.Sites.Count > 0));
            Assert.All(files.SelectMany(file => file.Sites), site =>
            {
                Assert.Equal(DiagnosticCode.SignatureUnresolved, site.Code);
                Assert.Equal(site.End - site.Start, site.SpanText.Length);
                Assert.True(site.Start >= 0 && site.Line > 0 && site.Column > 0);
                Assert.NotEmpty(site.Message);
                Assert.NotEmpty(site.SpanText);
            });
        }
        AssertCurrentMeasurement(expected, expected);
    }

    [Theory]
    [InlineData("counts-only")]
    [InlineData("moved-site")]
    [InlineData("wrong-code")]
    [InlineData("wrong-message")]
    [InlineData("wrong-text")]
    [InlineData("wrong-line")]
    [InlineData("wrong-column")]
    [InlineData("missing-site")]
    [InlineData("extra-site")]
    [InlineData("changed-input")]
    [InlineData("missing-zero-site-file")]
    [InlineData("extra-file")]
    [InlineData("wrong-file")]
    [InlineData("wrong-volume")]
    public void CurrentExpectation_RejectsCountPreservingAndInputMutations(string mutation)
    {
        var expected = ReadCurrentExpectation();
        var files = new SortedDictionary<string, FileExpectation>(expected.Files, StringComparer.Ordinal);
        var (file, identity) = files.First(pair => pair.Value.Sites.Count > 0);
        var sites = identity.Sites.ToList();
        var first = sites[0];
        var volume = expected.Volume;
        switch (mutation)
        {
            case "counts-only":
                foreach (var key in files.Keys.ToArray())
                    files[key] = files[key] with { Sites = [] };
                break;
            case "moved-site": sites[0] = first with { Start = first.Start + 1, End = first.End + 1 }; break;
            case "wrong-code": sites[0] = first with { Code = DiagnosticCode.UndefinedReference }; break;
            case "wrong-message": sites[0] = first with { Message = first.Message + " changed" }; break;
            case "wrong-text": sites[0] = first with { SpanText = first.SpanText + " changed" }; break;
            case "wrong-line": sites[0] = first with { Line = first.Line + 1 }; break;
            case "wrong-column": sites[0] = first with { Column = first.Column + 1 }; break;
            case "missing-site": sites.RemoveAt(0); break;
            case "extra-site": sites.Add(first); break;
            case "changed-input": files[file] = identity with { SourceHash = "changed" }; break;
            case "missing-zero-site-file": files.Remove(files.First(pair => pair.Value.Sites.Count == 0).Key); break;
            case "extra-file": files.Add("MediatR/src/extra.cs", new FileExpectation("extra", [])); break;
            case "wrong-file":
                var zero = files.First(pair => pair.Value.Sites.Count == 0).Key;
                files[zero] = files[zero] with { Sites = sites };
                files[file] = identity with { Sites = [] };
                break;
            case "wrong-volume": volume = volume with { AggregateDiagnostics = volume.AggregateDiagnostics + 1 }; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        if (mutation is "moved-site" or "wrong-code" or "wrong-message" or "wrong-text"
            or "wrong-line" or "wrong-column" or "missing-site" or "extra-site")
            files[file] = identity with { Sites = sites };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertCurrentMeasurement(expected, new CurrentExpectation(volume, files)));
    }

    [Theory]
    [InlineData("changed-hash")]
    [InlineData("missing-key")]
    [InlineData("renamed-key")]
    public void CurrentExpectation_CannotRedefineOriginalInputs(string mutation)
    {
        var expected = ReadCurrentExpectation();
        var files = new SortedDictionary<string, FileExpectation>(expected.Files, StringComparer.Ordinal);
        var (file, identity) = files.First();
        if (mutation == "changed-hash")
            files[file] = identity with { SourceHash = "changed" };
        else
        {
            files.Remove(file);
            if (mutation == "renamed-key")
                files.Add(file + ".renamed", identity);
        }
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertOriginalInputs(expected with { Files = files }));
    }

    [Fact]
    [Trait("Category", "CorpusRequired")]
    public void CurrentSource_ChangedInputWithIdenticalSitesStillFails()
    {
        const string file = "FluentValidation/src/CommonAssemblyInfo.cs";
        var path = Path.Combine(RepoRoot(), "bench", "corpus", file);
        var source = File.ReadAllText(path);
        var expected = ReadCurrentExpectation();
        var changed = MeasureSource(source + "\n// changed input\n", path);
        Assert.Equal(expected.Files[file].Sites, changed.Identity.Sites);
        Assert.NotEqual(expected.Files[file].SourceHash, changed.Identity.SourceHash);
        var files = new SortedDictionary<string, FileExpectation>(expected.Files, StringComparer.Ordinal)
        {
            [file] = changed.Identity
        };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertCurrentMeasurement(expected, expected with { Files = files }));
    }

    [Theory]
    [Trait("Category", "CorpusRequired")]
    [InlineData("FluentValidation/src/FluentValidation.Tests/DependencyInjectionExtensions/ServiceCollectionExtensionsTests.cs",
        "services", 2)]
    [InlineData("FluentValidation/src/FluentValidation/Internal/CollectionPropertyRule.cs", "validators", 2)]
    [InlineData("FluentValidation/src/FluentValidation/ValidatorOptions.cs", "chain", 1)]
    [InlineData("serilog/src/Serilog/Settings/KeyValuePairs/SettingValueConversions.cs", "rawMemberName", 1)]
    [InlineData("serilog/src/Serilog/Settings/KeyValuePairs/SettingValueConversions.cs", "shortAccessorTypeName", 1)]
    [InlineData("serilog/src/Serilog/Settings/KeyValuePairs/SettingValueConversions.cs", "extraQualifiers", 1)]
    public void CurrentSource_UnresolvedReceiversRetainActualProducersAndCalls(
        string file, string receiver, int warningCount)
    {
        var observed = MeasureFile(Path.Combine(RepoRoot(), "bench", "corpus", file));
        var source = CSharpSyntaxTree.ParseText(observed.Source, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        var emitted = CSharpSyntaxTree.ParseText(
            new Compiler.CodeGen.CSharpEmitter().Emit(observed.Module)).GetRoot();
        var originalBindings = Bindings(source);
        var currentBindings = Bindings(emitted);
        Assert.NotEmpty(originalBindings);
        Assert.Equal(originalBindings.Length, currentBindings.Length);
        Assert.Equal(originalBindings.Select(binding => ScopedOperationShape(binding.Initializer!.Value)),
            currentBindings.Select(binding => ScopedOperationShape(binding.Initializer!.Value)));
        Assert.All(currentBindings, binding =>
            Assert.Equal("var", Assert.IsType<VariableDeclarationSyntax>(binding.Parent).Type.ToString()));

        var originalCalls = Calls(source);
        Assert.NotEmpty(originalCalls);
        Assert.Equal(originalCalls, Calls(emitted));
        Assert.Equal(warningCount, observed.Identity.Sites.Count(site => site.SpanText == receiver));
        Assert.All(observed.Identity.Sites.Where(site => site.SpanText == receiver), site =>
            Assert.StartsWith($"Receiver '{receiver}' is an inferred local", site.Message));
        Assert.Equal(ReadCurrentExpectation().Files[file].Sites, observed.Identity.Sites);

        VariableDeclaratorSyntax[] Bindings(SyntaxNode root) => root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>().Where(binding => binding.Identifier.ValueText == receiver).ToArray();
        string[] Calls(SyntaxNode root) => root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax
                { Expression: IdentifierNameSyntax identifier } && identifier.Identifier.ValueText == receiver)
            .Select(call => ScopedOperationShape(call.Expression)).OrderBy(shape => shape, StringComparer.Ordinal).ToArray();
    }

    [Theory]
    [Trait("Category", "CorpusRequired")]
    [InlineData("FluentValidation/src/FluentValidation.Tests/AccessorCacheTests.cs", "s", "Elapsed", 1)]
    [InlineData("MediatR/src/MediatR/Mediator.cs", "i", "IsGenericType", 2)]
    public void CurrentSource_UnsupportedMetadataReadsRetainActualReceiverAndMember(
        string file, string receiver, string member, int warningCount)
    {
        var observed = MeasureFile(Path.Combine(RepoRoot(), "bench", "corpus", file));
        var source = CSharpSyntaxTree.ParseText(observed.Source, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        var emitted = CSharpSyntaxTree.ParseText(
            new Compiler.CodeGen.CSharpEmitter().Emit(observed.Module)).GetRoot();
        var originalReads = Reads(source);
        Assert.Equal(warningCount, originalReads.Length);
        Assert.Equal(originalReads, Reads(emitted));
        var diagnostics = observed.Identity.Sites.Where(site => site.SpanText == $"{receiver}.{member}").ToArray();
        Assert.Equal(warningCount, diagnostics.Length);
        Assert.All(diagnostics, site => Assert.Equal(
            "Metadata member resolution incomplete: The member result is outside supported scalar reference reads.",
            site.Message));
        Assert.Equal(ReadCurrentExpectation().Files[file].Sites, observed.Identity.Sites);

        string[] Reads(SyntaxNode root) => root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(read => read.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == receiver && read.Name.Identifier.ValueText == member)
            .Select(ScopedOperationShape).ToArray();
    }

    private static string ScopedOperationShape(SyntaxNode node) =>
        string.Join("/", node.Ancestors().OfType<MethodDeclarationSyntax>()
            .Select(method => method.Identifier.ValueText).Reverse()) + "|" + OperationShape(node);

    private static string OperationShape(SyntaxNode node) =>
        node is ParenthesizedExpressionSyntax parenthesized
            ? OperationShape(parenthesized.Expression)
            : node.Kind() + "[" + string.Join(",", node.ChildNodesAndTokens().Select(child =>
                child.IsNode ? OperationShape(child.AsNode()!) : child.Kind() + ":" + child.AsToken().ValueText)) + "]";

    /// <summary>
    /// The ledger's aggregate, restated so that a change of schema or of rule is
    /// observable rather than merely asserted in a PR body. K1 registered the
    /// pre-K1 values (193 / 38 / 305) to show it changed the schema and nothing
    /// else; W3(a) is the first PR entitled to move them, and did.
    ///
    /// <para><b>Moved by v0.16 W3(a) (#903, PR #1125), as K1 predicted here.</b>
    /// <c>AggregateModulesBound</c> is "modules that PARSE" = 364 − parse
    /// failures; W3(a) recovered all 59 (clusters 1–2 were registered for 57;
    /// cluster 3's two modules came with them), so it is now 364 — every subject
    /// module parses. The newly-parsing modules also carry Calor0270 Infos, which
    /// is the whole of the 193 → 315 move; the Calor0425 ledger's
    /// <c>ExcludedParseFailed</c> flip is the same event seen through the other
    /// ledger. Both were REGENERATED through their documented hooks
    /// (<c>CALOR_REGENERATE_CALOR0270_LEDGER=1</c>), not edited, and the
    /// per-subject causes are named in #1125's PR body.</para>
    /// </summary>
    /// <para>v0.17 R4(a) MOVED THE FIRST TWO, downward. Typing a `var` bound to
    /// an invocation gives the next call in a chain a receiver to resolve
    /// against, so fewer signatures go unresolved: 315 -> 302 diagnostics over
    /// 39 -> 35 modules, entirely in MediatR (26 -> 17) and FluentValidation
    /// (255 -> 251). A FALL here is an improvement, and the ledger was
    /// regenerated through its documented hook with the cause named — the same
    /// discipline #1125 followed. What this test guards is unchanged: that the
    /// bind rule is recorded, is this ledger's own, is NOT the Calor0425
    /// ledger's, and that the K1 hook does not regenerate this file.</para>
    /// <para>`ModulesBound` stays 364 exactly: every module still parses, which
    /// is the property gate 9's parse leg holds.</para>
    /// <para>v0.19 #1191: conditional receiver preservation changes 302 -> 299
    /// diagnostics (serilog 34 -> 33, FluentValidation 251 -> 249).
    /// The regenerated ledger still covers all 364 modules.</para>
    /// <para>The combined #1195 repair moves one additional unresolved `directives`
    /// receiver in KeyValuePairSettings.cs behind explicit query interop:
    /// 299 -> 298, with serilog 33 -> 32. This is not newly resolved native
    /// analysis; all 364 parsed modules remain in the denominator.</para>
    // #1384: 15 explicit unsupported metadata-member Infos; no receiver is
    // classified safe and existing packed-reference errors remain unchanged.
    // #1385: scalar STRING activation and its flow repairs resolve one
    // FluentValidation actionable receiver: 313/37 -> 312/36. All 364 parsed
    // modules remain in the denominator.
    // #1132 (0.25 F5): `var msg = result.Errors[0].ErrorMessage;` in FluentValidation.Tests/
    // GreaterThanOrEqualToValidatorTester.cs now converts in place as (§IDX result.Errors 0).ErrorMessage
    // (main: a hoisted temp), and the inferred local `msg` is an unresolved receiver: 312/36 -> 313/37.
    // #943 (0.25 F1): serilog 34/11 -> 16/7. Members whose C# `out var` has no type the per-file
    // semantic model can resolve, or whose statement keeps part of itself as C#, are now preserved
    // as C# (RO-DECL-1), so their unresolved receivers are no longer bound: 313/37 -> 295/33.
    // #1632 (0.27): FluentValidation Internal/CollectionPropertyRule.cs no longer drops the statement
    // after its `AfterValidate:` label; the restored `foreach (var dependentRule in DependentRules)`
    // body reads members of an inferred local, one more unresolved receiver: 295/33 -> 296/33.
    // #1541/#1524 (0.27): object and collection initializers with no exact Calor form are kept as
    // a typed §CS creation, so calls through the receivers they initialize (FluentValidation.Tests
    // `var validator = new TestValidator { … }; validator.Validate(…)`) report SignatureUnresolved:
    // FluentValidation 251/22 -> 330/35, serilog 16/7 -> 18/8 (KeyValuePairSettings.cs): 296/33 -> 377/47.
    // #1677 (0.27): a chained or indexed receiver, and the argument list of a statement call, were
    // converted twice, and the first copy's hoisted binds stayed in the output (each a second call
    // such as `validator.TestValidate(…)`, each reporting its own SignatureUnresolved). Each is now
    // converted once: FluentValidation 330 -> 235, MediatR 29 -> 27, modules unchanged: 377/47 -> 280/47.
    // #1588/#1686: explicit preservation reduces serilog 18 -> 13 and FluentValidation
    // 235 -> 210, not binding precision. All 364 converted modules still parse and bind.
    // 0.30 named-constructor support exposes Serilog's Logger(configureLogger, ...) body:
    // its unresolved lc.CreateLogger receiver adds one Info, 250/45 -> 251/46.
    // This is native-body visibility, not improved type resolution or Length precision.
    private const int AggregateDiagnostics = 251;
    private const int AggregateModulesWithDiagnostics = 46;
    private const int PreK1AggregateModulesBound = 364;

    /// <summary>
    /// v0.16 K1 (roadmap §3.1 K1, §6 row 2) — the schema field, and only the
    /// schema field. This ledger's numbers are NOT regenerated by K1, so the pin
    /// is: the bind rule is recorded, it is this ledger's own rule
    /// (<c>"parsed"</c>), it is not the Calor0425 ledger's rule, and the
    /// aggregate remains the immutable historical pin. Runs without the corpus
    /// submodules — it reads the committed JSON. Current-volume adjudication
    /// must not rewrite this historical schema or its aggregate.
    /// </summary>
    [Fact]
    public void Calor0270Ledger_NamesItsBindRule_AndIsNotRegeneratedByK1()
    {
        var committed = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerPath()))!;

        Assert.Equal(BindRuleText, committed.BindRule);
        Assert.NotEqual(Calor0425CorpusLedgerTests.BindRuleText, committed.BindRule);

        Assert.Equal(AggregateDiagnostics, committed.AggregateDiagnostics);
        Assert.Equal(
            AggregateModulesWithDiagnostics, committed.AggregateModulesWithDiagnostics);
        Assert.Equal(PreK1AggregateModulesBound, committed.AggregateModulesBound);
    }

    private static SubjectVolume MeasureSubject(string name, string srcRoot,
        SortedDictionary<string, FileExpectation> measuredFiles)
    {
        var files = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        int diagnostics = 0, modulesWithDiagnostics = 0, modulesBound = 0;
        foreach (var file in files)
        {
            var observed = MeasureFile(file);
            measuredFiles.Add(name + "/src/" + Path.GetRelativePath(srcRoot, file).Replace('\\', '/'), observed.Identity);
            modulesBound++;
            var count = observed.Identity.Sites.Count;
            diagnostics += count;
            if (count > 0)
                modulesWithDiagnostics++;
        }

        return new SubjectVolume(name, diagnostics, modulesWithDiagnostics, modulesBound, 0);
    }

    private static FileMeasurement MeasureFile(string file) => MeasureSource(File.ReadAllText(file), file);

    private static FileMeasurement MeasureSource(string source, string file)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse,
            SourceCodeKind.Regular, preprocessorSymbols: Array.Empty<string>());
        var conversion = new Compiler.Migration.CSharpToCalorConverter(
            new Compiler.Migration.ConversionOptions
            {
                Fidelity = Compiler.Migration.ConversionFidelity.Lossy,
                PreprocessorMode = Compiler.Migration.PreprocessorConversionMode.SelectActiveBranchLossy,
                ParseOptions = parseOptions,
                DefinedSymbols = Array.Empty<string>(),
                ModuleName = "Calor0270Leg",
                GracefulFallback = true,
                AutoGenerateIds = true
            }).Convert(source, Path.GetFileName(file));
        Assert.False(string.IsNullOrEmpty(conversion.CalorSource), $"Empty conversion: {file}");

        var serialized = RawCSharpFrame.NormalizeLineEndings(conversion.CalorSource);
        var parseDiagnostics = new DiagnosticBag();
        var module = new Parser(
            new Lexer(serialized, parseDiagnostics).TokenizeAllForParser(),
            parseDiagnostics).Parse();
        Assert.False(parseDiagnostics.HasErrors,
            $"Converted corpus input failed parsing: {file}\n{string.Join("\n", parseDiagnostics)}");

        var bindDiagnostics = new DiagnosticBag();
        new Binder(bindDiagnostics).Bind(module);
        var sites = CaptureSites(bindDiagnostics, serialized);
        // Match the original source-coverage input identity, not platform checkout line endings.
        var identity = new FileExpectation(Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(source.Replace("\r\n", "\n")))), sites);
        return new FileMeasurement(source, module, identity);
    }

    private static List<DiagnosticSite> CaptureSites(DiagnosticBag diagnostics, string serialized) =>
        diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.SignatureUnresolved)
            .Select(diagnostic => new DiagnosticSite(diagnostic.Code, diagnostic.Span.Start,
                diagnostic.Span.End, diagnostic.Span.Line, diagnostic.Span.Column, diagnostic.Message,
                serialized.Substring(diagnostic.Span.Start, diagnostic.Span.Length)))
            .OrderBy(site => site.Start).ThenBy(site => site.End).ThenBy(site => site.Message, StringComparer.Ordinal)
            .ToList();

    private sealed record DiagnosticSite(
        string Code, int Start, int End, int Line, int Column, string Message, string SpanText);
    private sealed record FileExpectation(string SourceHash, List<DiagnosticSite> Sites);
    private sealed record CurrentExpectation(Ledger Volume, SortedDictionary<string, FileExpectation> Files);
    private sealed record FileMeasurement(string Source, Compiler.Ast.ModuleNode Module, FileExpectation Identity);

    private sealed record SubjectVolume(
        string Subject,
        int Diagnostics,
        int ModulesWithDiagnostics,
        int ModulesBound,
        int ModulesNotMeasured);

    private sealed record Ledger(
        string Scope,
        /// <summary>v0.16 K1 — <c>"parsed"</c>: every module that PARSES is
        /// bound and counted, with no bind guard. The Calor0425 ledger carries
        /// <c>"propagated"</c>, the shipping compiler's rule.</summary>
        string BindRule,
        int AggregateDiagnostics,
        int AggregateModulesWithDiagnostics,
        int AggregateModulesBound,
        List<SubjectVolume> PerSubject);
}
