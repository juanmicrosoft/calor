using Calor.Compiler.Indexing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Index persistence and — the part that matters — staleness.
///
/// A missing index is harmless: you notice. A STALE index that answers anyway
/// is #788/#883 in a new component, and it is the failure this suite exists to
/// make impossible.
/// </summary>
public sealed class ProjectIndexTests : IDisposable
{
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private string NewProject(params (string Name, string Source)[] files)
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "calor-index-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        foreach (var (name, source) in files)
            File.WriteAllText(Path.Combine(dir, name), source);
        return dir;
    }

    private static ProjectIndexBuilder.Options OptionsFor(string dir, string token = "t") =>
        new(dir, token, ProjectIndexBuilder.DiscoverSources(dir));

    private const string Library = """
        §M{m001:Lib}
          §F{f001:Double:pub} (i32:n) -> i32
            §E{}
            §R (* n INT:2)
        """;

    private const string App = """
        §M{m002:App}
          §F{f001:Run:pub} () -> i32
            §E{}
            §R §C{Double} §A INT:5 §/C
        """;

    // --- staleness: every recorded input must be compared -------------------

    public static TheoryData<string> HeaderInputs()
    {
        var data = new TheoryData<string>();
        foreach (var input in new[]
                 { "compiler", "options", "manifest", "sources", "format", "semantics" })
        {
            data.Add(input);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(HeaderInputs))]
    public void EveryRecordedHeaderInputIsCompared(string input)
    {
        // The discriminating test the scoping doc requires: mutate one recorded
        // input and the index must refuse to be considered fresh. An input the
        // builder records but the check ignores is a silent staleness hole —
        // precisely the defect #788 and #883 were.
        var dir = NewProject(("lib.calr", Library), ("app.calr", App));
        var options = OptionsFor(dir);
        var index = ProjectIndexBuilder.Build(options);
        var inputs = ProjectIndexBuilder.CurrentInputs(options);

        Assert.Equal(
            ProjectIndex.Freshness.Fresh,
            index.CheckFreshness(
                inputs.CompilerHash, inputs.OptionsHash, inputs.ManifestHash, inputs.Files));

        var compiler = inputs.CompilerHash;
        var optionsHash = inputs.OptionsHash;
        var manifest = inputs.ManifestHash;
        var files = new Dictionary<string, string>(inputs.Files, StringComparer.Ordinal);
        var expected = ProjectIndex.Freshness.Fresh;

        switch (input)
        {
            case "compiler":
                compiler = "different";
                expected = ProjectIndex.Freshness.CompilerChanged;
                break;
            case "options":
                optionsHash = "different";
                expected = ProjectIndex.Freshness.OptionsChanged;
                break;
            case "manifest":
                manifest = "different";
                expected = ProjectIndex.Freshness.ManifestChanged;
                break;
            case "sources":
                files["lib.calr"] = "different";
                expected = ProjectIndex.Freshness.SourcesChanged;
                break;
            case "format":
                index.FormatVersion = "0.0-old";
                expected = ProjectIndex.Freshness.FormatChanged;
                break;
            case "semantics":
                index.CompilerSemanticsVersion = "calor-semantics-from-another-era";
                expected = ProjectIndex.Freshness.SemanticsChanged;
                break;
        }

        Assert.Equal(
            expected,
            index.CheckFreshness(compiler, optionsHash, manifest, files));
    }

    // --- v0.15 E5: the effects facet ---------------------------------------

    /// <summary>
    /// E4's 0.15.x obligation (roadmap §4.2 E5), the half E5 discharges:
    /// <c>FunctionBoundType.Row</c> has a production reader — the index records
    /// it for every rowed parameter/return position as <c>BoundRow</c> — and it
    /// AGREES with the <c>§E</c> node's row wherever the row mentions no
    /// <c>eff</c> variable. Where it does, the binder collapses the row to
    /// Unknown (E2b; <c>Binder.BindRow</c>) and the index says so, in the open:
    /// <c>declared e</c> beside <c>bound [unknown]</c>. That collapse is the
    /// half that stays registered. Discriminating revert: make the binder record
    /// pure for a row-less position, or drop the row from the bound type, and
    /// the agreement fails.
    /// </summary>
    [Fact]
    public void BoundPositionRow_AgreesWithTheDeclaredRow_WhereTheBinderDoesNotCollapse()
    {
        var dir = NewProject(("rows.calr", """
            §M{m001:Rows}
              §F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32
                §E{cw}
                §R §C{transform} §A value §/C
              §F{f002:Make:pub} (Func<i32,i32>:g §E{fs:w}) -> Func<i32,i32> §E{fs:w}
                §E{}
                §R g
              §F{f003:Map:pub}<eff e> (Func<i32,i32>:f §E{e}, i32:value) -> i32
                §E{e}
                §R §C{f} §A value §/C
            """));
        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        Assert.Empty(index.Residual.EffectRowsUnavailable);
        var positions = index.EffectRows.Where(row => row.Kind is "parameter" or "return").ToArray();
        Assert.Equal(4, positions.Length);
        foreach (var position in positions)
        {
            Assert.NotNull(position.BoundRow);
            if (position.DeclaredRow.Variables.Count == 0)
                Assert.Equal(position.DeclaredRow.Display, position.BoundRow);
            else
                Assert.Equal("[unknown]", position.BoundRow);
        }

        var transform = Assert.Single(positions, row => row.Name == "transform");
        Assert.Equal("cw", transform.DeclaredRow.Display);
        Assert.Equal("declared-only", transform.Verdict);
        Assert.NotEqual(transform.SymbolId, transform.OwnerSymbolId);

        var make = Assert.Single(positions, row => row.Kind == "return");
        Assert.Equal("fs:w", make.DeclaredRow.Display);
        Assert.Equal(make.SymbolId, make.OwnerSymbolId);

        var polymorphic = Assert.Single(positions, row => row.Name == "f");
        Assert.Equal("e", polymorphic.DeclaredRow.Display);
        Assert.Equal(0, Assert.Single(polymorphic.DeclaredRow.Variables).Ordinal);

        var map = Assert.Single(index.EffectRows, row => row.Name == "Map" && row.Kind == "function");
        Assert.Equal("e", map.DeclaredRow.Display);
        Assert.Equal("fits", map.Verdict);
    }

    /// <summary>
    /// The three-valued row state reaches the facet: interop content makes a
    /// function's effects ASSUMED, and the index carries the reasons the pass
    /// would print in Calor0419 — the "assumption reasons when Assumed" of
    /// design §8.6.
    /// </summary>
    [Fact]
    public void EffectsFacet_RecordsTheAssumedStateWithItsReasons()
    {
        var dir = NewProject(("assumed.calr", "\n§M{m001:Interop}\n  §F{f001:UsesInterop:pub}\n      §O{void}\n      §RAW\nvar x = System.Environment.TickCount;\n§/RAW\n"));
        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        var row = Assert.Single(index.EffectRows, row => row.Name == "UsesInterop");
        Assert.NotNull(row.InferredRow);
        Assert.Equal("assumed", row.InferredRow!.State);
        Assert.StartsWith("[assumed: ", row.InferredRow.Display, StringComparison.Ordinal);
        Assert.Contains(row.InferredRow.Reasons, reason => reason.Contains("interop", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("fits", row.Verdict);
        Assert.Equal("Calor0419", row.DiagnosticCode);
    }

    /// <summary>
    /// A file the binder reports errors for gets no rows and a residual entry —
    /// the CLI skips the effect pass on such a file, and an index answering
    /// with rows the CLI never computed would be an answer with no producer.
    /// </summary>
    [Fact]
    public void EffectsFacet_SkipsAFileWithBinderErrors_AndSaysSo()
    {
        var dir = NewProject(
            ("lib.calr", Library),
            ("broken.calr", """
                §M{m002:Broken}
                  §F{f001:Uses:pub} () -> i32
                    §E{}
                    §R (+ undefinedName INT:1)
                """));
        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        Assert.DoesNotContain(index.EffectRows, row => row.File == "broken.calr");
        Assert.Contains(index.EffectRows, row => row.File == "lib.calr" && row.Name == "Double");
        var entry = Assert.Single(index.Residual.EffectRowsUnavailable);
        Assert.StartsWith("broken.calr: ", entry, StringComparison.Ordinal);
        var uses = Assert.Single(index.FindDeclarations("Uses"));
        Assert.True(index.EffectsAnswerIsPartial(uses.SymbolId, uses.File));
        Assert.Empty(index.FindEffectRows(uses.SymbolId));
    }

    /// <summary>
    /// v0.16 W5 (review #2 M1) — a file whose effect inference stopped at a cap
    /// (Calor0406) gets NO rows and a residual entry, exactly like a file the
    /// binder rejected: <c>calor build</c> fails on it, and the pass's facts are
    /// partial by its own account, so the index must not present them as rows.
    /// Driven with an injected cap on three-hop fixtures (the SCC ring at cap 1,
    /// the E3b rank-1 chain at worklist cap 2); the other file's rows are
    /// unaffected, and the residual survives the JSON round-trip.
    /// </summary>
    [Theory]
    [InlineData("scc")]
    [InlineData("worklist")]
    public void EffectsFacet_SkipsAFileWhoseInferenceDidNotConverge_AndSaysSo(string site)
    {
        var (source, subject) = site == "scc"
            ? (ThreeHopMutualRecursion, "A")
            : (ThreeHopRank1Chain, "Top2");
        var dir = NewProject(("lib.calr", Library), ("capped.calr", source));
        var options = site == "scc"
            ? OptionsFor(dir) with { SccFixpointIterationCap = 1 }
            : OptionsFor(dir) with { InstantiatedChargeIterationCap = 2 };

        var index = ProjectIndexBuilder.Build(options);

        Assert.DoesNotContain(index.EffectRows, row => row.File == "capped.calr");
        Assert.Contains(index.EffectRows, row => row.File == "lib.calr" && row.Name == "Double");
        var entry = Assert.Single(index.Residual.EffectRowsUnavailable);
        Assert.Equal("capped.calr: effect inference did not converge (Calor0406), facts not recorded", entry);
        var declaration = Assert.Single(index.FindDeclarations(subject));
        Assert.True(index.EffectsAnswerIsPartial(declaration.SymbolId, declaration.File));
        Assert.Empty(index.FindEffectRows(declaration.SymbolId));

        // Round-trip: the residual is data, not a transient.
        var output = Path.Combine(dir, "index-out");
        index.Save(output);
        var (loaded, status) = ProjectIndex.Load(output);
        Assert.Equal(ProjectIndex.Freshness.Fresh, status);
        Assert.Equal(entry, Assert.Single(loaded!.Residual.EffectRowsUnavailable));

        // Negative: at the default caps the same file converges and is indexed.
        var converged = ProjectIndexBuilder.Build(OptionsFor(dir));
        Assert.Empty(converged.Residual.EffectRowsUnavailable);
        Assert.Contains(converged.EffectRows, row => row.File == "capped.calr" && row.Name == subject);
    }

    /// <summary>
    /// The contract of <see cref="CliTestHarness.StampForChildCli"/>, pinned
    /// directly: the hash it stamps is the one a child <c>calor</c> computes for
    /// itself. Built by the child (<c>calor index build</c>) and read back, so a
    /// helper that hashed the wrong file — the test host's instrumented copy, a
    /// stale path, the runtime assembly — is red here rather than showing up as
    /// a puzzling "index unusable" in whichever test used it next. Lane-neutral:
    /// nothing in-process contributes the value being compared.
    /// </summary>
    [Fact]
    public void StampForChildCli_UsesTheHashTheChildComputesForItself()
    {
        var dir = NewProject(("lib.calr", Library));

        var built = CliTestHarness.RunCli(dir, "index", "build", dir);
        Assert.True(built.ExitCode == 0, built.StdOut + built.StdErr);

        var (fromChild, status) = ProjectIndex.Load(Commands.IndexCommand.DefaultOutputDirectory(dir));
        Assert.Equal(ProjectIndex.Freshness.Fresh, status);
        Assert.NotNull(fromChild);
        Assert.NotEqual("", fromChild!.CompilerHash);
        Assert.Equal(fromChild.CompilerHash, CliTestHarness.ChildCompilerHash());
    }

    /// <summary>
    /// …and <c>calor query effects</c> on a symbol in that file answers with the
    /// residual, not a row. The index is built with the injected cap and saved
    /// where the CLI looks; the CLI finds it fresh (the cap is not an input) and
    /// answers from it.
    ///
    /// <para>The index is stamped with the child's compiler hash before it is
    /// saved (<see cref="CliTestHarness.StampForChildCli"/>): under
    /// <c>--collect:"XPlat Code Coverage"</c> the collector instruments the test
    /// host's copy of <c>calor.dll</c>, so an index built in-process would
    /// otherwise be refused by the child with <c>index unusable — the compiler
    /// changed</c>, which is how this pin failed in the coverage lane alone.
    /// <c>--no-build</c> is kept deliberately: it makes any remaining mismatch a
    /// hard error rather than a silent rebuild that would erase the residual, so
    /// the pin cannot pass by accident — and the assertions below check the
    /// residual line itself, not merely the exit code.</para>
    /// </summary>
    [Fact]
    public void QueryEffects_OnAFileWhoseInferenceDidNotConverge_ReportsTheResidual()
    {
        var dir = NewProject(("lib.calr", Library), ("capped.calr", ThreeHopMutualRecursion));
        var options = new ProjectIndexBuilder.Options(
            dir, "index-v1", ProjectIndexBuilder.DiscoverSources(dir)) { SccFixpointIterationCap = 1 };
        var index = ProjectIndexBuilder.Build(options);
        CliTestHarness.StampForChildCli(index);
        index.Save(Commands.IndexCommand.DefaultOutputDirectory(dir));

        var text = CliTestHarness.RunCli(dir, "query", "effects", "A", "--project", dir, "--no-build");
        Assert.True(
            text.StdErr.Length == 0,
            $"the child must accept the index; it said: {text.StdErr}");
        Assert.Equal(1, text.ExitCode);
        Assert.Contains(
            "function A — effect inference did not converge (Calor0406), facts not recorded",
            text.StdOut);
        Assert.DoesNotContain("declared:", text.StdOut);

        // The control: the un-capped file answers with a row through the same path.
        var control = CliTestHarness.RunCli(dir, "query", "effects", "Double", "--project", dir, "--no-build");
        Assert.True(control.ExitCode == 0, control.StdOut + control.StdErr);
        Assert.Contains("declared: [pure]", control.StdOut);
        Assert.DoesNotContain("did not converge", control.StdOut);
    }

    /// <summary>
    /// A → B → C → A, all printing (the index's binder has no BCL references, so
    /// the fixture stays on core statements). One effect shared by the ring
    /// means round 1 changes every member, so a cap of 1 fires.
    /// </summary>
    private const string ThreeHopMutualRecursion = """
        §M{m003:Ring}
          §F{f001:A:pub} (i32:n) -> i32
            §E{cw}
            §P "a"
            §IF{if1} (> n 0)
              §R §C{B} §A (- n 1) §/C
            §R n
          §F{f002:B:pub} (i32:n) -> i32
            §E{cw}
            §P "b"
            §IF{if2} (> n 0)
              §R §C{C} §A (- n 1) §/C
            §R n
          §F{f003:C:pub} (i32:n) -> i32
            §E{cw}
            §P "c"
            §IF{if3} (> n 0)
              §R §C{A} §A (- n 1) §/C
            §R n
        """;

    private const string ThreeHopRank1Chain = """
        §M{m004:Chain}
          §F{f001:Run:pub}<eff e> (Func<i32>:g §E{e}) -> i32
            §E{e}
            §R §C{g}
          §F{f002:Outer:pub} (Func<i32>:h §E{cw}) -> i32
            §E{cw}
            §R §C{Run} §A h §/C
          §F{f003:Top:pub} (Func<i32>:q §E{cw}) -> i32
            §E{cw}
            §R §C{Outer} §A q §/C
          §F{f004:Top2:pub} (Func<i32>:r §E{cw}) -> i32
            §E{cw}
            §R §C{Top} §A r §/C
        """;

    [Fact]
    public void AddingOrRemovingAFileIsStale()
    {
        var dir = NewProject(("lib.calr", Library), ("app.calr", App));
        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        File.WriteAllText(Path.Combine(dir, "extra.calr"), """
            §M{m003:Extra}
              §F{f001:Nothing:pub} () -> i32
                §E{}
                §R INT:0
            """);
        var afterAdd = ProjectIndexBuilder.CurrentInputs(OptionsFor(dir));
        Assert.Equal(
            ProjectIndex.Freshness.SourcesChanged,
            index.CheckFreshness(
                afterAdd.CompilerHash, afterAdd.OptionsHash, afterAdd.ManifestHash, afterAdd.Files));

        File.Delete(Path.Combine(dir, "extra.calr"));
        File.Delete(Path.Combine(dir, "app.calr"));
        var afterDelete = ProjectIndexBuilder.CurrentInputs(OptionsFor(dir));
        Assert.Equal(
            ProjectIndex.Freshness.SourcesChanged,
            index.CheckFreshness(
                afterDelete.CompilerHash, afterDelete.OptionsHash,
                afterDelete.ManifestHash, afterDelete.Files));
    }

    // --- persistence -------------------------------------------------------

    [Fact]
    public void SaveAndLoadRoundTripsContents()
    {
        var dir = NewProject(("lib.calr", Library), ("app.calr", App));
        var built = ProjectIndexBuilder.Build(OptionsFor(dir));
        var output = Path.Combine(dir, "obj", "calor");
        built.Save(output);

        var (loaded, status) = ProjectIndex.Load(output);
        Assert.Equal(ProjectIndex.Freshness.Fresh, status);
        Assert.NotNull(loaded);
        Assert.Equal(built.Declarations.Count, loaded!.Declarations.Count);
        Assert.Equal(built.CallEdges.Count, loaded.CallEdges.Count);
        Assert.Equal(built.Files, loaded.Files);

        var inputs = ProjectIndexBuilder.CurrentInputs(OptionsFor(dir));
        Assert.Equal(
            ProjectIndex.Freshness.Fresh,
            loaded.CheckFreshness(
                inputs.CompilerHash, inputs.OptionsHash, inputs.ManifestHash, inputs.Files));
    }

    [Fact]
    public void CorruptIndexIsUnreadableRatherThanTrusted()
    {
        var dir = NewProject(("lib.calr", Library));
        var output = Path.Combine(dir, "obj", "calor");
        Directory.CreateDirectory(output);
        File.WriteAllText(ProjectIndex.PathFor(output), "{ this is not json");

        var (index, status) = ProjectIndex.Load(output);
        Assert.Null(index);
        Assert.Equal(ProjectIndex.Freshness.Unreadable, status);
    }

    [Fact]
    public void BuildIsDeterministicRegardlessOfInputOrder()
    {
        // Gate 2 compares index contents byte-for-byte, so ordering may not
        // follow the order files happened to be enumerated in.
        var dir = NewProject(("lib.calr", Library), ("app.calr", App));
        var sources = ProjectIndexBuilder.DiscoverSources(dir);

        var forward = ProjectIndexBuilder.Build(
            new ProjectIndexBuilder.Options(dir, "t", sources));
        var reversed = ProjectIndexBuilder.Build(
            new ProjectIndexBuilder.Options(dir, "t", sources.Reverse().ToArray()));

        var a = Path.Combine(dir, "a");
        var b = Path.Combine(dir, "b");
        forward.Save(a);
        reversed.Save(b);
        Assert.Equal(
            File.ReadAllText(ProjectIndex.PathFor(a)),
            File.ReadAllText(ProjectIndex.PathFor(b)));
    }

    // --- residual ----------------------------------------------------------

    [Fact]
    public void UnresolvedAndAmbiguousCallsAreNamedNotDropped()
    {
        // An index that reports its edges and stays silent about what it could
        // not resolve is the shape this project keeps paying for.
        var dir = NewProject(
            ("a.calr", """
                §M{m001:A}
                  §F{f001:Shared:pub} () -> i32
                    §E{}
                    §R INT:1
                  §F{f002:Caller:pub} () -> i32
                    §E{}
                    §R §C{Missing} §/C
                """),
            ("b.calr", """
                §M{m002:B}
                  §F{f001:Shared:pub} () -> i32
                    §E{}
                    §R INT:2
                """),
            ("c.calr", """
                §M{m003:C}
                  §F{f001:Ask:pub} () -> i32
                    §E{}
                    §R §C{Shared} §/C
                """));

        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        Assert.False(index.Residual.IsEmpty);
        Assert.Contains(index.Residual.UnresolvedCalls, entry => entry.Target == "Missing");
        // `Shared` is declared twice, so the cross-file call from c.calr cannot
        // be attributed — and the ambiguity is named, not merely counted.
        Assert.Contains("Shared", index.Residual.AmbiguousCallees);
        Assert.DoesNotContain(
            index.CallEdges,
            edge => edge.File == "c.calr");
    }

    [Fact]
    public void UnreadableFilesAreReportedNotSilentlySkipped()
    {
        var dir = NewProject(
            ("good.calr", Library),
            ("broken.calr", "§M{m009:Broken}\n  this is not Calor\n"));

        var index = ProjectIndexBuilder.Build(OptionsFor(dir));
        Assert.Contains("broken.calr", index.Residual.UnreadableFiles);
        Assert.Contains(index.Declarations, declaration => declaration.Name == "Double");
    }

    // --- contents ----------------------------------------------------------

    [Fact]
    public void CrossFileCallProducesAnEdgeWithItsCaller()
    {
        var dir = NewProject(("lib.calr", Library), ("app.calr", App));
        var index = ProjectIndexBuilder.Build(OptionsFor(dir));

        var callee = Assert.Single(
            index.Declarations.Where(declaration => declaration.Name == "Double"));
        var caller = Assert.Single(
            index.Declarations.Where(declaration => declaration.Name == "Run"));
        var edge = Assert.Single(index.CallEdges);

        Assert.Equal(caller.SymbolId, edge.CallerSymbolId);
        Assert.Equal(callee.SymbolId, edge.CalleeSymbolId);
        Assert.Equal("app.calr", edge.File);
        Assert.True(index.Residual.IsEmpty);
    }
}
