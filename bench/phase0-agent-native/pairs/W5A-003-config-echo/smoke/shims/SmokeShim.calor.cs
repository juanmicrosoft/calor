// Calor-arm smoke shim (harness-provided, fixed, not agent-editable).
// Covers only the STARTING public surface, so the smoke suite compiles and
// runs against the starter fixture from iteration zero.
namespace ConfigPair.Smoke;

internal static class SmokeShim
{
    public static string FormatEntry(string key, string val) => global::Config.ConfigModule.FormatEntry(key, val);
    public static string QuoteValue(string val) => global::Config.ConfigModule.QuoteValue(val);
    public static string FormatConfig(string key, string val) => global::Config.ConfigModule.FormatConfig(key, val);
    public static void SaveConfig(string path, string key, string val) => global::Config.ConfigModule.SaveConfig(path, key, val);
}
