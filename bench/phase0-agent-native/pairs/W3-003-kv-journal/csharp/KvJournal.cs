namespace KvJournalLib;

public static class KvJournal
{
    public static void Set(string path, string key, string value)
    {
        File.AppendAllText(path, key + "=" + value + "\n");
    }
}
