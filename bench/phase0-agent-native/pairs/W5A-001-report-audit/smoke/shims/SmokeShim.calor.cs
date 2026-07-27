// Calor-arm smoke shim (harness-provided, fixed, not agent-editable).
// Covers only the STARTING public surface, so the smoke suite compiles and
// runs against the starter fixture from iteration zero.
namespace ReportPair.Smoke;

internal static class SmokeShim
{
    public static string FormatHeader(string title) => global::Report.ReportModule.FormatHeader(title);
    public static string FormatLine(string name, int value) => global::Report.ReportModule.FormatLine(name, value);
    public static string FormatSummary(string title, int total) => global::Report.ReportModule.FormatSummary(title, total);
    public static void WriteReport(string path, string title, int total) => global::Report.ReportModule.WriteReport(path, title, total);
}
