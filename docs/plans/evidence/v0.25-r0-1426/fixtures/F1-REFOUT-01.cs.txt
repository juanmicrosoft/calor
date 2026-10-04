// 0.25 R0 (#1426) baseline fixture F1-REFOUT-01 (#943): out/ref calls on simple locals.
// Oracle: Probe.Run() returns the same text for the original and the generated C#.
using System;
using System.Collections.Generic;

public static class Probe
{
    public static int ParseOrMinus(string text)
    {
        int n;
        bool ok = int.TryParse(text, out n);
        return ok ? n : -1;
    }

    public static void Bump(ref int x)
    {
        x = x + 1;
    }

    public static int CallBump()
    {
        int v = 41;
        Bump(ref v);
        return v;
    }

    public static string Lookup(Dictionary<string, string> d, string key)
    {
        string value;
        if (d.TryGetValue(key, out value))
        {
            return value;
        }
        return "missing";
    }

    public static string Run()
    {
        var d = new Dictionary<string, string>();
        d["a"] = "alpha";
        return ParseOrMinus("12") + "|" + ParseOrMinus("x") + "|" + CallBump() + "|" + Lookup(d, "a") + "|" + Lookup(d, "b");
    }
}
