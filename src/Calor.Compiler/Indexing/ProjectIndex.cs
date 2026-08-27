using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;

namespace Calor.Compiler.Indexing;

/// <summary>
/// A declaration the index can answer about.
/// </summary>
public sealed class IndexedDeclaration
{
    public string SymbolId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }

    /// <summary>
    /// Hash of this declaration's own definition text, so "what changed?" can be
    /// answered per declaration rather than per file.
    ///
    /// The file-grained alternative was measured before being rejected: on a
    /// 106-file corpus it gave the exactly-right impact answer for 1% of
    /// functions and claimed non-empty impact for 69% of functions whose true
    /// impact was empty — a ~13x over-report. Precision here is what makes the
    /// impact facet worth having.
    /// </summary>
    public string SemanticHash { get; set; } = "";
}

/// <summary>
/// One identifier token that denotes a symbol.
/// </summary>
public sealed class IndexedOccurrence
{
    public string SymbolId { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Kind { get; set; } = "";
}

/// <summary>
/// A resolved call from one function to another.
/// </summary>
public sealed class IndexedCallEdge
{
    public string CallerSymbolId { get; set; } = "";
    public string CalleeSymbolId { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

/// <summary>
/// A contract clause declared on a declaration.
///
/// The index records what is DECLARED, never a proof status. Outcomes come from
/// running the verifier (`calor verify`, `review-packet`); an index that carried
/// a stale "Proven" would be worse than one that carries none, because the whole
/// point of a proof is that you can rely on it.
/// </summary>
public sealed class IndexedContract
{
    public string SymbolId { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
}

/// <summary>
/// An assumption declared on a module or a declaration — the things a reader is
/// being asked to take on trust, which is exactly what a reviewer wants listed.
/// </summary>
public sealed class IndexedAssumption
{
    public string SymbolId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
}

/// <summary>
/// v0.15 E5 (design-doc §8.6) — one effect row as the index records it: the
/// compact display authors read, plus the structured form a tool can compare.
/// </summary>
public sealed class IndexedRow
{
    /// <summary>
    /// The row as <c>EffectRowDisplay.ToCompactDisplayString</c> spells it —
    /// <c>[pure]</c>, <c>cw, fs:w</c>, <c>[assumed: cw]</c>, <c>[unknown]</c> —
    /// with any <c>eff</c> binders the row mentions listed after the codes.
    /// </summary>
    public string Display { get; set; } = "";

    /// <summary><c>concrete</c>, <c>assumed</c> or <c>unknown</c> (§4.1's three states).</summary>
    public string State { get; set; } = "";

    /// <summary>Compact surface codes, ordinal-sorted. Empty for a pure or an Unknown row.</summary>
    public List<string> Effects { get; set; } = [];

    /// <summary>
    /// The <c>eff</c> binders the row mentions, by ordinal in the declaration's own
    /// <c>eff</c> list (§7). On a DECLARED row: what the author wrote. On an INFERRED
    /// row: the binders the body was charged through an invoked value's polymorphic
    /// row or a rank-1 instantiation's residual — the part <c>EffectSet</c> cannot
    /// carry, recorded by the pass beside its computed set.
    /// </summary>
    public List<IndexedEffectVariable> Variables { get; set; } = [];

    /// <summary>Why the row is only assumed — empty unless <see cref="State"/> is <c>assumed</c>.</summary>
    public List<string> Reasons { get; set; } = [];

    /// <summary>The <see cref="EffectRow"/> this record denotes; the variable part is not carried (it is a §7 instantiation input, not a lattice element).</summary>
    [JsonIgnore]
    public EffectRow Row => State switch
    {
        "unknown" => EffectRow.Unknown,
        "assumed" => EffectRow.Assumed(EffectSet.From([.. Effects]).ToRow().Codes, Reasons),
        _ => EffectSet.From([.. Effects]).ToRow(),
    };
}

/// <summary>An <c>eff</c> binder mention, by ordinal and by the name the author wrote.</summary>
public sealed class IndexedEffectVariable
{
    public int Ordinal { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// v0.15 E5 (design-doc §8.5/§8.6) — the effect-row fact for one declaration or
/// one function-typed position, keyed by symbol id and never by name.
///
/// For a declaration (<see cref="Kind"/> <c>function</c>, <c>method</c>,
/// <c>constructor</c>, <c>accessor</c>) the fact is the enforcement pass's own
/// per-declaration result (<c>EffectEnforcementPass.DeclarationFacts</c>): the
/// declared row, the inferred row and the verdict between them, together with
/// the diagnostic code that fires. Nothing here re-runs inference.
///
/// For a position (<c>parameter</c>, <c>return</c>) there is no inference: the
/// fact is the declared row, and <see cref="BoundRow"/> is what the binder's
/// <c>FunctionBoundType.Row</c> carries for it — the first production reader
/// of that row (E4's 0.15.x obligation, roadmap §4.2 E5), recorded so the two
/// can be pinned against each other.
/// </summary>
public sealed class IndexedEffectRow
{
    /// <summary>The declaration's symbol id; for a <c>return</c> position, the owning function's.</summary>
    public string SymbolId { get; set; } = "";

    /// <summary>The owning declaration's symbol id for a position; empty for a declaration.</summary>
    public string OwnerSymbolId { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary><c>function</c>, <c>method</c>, <c>constructor</c>, <c>accessor</c>, <c>parameter</c> or <c>return</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Whether a <c>§E</c> was written. An omitted declaration row is pure (§3.5); an omitted position row is Unknown.</summary>
    public bool Declared { get; set; }

    public IndexedRow DeclaredRow { get; set; } = new();

    /// <summary>What the enforcement pass computed for the body; null for a position.</summary>
    public IndexedRow? InferredRow { get; set; }

    /// <summary><c>fits</c>, <c>does-not-fit</c>, <c>cannot-tell</c>; <c>declared-only</c> for a position.</summary>
    public string Verdict { get; set; } = "";

    /// <summary>The diagnostic code the pass reports for this declaration, or null.</summary>
    public string? DiagnosticCode { get; set; }

    /// <summary>Surface codes the body uses that the declaration does not cover.</summary>
    public List<string> Forbidden { get; set; } = [];

    /// <summary>For a position: <c>FunctionBoundType.Row</c>'s compact display; null for a declaration.</summary>
    public string? BoundRow { get; set; }

    public string File { get; set; } = "";
    public int Line { get; set; }
}

/// <summary>
/// v0.15 E5 — one affected caller of an effect-change blast-radius answer.
/// </summary>
public sealed record IndexedEffectImpact(
    IndexedDeclaration Declaration,
    IndexedEffectRow? Row,
    EffectFit Verdict);

/// <summary>
/// A call site that resolved to nothing.
/// </summary>
public sealed class IndexedUnresolvedCall
{
    public string CallerSymbolId { get; set; } = "";
    public string Target { get; set; } = "";
    public string File { get; set; } = "";
}

/// <summary>
/// What the index could NOT account for. Every query reports this alongside its
/// answer, because Calor binds one file at a time: cross-file edges come from a
/// unique-name match that drops ambiguity, so an answer is "what we can name",
/// never "everything". Reporting a clean-looking answer over silent holes is the
/// failure shape this project has paid for repeatedly.
/// </summary>
public sealed class IndexResidual
{
    /// <summary>Files that did not parse or bind, so nothing in them is indexed.</summary>
    public List<string> UnreadableFiles { get; set; } = [];

    /// <summary>
    /// Call sites whose callee could not be resolved, each recorded with the
    /// function it sits in — without that, "what does X call?" cannot tell
    /// whether its own answer is partial.
    /// </summary>
    public List<IndexedUnresolvedCall> UnresolvedCalls { get; set; } = [];

    /// <summary>Callee names matching more than one declaration, so the edge was dropped.</summary>
    public List<string> AmbiguousCallees { get; set; } = [];

    /// <summary>
    /// v0.15 E5 — files whose declarations have NO effect rows: the binder
    /// reported errors (the CLI skips the effect pass there, so the index does
    /// too) or the pass threw. Each entry is <c>file: reason</c>.
    /// </summary>
    public List<string> EffectRowsUnavailable { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty =>
        UnreadableFiles.Count == 0
        && UnresolvedCalls.Count == 0
        && AmbiguousCallees.Count == 0
        && EffectRowsUnavailable.Count == 0;

    [JsonIgnore]
    public int Total =>
        UnreadableFiles.Count + UnresolvedCalls.Count + AmbiguousCallees.Count
        + EffectRowsUnavailable.Count;
}

/// <summary>
/// The persisted project index (§2.2; scoping doc §4).
///
/// The header carries the SAME invalidation inputs <see cref="BuildStateCache"/>
/// uses. That is the point, not a convenience: an index that answers from stale
/// state is #788/#883 in a new component — an input changes, nothing
/// invalidates, and the answer reflects the previous world. A query against a
/// header that no longer matches must rebuild or refuse; it may never answer.
///
/// Semantic hashes are per FILE, not per declaration (scoping doc §9.1, decided
/// with the maintainer). `impact` therefore answers at file granularity.
/// </summary>
public sealed class ProjectIndex
{
    // 4.0 (v0.15 E5, design-doc §8.5 / P24): the effects facet (EffectRows,
    // Residual.EffectRowsUnavailable). Gate 3's instrument compares serialized
    // index bytes, so a facet added without a bump would move them silently.
    public const string CurrentFormatVersion = "4.0";

    public string FormatVersion { get; set; } = CurrentFormatVersion;
    public string CompilerSemanticsVersion { get; set; } =
        BuildStateCache.CurrentCompilerSemanticsVersion;
    public string CompilerHash { get; set; } = "";
    public string OptionsHash { get; set; } = "";
    public string ManifestHash { get; set; } = "";

    /// <summary>
    /// Repository-relative path → content hash. Used for INVALIDATION only:
    /// deciding whether the index may still answer. Semantic hashes live per
    /// declaration (<see cref="IndexedDeclaration.SemanticHash"/>) because
    /// file granularity made the impact facet useless — see the measurement
    /// recorded there.
    /// </summary>
    public Dictionary<string, string> Files { get; set; } = [];

    public List<IndexedDeclaration> Declarations { get; set; } = [];
    public List<IndexedOccurrence> Occurrences { get; set; } = [];
    public List<IndexedCallEdge> CallEdges { get; set; } = [];
    public List<IndexedContract> Contracts { get; set; } = [];
    public List<IndexedAssumption> Assumptions { get; set; } = [];

    /// <summary>v0.15 E5 — the effects facet (design-doc §8.6). See <see cref="IndexedEffectRow"/>.</summary>
    public List<IndexedEffectRow> EffectRows { get; set; } = [];
    public IndexResidual Residual { get; set; } = new();

    public static string PathFor(string outputDirectory) =>
        Path.Combine(outputDirectory, ".calor-index.json");

    /// <summary>
    /// Why an index cannot be used as-is. <see cref="Fresh"/> is the only value
    /// that permits answering a query.
    /// </summary>
    public enum Freshness
    {
        Fresh,
        Missing,
        Unreadable,
        FormatChanged,
        SemanticsChanged,
        CompilerChanged,
        OptionsChanged,
        ManifestChanged,
        SourcesChanged,
    }

    /// <summary>
    /// Compares a loaded index against the inputs that would build it now.
    /// Every field the builder records is compared — a field recorded but not
    /// compared is a silent staleness hole, which is exactly the defect class
    /// #788 and #883 were.
    /// </summary>
    public Freshness CheckFreshness(
        string compilerHash,
        string optionsHash,
        string manifestHash,
        IReadOnlyDictionary<string, string> currentFiles)
    {
        ArgumentNullException.ThrowIfNull(currentFiles);

        if (FormatVersion != CurrentFormatVersion)
            return Freshness.FormatChanged;
        if (CompilerSemanticsVersion != BuildStateCache.CurrentCompilerSemanticsVersion)
            return Freshness.SemanticsChanged;
        if (CompilerHash != compilerHash)
            return Freshness.CompilerChanged;
        if (OptionsHash != optionsHash)
            return Freshness.OptionsChanged;
        if (ManifestHash != manifestHash)
            return Freshness.ManifestChanged;

        if (Files.Count != currentFiles.Count)
            return Freshness.SourcesChanged;
        foreach (var (path, hash) in currentFiles)
        {
            if (!Files.TryGetValue(path, out var recorded) || recorded != hash)
                return Freshness.SourcesChanged;
        }

        return Freshness.Fresh;
    }

    public static string Explain(Freshness freshness) => freshness switch
    {
        Freshness.Fresh => "up to date",
        Freshness.Missing => "no index has been built",
        Freshness.Unreadable => "the index file could not be read",
        Freshness.FormatChanged => "the index format version changed",
        Freshness.SemanticsChanged => "the compiler's semantics version changed",
        Freshness.CompilerChanged => "the compiler changed",
        Freshness.OptionsChanged => "the compilation options changed",
        Freshness.ManifestChanged => "an effect manifest changed",
        Freshness.SourcesChanged => "the source files changed",
        _ => freshness.ToString(),
    };

    /// <summary>
    /// Writes canonically: gate 2 compares index contents byte-for-byte between
    /// full and incremental runs, so ordering may not depend on enumeration
    /// order of the build.
    /// </summary>
    public void Save(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Canonicalize();
        var json = JsonSerializer.Serialize(this, ProjectIndexJsonContext.Default.ProjectIndex);
        File.WriteAllText(PathFor(outputDirectory), json + "\n");
    }

    public static (ProjectIndex? Index, Freshness Status) Load(string outputDirectory)
    {
        var path = PathFor(outputDirectory);
        if (!File.Exists(path))
            return (null, Freshness.Missing);

        try
        {
            var index = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                ProjectIndexJsonContext.Default.ProjectIndex);
            return index == null
                ? (null, Freshness.Unreadable)
                : (index, Freshness.Fresh);
        }
        catch (JsonException)
        {
            return (null, Freshness.Unreadable);
        }
        catch (IOException)
        {
            return (null, Freshness.Unreadable);
        }
    }

    // --- queries -----------------------------------------------------------

    /// <summary>
    /// Declarations bearing a name. Returns every match rather than picking one:
    /// two declarations sharing a name is the situation the caller most needs to
    /// see, not one the index should resolve on their behalf.
    /// </summary>
    public IReadOnlyList<IndexedDeclaration> FindDeclarations(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Declarations
            .Where(declaration => string.Equals(
                declaration.Name, name, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Declarations whose body contains a resolved call to <paramref name="symbolId"/>.
    ///
    /// This is "the callers we can name". A call site that did not resolve
    /// contributes nothing here and appears in <see cref="Residual"/> instead —
    /// which is why callers must be reported together with the residual, never
    /// alone.
    /// </summary>
    public IReadOnlyList<IndexedDeclaration> FindCallers(string symbolId)
    {
        ArgumentNullException.ThrowIfNull(symbolId);
        var callerIds = CallEdges
            .Where(edge => string.Equals(
                edge.CalleeSymbolId, symbolId, StringComparison.Ordinal))
            .Select(edge => edge.CallerSymbolId)
            .ToHashSet(StringComparer.Ordinal);

        return Declarations
            .Where(declaration => callerIds.Contains(declaration.SymbolId))
            .ToArray();
    }

    /// <summary>Declarations this one calls, by the same resolution limits.</summary>
    public IReadOnlyList<IndexedDeclaration> FindCallees(string symbolId)
    {
        ArgumentNullException.ThrowIfNull(symbolId);
        var calleeIds = CallEdges
            .Where(edge => string.Equals(
                edge.CallerSymbolId, symbolId, StringComparison.Ordinal))
            .Select(edge => edge.CalleeSymbolId)
            .ToHashSet(StringComparer.Ordinal);

        return Declarations
            .Where(declaration => calleeIds.Contains(declaration.SymbolId))
            .ToArray();
    }

    /// <summary>Contracts declared on a symbol.</summary>
    public IReadOnlyList<IndexedContract> FindContracts(string symbolId)
    {
        ArgumentNullException.ThrowIfNull(symbolId);
        return Contracts
            .Where(contract => string.Equals(
                contract.SymbolId, symbolId, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Assumptions in force for a symbol: its own, plus the module-scoped ones
    /// declared in its file. A module assumption applies to everything in that
    /// module, so omitting it would under-report what a reader must trust.
    /// </summary>
    public IReadOnlyList<IndexedAssumption> FindAssumptions(string symbolId, string file)
    {
        ArgumentNullException.ThrowIfNull(symbolId);
        return Assumptions
            .Where(assumption =>
                string.Equals(assumption.SymbolId, symbolId, StringComparison.Ordinal)
                || (assumption.Scope == "module"
                    && string.Equals(assumption.File, file, StringComparison.Ordinal)))
            .ToArray();
    }

    /// <summary>
    /// What a change to <paramref name="file"/> could affect: every declaration
    /// reachable by following call edges INTO the declarations that file holds,
    /// transitively.
    ///
    /// Granularity is the file, not the declaration (scoping doc §9.1). The
    /// per-file semantic hash cannot say WHICH declaration in a file changed, so
    /// this treats a change to any part of a file as a change to all of it. The
    /// result is sound in the direction that matters — it never omits an
    /// affected caller — but it over-reports: a file holding twenty
    /// declarations implicates all twenty's callers when one changed.
    ///
    /// The cycle guard is not defensive coding: mutually recursive functions are
    /// ordinary, and following their edges without one does not terminate.
    /// </summary>
    public IReadOnlyList<IndexedDeclaration> FindImpactOfDeclarations(
        IReadOnlyCollection<string> seedSymbolIds)
    {
        ArgumentNullException.ThrowIfNull(seedSymbolIds);
        var seedIds = seedSymbolIds.ToHashSet(StringComparer.Ordinal);
        if (seedIds.Count == 0)
            return [];

        var callersByCallee = CallEdges
            .GroupBy(edge => edge.CalleeSymbolId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.CallerSymbolId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var affected = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(seedIds);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!callersByCallee.TryGetValue(current, out var callers))
                continue;

            foreach (var caller in callers)
            {
                if (seedIds.Contains(caller) || !affected.Add(caller))
                    continue;
                queue.Enqueue(caller);
            }
        }

        return Declarations
            .Where(declaration => affected.Contains(declaration.SymbolId))
            .ToArray();
    }

    /// <summary>
    /// Impact of changing an entire file. Retained because "I rewrote this file"
    /// is a real question — but it is no longer how a change to one declaration
    /// is answered.
    /// </summary>
    public IReadOnlyList<IndexedDeclaration> FindImpactOfFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var seedIds = Declarations
            .Where(declaration => string.Equals(
                declaration.File, file, StringComparison.Ordinal))
            .Select(declaration => declaration.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        if (seedIds.Count == 0)
            return [];

        var callersByCallee = CallEdges
            .GroupBy(edge => edge.CalleeSymbolId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.CallerSymbolId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var affected = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(seedIds);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!callersByCallee.TryGetValue(current, out var callers))
                continue;

            foreach (var caller in callers)
            {
                // Seeds are the change itself, not something the change affects.
                if (seedIds.Contains(caller) || !affected.Add(caller))
                    continue;
                queue.Enqueue(caller);
            }
        }

        return Declarations
            .Where(declaration => affected.Contains(declaration.SymbolId))
            .ToArray();
    }

    /// <summary>
    /// v0.15 E5 — the effect-row facts recorded for a symbol: its own
    /// declaration-level fact (first, when there is one) and the rows of the
    /// function-typed positions it owns. A parameter's own symbol id answers with
    /// its position row.
    /// </summary>
    public IReadOnlyList<IndexedEffectRow> FindEffectRows(string symbolId)
    {
        ArgumentNullException.ThrowIfNull(symbolId);
        return EffectRows
            .Where(row =>
                (string.Equals(row.SymbolId, symbolId, StringComparison.Ordinal)
                    && row.OwnerSymbolId.Length == 0)
                || string.Equals(row.OwnerSymbolId, symbolId, StringComparison.Ordinal)
                || (string.Equals(row.SymbolId, symbolId, StringComparison.Ordinal)
                    && row.Kind == "parameter"))
            .OrderBy(row => row.OwnerSymbolId.Length == 0 ? 0 : 1)
            .ThenBy(row => row.Line)
            .ThenBy(row => row.Kind, StringComparer.Ordinal)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The declaration-level effect fact of a symbol, or null when none was recorded.</summary>
    public IndexedEffectRow? FindEffectRow(string symbolId)
    {
        ArgumentNullException.ThrowIfNull(symbolId);
        return EffectRows.FirstOrDefault(row =>
            string.Equals(row.SymbolId, symbolId, StringComparison.Ordinal)
            && row.OwnerSymbolId.Length == 0
            && row.Kind is not ("parameter" or "return"));
    }

    /// <summary>
    /// v0.15 E5 — effect-change blast radius (design-doc §8.6). Every
    /// transitive caller <see cref="FindImpactOfDeclarations"/> names — that
    /// closure is reused unchanged — paired with the verdict of fitting
    /// <paramref name="hypotheticalRow"/> (the row the seed would carry after the
    /// change) into the caller's DECLARED row. A caller whose verdict is not
    /// <see cref="EffectFit.Fits"/> is one the change would stop fitting; a
    /// caller with no recorded row answers <see cref="EffectFit.CannotTell"/>.
    /// </summary>
    public IReadOnlyList<IndexedEffectImpact> FindEffectImpact(
        string seedSymbolId,
        EffectRow hypotheticalRow)
    {
        ArgumentNullException.ThrowIfNull(seedSymbolId);
        ArgumentNullException.ThrowIfNull(hypotheticalRow);
        var affected = FindImpactOfDeclarations([seedSymbolId]);
        var result = new List<IndexedEffectImpact>(affected.Count);
        foreach (var declaration in affected)
        {
            var row = FindEffectRow(declaration.SymbolId);
            var verdict = row == null
                ? EffectFit.CannotTell
                : EffectRow.Fits(hypotheticalRow, row.DeclaredRow.Row);
            result.Add(new IndexedEffectImpact(declaration, row, verdict));
        }
        return result;
    }

    /// <summary>
    /// v0.15 E5 — an effects answer is partial when a call INSIDE the subject
    /// failed to resolve (its inferred row rests on an unknown), when its file's
    /// rows could not be recorded at all, or when a file was unreadable.
    /// </summary>
    public bool EffectsAnswerIsPartial(string symbolId, string file) =>
        CalleesAnswerIsPartial(symbolId)
        || Residual.EffectRowsUnavailable.Any(entry =>
            entry.StartsWith(file + ":", StringComparison.Ordinal));

    /// <summary>
    /// Impact is partial whenever ANY call failed to resolve anywhere in the
    /// project: an unresolved edge might have been the one that reached this
    /// change. Unlike callers/callees this cannot be narrowed to the subject —
    /// the missing edge is by definition one we cannot attribute.
    /// </summary>
    public bool ImpactAnswerIsPartial() => !Residual.IsEmpty;

    /// <summary>
    /// Whether the residual could change what an answer MEANS. Partiality is
    /// facet-specific, which the first version of this method got wrong: it
    /// keyed on the queried name appearing in the residual, but the residual
    /// names the CALLEE, so "what does X call?" never came out partial even when
    /// a call inside X had failed to resolve. The gate-3 golden corpus caught it.
    /// </summary>
    public bool DeclarationLookupIsPartial() => Residual.UnreadableFiles.Count > 0;

    /// <summary>
    /// Callers of a symbol are partial when a call that failed to resolve, or a
    /// name several declarations share, might have been a call TO it.
    /// </summary>
    public bool CallersAnswerIsPartial(string name) =>
        Residual.UnreadableFiles.Count > 0
        || Residual.AmbiguousCallees.Contains(name, StringComparer.Ordinal)
        || Residual.UnresolvedCalls.Any(entry =>
            string.Equals(entry.Target, name, StringComparison.Ordinal));

    /// <summary>
    /// Callees of a symbol are partial when a call INSIDE it failed to resolve.
    /// </summary>
    public bool CalleesAnswerIsPartial(string symbolId) =>
        Residual.UnreadableFiles.Count > 0
        || Residual.UnresolvedCalls.Any(entry =>
            string.Equals(entry.CallerSymbolId, symbolId, StringComparison.Ordinal));

    public void Canonicalize()
    {
        Files = Files
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Declarations = [.. Declarations
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.SymbolId, StringComparer.Ordinal)];
        Occurrences = [.. Occurrences
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.SymbolId, StringComparer.Ordinal)];
        CallEdges = [.. CallEdges
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.CalleeSymbolId, StringComparer.Ordinal)];
        Contracts = [.. Contracts
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Index)];
        Assumptions = [.. Assumptions
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Description, StringComparer.Ordinal)];
        EffectRows = [.. EffectRows
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.OwnerSymbolId, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.SymbolId, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)];
        foreach (var row in EffectRows)
        {
            row.DeclaredRow.Effects.Sort(StringComparer.Ordinal);
            row.DeclaredRow.Reasons.Sort(StringComparer.Ordinal);
            row.DeclaredRow.Variables = [.. row.DeclaredRow.Variables.OrderBy(v => v.Ordinal)];
            if (row.InferredRow != null)
            {
                row.InferredRow.Effects.Sort(StringComparer.Ordinal);
                row.InferredRow.Reasons.Sort(StringComparer.Ordinal);
            }
            row.Forbidden.Sort(StringComparer.Ordinal);
        }
        Residual.UnreadableFiles.Sort(StringComparer.Ordinal);
        Residual.EffectRowsUnavailable.Sort(StringComparer.Ordinal);
        Residual.UnresolvedCalls = [.. Residual.UnresolvedCalls
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Target, StringComparer.Ordinal)
            .ThenBy(item => item.CallerSymbolId, StringComparer.Ordinal)];
        Residual.AmbiguousCallees.Sort(StringComparer.Ordinal);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProjectIndex))]
internal sealed partial class ProjectIndexJsonContext : JsonSerializerContext { }
