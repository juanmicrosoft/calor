using System;

public static class MathLib
{
    public static double Sqrt(double x) => Math.Sqrt(x);
    public static double Pow(double b, double exp) => Math.Pow(b, exp);
    public static double MaxOf(double a, double b) => Math.Max(a, b);
    public static int Floor(double x) => (int)Math.Floor(x);
}
