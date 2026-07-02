// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): AddItem / RemoveItem / TotalCount.
using Xunit;

namespace Inventory.HeldOut;

public sealed class InventoryHeldOutTests
{
    [Fact]
    public void AddItem_NewName_CreatesEntry()
    {
        var items = new Dictionary<string, int>();
        TestShim.AddItem(items, "bolt", 5);
        Assert.Equal(5, items["bolt"]);
    }

    [Fact]
    public void AddItem_ExistingName_IncreasesQuantity()
    {
        var items = new Dictionary<string, int>();
        TestShim.AddItem(items, "bolt", 5);
        TestShim.AddItem(items, "bolt", 3);
        Assert.Equal(8, items["bolt"]);
    }

    [Fact]
    public void RemoveItem_PartialQuantity_Decrements()
    {
        var items = new Dictionary<string, int> { ["bolt"] = 5 };
        TestShim.RemoveItem(items, "bolt", 2);
        Assert.Equal(3, items["bolt"]);
    }

    [Fact]
    public void RemoveItem_ExactQuantity_RemovesEntry()
    {
        var items = new Dictionary<string, int> { ["bolt"] = 5 };
        TestShim.RemoveItem(items, "bolt", 5);
        Assert.False(items.ContainsKey("bolt"));
    }

    [Fact]
    public void RemoveItem_MoreThanHeld_RemovesEntry()
    {
        var items = new Dictionary<string, int> { ["bolt"] = 5 };
        TestShim.RemoveItem(items, "bolt", 9);
        Assert.False(items.ContainsKey("bolt"));
    }

    [Fact]
    public void RemoveItem_MissingName_DoesNothing()
    {
        var items = new Dictionary<string, int> { ["nut"] = 2 };
        TestShim.RemoveItem(items, "bolt", 1);
        Assert.Single(items);
        Assert.Equal(2, items["nut"]);
    }

    [Fact]
    public void RemoveItem_DoesNotDisturbOtherEntries()
    {
        var items = new Dictionary<string, int> { ["nut"] = 2, ["bolt"] = 4 };
        TestShim.RemoveItem(items, "bolt", 4);
        Assert.Equal(2, items["nut"]);
        Assert.False(items.ContainsKey("bolt"));
    }

    [Fact]
    public void TotalCount_EmptyInventory_ReturnsZero()
    {
        Assert.Equal(0, TestShim.TotalCount(new Dictionary<string, int>()));
    }

    [Fact]
    public void TotalCount_SumsAllQuantities()
    {
        var items = new Dictionary<string, int> { ["nut"] = 2, ["bolt"] = 4, ["washer"] = 10 };
        Assert.Equal(16, TestShim.TotalCount(items));
    }

    [Fact]
    public void AddThenRemove_EndToEnd()
    {
        var items = new Dictionary<string, int>();
        TestShim.AddItem(items, "bolt", 5);
        TestShim.AddItem(items, "nut", 7);
        TestShim.RemoveItem(items, "bolt", 5);
        Assert.Equal(7, TestShim.TotalCount(items));
    }
}
