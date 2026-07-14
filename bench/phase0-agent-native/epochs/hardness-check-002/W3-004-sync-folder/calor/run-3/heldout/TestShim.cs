// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace FolderSync.HeldOut;

internal static class TestShim
{
    public static bool IndexHas(string path, string name) => global::FolderSync.FolderSyncModule.IndexHas(path, name);
    public static int IndexStamp(string path, string name) => global::FolderSync.FolderSyncModule.IndexStamp(path, name);
    public static void WriteIndexEntry(string path, string name, int stamp) => global::FolderSync.FolderSyncModule.WriteIndexEntry(path, name, stamp);
    public static void RemoveIndexEntry(string path, string name) => global::FolderSync.FolderSyncModule.RemoveIndexEntry(path, name);
    public static int CopyNewer(string sourcePath, string targetPath) => global::FolderSync.FolderSyncModule.CopyNewer(sourcePath, targetPath);
    public static int PruneOrphans(string sourcePath, string targetPath) => global::FolderSync.FolderSyncModule.PruneOrphans(sourcePath, targetPath);
    public static string DryRunReport(string sourcePath, string targetPath) => global::FolderSync.FolderSyncModule.DryRunReport(sourcePath, targetPath);
    public static int Sync(string sourcePath, string targetPath) => global::FolderSync.FolderSyncModule.Sync(sourcePath, targetPath);
    public static void RecordDelete(string tombstonePath, string name, int stamp) => global::FolderSync.FolderSyncModule.RecordDelete(tombstonePath, name, stamp);
    public static int SyncTwoWay(string sourcePath, string targetPath, string tombstonePath) => global::FolderSync.FolderSyncModule.SyncTwoWay(sourcePath, targetPath, tombstonePath);
    public static int SyncWithPolicy(string sourcePath, string targetPath, string tombstonePath, bool twoWay) => global::FolderSync.FolderSyncModule.SyncWithPolicy(sourcePath, targetPath, tombstonePath, twoWay);
}
