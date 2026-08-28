using System.Text.Json.Serialization;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Effects;
using Calor.Compiler.Indexing;

namespace Calor.Compiler.Commands;

/// <summary>
/// Reads answers off the project index (§2.2; v0.16 E7). Every surface that
/// asks the index a question — <c>calor query</c> and the MCP tool
/// <c>calor_query</c> — comes through here, so the two cannot disagree: they
/// share the resolution rule (a stale index never answers; it is rebuilt or
/// refused), the subject rule (an ambiguous name is refused, never guessed)
/// and the answer records the formatters print.
///
/// The reader returns data, not text. The CLI's text and <c>--json</c> shapes
/// are <see cref="QueryCommand"/>'s; the MCP tool renders the same records
/// through the same formatters.
/// </summary>
public static class ProjectIndexQueryReader
{
    /// <summary>
    /// The options token the index is built under. Shared with
    /// <c>calor index build</c> so a query never rebuilds what the build wrote
    /// under a different header.
    /// </summary>
    public const string OptionsToken = "index-v1";

    /// <summary>The facets E7 exposes over the index, in the order the CLI lists them.</summary>
    public static readonly IReadOnlyList<string> Facets = ["callers", "callees", "impact", "effects"];

    // --- resolution ---------------------------------------------------------

    /// <summary>
    /// The index for <paramref name="projectDirectory"/>, rebuilt when stale
    /// unless <paramref name="noBuild"/> refuses instead. Null with
    /// <paramref name="error"/> set on refusal; the error text is exactly what
    /// the CLI prints.
    /// </summary>
    /// <param name="indexDirectory">
    /// Where the index lives; defaults to the directory <c>calor index build</c>
    /// writes to (<c>obj/calor</c> under the project).
    /// </param>
    public static ProjectIndex? Resolve(
        string projectDirectory,
        bool noBuild,
        out string? error,
        string? indexDirectory = null)
    {
        error = null;
        if (!Directory.Exists(projectDirectory))
        {
            error = $"Error: directory not found: {projectDirectory}";
            return null;
        }

        // A blank override is "no override": Path.GetFullPath("") throws, and an
        // exception here would surface as a protocol-level internal error
        // instead of the refusal every other bad input gets.
        var output = string.IsNullOrWhiteSpace(indexDirectory)
            ? IndexCommand.DefaultOutputDirectory(projectDirectory)
            : indexDirectory;
        var sources = ProjectIndexBuilder.DiscoverSources(projectDirectory);
        if (sources.Count == 0)
        {
            error = $"Error: no .calr files under {projectDirectory}";
            return null;
        }

        var options = new ProjectIndexBuilder.Options(
            projectDirectory, OptionsToken, sources);
        var inputs = ProjectIndexBuilder.CurrentInputs(options);

        var (loaded, status) = ProjectIndex.Load(output);
        var freshness = loaded == null
            ? status
            : loaded.CheckFreshness(
                inputs.CompilerHash, inputs.OptionsHash, inputs.ManifestHash, inputs.Files);

        if (freshness == ProjectIndex.Freshness.Fresh && loaded != null)
            return loaded;

        // A stale index is never answered from — that is the whole discipline.
        if (noBuild)
        {
            error = $"Error: index unusable — {ProjectIndex.Explain(freshness)}. "
                + "Run `calor index build` (or drop --no-build).";
            return null;
        }

        var rebuilt = ProjectIndexBuilder.Build(options);
        rebuilt.Save(output);
        return rebuilt;
    }

    /// <summary>
    /// The declaration a name denotes. <see cref="Subject"/> is null when the
    /// name is unknown (<see cref="Candidates"/> empty) or ambiguous (several
    /// candidates and <c>inFile</c> did not narrow them to one).
    /// </summary>
    public sealed record SubjectLookup(
        IndexedDeclaration? Subject,
        IReadOnlyList<IndexedDeclaration> Candidates)
    {
        public bool NotFound => Candidates.Count == 0;
        public bool Ambiguous => Subject == null && Candidates.Count > 1;
    }

