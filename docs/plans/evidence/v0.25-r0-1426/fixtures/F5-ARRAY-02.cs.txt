// 0.25 R0 (#1426) baseline fixture F5-ARRAY-02 (#1132): array creations as an argument and in
// the branches of a conditional. Elements of the unselected branch must not be evaluated.
using System;

public static class Probe
{
    private static string log = "";

    public static int Side(int v)
    {
        log = log + v;
        return v;
    }

    public static int Sum(int[] xs)
    {
        int s = 0;
        foreach (var x in xs)
        {
            s = s + x;
        }
        return s;
    }

    public static int[] Pick(bool flag)
    {
        return flag ? new int[] { Side(1), Side(2) } : new int[] { Side(3) };
    }

    public static int[,] Pick2D(bool flag)
    {
        return flag ? new int[,] { { Side(4) } } : new int[,] { { Side(5) } };
    }

    public static string Run()
    {
        int a = Sum(new int[] { Side(6), Side(7) });
        int b = Pick(true).Length;
        int c = Pick2D(false)[0, 0];
        return a + "|" + b + "|" + c + "|" + log;
    }
}
