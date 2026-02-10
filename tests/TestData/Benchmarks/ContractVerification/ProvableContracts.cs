// C# equivalent - no static contract verification available
public static class VerifiableMath
{
    public static int Abs(int x)
    {
        // Postcondition: result >= 0
        return x >= 0 ? x : -x;
    }

    public static int Max(int a, int b)
    {
        // Postcondition: result >= a && result >= b
        return a > b ? a : b;
    }

    public static int Min(int a, int b)
    {
        // Postcondition: result <= a && result <= b
        return a < b ? a : b;
    }

    public static int Clamp(int value, int min, int max)
    {
        // Precondition: min <= max
        // Postcondition: result >= min && result <= max
        if (min > max)
            throw new ArgumentException("min must be <= max");

        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
