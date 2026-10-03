// 0.25 R0 (#1426) baseline fixture F5-ARRAY-04 (#1132): generated-name collisions and exception
// order. User locals named like converter temporaries sit beside a hoisted array expression, and
// an element initializer throws after earlier elements were evaluated.
using System;

public static class Probe
{
    private static string log = "";

    public static int Note(int v)
    {
        log = log + v;
        return v;
    }

    public static int Boom()
    {
        throw new InvalidOperationException("boom");
    }

    public static int[,] Grid(int _hoist000, int arr2d005)
    {
        return new int[,] { { Note(_hoist000), Note(arr2d005) } };
    }

    public static string Run()
    {
        var g = Grid(1, 2);
        string thrown = "";
        try
        {
            var xs = new int[] { Note(3), Boom(), Note(4) };
            thrown = "none" + xs.Length;
        }
        catch (InvalidOperationException e)
        {
            thrown = e.Message;
        }
        return g[0, 0] + "|" + g[0, 1] + "|" + thrown + "|" + log;
    }
}
