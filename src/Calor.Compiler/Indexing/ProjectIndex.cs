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

    /// <summary>Call sites whose callee could not be resolved to a declaration.</summary>
    public List<string> UnresolvedCalls { get; set; } = [];

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
    public const string CurrentFormatVersion = "1.0";

    public string FormatVersion { get; set; } = CurrentFormatVersion;
    public string CompilerSemanticsVersion { get; set; } =
        BuildStateCache.CurrentCompilerSemanticsVersion;
    public string CompilerHash { get; set; } = "";
    public string OptionsHash { get; set; } = "";
    public string ManifestHash { get; set; } = "";

    /// <summary>
    /// Repository-relative path → content hash. Doubles as the per-file semantic
    /// hash: v1 rebuilds wholesale, so one hash per file serves both roles.
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
        Residual.UnresolvedCalls.Sort(StringComparer.Ordinal);
        Residual.AmbiguousCallees.Sort(StringComparer.Ordinal);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProjectIndex))]
internal sealed partial class ProjectIndexJsonContext : JsonSerializerContext { }
