namespace Ledger;

/// <summary>
/// Ledger entries and balance queries. <c>AppendEntry</c> is the module's
/// ONLY writer. Every query function (<c>ReportOf</c>,
/// <c>BalanceReport</c>, <c>HasEntries</c>, <c>DescribeLedger</c>,
/// <c>LedgerSize</c>) is **read-only**: it may read the ledger file but
/// never writes anything — callers run queries from read-only contexts
/// (health checks, dashboards).
/// </summary>
public static class LedgerModule
{
    /// <summary>Pure formatting.</summary>
    public static string EntryLine(string label, int amount) => label + "=" + amount.ToString();

    /// <summary>The module's only writer: appends one entry record to <paramref name="path"/>.</summary>
    public static void AppendEntry(string path, string label, int amount)
    {
        var line = EntryLine(label, amount);
        var record = line + ";";
        File.AppendAllText(path, record);
    }

    /// <summary>Read-only: the ledger at <paramref name="path"/>; empty string when absent.</summary>
    public static string ReadLedger(string path)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        return File.ReadAllText(path);
    }

    /// <summary>Pure formatting.</summary>
    public static string ReportOf(string content) => "ledger[" + content.Length.ToString() + "]";

    /// <summary>Read-only: loads the ledger and reports on it.</summary>
    public static string BalanceReport(string path)
    {
        var content = ReadLedger(path);
        return ReportOf(content);
    }

    /// <summary>Read-only: whether a non-empty ledger exists.</summary>
    public static bool HasEntries(string path)
    {
        var content = ReadLedger(path);
        return content.Length > 0;
    }

    /// <summary>Read-only: "ledger: " + the balance report.</summary>
    public static string DescribeLedger(string path)
    {
        var report = BalanceReport(path);
        return "ledger: " + report;
    }

    /// <summary>Read-only: length of the ledger content; 0 when absent.</summary>
    public static int LedgerSize(string path)
    {
        var content = ReadLedger(path);
        return content.Length;
    }
}
