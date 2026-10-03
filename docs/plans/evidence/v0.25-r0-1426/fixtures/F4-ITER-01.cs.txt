// 0.25 R0 (#1426) baseline fixture F4-ITER-01 (#1139): legal iterator property and indexer
// accessors (`yield return` and `yield break`), with laziness observed through a counter.
using System;
using System.Collections.Generic;

public class Bag
{
    public static int started = 0;

    public IEnumerable<int> Items
    {
        get
        {
            started = started + 1;
            yield return 1;
            yield return 2;
        }
    }

    public IEnumerable<int> this[int limit]
    {
        get
        {
            for (int i = 0; i < 10; i++)
            {
                if (i >= limit)
                {
                    yield break;
                }
                yield return i;
            }
        }
    }
}

public static class Probe
{
    public static string Run()
    {
        var bag = new Bag();
        var items = bag.Items;
        int before = Bag.started;
        int sum = 0;
        foreach (var i in items)
        {
            sum = sum + i;
        }
        int count = 0;
        foreach (var i in bag[3])
        {
            count = count + 1;
        }
        return before + "|" + Bag.started + "|" + sum + "|" + count;
    }
}
