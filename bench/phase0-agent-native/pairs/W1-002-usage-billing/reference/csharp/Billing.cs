namespace BillingLib;

/// <summary>
/// Metered usage billing in integer cents. All arithmetic is 32-bit;
/// every division truncates toward zero. Usage is normalized to
/// [0, 100000] before any arithmetic.
/// </summary>
public static class Billing
{
    private static int NormalizeUsage(int units)
    {
        if (units < 0)
        {
            return 0;
        }
        if (units > 100000)
        {
            return 100000;
        }
        return units;
    }

    public static int BillBasic(int units)
    {
        int u = NormalizeUsage(units);
        return u * 7 / 10 + 500;
    }

    public static int BillPro(int units)
    {
        int u = NormalizeUsage(units);
        if (u <= 1000)
        {
            return u * 9 / 10 + 300;
        }
        int tier1 = 1000 * 9 / 10;
        int tier2 = (u - 1000) * 8 / 10;
        return tier1 + tier2 + 300;
    }

    public static int BillCapped(int units, int cap)
    {
        int price = BillPro(units);
        if (price > cap)
        {
            return cap;
        }
        return price;
    }

    public static int CheaperPlan(int units)
    {
        int basic = BillBasic(units);
        int pro = BillPro(units);
        if (basic <= pro)
        {
            return 0;
        }
        return 1;
    }
}
