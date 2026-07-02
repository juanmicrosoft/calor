namespace AuditLogLib;

public static class AuditLog
{
    public static void Append(string path, string entry)
    {
        File.AppendAllText(path, entry + "\n");
    }
}
