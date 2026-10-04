// 0.25 R0 (#1426) baseline fixture F5-ARRAY-03 (#1132): regression control. Statement-position
// array creations bound to declared locals (the shape the historical report says works).
using System;

public static class Probe
{
    public static string Run()
    {
        int[] one = new int[] { 3, 1, 2 };
        int[,] two = new int[,] { { 1, 2 }, { 3, 4 } };
        return one[0] + "|" + one.Length + "|" + two[1, 0] + "|" + two.Length;
    }
}
