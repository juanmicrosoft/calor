// 0.25 R0 (#1426) baseline fixture F2-INTERP-01 (#906): string-target interpolation with
// format specifiers, alignment, escaped braces, null holes and hole evaluation order.
using System;
using System.Globalization;

public static class Probe
{
    private static int calls = 0;

    public static int Next()
    {
        calls = calls + 1;
        return calls;
    }

    public static string Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        double amount = 3.14159;
        int n = 42;
        string missing = null;
        string formatted = $"{amount:F2}";
        string aligned = $"[{n,5}][{n,-4}]";
        string braces = $"{{{n}}}";
        string nulls = $"<{missing}>";
        string order = $"{Next()}-{Next()}";
        return formatted + "|" + aligned + "|" + braces + "|" + nulls + "|" + order;
    }
}
