using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Mcp.Tools;

namespace Calor.Compiler.Mcp.Sessions;

/// <summary>
/// Session-scoped project context for the MCP server (loop plan WS2 D2.1).
/// A session opens a directory and holds the parse state of every .calr file
/// under it. Dirty-state invalidation is stat-on-access: <see cref="Refresh"/>
/// re-stats known files and reparses the ones whose stat changed, and picks
/// up added and deleted files. The stat gate has BuildStateCache semantics —
/// an edit that preserves both mtime and size is not detected until the stat
/// next changes; the content hash exists to suppress reparses of touched-but-
/// unchanged files, not to catch stat-preserving edits.
/// No project-file format exists in v0.9 — the directory is the project
/// (docs/plans/loop-m3-ws2.md §2).
/// </summary>
internal sealed class ProjectSession
{
    // Sessions hold whole-project parse state in memory; refuse directories
    // that are clearly not a Calor project workspace.
    internal const int MaxFiles = 2000;

    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        // Skipping reparse points keeps enumeration from following symlink
        // cycles and from pulling in files outside the root; symlinked .calr
        // entries inside the root are deliberately not part of a session.
        // Device entries are skipped for the same do-not-open reason.
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Device,
    };

    private readonly Dictionary<string, SessionFileState> _files = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public string Id { get; }

    /// <summary>Canonical (symlink-resolved) root; the write-confinement boundary.</summary>
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
        var root = CanonicalPath.Resolve(rootDirectory);
        var session = new ProjectSession(id, root);

        foreach (var path in Directory.EnumerateFiles(root, "*.calr", Enumeration))
        {
            if (session._files.Count >= MaxFiles)
                throw new InvalidOperationException(
                    $"Directory contains more than {MaxFiles} .calr files — not opening a session over it");

            var fullPath = Path.GetFullPath(path);
            session._files[fullPath] = SessionFileState.Load(fullPath);
        }

        return session;
    }

    /// <summary>
    /// Re-stats every known file and re-enumerates the directory, reparsing
    /// only files whose stat changed. Returns what changed so callers can
    /// report it. See the class remarks for the stat gate's limits.
    /// </summary>
    public RefreshResult Refresh()
    {
        lock (_sync)
        {
            var reparsed = 0;
            var added = 0;
            var removed = 0;

            var onDisk = Directory.Exists(RootDirectory)
                ? Directory.EnumerateFiles(RootDirectory, "*.calr", Enumeration)
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
                try
                {
                    if (!_files.TryGetValue(path, out var state))
                    {
                        _files[path] = SessionFileState.Load(path);
                        added++;
                        continue;
                    }

                    var info = new FileInfo(path);
                    if (info.LastWriteTimeUtc == state.LastWriteUtc && info.Length == state.FileSize)
                        continue;

                    // A file that grew past the cap since open must go
                    // through the same oversize guard as Load — "a session
                    // refuses to load what a tool refuses to accept" holds
                    // across the file's whole session lifetime, not just
                    // at open.
                    if (info.Length > SessionFileState.MaxFileBytes)
                    {
                        _files[path] = SessionFileState.Load(path);
                        reparsed++;
                        continue;
                    }

                    // Stat changed — hash to decide whether the content did
                    // (a touch without an edit must not trigger a reparse).
                    // Zero-length entries are never opened (see Load).
                    var source = info.Length == 0 ? "" : File.ReadAllText(path);
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
                catch (IOException)
                {
                    // Deleted or unreadable mid-refresh: drop it; the next
                    // Refresh re-adds it if it reappears.
                    if (_files.Remove(path))
                        removed++;
                }
                catch (UnauthorizedAccessException)
                {
                    if (_files.Remove(path))
                        removed++;
                }
            }

            return new RefreshResult(reparsed, added, removed);
        }
    }

    /// <summary>
    /// True when <paramref name="absolutePath"/> is inside the session root
    /// after symlink resolution — a lexical prefix match alone would let a
    /// symlinked subdirectory escape the boundary.
    /// </summary>
    public bool ContainsPath(string absolutePath)
        => CanonicalPath.IsUnder(CanonicalPath.Resolve(absolutePath), RootDirectory);

    /// <summary>Snapshot of the session's file states, for read-only iteration.</summary>
    public List<SessionFileState> SnapshotFiles()
    {
        lock (_sync)
        {
            return _files.Values.ToList();
        }
    }

    /// <summary>
    /// The session's state for <paramref name="absolutePath"/>, or null when
    /// the file is not part of the session. Lets the write path reuse the
    /// cached parse instead of re-parsing on-disk content the session
    /// already holds (WS3 D3.1). On case-insensitive platforms an exact
    /// (Ordinal) miss falls back to a case-insensitive scan — a client that
    /// writes `Math.calr` against an enumerated `math.calr` addresses the
    /// same file there, and without the fallback the warm cache would be
    /// silently defeated on every call. Callers hash-verify content before
    /// reuse, so even a wrong-entry match (case-variant files on a
    /// case-sensitive volume mounted on such a platform) can only fall
    /// back cold, never serve a wrong parse.
    /// The platform rule is a per-OS proxy for a per-volume property:
    /// `!IsLinux()` treats macOS/Windows (and other non-Linux OSes) as
    /// case-insensitive even though case-sensitive volumes can be mounted
    /// there — the read path is hash-guarded against that, the write-path
    /// consumers carry their own guards where the proxy is load-bearing.
    /// </summary>
    public SessionFileState? TryGetFile(string absolutePath)
    {
        var path = Path.GetFullPath(absolutePath);
        lock (_sync)
        {
            if (_files.TryGetValue(path, out var state))
                return state;
            if (!OperatingSystem.IsLinux())
            {
                foreach (var (key, value) in _files)
                {
                    if (string.Equals(key, path, StringComparison.OrdinalIgnoreCase))
                        return value;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Records the result of an applied write, so subsequent checks in this
    /// session see the new content without re-reading the file. A
    /// case-variant of an existing key replaces that entry rather than
    /// inserting a duplicate (same platform rule as <see cref="TryGetFile"/>
    /// — a duplicate would make the file its own phantom neighbor in
    /// project-wide checks) — but only after confirming the two names are
    /// the same directory entry: on a case-sensitive volume mounted on a
    /// case-insensitive OS they can be genuinely different files, and
    /// reusing the key would clobber a different neighbor's state. The
    /// identity check is whether the directory holds an entry under this
    /// exact name — a distinct file does, a same-file case-variant does not.
    /// </summary>
    public void UpdateFile(string absolutePath, string source, ParseResult parse)
    {
        var path = Path.GetFullPath(absolutePath);
        var info = new FileInfo(path);
        lock (_sync)
        {
            if (!_files.ContainsKey(path) && !OperatingSystem.IsLinux())
            {
                var variant = _files.Keys.FirstOrDefault(
                    k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase));
                if (variant != null && !DirectoryHasExactEntry(path))
                    path = variant;
            }

            _files[path] = new SessionFileState(path, source, SessionFileState.HashContent(source),
                info.LastWriteTimeUtc, info.Length, parse);
        }
    }

    /// <summary>
    /// True when the parent directory holds an entry whose name equals
    /// <paramref name="absolutePath"/>'s file name exactly (Ordinal). On a
    /// case-insensitive filesystem a write through a differently-cased name
    /// lands on the existing entry, so no exact-name entry appears; on a
    /// case-sensitive volume a distinct file with the exact name does.
    /// </summary>
    private static bool DirectoryHasExactEntry(string absolutePath)
    {
        var directory = Path.GetDirectoryName(absolutePath);
        var name = Path.GetFileName(absolutePath);
        if (directory == null)
            return false;

        try
        {
            foreach (var entry in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.Ordinal))
                    return true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    public readonly record struct RefreshResult(int Reparsed, int Added, int Removed);
}

/// <summary>Per-file state held by a <see cref="ProjectSession"/>.</summary>
internal sealed class SessionFileState
{
    /// <summary>Matches McpToolBase.MaxSourceLength — a session refuses to load what a tool refuses to accept.</summary>
    internal const long MaxFileBytes = 512 * 1024;

    private static readonly IReadOnlySet<string> NoTargets = new HashSet<string>();

    private readonly Lazy<IReadOnlySet<string>> _callTargets;

    public string Path { get; }
    public string Source { get; }
    public string ContentHash { get; }
    public DateTime LastWriteUtc { get; set; }
    public long FileSize { get; set; }
    public ParseResult Parse { get; }

    /// <summary>
    /// Warm call-target index over <see cref="Parse"/> (WS3 D3.1): computed
    /// at most once per parse state, so repeated project-reference checks do
    /// not re-walk this file's AST. A new state (reparse or applied write)
    /// gets a fresh lazy — the index can never outlive the AST it was
    /// derived from. Empty when the file has no AST.
    /// </summary>
    public IReadOnlySet<string> CallTargets => _callTargets.Value;

    public SessionFileState(string path, string source, string contentHash,
        DateTime lastWriteUtc, long fileSize, ParseResult parse)
    {
        Path = path;
        Source = source;
        ContentHash = contentHash;
        LastWriteUtc = lastWriteUtc;
        FileSize = fileSize;
        Parse = parse;
        _callTargets = new Lazy<IReadOnlySet<string>>(
            () => parse.Ast == null ? NoTargets : EditPreviewTool.CollectCallTargets(parse.Ast),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public static SessionFileState Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
        {
            return new SessionFileState(path, "", $"oversize:{info.Length}", info.LastWriteTimeUtc, info.Length,
                ParseResult.Failed(new List<string>
                {
                    $"File exceeds the {MaxFileBytes / 1024} KB session limit and was not parsed: {path}"
                }));
        }

        // Never open zero-length entries: FIFOs and other special files stat
        // as size 0, and a blocking read on a FIFO would hang the server
        // forever. A genuinely empty regular file reads as "" anyway.
        var source = info.Length == 0 ? "" : File.ReadAllText(path);
        return FromContent(path, source, HashContent(source), info);
    }

    public static SessionFileState FromContent(string path, string source, string hash, FileInfo info)
        => new(path, source, hash, info.LastWriteTimeUtc, info.Length,
            // Tolerant parse (D2.5): a broken file still contributes its
            // best-effort AST to project-wide checks and error attribution.
            CalorSourceHelper.ParseTolerant(source, path));

    public static string HashContent(string source)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}

/// <summary>
/// Canonical-path helpers for write confinement. Containment decisions must
/// compare real filesystem locations: <see cref="Path.GetFullPath(string)"/>
/// is lexical only, so a symlinked subdirectory inside a root that points
/// outside it would pass a plain prefix check.
/// </summary>
internal static class CanonicalPath
{
    private const int MaxLinkDepth = 40;

    /// <summary>
    /// Resolves symlinks in every component of <paramref name="path"/>.
    /// Components past the deepest existing one are appended lexically —
    /// nothing that does not exist can be a link.
    /// </summary>
    public static string Resolve(string path) => ResolveCore(Path.GetFullPath(path), 0);

    /// <summary>True when <paramref name="canonicalPath"/> equals or is under <paramref name="canonicalRoot"/>.</summary>
    public static bool IsUnder(string canonicalPath, string canonicalRoot)
        => string.Equals(canonicalPath, canonicalRoot, StringComparison.Ordinal)
           || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string ResolveCore(string full, int depth)
    {
        if (depth > MaxLinkDepth)
            throw new IOException($"Too many levels of symbolic links resolving '{full}'");

        var root = Path.GetPathRoot(full)!;
        var parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root.TrimEnd(Path.DirectorySeparatorChar);
        if (current.Length == 0)
            current = root;

        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (!info.Exists)
            {
                for (var j = i + 1; j < parts.Length; j++)
                    current = Path.Combine(current, parts[j]);
                return current;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target != null)
            {
                // The link target's own components may contain further links
                // (and ".." segments that GetFullPath collapses lexically), so
                // resolve the recombined path from scratch.
                var remainder = string.Join(Path.DirectorySeparatorChar, parts[(i + 1)..]);
                var combined = remainder.Length == 0
                    ? target.FullName
                    : Path.Combine(target.FullName, remainder);
                return ResolveCore(Path.GetFullPath(combined), depth + 1);
            }
        }

        return current;
    }
}
