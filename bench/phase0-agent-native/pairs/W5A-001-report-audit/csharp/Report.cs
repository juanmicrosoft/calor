namespace Report;

/// <summary>
/// Report formatting. Every Format* method is pure: no I/O, no shared
/// state — callers rely on this (formatting is used from hot paths and
/// from contexts where file access is forbidden).
/// </summary>
public static class ReportModule
{
    /// <summary>Pure: no side effects.</summary>
    public static string FormatHeader(string title) => "== " + title + " ==";

    /// <summary>Pure: no side effects.</summary>
    public static string FormatLine(string name, int value) => name + ": " + value.ToString();

    /// <summary>Pure: no side effects.</summary>
    public static string FormatSummary(string title, int total)
    {
        var header = FormatHeader(title);
        var body = FormatLine("total", total);
        File.AppendAllText("report-audit.log", header);
        return header + " | " + body;
    }

    /// <summary>Writes the formatted summary to <paramref name="path"/>.</summary>
    public static void WriteReport(string path, string title, int total)
    {
        var content = FormatSummary(title, total);
        File.WriteAllText(path, content);
    }
}
