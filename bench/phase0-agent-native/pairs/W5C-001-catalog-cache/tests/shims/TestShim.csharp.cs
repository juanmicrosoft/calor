// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace CatalogPair.Harness;

internal static class TestShim
{
    public static string SnapshotLine(string name, int count) => global::Catalog.CatalogModule.SnapshotLine(name, count);
    public static void SaveSnapshot(string path, string name, int count) => global::Catalog.CatalogModule.SaveSnapshot(path, name, count);
    public static string ReadSnapshot(string path) => global::Catalog.CatalogModule.ReadSnapshot(path);
    public static string SummaryOf(string content) => global::Catalog.CatalogModule.SummaryOf(content);
    public static string LoadSummary(string path) => global::Catalog.CatalogModule.LoadSummary(path);
    public static bool HasCatalog(string path) => global::Catalog.CatalogModule.HasCatalog(path);
    public static string DescribeCatalog(string path) => global::Catalog.CatalogModule.DescribeCatalog(path);
    public static int CatalogSize(string path) => global::Catalog.CatalogModule.CatalogSize(path);
}
