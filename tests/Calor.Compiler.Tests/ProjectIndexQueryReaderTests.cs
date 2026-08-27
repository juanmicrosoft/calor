using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Indexing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.16 E7 — the reader every query surface goes through
/// (<see cref="ProjectIndexQueryReader"/>): resolution (a stale index is
/// rebuilt or refused, never answered from), subject lookup (an ambiguous
/// name is refused, never guessed), the four facets as records, and the
/// error paths the CLI and the MCP tool must report identically. The JSON
/// shape each record serialises to is pinned here so a consumer's parser
/// cannot be broken silently.
/// </summary>
public sealed class ProjectIndexQueryReaderTests : IDisposable
{
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private string TempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "calor-reader-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    private string Fixture()
    {
        var dir = TempDir();
        var corpus = Path.Combine(
            CliTestHarness.FindRepoRoot(), "tests", "TestData", "QueryCorpus", "project");
        foreach (var source in Directory.GetFiles(corpus, "*.calr"))
            File.Copy(source, Path.Combine(dir, Path.GetFileName(source)));
        return dir;
    }

    private static ProjectIndex BuildAndSave(string dir, string? output = null)
    {
        var index = ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
            dir, ProjectIndexQueryReader.OptionsToken, ProjectIndexBuilder.DiscoverSources(dir)));
        index.Save(output ?? IndexCommand.DefaultOutputDirectory(dir));
        return index;
    }

    /// <summary>Rewrites the on-disk index as an older format would have (header only).</summary>
    private static void DowngradeFormat(string dir)
    {
        var output = IndexCommand.DefaultOutputDirectory(dir);
        var (index, _) = ProjectIndex.Load(output);
        Assert.NotNull(index);
        index!.FormatVersion = "3.0";
        index.Save(output);
        var (reloaded, _) = ProjectIndex.Load(output);
        Assert.Equal("3.0", reloaded!.FormatVersion);
    }

    private static IndexedDeclaration Subject(ProjectIndex index, string name, string? inFile = null)
    {
        var lookup = ProjectIndexQueryReader.ResolveSubject(index, name, inFile);
        Assert.NotNull(lookup.Subject);
        return lookup.Subject!;
    }

    // --- resolution ---------------------------------------------------------

    [Fact]
    public void Resolve_DirectoryMissing_ReportsTheCliError()
    {
        var missing = Path.Combine(TempDir(), "nope");
        Assert.Null(ProjectIndexQueryReader.Resolve(missing, noBuild: false, out var error));
        Assert.Equal($"Error: directory not found: {missing}", error);
    }

    [Fact]
    public void Resolve_NoSources_ReportsTheCliError()
    {
        var dir = TempDir();
        Assert.Null(ProjectIndexQueryReader.Resolve(dir, noBuild: false, out var error));
        Assert.Equal($"Error: no .calr files under {dir}", error);
    }

    [Fact]
    public void Resolve_MissingIndexWithNoBuild_RefusesAndNamesTheReason()
    {
        var dir = Fixture();
        Assert.Null(ProjectIndexQueryReader.Resolve(dir, noBuild: true, out var error));
        Assert.Equal(
            "Error: index unusable — no index has been built. Run `calor index build` (or drop --no-build).",
            error);
        Assert.False(File.Exists(ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(dir))));
    }

    [Fact]
    public void Resolve_MissingIndex_BuildsAndWritesIt()
    {
        var dir = Fixture();
        var index = ProjectIndexQueryReader.Resolve(dir, noBuild: false, out var error);
        Assert.Null(error);
        Assert.NotNull(index);
        Assert.True(File.Exists(ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(dir))));
        Assert.NotEmpty(index!.FindDeclarations("Scale"));
    }

    [Fact]
    public void Resolve_FreshIndex_IsReturnedWithoutRewriting()
    {
        var dir = Fixture();
        BuildAndSave(dir);
        var path = ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(dir));
        var before = File.ReadAllBytes(path);
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var index = ProjectIndexQueryReader.Resolve(dir, noBuild: true, out var error);

        Assert.Null(error);
        Assert.NotNull(index);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Resolve_SourcesChangedWithNoBuild_Refuses()
    {
        var dir = Fixture();
        BuildAndSave(dir);
        File.AppendAllText(Path.Combine(dir, "app.calr"), "\n");

        Assert.Null(ProjectIndexQueryReader.Resolve(dir, noBuild: true, out var error));
        Assert.Equal(
            "Error: index unusable — the source files changed. Run `calor index build` (or drop --no-build).",
            error);
    }

    [Fact]
    public void Resolve_StaleFormatVersionWithNoBuild_Refuses()
    {
        // The index format is 4.0; an index written by an older compiler must
        // never answer a 4.0 reader's question.
        var dir = Fixture();
        BuildAndSave(dir);
        DowngradeFormat(dir);

        Assert.Null(ProjectIndexQueryReader.Resolve(dir, noBuild: true, out var error));
        Assert.Equal(
            "Error: index unusable — the index format version changed. Run `calor index build` (or drop --no-build).",
            error);
    }

    [Fact]
    public void Resolve_StaleFormatVersion_IsRebuiltToTheCurrentFormat()
    {
        var dir = Fixture();
        BuildAndSave(dir);
        DowngradeFormat(dir);

        var index = ProjectIndexQueryReader.Resolve(dir, noBuild: false, out var error);

        Assert.Null(error);
        Assert.Equal(ProjectIndex.CurrentFormatVersion, index!.FormatVersion);
        var (reloaded, _) = ProjectIndex.Load(IndexCommand.DefaultOutputDirectory(dir));
        Assert.Equal(ProjectIndex.CurrentFormatVersion, reloaded!.FormatVersion);
    }

    [Fact]
    public void Resolve_UnreadableIndexWithNoBuild_Refuses()
    {
        var dir = Fixture();
        BuildAndSave(dir);
        File.WriteAllText(ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(dir)), "{ not json");

        Assert.Null(ProjectIndexQueryReader.Resolve(dir, noBuild: true, out var error));
        Assert.Equal(
            "Error: index unusable — the index file could not be read. Run `calor index build` (or drop --no-build).",
            error);
    }

    [Fact]
    public void Resolve_IndexDirectoryOverride_IsReadAndWrittenThere()
    {
        var dir = Fixture();
        var custom = Path.Combine(dir, "elsewhere");

        Assert.Null(ProjectIndexQueryReader.Resolve(dir, noBuild: true, out _, custom));
        var built = ProjectIndexQueryReader.Resolve(dir, noBuild: false, out var error, custom);
        Assert.Null(error);
        Assert.NotNull(built);
        Assert.True(File.Exists(ProjectIndex.PathFor(custom)));
        Assert.False(File.Exists(ProjectIndex.PathFor(IndexCommand.DefaultOutputDirectory(dir))));
        Assert.NotNull(ProjectIndexQueryReader.Resolve(dir, noBuild: true, out _, custom));
    }

    [Fact]
    public void OptionsToken_MatchesWhatCalorIndexBuildWrites()
    {
        // A query that rebuilt under a different token would see every index
        // `calor index build` wrote as stale and rebuild it on every question.
        var dir = Fixture();
        var run = CliTestHarness.RunCli(dir, "index", "build", dir);
        Assert.True(run.ExitCode == 0, run.StdOut + run.StdErr);

        Assert.NotNull(ProjectIndexQueryReader.Resolve(dir, noBuild: true, out var error));
        Assert.Null(error);
    }

    // --- subject lookup -----------------------------------------------------

    [Fact]
    public void ResolveSubject_UnknownName_IsNotFound()
    {
        var index = BuildAndSave(Fixture());
        var lookup = ProjectIndexQueryReader.ResolveSubject(index, "NoSuchName", null);
        Assert.True(lookup.NotFound);
        Assert.False(lookup.Ambiguous);
        Assert.Null(lookup.Subject);
    }

    [Fact]
    public void ResolveSubject_UniqueName_IsTheDeclaration()
    {
        var index = BuildAndSave(Fixture());
        var lookup = ProjectIndexQueryReader.ResolveSubject(index, "Scale", null);
        Assert.NotNull(lookup.Subject);
        Assert.Equal("math.calr", lookup.Subject!.File);
        Assert.Single(lookup.Candidates);
    }

    [Fact]
    public void ResolveSubject_AmbiguousName_IsRefusedUntilNarrowed()
    {
        var index = BuildAndSave(Fixture());

        var bare = ProjectIndexQueryReader.ResolveSubject(index, "Shared", null);
        Assert.True(bare.Ambiguous);
        Assert.Null(bare.Subject);
        Assert.Equal(2, bare.Candidates.Count);

        var narrowed = ProjectIndexQueryReader.ResolveSubject(index, "Shared", "ambiguous2.calr");
        Assert.NotNull(narrowed.Subject);
        Assert.Equal("ambiguous2.calr", narrowed.Subject!.File);

        // A file that declares no such name narrows to nothing — still refused.
        var wrong = ProjectIndexQueryReader.ResolveSubject(index, "Shared", "math.calr");
        Assert.True(wrong.Ambiguous);
        Assert.Null(wrong.Subject);
    }

    [Fact]
    public void AmbiguityLines_AreTheCliRefusal()
    {
        var index = BuildAndSave(Fixture());
        var lookup = ProjectIndexQueryReader.ResolveSubject(index, "Shared", null);
        Assert.Equal(
            new[]
            {
                "Error: 'Shared' is declared in 2 places; narrow it with --in-file:",
                "  ambiguous.calr:2:11 function Shared",
                "  ambiguous2.calr:2:11 function Shared",
            },
            ProjectIndexQueryReader.AmbiguityLines("Shared", lookup.Candidates));
    }

    // --- facets -------------------------------------------------------------

    [Fact]
    public void Callers_OrderedByPosition_NotPartialWhenEveryCallResolved()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.Callers(index, Subject(index, "Scale"));

        Assert.Equal("callers", answer.Facet);
        Assert.Equal("math.calr:2:11 function Scale", answer.Subject);
        Assert.Equal(
            new[] { "app.calr:2:Run", "math.calr:5:ScaleTwice" },
            answer.Declarations.Select(d => $"{d.File}:{d.Line}:{d.Name}"));
        Assert.False(answer.Partial);
        Assert.Null(answer.Residual);
    }

    [Fact]
    public void Callers_OfAnAmbiguousName_IsPartialAndCarriesTheResidual()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.Callers(index, Subject(index, "Shared", "ambiguous2.calr"));

        Assert.Equal(["ambiguous2.calr:5:AsksShared"], answer.Declarations.Select(d => $"{d.File}:{d.Line}:{d.Name}"));
        Assert.True(answer.Partial);
        Assert.NotNull(answer.Residual);
        Assert.Contains("Shared", answer.Residual!.AmbiguousCallees);
    }

    [Fact]
    public void Callees_IsTheDeclarationSet_NotTheCallCount()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.Callees(index, Subject(index, "ScaleTwice"));

        Assert.Equal("callees", answer.Facet);
        Assert.Equal(["math.calr:2:Scale"], answer.Declarations.Select(d => $"{d.File}:{d.Line}:{d.Name}"));
        Assert.False(answer.Partial);
    }

    [Fact]
    public void Callees_OfAnUnresolvedCall_IsPartial()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.Callees(index, Subject(index, "AsksMissing"));

        Assert.Empty(answer.Declarations);
        Assert.True(answer.Partial);
        Assert.Contains(answer.Residual!.UnresolvedCalls, call => call.Target == "NoSuchFunction");
    }

    [Fact]
    public void Impact_SeededByDeclaration_CountsAffectedFiles()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.Impact(index, Subject(index, "Scale"));

        Assert.Equal("math.calr:2:11 function Scale", answer.Subject);
        Assert.NotNull(answer.SymbolId);
        Assert.Null(answer.File);
        Assert.Equal(
            new[] { "app.calr:2:Run", "math.calr:5:ScaleTwice" },
            answer.Affected.Select(d => $"{d.File}:{d.Line}:{d.Name}"));
        Assert.Equal(2, answer.AffectedFiles);
        Assert.True(answer.Partial);
        Assert.NotNull(answer.Residual);
    }

    [Fact]
    public void ImpactOfFile_UnknownFile_ReportsTheCliError()
    {
        var index = BuildAndSave(Fixture());
        Assert.Null(ProjectIndexQueryReader.ImpactOfFile(index, "nope.calr", out var error));
        Assert.Equal("Error: 'nope.calr' is not an indexed source file.", error);
    }

    [Fact]
    public void ImpactOfFile_NormalisesBackslashes()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.ImpactOfFile(index, "math.calr", out var error);
        Assert.Null(error);
        Assert.Equal("math.calr", answer!.File);
        Assert.Null(answer.SymbolId);
        Assert.Equal("the whole file math.calr", answer.Subject);
        Assert.Equal(["app.calr:2:Run"], answer.Affected.Select(d => $"{d.File}:{d.Line}:{d.Name}"));
    }

    [Fact]
    public void EffectImpact_HypotheticalRow_CountsCallersThatStopFitting()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Log"), "fs:w", out var error);

        Assert.Null(error);
        Assert.Equal("fs:w", answer!.Row);
        Assert.False(answer.RowIsCurrentDeclared);
        Assert.Equal(
            new[] { "app.calr:11:Leaky:does-not-fit", "app.calr:14:Relay:does-not-fit", "app.calr:17:Fan:does-not-fit" },
            answer.Impacts.Select(i => $"{i.Declaration.File}:{i.Declaration.Line}:{i.Declaration.Name}:{i.Verdict}"));
        Assert.Equal(3, answer.StopFitting);
        Assert.Equal(0, answer.CannotTell);
    }

    [Fact]
    public void EffectImpact_NoRow_DefaultsToTheCurrentDeclaredRow()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Log"), null, out var error);

        Assert.Null(error);
        Assert.Equal("cw", answer!.Row);
        Assert.True(answer.RowIsCurrentDeclared);
        Assert.Equal(1, answer.StopFitting);
        Assert.Equal("[pure]", answer.Impacts.Single(i => i.Declaration.Name == "Leaky").DeclaredRow);
    }

    [Fact]
    public void EffectImpact_EmptyRow_MeansPure()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Log"), "", out var error);
        Assert.Null(error);
        Assert.Equal("[pure]", answer!.Row);
        Assert.Equal(0, answer.StopFitting);
        Assert.All(answer.Impacts, i => Assert.Equal("fits", i.Verdict));
    }

    [Fact]
    public void EffectImpact_RowThatDoesNotParse_ReportsTheCliError()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Log"), "not-a-code", out var error);
        Assert.Null(answer);
        Assert.NotNull(error);
        Assert.StartsWith("Error: --row 'not-a-code' is not a row of effect codes: ", error);
    }

    [Fact]
    public void EffectImpact_CallerWithoutARow_IsCannotTell_NotBroken()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "lib.calr"), """
            §M{m001:Lib}
              §F{f001:Double:pub} (i32:n) -> i32
                §E{cw}
                §P n
                §R (* n INT:2)
            """);
        File.WriteAllText(Path.Combine(dir, "broken.calr"), """
            §M{m002:Broken}
              §F{f001:Uses:pub} () -> i32
                §E{}
                §R (+ §C{Double} §A undefinedName §/C INT:1)
            """);
        var index = BuildAndSave(dir);

        var answer = ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Double"), "fs:w", out var error);
        Assert.Null(error);
        var uses = Assert.Single(answer!.Impacts);
        Assert.Equal("cannot-tell", uses.Verdict);
        Assert.Null(uses.DeclaredRow);
        Assert.Equal(0, answer.StopFitting);
        Assert.Equal(1, answer.CannotTell);

        // And the effects facet on the rowless declaration names the reason.
        var effects = ProjectIndexQueryReader.Effects(index, Subject(index, "Uses"));
        Assert.Empty(effects.Rows);
        Assert.NotNull(effects.Unavailable);
        Assert.Null(effects.Own);
    }

    [Fact]
    public void EffectImpact_SubjectWithoutARowAndNoRowGiven_ReportsTheCliError()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "broken.calr"), """
            §M{m002:Broken}
              §F{f001:Uses:pub} () -> i32
                §E{}
                §R (+ undefinedName INT:1)
            """);
        var index = BuildAndSave(dir);
        var answer = ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Uses"), null, out var error);
        Assert.Null(answer);
        Assert.Equal(
            "Error: no effect row is recorded for broken.calr:2:11 function Uses; pass --row to ask about a hypothetical one.",
            error);
    }

    [Fact]
    public void Effects_CarriesTheIndexesOwnRecords()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.Effects(index, Subject(index, "Leaky"));

        Assert.Equal("app.calr:11:11 function Leaky", answer.Subject);
        var own = Assert.Single(answer.Rows);
        Assert.Same(own, answer.Own);
        Assert.True(ProjectIndexQueryReader.IsOwnRow(own));
        Assert.Equal("does-not-fit", own.Verdict);
        Assert.Equal("Calor0410", own.DiagnosticCode);
        Assert.Equal("does not fit — Calor0410 fires (undeclared: cw)", ProjectIndexQueryReader.DescribeVerdict(own));
        Assert.False(answer.Partial);
        Assert.Null(answer.Residual);
        Assert.Null(answer.Unavailable);
    }

    [Fact]
    public void Effects_PositionRows_AreNotTheOwnRow()
    {
        var index = BuildAndSave(Fixture());
        var answer = ProjectIndexQueryReader.Effects(index, Subject(index, "Twice"));
        Assert.Equal(2, answer.Rows.Count);
        Assert.Equal("Twice", answer.Own!.Name);
        var position = Assert.Single(answer.Rows.Where(row => !ProjectIndexQueryReader.IsOwnRow(row)));
        Assert.Equal("parameter", position.Kind);
        Assert.Equal("g", position.Name);
    }

    [Fact]
    public void ResidualLines_NameEveryKindOfHole()
    {
        var index = BuildAndSave(Fixture());
        var lines = ProjectIndexQueryReader.ResidualLines(index.Residual);
        Assert.StartsWith("query: PARTIAL — this answer may be incomplete.", lines[0]);
        Assert.Contains(lines, line => line == "  unresolved call: asks-across.calr: NoSuchFunction");
        Assert.Contains(lines, line => line == "  ambiguous name: Shared (several declarations share it)");
    }

    // --- JSON shape pins ----------------------------------------------------

    private static JsonElement Data(string envelope)
    {
        using var document = JsonDocument.Parse(envelope);
        var root = document.RootElement;
        Assert.Equal(
            new[] { "version", "command", "diagnostics", "summary", "data" },
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("query", root.GetProperty("command").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        return root.GetProperty("data").Clone();
    }

    private static string[] Keys(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void Json_Callers_Shape()
    {
        var index = BuildAndSave(Fixture());
        var data = Data(QueryCommand.ToJson(ProjectIndexQueryReader.Callers(index, Subject(index, "Scale"))));
        Assert.Equal(new[] { "facet", "subject", "symbolId", "declarations", "partial" }, Keys(data));
        var declaration = data.GetProperty("declarations").EnumerateArray().First();
        Assert.Equal(
            new[] { "symbolId", "name", "kind", "file", "line", "column", "semanticHash" },
            Keys(declaration));
    }

    [Fact]
    public void Json_Callers_PartialCarriesTheResidual()
    {
        var index = BuildAndSave(Fixture());
        var data = Data(QueryCommand.ToJson(
            ProjectIndexQueryReader.Callers(index, Subject(index, "Shared", "ambiguous2.calr"))));
        Assert.Equal(new[] { "facet", "subject", "symbolId", "declarations", "partial", "residual" }, Keys(data));
        Assert.True(data.GetProperty("partial").GetBoolean());
        Assert.Equal(
            new[] { "unreadableFiles", "unresolvedCalls", "ambiguousCallees", "effectRowsUnavailable" },
            Keys(data.GetProperty("residual")));
    }

    [Fact]
    public void Json_Impact_Shape()
    {
        var index = BuildAndSave(Fixture());
        var data = Data(QueryCommand.ToJson(ProjectIndexQueryReader.Impact(index, Subject(index, "Scale"))));
        Assert.Equal(new[] { "subject", "symbolId", "affected", "affectedFiles", "partial", "residual" }, Keys(data));

        var file = Data(QueryCommand.ToJson(ProjectIndexQueryReader.ImpactOfFile(index, "math.calr", out _)!));
        Assert.Equal(new[] { "subject", "file", "affected", "affectedFiles", "partial", "residual" }, Keys(file));
    }

    [Fact]
    public void Json_EffectImpact_Shape()
    {
        var index = BuildAndSave(Fixture());
        var data = Data(QueryCommand.ToJson(
            ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Log"), "fs:w", out _)!));
        Assert.Equal(
            new[] { "subject", "symbolId", "row", "rowIsCurrentDeclared", "impacts", "stopFitting", "cannotTell", "partial", "residual" },
            Keys(data));
        var impact = data.GetProperty("impacts").EnumerateArray().First();
        Assert.Equal(new[] { "declaration", "declaredRow", "verdict" }, Keys(impact));
    }

    [Fact]
    public void Json_Effects_ShapeIsTheV015One_WhenComplete()
    {
        // v0.15 E5 shipped {subject, symbolId, rows, partial}; E7 adds residual
        // and unavailable only when they carry something, so a v0.15 consumer
        // of a complete answer sees the same bytes.
        var index = BuildAndSave(Fixture());
        var data = Data(QueryCommand.ToJson(ProjectIndexQueryReader.Effects(index, Subject(index, "Leaky"))));
        Assert.Equal(new[] { "subject", "symbolId", "rows", "partial" }, Keys(data));
        var row = Assert.Single(data.GetProperty("rows").EnumerateArray());
        Assert.Equal(
            new[] { "symbolId", "ownerSymbolId", "name", "kind", "declared", "declaredRow", "inferredRow", "verdict", "diagnosticCode", "forbidden", "file", "line" },
            Keys(row));
        Assert.Equal("does-not-fit", row.GetProperty("verdict").GetString());
    }

    [Fact]
    public void Json_Effects_PartialAnswerCarriesTheResidual()
    {
        var index = BuildAndSave(Fixture());
        var data = Data(QueryCommand.ToJson(ProjectIndexQueryReader.Effects(index, Subject(index, "AsksMissing"))));
        Assert.Equal(new[] { "subject", "symbolId", "rows", "partial", "residual" }, Keys(data));
        Assert.True(data.GetProperty("partial").GetBoolean());
    }

    // --- text formatter pins ------------------------------------------------

    [Fact]
    public void Text_Callers_MatchesTheCliLines()
    {
        var index = BuildAndSave(Fixture());
        var writer = new StringWriter();
        QueryCommand.WriteDeclarations(writer, ProjectIndexQueryReader.Callers(index, Subject(index, "Scale")));
        Assert.Equal(
            "  app.calr:2:11 function Run" + Environment.NewLine
                + "  math.calr:5:11 function ScaleTwice" + Environment.NewLine
                + "query: 2 caller(s) of math.calr:2:11 function Scale" + Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void Text_Callers_NoneFound()
    {
        var index = BuildAndSave(Fixture());
        var writer = new StringWriter();
        QueryCommand.WriteDeclarations(writer, ProjectIndexQueryReader.Callers(index, Subject(index, "Unused")));
        Assert.Equal("query: no callers found for app.calr:5:11 function Unused" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void Text_ImpactOfFile_SaysItIsFileGrained()
    {
        var index = BuildAndSave(Fixture());
        var writer = new StringWriter();
        QueryCommand.WriteImpact(writer, ProjectIndexQueryReader.ImpactOfFile(index, "math.calr", out _)!);
        var lines = writer.ToString().Split(Environment.NewLine);
        Assert.Equal("  app.calr:2:11 function Run", lines[0]);
        Assert.Equal("impact: 1 declaration(s) in 1 file(s) affected by a change to the whole file math.calr", lines[1]);
        Assert.StartsWith("impact: file-grained — a change to ANY declaration in this file", lines[2]);
        Assert.StartsWith("query: PARTIAL", lines[3]);
    }

    [Fact]
    public void Text_EffectImpact_CurrentDeclaredRowIsNamedAsSuch()
    {
        var index = BuildAndSave(Fixture());
        var writer = new StringWriter();
        QueryCommand.WriteEffectImpact(writer, ProjectIndexQueryReader.EffectImpact(index, Subject(index, "Log"), null, out _)!);
        Assert.Contains(
            "impact: 1 of 3 affected declaration(s) would stop fitting a row of cw (its current declared row) on app.calr:8:11 function Log",
            writer.ToString());
    }

    [Fact]
    public void Text_Effects_NoRowRecorded_ExitsOne()
    {
        var index = BuildAndSave(Fixture());
        // A parameter position is indexed as a declaration but carries rows
        // only through its owner; asking for it directly finds no own row.
        var writer = new StringWriter();
        var subject = new IndexedDeclaration { SymbolId = "no-such-symbol", Name = "Ghost", Kind = "function", File = "app.calr", Line = 1, Column = 1 };
        var exit = QueryCommand.WriteEffects(writer, ProjectIndexQueryReader.Effects(index, subject));
        Assert.Equal(1, exit);
        Assert.StartsWith("query: no effect row is recorded for app.calr:1:1 function Ghost (only functions,", writer.ToString());
    }
}
