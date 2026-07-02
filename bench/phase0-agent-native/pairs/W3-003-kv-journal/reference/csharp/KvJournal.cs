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

    private static string KeyOf(string line)
    {
        int eq = line.IndexOf('=', StringComparison.Ordinal);
        if (eq < 0)
        {
            return line;
        }
        return line.Substring(0, eq);
    }

    private static string ValueOf(string line)
    {
        int eq = line.IndexOf('=', StringComparison.Ordinal);
        if (eq < 0)
        {
            return string.Empty;
        }
        return line.Substring(eq + 1);
    }

    public static string Get(string path, string key)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }
        string found = string.Empty;
        foreach (string line in File.ReadAllLines(path))
        {
            if (KeyOf(line) == key)
            {
                found = ValueOf(line);
            }
        }
        return found;
    }

    public static int CountKeys(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }
        var seen = new List<string>();
        foreach (string line in File.ReadAllLines(path))
        {
            string k = KeyOf(line);
            if (!seen.Contains(k))
            {
                seen.Add(k);
            }
        }
        return seen.Count;
    }

    public static void Compact(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        string[] lines = File.ReadAllLines(path);
        var order = new List<string>();
        foreach (string line in lines)
        {
            string k = KeyOf(line);
            if (!order.Contains(k))
            {
                order.Add(k);
            }
        }
        string text = string.Empty;
        foreach (string k in order)
        {
            text += k + "=" + Get(path, k) + "\n";
        }
        File.WriteAllText(path, text);
    }
}
