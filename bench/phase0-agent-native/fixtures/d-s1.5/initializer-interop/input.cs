using System.Collections.Generic;

public class Bag
{
    public List<int> Items { get; } = new List<int> { 1 };
}

public static class InitializerInterop
{
    public static int Verify()
    {
        var bag = new Bag { Items = { 2, 3 } };
        return bag.Items.Count * 10 + bag.Items[2];
    }
}
