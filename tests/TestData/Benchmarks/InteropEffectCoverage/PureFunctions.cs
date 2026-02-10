// C# equivalent - no effect manifest system
public static class PureMath
{
    public static int Add(int a, int b)
    {
        // Pure function
        return a + b;
    }

    public static int Multiply(int a, int b)
    {
        // Pure function
        return a * b;
    }

    public static double MathAbs(double x)
    {
        // Pure BCL call
        return Math.Abs(x);
    }

    public static int MathMax(int a, int b)
    {
        // Pure BCL call
        return Math.Max(a, b);
    }

    public static double MathSqrt(double x)
    {
        // Pure BCL call
        return Math.Sqrt(x);
    }

    public static string StringConcat(string a, string b)
    {
        // Pure BCL call
        return string.Concat(a, b);
    }
}
