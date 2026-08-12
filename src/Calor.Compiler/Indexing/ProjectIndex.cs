using System.Text.Json;
using System.Text.Json.Serialization;
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

    [JsonIgnore]
    public bool IsEmpty =>
        UnreadableFiles.Count == 0
        && UnresolvedCalls.Count == 0
        && AmbiguousCallees.Count == 0;

    [JsonIgnore]
    public int Total =>
        UnreadableFiles.Count + UnresolvedCalls.Count + AmbiguousCallees.Count;
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
    public const string CurrentFormatVersion = "2.0";

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
        Residual.UnreadableFiles.Sort(StringComparer.Ordinal);
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
