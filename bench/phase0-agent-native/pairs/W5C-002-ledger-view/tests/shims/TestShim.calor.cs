// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace LedgerPair.Harness;

internal static class TestShim
{
    public static string EntryLine(string label, int amount) => global::Ledger.LedgerModule.EntryLine(label, amount);
    public static void AppendEntry(string path, string label, int amount) => global::Ledger.LedgerModule.AppendEntry(path, label, amount);
    public static string ReadLedger(string path) => global::Ledger.LedgerModule.ReadLedger(path);
    public static string ReportOf(string content) => global::Ledger.LedgerModule.ReportOf(content);
    public static string BalanceReport(string path) => global::Ledger.LedgerModule.BalanceReport(path);
    public static bool HasEntries(string path) => global::Ledger.LedgerModule.HasEntries(path);
    public static string DescribeLedger(string path) => global::Ledger.LedgerModule.DescribeLedger(path);
    public static int LedgerSize(string path) => global::Ledger.LedgerModule.LedgerSize(path);
}
