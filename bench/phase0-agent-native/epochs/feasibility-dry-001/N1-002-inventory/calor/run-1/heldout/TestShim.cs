// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace Inventory.HeldOut;

internal static class TestShim
{
    public static void AddItem(Dictionary<string, int> items, string name, int qty) => global::Inventory.InventoryModule.AddItem(items, name, qty);
    public static void RemoveItem(Dictionary<string, int> items, string name, int qty) => global::Inventory.InventoryModule.RemoveItem(items, name, qty);
    public static int TotalCount(Dictionary<string, int> items) => global::Inventory.InventoryModule.TotalCount(items);
}
