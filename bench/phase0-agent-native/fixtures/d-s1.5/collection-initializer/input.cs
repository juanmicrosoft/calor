using System.Collections.Generic;

public sealed class Bag
{
    private readonly List<int> _items;

    public Bag(List<int> items) { _items = items; }

    public int Count => _items.Count;

    public int Sum()
    {
        var total = 0;
        foreach (var item in _items) total += item;
        return total;
    }
}

public static class CollectionInitializerHarness
{
    public static int Run()
    {
        var bag = new Bag(new List<int> { 5, 7, 9 });
        return bag.Count * 100 + bag.Sum();
    }
}
