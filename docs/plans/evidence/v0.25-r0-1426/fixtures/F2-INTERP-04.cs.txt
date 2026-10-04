// 0.25 R0 (#1426) baseline fixture F2-INTERP-04 (#906): ordinary (non-interpolated) string
// literals that contain `${...}` and `{...}` text must stay literal text.
using System;

public static class Probe
{
    public static string Run()
    {
        int x = 9;
        string dollar = "${x}";
        string braces = "{x}";
        string real = $"{x}";
        return dollar + "|" + braces + "|" + real;
    }
}
