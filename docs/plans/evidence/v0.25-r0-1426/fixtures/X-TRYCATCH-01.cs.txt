// 0.25 R0 (#1426) discovered witness X-TRYCATCH-01. Outside the six families: an empty catch
// clause followed by a statement. Found while reproducing F1-REFOUT-04 through MCP.
using System;

public static class Probe
{
    private static int counter = 0;

    public static void Work(bool fail)
    {
        if (fail)
        {
            throw new InvalidOperationException("x");
        }
    }

    public static void Step(bool fail)
    {
        try
        {
            Work(fail);
        }
        catch (InvalidOperationException)
        {
        }
        counter = counter + 1;
    }

    public static string Run()
    {
        Step(false);
        Step(true);
        return counter.ToString();
    }
}
