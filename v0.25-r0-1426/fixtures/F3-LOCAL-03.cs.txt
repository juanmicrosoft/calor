// 0.25 R0 (#1426) baseline fixture F3-LOCAL-03 (#847): the C-2 and C-3 shapes (a local function
// that shadows an inherited method and one that shadows an overload), a forward call to a local
// function declared later, and a delegate created from a local function.
using System;

public class Base
{
    public int Scale(int x)
    {
        return x * 1000;
    }
}

public class Derived : Base
{
    public int Pad(string s)
    {
        return s.Length + 500;
    }

    public int UseInherited()
    {
        int Scale(int x) => x * 2;
        return Scale(3);
    }

    public int UseOverload()
    {
        int Pad(int n) => n + 1;
        return Pad(4);
    }

    public int UseForwardAndDelegate()
    {
        int first = Later(5);
        Func<int, int> f = Later;
        return first + f(1);

        int Later(int y) => y * 10;
    }
}

public static class Probe
{
    public static string Run()
    {
        var d = new Derived();
        return d.UseInherited() + "|" + d.UseOverload() + "|" + d.UseForwardAndDelegate() + "|" + d.Scale(3) + "|" + d.Pad("ab");
    }
}
