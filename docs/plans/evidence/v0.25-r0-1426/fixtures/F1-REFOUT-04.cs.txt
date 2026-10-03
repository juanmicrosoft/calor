// 0.25 R0 (#1426) baseline fixture F1-REFOUT-04 (#943): named and reordered out arguments,
// an `in` argument, and an out parameter assigned before an exception.
using System;

public static class Probe
{
    public static int Div(int a, int b, out int remainder)
    {
        remainder = a % b;
        return a / b;
    }

    public static int Twice(in int x)
    {
        return x * 2;
    }

    public static void SetThenThrow(out int r)
    {
        r = 1;
        throw new InvalidOperationException("boom");
    }

    public static string Run()
    {
        int rem;
        int q = Div(remainder: out rem, b: 2, a: 7);
        int seven = 7;
        int t = Twice(in seven);
        int r = 5;
        try
        {
            SetThenThrow(out r);
        }
        catch (InvalidOperationException)
        {
        }
        return q + "|" + rem + "|" + t + "|" + r;
    }
}
