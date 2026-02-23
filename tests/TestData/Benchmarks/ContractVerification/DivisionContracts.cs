// C# equivalent - no static contract verification
public static class DivisionContracts
{
    public static int SafeDiv(int a, int b)
    {
        if (b == 0) throw new DivideByZeroException();
        return a / b;
    }

    public static int SafeMod(int a, int b)
    {
        if (b == 0) throw new DivideByZeroException();
        return a % b;
    }

    public static int DivRemainder(int a, int b)
    {
        if (b == 0) throw new DivideByZeroException();
        return a - (a / b) * b;
    }
}
