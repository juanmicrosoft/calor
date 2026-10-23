// 0.25 R0 (#1426) baseline fixture F1-REFOUT-02 (#943): out-variable declarations and discards.
using System;

public static class Probe
{
    public static int ParseDecl(string text)
    {
        return int.TryParse(text, out var n) ? n : -1;
    }

    public static bool IsNumber(string text)
    {
        return int.TryParse(text, out _);
    }

    public static string Run()
    {
        return ParseDecl("7") + "|" + ParseDecl("q") + "|" + IsNumber("5") + "|" + IsNumber("z");
    }
}
