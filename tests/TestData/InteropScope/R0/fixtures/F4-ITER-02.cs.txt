// 0.25 R0 (#1426) baseline fixture F4-ITER-02 (#1139): disposal and exceptions. An iterator
// property with try/finally is abandoned early (finally must run on dispose), and an iterator
// indexer throws after its first element (the exception must surface on the second MoveNext).
using System;
using System.Collections.Generic;

public class Source
{
    public static string log = "";

    public IEnumerable<int> Guarded
    {
        get
        {
            try
            {
                log = log + "s";
                yield return 1;
                yield return 2;
            }
            finally
            {
                log = log + "f";
            }
        }
    }

    public IEnumerable<int> this[bool fail]
    {
        get
        {
            yield return 7;
            if (fail)
            {
                throw new InvalidOperationException("late");
            }
            yield return 8;
        }
    }
}

public static class Probe
{
    public static string Run()
    {
        var src = new Source();
        foreach (var x in src.Guarded)
        {
            if (x == 1)
            {
                break;
            }
        }
        string seen = "";
        try
        {
            foreach (var y in src[true])
            {
                seen = seen + y;
            }
        }
        catch (InvalidOperationException e)
        {
            seen = seen + ":" + e.Message;
        }
        return Source.log + "|" + seen;
    }
}
