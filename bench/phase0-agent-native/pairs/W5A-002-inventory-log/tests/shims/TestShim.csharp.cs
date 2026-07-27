// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace InventoryPair.Harness;

internal static class TestShim
{
    public static string FormatItem(string name, int count) => global::Inventory.InventoryModule.FormatItem(name, count);
    public static int CountTotal(int shelf, int backroom) => global::Inventory.InventoryModule.CountTotal(shelf, backroom);
    public static string FormatStock(string name, int shelf, int backroom) => global::Inventory.InventoryModule.FormatStock(name, shelf, backroom);
    public static void SaveStock(string path, string name, int shelf, int backroom) => global::Inventory.InventoryModule.SaveStock(path, name, shelf, backroom);
    public static string FormatRestock(string name, int needed) => global::Inventory.InventoryModule.FormatRestock(name, needed);
    public static int RestockAmount(int shelf, int backroom, int target) => global::Inventory.InventoryModule.RestockAmount(shelf, backroom, target);
}
