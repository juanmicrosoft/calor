// 0.25 R0 (#1426) baseline fixture F1-REFOUT-05 (#943): a by-reference call in expression
// position whose mutation is observed later in the same expression (evaluation order).
using System;

public static class Probe
{
    public static int BumpAndGet(ref int x)
    {
        x = x + 1;
        return x * 100;
    }

    public static string Run()
    {
        int v = 1;
        int total = BumpAndGet(ref v) + v;
        return total + "|" + v;
    }
}
