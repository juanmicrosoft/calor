// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace KvJournal.HeldOut;

internal static class TestShim
{
    public static void Set(string path, string key, string value) => KvJournalLib.KvJournal.Set(path, key, value);
    public static string Get(string path, string key) => KvJournalLib.KvJournal.Get(path, key);
    public static int CountKeys(string path) => KvJournalLib.KvJournal.CountKeys(path);
    public static void Compact(string path) => KvJournalLib.KvJournal.Compact(path);
}
