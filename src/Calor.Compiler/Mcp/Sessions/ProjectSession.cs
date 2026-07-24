using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Mcp.Tools;

namespace Calor.Compiler.Mcp.Sessions;

/// <summary>
/// Session-scoped project context for the MCP server (loop plan WS2 D2.1).
/// A session opens a directory and holds the parse state of every .calr file
/// under it. Dirty-state invalidation is stat-on-access: <see cref="Refresh"/>
/// re-stats known files (mtime/size gate, then content hash — the
/// BuildStateCache two-level pattern) and reparses only files that changed
/// behind the session's back; it also picks up added and deleted files.
/// No project-file format exists in v0.9 — the directory is the project
/// (docs/plans/loop-m3-ws2.md §2).
/// </summary>
internal sealed class ProjectSession
{
    // Sessions hold whole-project parse state in memory; refuse directories
    // that are clearly not a Calor project workspace.
    internal const int MaxFiles = 2000;

    private readonly Dictionary<string, SessionFileState> _files = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public string Id { get; }
    public string RootDirectory { get; }

    private ProjectSession(string id, string rootDirectory)
    {
        Id = id;
        RootDirectory = rootDirectory;
    }

    /// <summary>
    /// Opens a session over <paramref name="rootDirectory"/>, parsing every
    /// .calr file under it. Throws <see cref="InvalidOperationException"/>
    /// when the directory exceeds <see cref="MaxFiles"/>.
    /// </summary>
    public static ProjectSession Open(string id, string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        var session = new ProjectSession(id, root);

        var paths = Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories).ToList();
        if (paths.Count > MaxFiles)
            throw new InvalidOperationException(
                $"Directory contains {paths.Count} .calr files, exceeding the session limit of {MaxFiles}");

        foreach (var path in paths)
            session._files[Path.GetFullPath(path)] = SessionFileState.Load(Path.GetFullPath(path));

        return session;
    }

    /// <summary>
    /// Re-stats every known file and re-enumerates the directory, reparsing
    /// only files whose content actually changed. Returns what changed so
    /// callers can report it.
    /// </summary>
    public RefreshResult Refresh()
    {
        lock (_sync)
        {
            var reparsed = 0;
            var added = 0;
            var removed = 0;

            var onDisk = Directory.Exists(RootDirectory)
                ? Directory.EnumerateFiles(RootDirectory, "*.calr", SearchOption.AllDirectories)
                    .Select(Path.GetFullPath)
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var stale in _files.Keys.Where(p => !onDisk.Contains(p)).ToList())
            {
                _files.Remove(stale);
                removed++;
            }

            foreach (var path in onDisk)
            {
                if (!_files.TryGetValue(path, out var state))
                {
                    _files[path] = SessionFileState.Load(path);
                    added++;
                    continue;
                }

                // Level 1: mtime/size stat gate. Level 2: content hash — a
                // touch without a content change must not trigger a reparse.
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc == state.LastWriteUtc && info.Length == state.FileSize)
                    continue;

                var source = File.ReadAllText(path);
                var hash = SessionFileState.HashContent(source);
                if (hash == state.ContentHash)
                {
                    state.LastWriteUtc = info.LastWriteTimeUtc;
                    state.FileSize = info.Length;
                    continue;
                }

                _files[path] = SessionFileState.FromContent(path, source, hash, info);
                reparsed++;
            }

            return new RefreshResult(reparsed, added, removed);
        }
    }

    /// <summary>True when <paramref name="absolutePath"/> is inside the session root.</summary>
    public bool ContainsPath(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);
        return normalized.StartsWith(RootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(Path.GetDirectoryName(normalized), RootDirectory, StringComparison.Ordinal);
    }

    /// <summary>Snapshot of the session's file states, for read-only iteration.</summary>
    public List<SessionFileState> SnapshotFiles()
    {
        lock (_sync)
        {
            return _files.Values.ToList();
        }
    }

    /// <summary>
    /// Records the result of an applied write, so subsequent checks in this
    /// session see the new content without re-reading the file.
    /// </summary>
    public void UpdateFile(string absolutePath, string source, ParseResult parse)
    {
        var path = Path.GetFullPath(absolutePath);
        var info = new FileInfo(path);
        lock (_sync)
        {
            _files[path] = new SessionFileState(path, source, SessionFileState.HashContent(source),
                info.LastWriteTimeUtc, info.Length, parse);
        }
    }

    public readonly record struct RefreshResult(int Reparsed, int Added, int Removed);
}

/// <summary>Per-file state held by a <see cref="ProjectSession"/>.</summary>
internal sealed class SessionFileState
{
    public string Path { get; }
    public string Source { get; }
    public string ContentHash { get; }
    public DateTime LastWriteUtc { get; set; }
    public long FileSize { get; set; }
    public ParseResult Parse { get; }

    public SessionFileState(string path, string source, string contentHash,
        DateTime lastWriteUtc, long fileSize, ParseResult parse)
    {
        Path = path;
        Source = source;
        ContentHash = contentHash;
        LastWriteUtc = lastWriteUtc;
        FileSize = fileSize;
        Parse = parse;
    }

    public static SessionFileState Load(string path)
    {
        var source = File.ReadAllText(path);
        return FromContent(path, source, HashContent(source), new FileInfo(path));
    }

    public static SessionFileState FromContent(string path, string source, string hash, FileInfo info)
        => new(path, source, hash, info.LastWriteTimeUtc, info.Length,
            CalorSourceHelper.Parse(source, path));

    public static string HashContent(string source)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}
