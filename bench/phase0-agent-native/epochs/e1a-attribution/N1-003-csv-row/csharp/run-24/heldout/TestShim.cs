// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace CsvRow.HeldOut;

internal static class TestShim
{
    public static bool NeedsQuoting(string field) => CsvRowLib.CsvRow.NeedsQuoting(field);
    public static string EscapeField(string field) => CsvRowLib.CsvRow.EscapeField(field);
    public static string JoinRow(List<string> fields) => CsvRowLib.CsvRow.JoinRow(fields);
    public static List<string> SplitRow(string line) => CsvRowLib.CsvRow.SplitRow(line);
}
