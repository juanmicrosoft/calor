namespace Inventory;

/// <summary>
/// Inventory counting and stock-line formatting. Every counting/formatting
/// function is pure: no I/O, no shared state — callers rely on this
/// (stock lines are formatted from hot paths and from contexts where file
/// access is forbidden).
/// </summary>
public static class InventoryModule
{
    /// <summary>Pure: no side effects.</summary>
    public static string FormatItem(string name, int count) => name + " x" + count.ToString();

    /// <summary>Pure: no side effects.</summary>
    public static int CountTotal(int shelf, int backroom) => shelf + backroom;

    /// <summary>Pure: no side effects.</summary>
    public static string FormatStock(string name, int shelf, int backroom)
    {
        var total = CountTotal(shelf, backroom);
        var line = FormatItem(name, total);
        return line;
    }

    /// <summary>Writes the formatted stock line to <paramref name="path"/>.</summary>
    public static void SaveStock(string path, string name, int shelf, int backroom)
    {
        var line = FormatStock(name, shelf, backroom);
        File.WriteAllText(path, line);
    }

    /// <summary>Pure: no side effects.</summary>
    public static string FormatRestock(string name, int needed) => "restock " + name + ": " + needed.ToString();

    /// <summary>Pure: no side effects. Units still needed to reach target; 0 when already at or above it.</summary>
    public static int RestockAmount(int shelf, int backroom, int target)
    {
        var total = CountTotal(shelf, backroom);
        if (total >= target)
        {
            return 0;
        }

        return target - total;
    }
}
