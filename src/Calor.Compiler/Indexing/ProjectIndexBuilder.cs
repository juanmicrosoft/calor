using Calor.Compiler.Binding;
using Calor.Compiler.Incremental;
using Calor.Compiler.Parsing;
using Calor.Compiler.Refactoring;

namespace Calor.Compiler.Indexing;

/// <summary>
/// Builds a <see cref="ProjectIndex"/> from a set of Calor sources.
///
/// The in-memory model is <see cref="ProjectSymbolIndex"/> — the same one the
/// rename harness addresses (§2.5 gate 4). This builder persists that model
/// rather than growing a second one, so identity, cross-file resolution, and the
/// exact-identifier rule cannot drift between the two consumers.
///
/// v1 rebuilds wholesale (scoping doc §3): there is no incremental path, and the
/// header's inputs are what make a stale index detectable rather than silent.
/// </summary>
public static class ProjectIndexBuilder
{
    public sealed record Options(
        string ProjectDirectory,
        string OptionsToken,
        IReadOnlyList<string> Files);

    /// <summary>
    /// Collects the .calr sources under a directory, in a deterministic order.
    /// </summary>
    public static IReadOnlyList<string> DiscoverSources(string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        if (!Directory.Exists(projectDirectory))
            return [];

        return Directory
            .GetFiles(projectDirectory, "*.calr", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsExcluded(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal);
    }

    /// <summary>
    /// The invalidation inputs as they stand right now, in the same shape the
    /// index header records them. Callers compare these against a loaded index
    /// to decide whether it may answer.
    /// </summary>
    public static (string CompilerHash, string OptionsHash, string ManifestHash,
        Dictionary<string, string> Files) CurrentInputs(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var manifestDirectories = options.Files
            .Select(file => Path.GetDirectoryName(Path.GetFullPath(file))!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (manifestDirectories.Count == 0)
            manifestDirectories.Add(Path.GetFullPath(options.ProjectDirectory));

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in options.Files)
        {
            files[Relative(options.ProjectDirectory, file)] =
                BuildStateCache.ComputeFileHash(file);
        }

        return (
            BuildStateCache.ComputeCliCompilerHash(),
            BuildStateCache.ComputeOptionsHash(options.OptionsToken),
            BuildStateCache.ComputeManifestHash(manifestDirectories),
            files);
    }

    public static ProjectIndex Build(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var inputs = CurrentInputs(options);
        var index = new ProjectIndex
        {
            CompilerHash = inputs.CompilerHash,
            OptionsHash = inputs.OptionsHash,
            ManifestHash = inputs.ManifestHash,
            Files = inputs.Files,
        };

        var symbols = ProjectSymbolIndex.Build(options.Files, out var skipped);
        foreach (var unreadable in skipped)
            index.Residual.UnreadableFiles.Add(Relative(options.ProjectDirectory, unreadable));

        var declarationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in symbols.Documents)
        {
            var relative = Relative(options.ProjectDirectory, document.FilePath);

            foreach (var symbol in document.BoundModule.SymbolsById.Values)
            {
                if (symbol.Id.IsNone || !declarationIds.Add(symbol.Id.Value))
                    continue;

                var (line, column) = LineColumn(document.Source, symbol.DeclarationSpan.Start);
                index.Declarations.Add(new IndexedDeclaration
                {
                    SymbolId = symbol.Id.Value,
                    Name = symbol.Name,
                    Kind = KindOf(symbol),
                    File = relative,
                    Line = line,
                    Column = column,
                });
            }
        }

        foreach (var document in symbols.Documents)
        {
            var relative = Relative(options.ProjectDirectory, document.FilePath);
            foreach (var occurrence in symbols.OccurrencesIn(document.FilePath))
            {
                var (line, column) = LineColumn(document.Source, occurrence.Span.Start);
                index.Occurrences.Add(new IndexedOccurrence
                {
                    SymbolId = occurrence.SymbolId.Value,
                    File = relative,
                    Line = line,
                    Column = column,
                    Kind = occurrence.Kind.ToString(),
                });
            }
        }

        var sourcesByPath = symbols.Documents.ToDictionary(
            document => document.FilePath,
            document => document.Source,
            StringComparer.Ordinal);
        foreach (var edge in symbols.CallEdges)
        {
            var (line, column) = LineColumn(sourcesByPath[edge.FilePath], edge.Span.Start);
            index.CallEdges.Add(new IndexedCallEdge
            {
                CallerSymbolId = edge.CallerSymbolId.Value,
                CalleeSymbolId = edge.CalleeSymbolId.Value,
                File = Relative(options.ProjectDirectory, edge.FilePath),
                Line = line,
                Column = column,
            });
        }

        foreach (var unresolved in symbols.Residual.UnresolvedCalls)
            index.Residual.UnresolvedCalls.Add(RelativePrefix(options.ProjectDirectory, unresolved));
        foreach (var ambiguous in symbols.Residual.AmbiguousCallees)
            index.Residual.AmbiguousCallees.Add(ambiguous);

        index.Canonicalize();
        return index;
    }

    private static string KindOf(Symbol symbol) => symbol switch
    {
        FunctionSymbol => "function",
        TypeSymbol => "type",
        VariableSymbol { IsParameter: true } => "parameter",
        VariableSymbol { IsField: true } => "field",
        VariableSymbol { IsProperty: true } => "property",
        VariableSymbol => "local",
        _ => "symbol",
    };

    private static string Relative(string projectDirectory, string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(projectDirectory);
        return Path.GetRelativePath(root, full).Replace('\\', '/');
    }

    private static string RelativePrefix(string projectDirectory, string entry)
    {
        var separator = entry.IndexOf(": ", StringComparison.Ordinal);
        return separator < 0
            ? entry
            : Relative(projectDirectory, entry[..separator]) + entry[separator..];
    }

    private static (int Line, int Column) LineColumn(string source, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }
}
