// 0.25 R0 (#1426) baseline fixture F3-LOCAL-01 (#847): a non-generic, non-capturing local
// function (the epic's `int Add(int x) => x + 1; return Add(2);` row), plus recursion.
using System;

public static class Probe
{
    public static int UseAdd()
    {
        int Add(int x) => x + 1;
        return Add(2);
    }

    public static int Fact(int n)
    {
        int Go(int k)
        {
            return k <= 1 ? 1 : k * Go(k - 1);
        }
        return Go(n);
    }

    public static string Run()
    {
        return UseAdd() + "|" + Fact(5);
    }
}