    /// <summary>
    /// Several declarations sharing the name: the caller must say which, rather
    /// than the tool picking one and presenting the result as if unambiguous.
    /// </summary>
    public static SubjectLookup ResolveSubject(ProjectIndex index, string name, string? inFile)
    {
        ArgumentNullException.ThrowIfNull(index);
        var declarations = index.FindDeclarations(name);
        if (declarations.Count == 0)
            return new SubjectLookup(null, declarations);
        if (declarations.Count == 1)
            return new SubjectLookup(declarations[0], declarations);

        var matches = inFile == null
            ? declarations
            : declarations
                .Where(declaration => string.Equals(
                    declaration.File, inFile, StringComparison.Ordinal))
                .ToArray();
        return new SubjectLookup(matches.Count == 1 ? matches[0] : null, declarations);
    }

    /// <summary>The CLI's refusal text for an ambiguous name, line by line.</summary>
    public static IReadOnlyList<string> AmbiguityLines(string name, IReadOnlyList<IndexedDeclaration> candidates)
    {
        var lines = new List<string>
        {
            $"Error: '{name}' is declared in {candidates.Count} places; narrow it with --in-file:",
        };
        foreach (var declaration in candidates)
            lines.Add($"  {Describe(declaration)}");
        return lines;
    }

    // --- answers ------------------------------------------------------------

    /// <summary>
    /// <c>callers</c> / <c>callees</c>: the declaration set (not the call
    /// count), ordered by position, with the residual attached when the answer
    /// may be incomplete.
    /// </summary>
    public sealed record DeclarationsAnswer(
        string Facet,
        string Subject,
        string SymbolId,
        IReadOnlyList<IndexedDeclaration> Declarations,
        bool Partial,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IndexResidual? Residual);

