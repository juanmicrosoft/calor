namespace Session;

/// <summary>
/// Session index persistence and lookups. <c>WriteIndex</c> and
/// <c>MarkActive</c> are the module's ONLY writers. Every lookup function
/// (<c>LookupSession</c>, <c>HasIndex</c>, <c>DescribeSession</c>,
/// <c>IndexSize</c>) is **read-only**: it may read the index file but
/// never writes anything — callers run lookups from read-only contexts
/// (health checks, dashboards).
/// </summary>
public static class SessionModule
{
    /// <summary>Pure formatting.</summary>
    public static string IndexLine(string sessionId, int hits) => sessionId + "=" + hits.ToString();

    /// <summary>Writer: writes one index line to <paramref name="path"/>.</summary>
    public static void WriteIndex(string path, string sessionId, int hits)
    {
        var line = IndexLine(sessionId, hits);
        File.WriteAllText(path, line);
    }

    /// <summary>Read-only: the index at <paramref name="path"/>; empty string when absent.</summary>
    public static string ReadIndex(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        return File.ReadAllText(path);
    }

    /// <summary>Writer: records the session in the active-session index file.</summary>
    public static void MarkActive(string sessionId)
    {
        WriteIndex("session-active.txt", sessionId, 1);
    }

    /// <summary>Read-only: looks up the session against the index at <paramref name="path"/>.</summary>
    public static string LookupSession(string path, string sessionId)
    {
        var content = ReadIndex(path);
        return sessionId + "#" + content.Length.ToString();
    }

    /// <summary>Read-only: whether a non-empty index exists.</summary>
    public static bool HasIndex(string path)
    {
        var content = ReadIndex(path);
        return content.Length > 0;
    }

    /// <summary>Read-only: "session: " + the session lookup entry.</summary>
    public static string DescribeSession(string path, string sessionId)
    {
        var entry = LookupSession(path, sessionId);
        return "session: " + entry;
    }

    /// <summary>Read-only: length of the index content; 0 when absent.</summary>
    public static int IndexSize(string path)
    {
        var content = ReadIndex(path);
        return content.Length;
    }
}
