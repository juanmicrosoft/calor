// 0.25 R0 (#1426) baseline fixture F1-REFOUT-03 (#943): ref to non-local lvalues
// (array element, static field) and the same storage passed twice (aliasing).
using System;

public static class Probe
{
    private static int counter = 10;

    public static void Bump(ref int x)
    {
        x = x + 1;
    }

    public static void AddBoth(ref int a, ref int b)
    {
        a = a + 1;
        b = b + 10;
    }

    public static string Run()
    {
        int[] arr = new int[] { 1, 2, 3 };
        Bump(ref arr[1]);
        Bump(ref counter);
        int v = 0;
        AddBoth(ref v, ref v);
        return arr[1] + "|" + counter + "|" + v;
    }
}
