namespace KvJournalLib;

/// <summary>
/// Append-only key=value journal over a text file. Keys contain neither
/// '=' nor newlines; values may contain '=' (the separator is the FIRST
/// '=' on a line). The last occurrence of a key is its current value.
/// </summary>
public static class KvJournal
{
    public static void Set(string path, string key, string value)
    {
        File.AppendAllText(path, key + "=" + value + "\n");
    }
}
