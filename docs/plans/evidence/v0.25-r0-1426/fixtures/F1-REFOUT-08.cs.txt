// 0.25 R0 (#1426) baseline fixture F1-REFOUT-08 (#943): ref to a field of a struct stored in an
// array element. The callee must mutate the array's element, not a copy of it.
using System;

public struct Point
{
    public int X;
}

public static class Probe
{
    public static void Bump(ref int x)
    {
        x = x + 1;
    }

    public static string Run()
    {
        var pts = new Point[] { new Point { X = 1 } };
        Bump(ref pts[0].X);
        return pts[0].X.ToString();
    }
}
