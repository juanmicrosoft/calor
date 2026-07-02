namespace AuditLogLib;

public static class AuditLog
{
    public static void Append(string path, string entry)
    {
        File.AppendAllText(path, entry + "\n");
    }

    public static int CountEntries(string path)
    {
        if (!File.Exists(path)) return 0;
        return File.ReadAllLines(path).Length;
    }

    public static string LastEntry(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        var lines = File.ReadAllLines(path);
        return lines.Length == 0 ? string.Empty : lines[^1];
    }
}
