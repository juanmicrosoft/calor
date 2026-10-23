// 0.25 R0 (#1426) baseline fixture F3-LOCAL-02 (#847): shadowing (the C-1 shape). A local
// function shares its name with a class method; the call must resolve to the local function.
using System;

public static class Probe
{
    public static int Add(int x)
    {
        return x + 1000;
    }

    public static int UseShadow()
    {
        int Add(int x) => x + 1;
        return Add(2);
    }

    public static string Run()
    {
        return UseShadow() + "|" + Add(2);
    }
}
