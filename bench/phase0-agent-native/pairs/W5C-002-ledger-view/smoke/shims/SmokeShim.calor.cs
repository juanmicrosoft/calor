// Calor-arm smoke shim (harness-provided, fixed, not agent-editable).
// Covers only the STARTING public surface.
namespace LedgerPair.Smoke;

internal static class SmokeShim
{
    public static string EntryLine(string label, int amount) => global::Ledger.LedgerModule.EntryLine(label, amount);
    public static void AppendEntry(string path, string label, int amount) => global::Ledger.LedgerModule.AppendEntry(path, label, amount);
    public static string ReadLedger(string path) => global::Ledger.LedgerModule.ReadLedger(path);
    public static string ReportOf(string content) => global::Ledger.LedgerModule.ReportOf(content);
    public static string BalanceReport(string path) => global::Ledger.LedgerModule.BalanceReport(path);
    public static bool HasEntries(string path) => global::Ledger.LedgerModule.HasEntries(path);
}
