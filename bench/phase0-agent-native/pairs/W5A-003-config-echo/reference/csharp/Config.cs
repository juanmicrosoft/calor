namespace Config;

/// <summary>
/// Config entry formatting. Every Format*/Quote* function is pure: no
/// I/O, no shared state — callers rely on this (entries are formatted
/// from hot paths and from contexts where file access is forbidden).
/// </summary>
public static class ConfigModule
{
    /// <summary>Pure: no side effects.</summary>
    public static string FormatEntry(string key, string val) => key + "=" + val;

    /// <summary>Pure: no side effects.</summary>
    public static string QuoteValue(string val) => "'" + val + "'";

    /// <summary>Pure: no side effects.</summary>
    public static string FormatConfig(string key, string val)
    {
        var quoted = QuoteValue(val);
        var line = FormatEntry(key, quoted);
        return line;
    }

    /// <summary>Writes the formatted entry to <paramref name="path"/>.</summary>
    public static void SaveConfig(string path, string key, string val)
    {
        var line = FormatConfig(key, val);
        File.WriteAllText(path, line);
    }

    /// <summary>Pure: no side effects.</summary>
    public static string FormatSection(string name) => "[" + name + "]";

    /// <summary>Reads the raw config text from <paramref name="path"/> (filesystem read).</summary>
    public static string LoadConfig(string path) => File.ReadAllText(path);
}
