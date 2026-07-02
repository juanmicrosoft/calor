// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace Inventory.HeldOut;

internal static class TestShim
{
    public static void AddItem(Dictionary<string, int> items, string name, int qty) => InventoryLib.Inventory.AddItem(items, name, qty);
    public static void RemoveItem(Dictionary<string, int> items, string name, int qty) => InventoryLib.Inventory.RemoveItem(items, name, qty);
    public static int TotalCount(Dictionary<string, int> items) => InventoryLib.Inventory.TotalCount(items);
}
