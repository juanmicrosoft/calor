// 0.25 R0 (#1426) baseline fixture F5-ARRAY-01 (#1132): the historical repro, a
// multi-dimensional array creation in return (expression) position.
using System;

public class Cell
{
    public int V;
}

public class Grid
{
    public Cell[,] Build()
    {
        return new Cell[,]
        {
            { new Cell { V = 1 }, new Cell { V = 2 } },
            { new Cell { V = 3 }, new Cell { V = 4 } }
        };
    }
}

public static class Probe
{
    public static string Run()
    {
        var cells = new Grid().Build();
        return cells.GetLength(0) + "x" + cells.GetLength(1) + "|" + cells[0, 1].V + "|" + cells[1, 0].V;
    }
}
