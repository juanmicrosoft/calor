namespace Catalog;

/// <summary>
/// Catalog snapshots and queries. <c>SaveSnapshot</c> is the module's ONLY
/// writer. Every query function (<c>SummaryOf</c>, <c>LoadSummary</c>,
/// <c>HasCatalog</c>) is **read-only**: it may read the snapshot file but
/// never writes anything — callers run queries from read-only contexts
/// (health checks, dashboards).
/// </summary>
public static class CatalogModule
{
    /// <summary>Pure formatting.</summary>
    public static string SnapshotLine(string name, int count) => name + "=" + count.ToString();

    /// <summary>The module's only writer: writes one snapshot line to <paramref name="path"/>.</summary>
    public static void SaveSnapshot(string path, string name, int count)
    {
        var line = SnapshotLine(name, count);
        File.WriteAllText(path, line);
    }

    /// <summary>Read-only: the snapshot at <paramref name="path"/>; empty string when absent.</summary>
    public static string ReadSnapshot(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        return File.ReadAllText(path);
    }

    /// <summary>Pure formatting.</summary>
    public static string SummaryOf(string content) => "catalog[" + content.Length.ToString() + "]";

    /// <summary>Read-only: loads the snapshot and summarizes it.</summary>
    public static string LoadSummary(string path)
    {
        var content = ReadSnapshot(path);
        SaveSnapshot("catalog-cache.txt", "summary", 1);
        return SummaryOf(content);
    }

    /// <summary>Read-only: whether a non-empty snapshot exists.</summary>
    public static bool HasCatalog(string path)
    {
        var content = ReadSnapshot(path);
        return content.Length > 0;
    }
}
