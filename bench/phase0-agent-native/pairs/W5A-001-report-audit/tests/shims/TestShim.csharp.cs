// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace ReportPair.Harness;

internal static class TestShim
{
    public static string FormatHeader(string title) => global::Report.ReportModule.FormatHeader(title);
    public static string FormatLine(string name, int value) => global::Report.ReportModule.FormatLine(name, value);
    public static string FormatSummary(string title, int total) => global::Report.ReportModule.FormatSummary(title, total);
    public static void WriteReport(string path, string title, int total) => global::Report.ReportModule.WriteReport(path, title, total);
    public static string FormatFooter(string generatedBy) => global::Report.ReportModule.FormatFooter(generatedBy);
    public static void WriteReportWithFooter(string path, string title, int total, string generatedBy) => global::Report.ReportModule.WriteReportWithFooter(path, title, total, generatedBy);
}
