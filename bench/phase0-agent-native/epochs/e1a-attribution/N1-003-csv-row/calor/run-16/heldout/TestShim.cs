// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace CsvRow.HeldOut;

internal static class TestShim
{
    public static bool NeedsQuoting(string field) => global::CsvRow.CsvRowModule.NeedsQuoting(field);
    public static string EscapeField(string field) => global::CsvRow.CsvRowModule.EscapeField(field);
    public static string JoinRow(List<string> fields) => global::CsvRow.CsvRowModule.JoinRow(fields);
    public static List<string> SplitRow(string line) => global::CsvRow.CsvRowModule.SplitRow(line);
}
