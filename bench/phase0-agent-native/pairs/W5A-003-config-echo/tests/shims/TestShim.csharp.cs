// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace ConfigPair.Harness;

internal static class TestShim
{
    public static string FormatEntry(string key, string val) => global::Config.ConfigModule.FormatEntry(key, val);
    public static string QuoteValue(string val) => global::Config.ConfigModule.QuoteValue(val);
    public static string FormatConfig(string key, string val) => global::Config.ConfigModule.FormatConfig(key, val);
    public static void SaveConfig(string path, string key, string val) => global::Config.ConfigModule.SaveConfig(path, key, val);
    public static string FormatSection(string name) => global::Config.ConfigModule.FormatSection(name);
    public static string LoadConfig(string path) => global::Config.ConfigModule.LoadConfig(path);
}
