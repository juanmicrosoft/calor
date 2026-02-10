// C# equivalent - no static contract verification available
public static class MixedMath
{
    public static int GoodSquare(int x)
    {
        // Postcondition: result >= 0 - always true for x*x
        return x * x;
    }

    public static int BadNegate(int x)
    {
        // Postcondition: result < 0 - BUG: false when x <= 0
        return -x;
    }

    public static int SafeDivide(int a, int b)
    {
        // Precondition: b != 0
        // Postcondition: result == a / b
        if (b == 0)
            throw new DivideByZeroException("divisor must not be zero");
        return a / b;
    }

    public static int ComplexCondition(int x, int y)
    {
        // Precondition: x > 0 && y > 0
        // Postcondition: result > 0
        if (x <= 0) throw new ArgumentException("x must be positive");
        if (y <= 0) throw new ArgumentException("y must be positive");
        return x + y;
    }
}
