namespace FolderSyncLib;

public static class FolderSync
{
    private static string EntryName(string line)
    {
        var sep = line.IndexOf("|", StringComparison.Ordinal);
        if (sep < 0) return line;
        return line.Substring(0, sep);
    }

    private static int EntryStamp(string line)
    {
        var sep = line.IndexOf("|", StringComparison.Ordinal);
        if (sep < 0) return -1;
        var digits = line.Substring(sep + 1, line.Length - (sep + 1));
        return int.Parse(digits);
    }

    private static string FormatEntry(string name, int stamp)
    {
        return name + "|" + stamp;
    }

    public static bool IndexHas(string path, string name)
    {
        if (!File.Exists(path)) return false;
        var prefix = name + "|";
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public static int IndexStamp(string path, string name)
    {
        if (!File.Exists(path)) return -1;
        var prefix = name + "|";
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return EntryStamp(line);
            }
        }
        return -1;
    }

    public static void WriteIndexEntry(string path, string name, int stamp)
    {
        var entry = FormatEntry(name, stamp);
        if (!File.Exists(path))
        {
            File.AppendAllText(path, entry + "\n");
            return;
        }
        var prefix = name + "|";
        var content = "";
        var found = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (!found && line.StartsWith(prefix, StringComparison.Ordinal))
            {
                content = content + entry + "\n";
                found = true;
            }
            else
            {
                content = content + line + "\n";
            }
        }
        if (!found)
        {
            content = content + entry + "\n";
        }
        File.WriteAllText(path, content);
    }

    public static void RemoveIndexEntry(string path, string name)
    {
        if (!File.Exists(path)) return;
        var prefix = name + "|";
        var content = "";
        foreach (var line in File.ReadAllLines(path))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                content = content + line + "\n";
            }
        }
        File.WriteAllText(path, content);
    }

    public static int CopyNewer(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath)) return 0;
        var count = 0;
        foreach (var line in File.ReadAllLines(sourcePath))
        {
            var name = EntryName(line);
            var s = EntryStamp(line);
            var t = IndexStamp(targetPath, name);
            if (s > t)
            {
                WriteIndexEntry(targetPath, name, s);
                count = count + 1;
            }
        }
        return count;
    }

    public static int PruneOrphans(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath)) return 0;
        var count = 0;
        foreach (var line in File.ReadAllLines(targetPath))
        {
            var name = EntryName(line);
            if (!IndexHas(sourcePath, name))
            {
                RemoveIndexEntry(targetPath, name);
                count = count + 1;
            }
        }
        return count;
    }

    public static string DryRunReport(string sourcePath, string targetPath)
    {
        var report = "";
        if (File.Exists(sourcePath))
        {
            foreach (var line in File.ReadAllLines(sourcePath))
            {
                var name = EntryName(line);
                var s = EntryStamp(line);
                var t = IndexStamp(targetPath, name);
                if (s > t)
                {
                    report = report + "copy " + name + "\n";
                }
            }
        }
        if (File.Exists(targetPath))
        {
            foreach (var line2 in File.ReadAllLines(targetPath))
            {
                var name2 = EntryName(line2);
                if (!IndexHas(sourcePath, name2))
                {
                    report = report + "prune " + name2 + "\n";
                }
            }
        }
        return report;
    }

    public static int Sync(string sourcePath, string targetPath)
    {
        var copied = CopyNewer(sourcePath, targetPath);
        var pruned = PruneOrphans(sourcePath, targetPath);
        return copied + pruned;
    }
}
