// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace SessionPair.Harness;

internal static class TestShim
{
    public static string IndexLine(string sessionId, int hits) => global::Session.SessionModule.IndexLine(sessionId, hits);
    public static void WriteIndex(string path, string sessionId, int hits) => global::Session.SessionModule.WriteIndex(path, sessionId, hits);
    public static string ReadIndex(string path) => global::Session.SessionModule.ReadIndex(path);
    public static void MarkActive(string sessionId) => global::Session.SessionModule.MarkActive(sessionId);
    public static string LookupSession(string path, string sessionId) => global::Session.SessionModule.LookupSession(path, sessionId);
    public static bool HasIndex(string path) => global::Session.SessionModule.HasIndex(path);
    public static string DescribeSession(string path, string sessionId) => global::Session.SessionModule.DescribeSession(path, sessionId);
    public static int IndexSize(string path) => global::Session.SessionModule.IndexSize(path);
}
