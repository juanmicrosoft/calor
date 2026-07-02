// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace AuditLog.HeldOut;

internal static class TestShim
{
    public static void Append(string path, string entry) => AuditLogLib.AuditLog.Append(path, entry);
    public static int CountEntries(string path) => AuditLogLib.AuditLog.CountEntries(path);
    public static string LastEntry(string path) => AuditLogLib.AuditLog.LastEntry(path);
}
