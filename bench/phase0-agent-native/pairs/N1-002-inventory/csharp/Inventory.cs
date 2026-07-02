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
}
