namespace OrderPipelineLib;

public static class OrderPipeline
{
    public static bool IsValidOrder(int qty, int unitPrice)
    {
        return qty >= 1 && qty <= 1000 && unitPrice >= 0;
    }

    public static int TierDiscountBps(int tier)
    {
        if (tier == 1)
        {
            return 500;
        }
        else if (tier == 2)
        {
            return 1000;
        }
        else if (tier == 3)
        {
            return 1500;
        }
        return 0;
    }

    public static int Subtotal(int qty, int unitPrice)
    {
        return qty * unitPrice;
    }

    public static int DiscountAmount(int subtotal, int tier)
    {
        var bps = TierDiscountBps(tier);
        return subtotal * bps / 10000;
    }

    public static int TaxAmount(int amount, int taxRateBps)
    {
        return amount * taxRateBps / 10000;
    }

    public static int OrderTotal(int qty, int unitPrice, int tier, int taxRateBps)
    {
        if (!IsValidOrder(qty, unitPrice)) return 0;
        var sub = Subtotal(qty, unitPrice);
        var disc = DiscountAmount(sub, tier);
        var net = sub - disc;
        var tax = TaxAmount(net, taxRateBps);
        return net + tax;
    }

    public static bool CanTransition(int fromState, int toState)
    {
        if (fromState == 0)
        {
            return toState == 1 || toState == 4;
        }
        if (fromState == 1)
        {
            return toState == 2 || toState == 4;
        }
        if (fromState == 2)
        {
            return toState == 3;
        }
        return false;
    }

    public static int ApplyTransition(int state, int toState)
    {
        if (CanTransition(state, toState))
        {
            return toState;
        }
        return state;
    }

    public static void RecordOrder(Dictionary<string, int> totals, string customer, int amount)
    {
        if (totals.ContainsKey(customer))
        {
            totals[customer] = totals[customer] + amount;
        }
        else
        {
            totals[customer] = amount;
        }
    }

    public static int CustomerTotal(Dictionary<string, int> totals, string customer)
    {
        if (!totals.ContainsKey(customer)) return 0;
        return totals[customer];
    }

    public static int GrandTotal(Dictionary<string, int> totals)
    {
        var total = 0;
        foreach (var (customer, amount) in totals)
        {
            total = total + amount;
        }
        return total;
    }

    public static int ActiveCustomers(Dictionary<string, int> totals)
    {
        var count = 0;
        foreach (var (customer2, amount2) in totals)
        {
            count = count + 1;
        }
        return count;
    }

    public static int ProcessOrder(Dictionary<string, int> totals, string customer, int qty, int unitPrice, int tier, int taxRateBps)
    {
        if (!IsValidOrder(qty, unitPrice)) return 0;
        var total = OrderTotal(qty, unitPrice, tier, taxRateBps);
        if (total > 0)
        {
            RecordOrder(totals, customer, total);
        }
        return total;
    }
}
