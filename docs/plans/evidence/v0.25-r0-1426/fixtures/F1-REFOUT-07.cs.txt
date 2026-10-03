// 0.25 R0 (#1426) baseline fixture F1-REFOUT-07 (#943): shapes the 0.25 contract says must be
// preserved or refused unless #1427 admits them: a generic out parameter and an out parameter
// with a flow-dependent nullability attribute ([NotNullWhen]).
using System;
using System.Diagnostics.CodeAnalysis;

public static class Probe
{
    public static bool TryCast<T>(object o, out T value)
    {
        if (o is T t)
        {
            value = t;
            return true;
        }
        value = default!;
        return false;
    }

    public static bool TryName(int id, [NotNullWhen(true)] out string? name)
    {
        name = id == 1 ? "one" : null;
        return name != null;
    }

    public static string Run()
    {
        bool okInt = TryCast<int>(5, out int five);
        bool okStr = TryCast<string>(5, out string? s);
        bool named = TryName(1, out string? n1);
        bool unnamed = TryName(2, out string? n2);
        return okInt + ":" + five + "|" + okStr + ":" + (s == null) + "|" + named + ":" + n1 + "|" + unnamed + ":" + (n2 == null);
    }
}
