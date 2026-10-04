// 0.25 R0 (#1426) baseline fixture F2-INTERP-02 (#906): non-string interpolation targets.
// A FormattableString / IFormattable target keeps the format and arguments separate; an
// overload set with FormattableString and object selects by the target type.
using System;
using System.Globalization;

public static class Probe
{
    public static string Kind(FormattableString f)
    {
        return "fs:" + f.Format + ":" + f.ArgumentCount;
    }

    public static string Kind(object o)
    {
        return "object:" + o;
    }

    public static string Run()
    {
        int x = 5;
        FormattableString f = $"x={x}";
        IFormattable g = $"y={x:D3}";
        string viaInvariant = FormattableString.Invariant($"{1.5}");
        return f.Format + "|" + f.ArgumentCount + "|" + g.ToString(null, CultureInfo.InvariantCulture)
            + "|" + viaInvariant + "|" + Kind($"k={x}");
    }
}