    public static DeclarationsAnswer Callers(ProjectIndex index, IndexedDeclaration subject)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(subject);
        var partial = index.CallersAnswerIsPartial(subject.Name);
        return new DeclarationsAnswer(
            "callers", Describe(subject), subject.SymbolId,
            ByPosition(index.FindCallers(subject.SymbolId)), partial,
            partial ? index.Residual : null);
    }

    public static DeclarationsAnswer Callees(ProjectIndex index, IndexedDeclaration subject)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(subject);
        var partial = index.CalleesAnswerIsPartial(subject.SymbolId);
        return new DeclarationsAnswer(
            "callees", Describe(subject), subject.SymbolId,
            ByPosition(index.FindCallees(subject.SymbolId)), partial,
            partial ? index.Residual : null);
    }

    /// <summary>
    /// <c>impact</c>: the declarations a change to the subject could affect —
    /// seeded by one declaration (<see cref="File"/> null) or by a whole file
    /// (<see cref="SymbolId"/> null, the file-grained answer).
    /// </summary>
    public sealed record ImpactAnswer(
        string Facet,
        string Subject,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? SymbolId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? File,
        IReadOnlyList<IndexedDeclaration> Affected,
        int AffectedFiles,
        bool Partial,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IndexResidual? Residual);

    public static ImpactAnswer Impact(ProjectIndex index, IndexedDeclaration subject)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(subject);
        var affected = ByPosition(index.FindImpactOfDeclarations([subject.SymbolId]));
        var partial = index.ImpactAnswerIsPartial();
        return new ImpactAnswer(
            "impact", Describe(subject), subject.SymbolId, null, affected, CountFiles(affected),
            partial, partial ? index.Residual : null);
    }

    /// <summary>
    /// Whole-file impact. Null with <paramref name="error"/> set when the file
    /// is not one the index holds.
    /// </summary>
    public static ImpactAnswer? ImpactOfFile(ProjectIndex index, string file, out string? error)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(file);
        error = null;
        var normalized = file.Replace('\\', '/');
        if (!index.Files.ContainsKey(normalized))
        {
            error = $"Error: '{file}' is not an indexed source file.";
            return null;
        }

        var affected = ByPosition(index.FindImpactOfFile(normalized));
        var partial = index.ImpactAnswerIsPartial();
        return new ImpactAnswer(
            "impact", $"the whole file {normalized}", null, normalized, affected, CountFiles(affected),
            partial, partial ? index.Residual : null);
    }

    /// <summary>
    /// One affected caller and whether the hypothetical row still fits its
    /// declared row. <see cref="DeclaredRow"/> is ABSENT (null, and omitted from
    /// the JSON) exactly when the index holds no row for that caller — the case
    /// the text answer renders as "(no row recorded)" and the verdict reports as
    /// <c>cannot-tell</c>.
    /// </summary>
    public sealed record EffectImpactEntry(
        IndexedDeclaration Declaration,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? DeclaredRow,
        string Verdict);

    /// <summary>
    /// <c>impact --effects</c> (design-doc §8.6): the impact closure joined with
    /// the verdict of fitting the row the subject WOULD carry into each affected
    /// caller's DECLARED row. <see cref="StopFitting"/> counts DoesNotFit only;
    /// a caller whose row is Unknown or unrecorded is <see cref="CannotTell"/> —
    /// undecided, never broken.
    /// </summary>
    public sealed record EffectImpactAnswer(
        string Facet,
        string Subject,
        string SymbolId,
        string Row,
        bool RowIsCurrentDeclared,
        IReadOnlyList<EffectImpactEntry> Impacts,
        int StopFitting,
        int CannotTell,
        bool Partial,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IndexResidual? Residual);

    /// <summary>
    /// Null with <paramref name="error"/> set when <paramref name="row"/> does
    /// not parse as effect codes, or when no row is given and the index holds
    /// none for the subject to default to.
    /// </summary>
    public static EffectImpactAnswer? EffectImpact(
        ProjectIndex index,
        IndexedDeclaration subject,
        string? row,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(subject);
        error = null;
        var own = index.FindEffectRow(subject.SymbolId);
        EffectRow hypothetical;
        string rowDescribed;
        bool current;
        if (row != null)
        {
            var codes = row.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            try
            {
                hypothetical = EffectSet.From(codes).ToRow();
            }
            catch (ArgumentException exception)
            {
                error = $"Error: --row '{row}' is not a row of effect codes: {exception.Message}";
                return null;
            }
            rowDescribed = hypothetical.ToCompactDisplayString();
            current = false;
        }
        else if (own != null)
        {
            hypothetical = own.DeclaredRow.Row;
            rowDescribed = own.DeclaredRow.Display;
            current = true;
        }
        else
        {
            error = $"Error: no effect row is recorded for {Describe(subject)}; pass --row to ask about a hypothetical one.";
            return null;
        }

        var impacts = index.FindEffectImpact(subject.SymbolId, hypothetical)
            .OrderBy(impact => $"{impact.Declaration.File}:{impact.Declaration.Line}", StringComparer.Ordinal)
            .Select(impact => new EffectImpactEntry(
                impact.Declaration,
                impact.Row?.DeclaredRow.Display,
                ProjectIndexBuilder.VerdictText(impact.Verdict)))
            .ToArray();
        var partial = index.ImpactAnswerIsPartial();
        return new EffectImpactAnswer(
            "impact-effects",
            Describe(subject),
            subject.SymbolId,
            rowDescribed,
            current,
            impacts,
            impacts.Count(impact => impact.Verdict == ProjectIndexBuilder.VerdictText(EffectFit.DoesNotFit)),
            impacts.Count(impact => impact.Verdict == ProjectIndexBuilder.VerdictText(EffectFit.CannotTell)),
            partial,
            partial ? index.Residual : null);
    }

    /// <summary>
    /// <c>effects</c> (v0.15 E5): the index's own per-declaration records —
    /// declared row, inferred row, verdict, the code that fires — plus the rows
    /// of the positions the declaration owns. <see cref="Unavailable"/> names
    /// why a declaration has no row when the index recorded a reason.
    /// </summary>
    public sealed record EffectsAnswer(
        string Facet,
        string Subject,
        string SymbolId,
        IReadOnlyList<IndexedEffectRow> Rows,
        bool Partial,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IndexResidual? Residual,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Unavailable);

    public static EffectsAnswer Effects(ProjectIndex index, IndexedDeclaration subject)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(subject);
        var rows = index.FindEffectRows(subject.SymbolId);
        var partial = index.EffectsAnswerIsPartial(subject.SymbolId, subject.File);
        string? unavailable = null;
        if (rows.Count == 0)
        {
            var entry = index.Residual.EffectRowsUnavailable
                .FirstOrDefault(entry => entry.StartsWith(subject.File + ":", StringComparison.Ordinal));
            if (entry != null)
                unavailable = entry[(subject.File.Length + 2)..];
        }

        return new EffectsAnswer(
            "effects", Describe(subject), subject.SymbolId, rows, partial,
            partial ? index.Residual : null, unavailable);
    }

    /// <summary>A declaration's own row, as opposed to a parameter or return position it owns.</summary>
    public static bool IsOwnRow(IndexedEffectRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.OwnerSymbolId.Length == 0 && row.Kind is not ("parameter" or "return");
    }

    // --- rendering helpers shared by every formatter ------------------------

    public static string Describe(IndexedDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return $"{declaration.File}:{declaration.Line}:{declaration.Column} "
            + $"{declaration.Kind} {declaration.Name}";
    }

    /// <summary>"fits", "does not fit — Calor0410 fires (undeclared: cw)", …</summary>
    public static string DescribeVerdict(IndexedEffectRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var text = row.Verdict switch
        {
            "fits" => "fits",
            "does-not-fit" => "does not fit",
            "cannot-tell" => "cannot tell",
            _ => row.Verdict,
        };
        if (row.DiagnosticCode != null)
        {
            text += $" — {row.DiagnosticCode} fires";
            if (row.Forbidden.Count > 0)
                text += $" (undeclared: {string.Join(", ", row.Forbidden)})";
        }
        return text;
    }

    /// <summary>
    /// The residual, line by line, as every answer prints it when partial.
    /// "3 callers" over a silently dropped fourth is the failure this project
    /// keeps paying for, so the lines are part of the answer, never on request.
    /// </summary>
    public static IReadOnlyList<string> ResidualLines(IndexResidual residual)
    {
        ArgumentNullException.ThrowIfNull(residual);
        var lines = new List<string>
        {
            "query: PARTIAL — this answer may be incomplete. Calor binds one file "
                + "at a time, so a call resolves only when exactly one declaration "
                + "bears the name:",
        };
        foreach (var file in residual.UnreadableFiles)
            lines.Add($"  unreadable file: {file} (nothing in it is indexed)");
        foreach (var call in residual.UnresolvedCalls)
            lines.Add($"  unresolved call: {call.File}: {call.Target}");
        foreach (var ambiguous in residual.AmbiguousCallees)
            lines.Add($"  ambiguous name: {ambiguous} (several declarations share it)");
        return lines;
    }

    private static IReadOnlyList<IndexedDeclaration> ByPosition(IReadOnlyList<IndexedDeclaration> declarations) =>
        declarations
            .OrderBy(declaration => $"{declaration.File}:{declaration.Line}", StringComparer.Ordinal)
            .ToArray();

    private static int CountFiles(IReadOnlyList<IndexedDeclaration> declarations) =>
        declarations.Select(declaration => declaration.File).Distinct(StringComparer.Ordinal).Count();
}
