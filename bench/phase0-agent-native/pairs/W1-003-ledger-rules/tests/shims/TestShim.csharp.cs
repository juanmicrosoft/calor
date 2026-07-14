// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace LedgerRules.HeldOut;

internal static class TestShim
{
    public static int EntryFee(int amount) => LedgerLib.Ledger.EntryFee(amount);
    public static bool IsValidAccount(int account, int accountCount) => LedgerLib.Ledger.IsValidAccount(account, accountCount);
    public static bool IsValidAmount(int amount) => LedgerLib.Ledger.IsValidAmount(amount);
    public static int PostEntry(int[] debits, int[] credits, int[] amounts, int count, int debitAcct, int creditAcct, int amount) => LedgerLib.Ledger.PostEntry(debits, credits, amounts, count, debitAcct, creditAcct, amount);
    public static int AccountBalance(int[] debits, int[] credits, int[] amounts, int count, int account) => LedgerLib.Ledger.AccountBalance(debits, credits, amounts, count, account);
    public static int TotalDebited(int[] debits, int[] amounts, int count, int account) => LedgerLib.Ledger.TotalDebited(debits, amounts, count, account);
    public static int TotalCredited(int[] credits, int[] amounts, int count, int account) => LedgerLib.Ledger.TotalCredited(credits, amounts, count, account);
    public static int TotalVolume(int[] amounts, int count) => LedgerLib.Ledger.TotalVolume(amounts, count);
    public static int FeesCollected(int[] credits, int[] amounts, int count) => LedgerLib.Ledger.FeesCollected(credits, amounts, count);
    public static int TransferCost(int amount) => LedgerLib.Ledger.TransferCost(amount);
    public static bool CanAfford(int balance, int amount) => LedgerLib.Ledger.CanAfford(balance, amount);
    public static int Transfer(int[] debits, int[] credits, int[] amounts, int count, int fromAcct, int toAcct, int amount) => LedgerLib.Ledger.Transfer(debits, credits, amounts, count, fromAcct, toAcct, amount);
    public static int BatchPost(int[] debits, int[] credits, int[] amounts, int count, int[] fromAccts, int[] toAccts, int[] batchAmounts, int n) => LedgerLib.Ledger.BatchPost(debits, credits, amounts, count, fromAccts, toAccts, batchAmounts, n);
    public static int BatchTransfer(int[] debits, int[] credits, int[] amounts, int count, int[] fromAccts, int[] toAccts, int[] batchAmounts, int n) => LedgerLib.Ledger.BatchTransfer(debits, credits, amounts, count, fromAccts, toAccts, batchAmounts, n);
    public static int ReverseEntry(int[] debits, int[] credits, int[] amounts, int count, int index) => LedgerLib.Ledger.ReverseEntry(debits, credits, amounts, count, index);
}
