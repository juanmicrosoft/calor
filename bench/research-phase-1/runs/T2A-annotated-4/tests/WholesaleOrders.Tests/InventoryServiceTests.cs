using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class InventoryServiceTests
{
    [Fact]
    public async Task AddItem_Persists()
    {
        var f = new TestFactory();
        var item = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 50, unitPrice: 9.99m);
        Assert.Equal(50, item.OnHand);
        Assert.Equal(0, item.Reserved);
        Assert.Equal(50, item.Available);
    }

    [Fact]
    public async Task Reserve_Increments_Reserved()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        var item = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 3);
        var fresh = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(3, fresh!.Reserved);
    }

    [Fact]
    public async Task Reserve_Beyond_Available_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 10));
    }

    [Fact]
    public async Task Release_Returns_Quantity_To_Available()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        var item = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 4);
        var afterReserve = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(6, afterReserve!.Available);

        await f.Inventory.ReleaseAsync(r.Id);
        var afterRelease = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(10, afterRelease!.Available);
    }

    [Fact]
    public async Task Fulfill_Reduces_OnHand()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        var item = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 3);
        await f.Inventory.ConfirmAsync(r.Id);
        await f.Inventory.FulfillAsync(r.Id);

        var fresh = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(7, fresh!.OnHand);
        Assert.Equal(0, fresh.Reserved);
        Assert.Equal(7, fresh.Available);
    }
}
