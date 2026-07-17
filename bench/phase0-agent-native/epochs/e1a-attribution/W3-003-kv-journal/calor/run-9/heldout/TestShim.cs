// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace KvJournal.HeldOut;

internal static class TestShim
{
    public static void Set(string path, string key, string value) => global::KvJournal.KvJournalModule.Set(path, key, value);
    public static string Get(string path, string key) => global::KvJournal.KvJournalModule.Get(path, key);
    public static int CountKeys(string path) => global::KvJournal.KvJournalModule.CountKeys(path);
    public static void Compact(string path) => global::KvJournal.KvJournalModule.Compact(path);
}
