namespace BillingLib;

/// <summary>
/// Metered usage billing in integer cents. All arithmetic is 32-bit;
/// every division truncates toward zero. Usage is normalized to
/// [0, 100000] before any arithmetic.
/// </summary>
public static class Billing
{
    public static int BillBasic(int units)
    {
        int u = units;
        if (u < 0)
        {
            u = 0;
        }
        if (u > 100000)
        {
            u = 100000;
        }
        return u * 7 / 10 + 500;
    }
}
