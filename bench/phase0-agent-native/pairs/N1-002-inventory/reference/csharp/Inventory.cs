namespace InventoryLib;

public static class Inventory
{
    public static void AddItem(Dictionary<string, int> items, string name, int qty)
    {
        if (items.ContainsKey(name))
        {
            items[name] += qty;
        }
        else
        {
            items[name] = qty;
        }
    }

    public static void RemoveItem(Dictionary<string, int> items, string name, int qty)
    {
        if (!items.ContainsKey(name))
        {
            return;
        }
        var remaining = items[name] - qty;
        if (remaining <= 0)
        {
            items.Remove(name);
        }
        else
        {
            items[name] = remaining;
        }
    }

    public static int TotalCount(Dictionary<string, int> items)
    {
        var total = 0;
        foreach (var qty in items.Values)
        {
            total += qty;
        }
        return total;
    }
}
