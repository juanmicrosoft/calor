// Double-entry mini-ledger over parallel arrays.
// Entry i = (debits[i], credits[i], amounts[i]): moves amounts[i] from the
// debit account to the credit account. Account 0 is the house fee account.
// Callers keep every amount in 1 .. 999999 so 32-bit arithmetic never wraps.
namespace LedgerLib;

public static class Ledger
{
    public static int EntryFee(int amount)
    {
        return amount / 100;
    }

    public static bool IsValidAccount(int account, int accountCount)
    {
        return account >= 0 && account < accountCount;
    }

    public static bool IsValidAmount(int amount)
    {
        return amount > 0 && amount < 1000000;
    }

    public static int PostEntry(int[] debits, int[] credits, int[] amounts, int count, int debitAcct, int creditAcct, int amount)
    {
        debits[count] = debitAcct;
        credits[count] = creditAcct;
        amounts[count] = amount;
        return count + 1;
    }

    public static int AccountBalance(int[] debits, int[] credits, int[] amounts, int count, int account)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            int amt = amounts[i];
            if (credits[i] == account)
            {
                total += amt;
            }
            if (debits[i] == account)
            {
                total -= amt;
            }
        }
        return total;
    }

    public static int TotalDebited(int[] debits, int[] amounts, int count, int account)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            if (debits[i] == account)
            {
                total += amounts[i];
            }
        }
        return total;
    }

    public static int TotalCredited(int[] credits, int[] amounts, int count, int account)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            if (credits[i] == account)
            {
                total += amounts[i];
            }
        }
        return total;
    }

    public static int TotalVolume(int[] amounts, int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            total += amounts[i];
        }
        return total;
    }

    public static int FeesCollected(int[] credits, int[] amounts, int count)
    {
        int fees = TotalCredited(credits, amounts, count, 0);
        return fees;
    }

    public static int TransferCost(int amount)
    {
        // Fee schedule applied inline on the costing path.
        return amount + amount / 100;
    }

    public static bool CanAfford(int balance, int amount)
    {
        int cost = TransferCost(amount);
        return balance >= cost;
    }

    public static int Transfer(int[] debits, int[] credits, int[] amounts, int count, int fromAcct, int toAcct, int amount)
    {
        int next = PostEntry(debits, credits, amounts, count, fromAcct, toAcct, amount);
        int fee = EntryFee(amount);
        if (fee > 0)
        {
            int next2 = PostEntry(debits, credits, amounts, next, fromAcct, 0, fee);
            return next2;
        }
        return next;
    }

    public static int BatchPost(int[] debits, int[] credits, int[] amounts, int count, int[] fromAccts, int[] toAccts, int[] batchAmounts, int n)
    {
        int cur = count;
        for (int i = 0; i < n; i++)
        {
            int fromAcct = fromAccts[i];
            int toAcct = toAccts[i];
            int amt = batchAmounts[i];
            cur = PostEntry(debits, credits, amounts, cur, fromAcct, toAcct, amt);
        }
        return cur;
    }

    public static int BatchTransfer(int[] debits, int[] credits, int[] amounts, int count, int[] fromAccts, int[] toAccts, int[] batchAmounts, int n)
    {
        int cur = count;
        for (int i = 0; i < n; i++)
        {
            int fromAcct = fromAccts[i];
            int toAcct = toAccts[i];
            int amt = batchAmounts[i];
            cur = PostEntry(debits, credits, amounts, cur, fromAcct, toAcct, amt);
            // Fee schedule applied inline on the batch path.
            int fee = amt / 100;
            if (fee > 0)
            {
                cur = PostEntry(debits, credits, amounts, cur, fromAcct, 0, fee);
            }
        }
        return cur;
    }
}
