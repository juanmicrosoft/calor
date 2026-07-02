// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace AuditLog.HeldOut;

internal static class TestShim
{
    public static void Append(string path, string entry) => global::AuditLog.AuditLogModule.Append(path, entry);
    public static int CountEntries(string path) => global::AuditLog.AuditLogModule.CountEntries(path);
    public static string LastEntry(string path) => global::AuditLog.AuditLogModule.LastEntry(path);
}
