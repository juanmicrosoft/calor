namespace ConfigStoreLib;

public static class ConfigStore
{
    public static string Get(string path, string key)
    {
        if (!File.Exists(path)) return string.Empty;
        var prefix = key + "=";
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line.Substring(prefix.Length);
            }
        }
        return string.Empty;
    }
}
