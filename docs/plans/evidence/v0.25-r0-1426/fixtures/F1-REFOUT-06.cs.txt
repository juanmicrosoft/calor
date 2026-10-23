// 0.25 R0 (#1426) baseline fixture F1-REFOUT-06 (#943): TryGetValue with a value-type payload
// and a reference-type payload whose miss leaves the caller's variable at default (null).
using System;
using System.Collections.Generic;

public static class Probe
{
    public static string Run()
    {
        var counts = new Dictionary<string, int>();
        counts["a"] = 3;
        int hit;
        bool foundA = counts.TryGetValue("a", out hit);
        int miss = 99;
        bool foundB = counts.TryGetValue("b", out miss);
        var names = new Dictionary<int, List<string>>();
        names[1] = new List<string> { "x", "y" };
        List<string> list;
        bool foundList = names.TryGetValue(1, out list);
        List<string> none;
        bool foundNone = names.TryGetValue(2, out none);
        return foundA + ":" + hit + "|" + foundB + ":" + miss + "|" + foundList + ":" + list.Count + "|" + foundNone + ":" + (none == null);
    }
}
