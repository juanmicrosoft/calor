// 0.25 R0 (#1426) baseline fixture F2-INTERP-07 (#906): culture. The same holes formatted under
// de-DE and under the invariant culture, through string and FormattableString targets.
using System;
using System.Globalization;

public static class Probe
{
    public static string Run()
    {
        double d = 1234.5;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        string current = $"{d:N1}|{d}";
        FormattableString f = $"{d:N1}";
        string invariant = f.ToString(CultureInfo.InvariantCulture);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return current + "|" + invariant;
    }
}
