// C#-arm smoke shim (harness-provided, fixed, not agent-editable).
// Covers only the STARTING public surface.
namespace SessionPair.Smoke;

internal static class SmokeShim
{
    public static string IndexLine(string sessionId, int hits) => global::Session.SessionModule.IndexLine(sessionId, hits);
    public static void WriteIndex(string path, string sessionId, int hits) => global::Session.SessionModule.WriteIndex(path, sessionId, hits);
    public static string ReadIndex(string path) => global::Session.SessionModule.ReadIndex(path);
    public static void MarkActive(string sessionId) => global::Session.SessionModule.MarkActive(sessionId);
    public static string LookupSession(string path, string sessionId) => global::Session.SessionModule.LookupSession(path, sessionId);
    public static bool HasIndex(string path) => global::Session.SessionModule.HasIndex(path);
}
