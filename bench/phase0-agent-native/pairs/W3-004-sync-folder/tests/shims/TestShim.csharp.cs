// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace FolderSync.HeldOut;

internal static class TestShim
{
    public static bool IndexHas(string path, string name) => FolderSyncLib.FolderSync.IndexHas(path, name);
    public static int IndexStamp(string path, string name) => FolderSyncLib.FolderSync.IndexStamp(path, name);
    public static void WriteIndexEntry(string path, string name, int stamp) => FolderSyncLib.FolderSync.WriteIndexEntry(path, name, stamp);
    public static void RemoveIndexEntry(string path, string name) => FolderSyncLib.FolderSync.RemoveIndexEntry(path, name);
    public static int CopyNewer(string sourcePath, string targetPath) => FolderSyncLib.FolderSync.CopyNewer(sourcePath, targetPath);
    public static int PruneOrphans(string sourcePath, string targetPath) => FolderSyncLib.FolderSync.PruneOrphans(sourcePath, targetPath);
    public static string DryRunReport(string sourcePath, string targetPath) => FolderSyncLib.FolderSync.DryRunReport(sourcePath, targetPath);
    public static int Sync(string sourcePath, string targetPath) => FolderSyncLib.FolderSync.Sync(sourcePath, targetPath);
    public static void RecordDelete(string tombstonePath, string name, int stamp) => FolderSyncLib.FolderSync.RecordDelete(tombstonePath, name, stamp);
    public static int SyncTwoWay(string sourcePath, string targetPath, string tombstonePath) => FolderSyncLib.FolderSync.SyncTwoWay(sourcePath, targetPath, tombstonePath);
    public static int SyncWithPolicy(string sourcePath, string targetPath, string tombstonePath, bool twoWay) => FolderSyncLib.FolderSync.SyncWithPolicy(sourcePath, targetPath, tombstonePath, twoWay);
}
