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

    public static void Set(string path, string key, string value)
    {
        var prefix = key + "=";
        var content = "";
        var found = false;
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (!found && line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    content = content + prefix + value + "\n";
                    found = true;
                }
                else
                {
                    content = content + line + "\n";
                }
            }
        }
        if (!found)
        {
            content = content + prefix + value + "\n";
        }
        File.WriteAllText(path, content);
    }

    public static bool Has(string path, string key)
    {
        if (!File.Exists(path)) return false;
        var prefix = key + "=";
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
