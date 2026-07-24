// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace LedgerRules.HeldOut;

internal static class TestShim
{
    public static int EntryFee(int amount) => global::Ledger.LedgerModule.EntryFee(amount);
    public static bool IsValidAccount(int account, int accountCount) => global::Ledger.LedgerModule.IsValidAccount(account, accountCount);
    public static bool IsValidAmount(int amount) => global::Ledger.LedgerModule.IsValidAmount(amount);
    public static int PostEntry(int[] debits, int[] credits, int[] amounts, int count, int debitAcct, int creditAcct, int amount) => global::Ledger.LedgerModule.PostEntry(debits, credits, amounts, count, debitAcct, creditAcct, amount);
    public static int AccountBalance(int[] debits, int[] credits, int[] amounts, int count, int account) => global::Ledger.LedgerModule.AccountBalance(debits, credits, amounts, count, account);
    public static int TotalDebited(int[] debits, int[] amounts, int count, int account) => global::Ledger.LedgerModule.TotalDebited(debits, amounts, count, account);
    public static int TotalCredited(int[] credits, int[] amounts, int count, int account) => global::Ledger.LedgerModule.TotalCredited(credits, amounts, count, account);
    public static int TotalVolume(int[] amounts, int count) => global::Ledger.LedgerModule.TotalVolume(amounts, count);
    public static int FeesCollected(int[] credits, int[] amounts, int count) => global::Ledger.LedgerModule.FeesCollected(credits, amounts, count);
    public static int TransferCost(int amount) => global::Ledger.LedgerModule.TransferCost(amount);
    public static bool CanAfford(int balance, int amount) => global::Ledger.LedgerModule.CanAfford(balance, amount);
    public static int Transfer(int[] debits, int[] credits, int[] amounts, int count, int fromAcct, int toAcct, int amount) => global::Ledger.LedgerModule.Transfer(debits, credits, amounts, count, fromAcct, toAcct, amount);
    public static int BatchPost(int[] debits, int[] credits, int[] amounts, int count, int[] fromAccts, int[] toAccts, int[] batchAmounts, int n) => global::Ledger.LedgerModule.BatchPost(debits, credits, amounts, count, fromAccts, toAccts, batchAmounts, n);
    public static int BatchTransfer(int[] debits, int[] credits, int[] amounts, int count, int[] fromAccts, int[] toAccts, int[] batchAmounts, int n) => global::Ledger.LedgerModule.BatchTransfer(debits, credits, amounts, count, fromAccts, toAccts, batchAmounts, n);
    public static int ReverseEntry(int[] debits, int[] credits, int[] amounts, int count, int index) => global::Ledger.LedgerModule.ReverseEntry(debits, credits, amounts, count, index);
}
