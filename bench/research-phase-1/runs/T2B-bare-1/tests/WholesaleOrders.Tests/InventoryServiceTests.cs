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
    public async Task PartialRelease_Returns_Portion_To_Available_And_Keeps_Reservation()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        var item = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 5);
        var afterReserve = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(5, afterReserve!.Reserved);
        Assert.Equal(5, afterReserve.Available);

        var partial = await f.Inventory.PartialReleaseAsync(r.Id, 2);

        Assert.Equal(3, partial.Quantity);
        Assert.Equal(ReservationStatus.Created, partial.Status);
        Assert.Null(partial.ReleasedAt);

        var afterPartial = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(3, afterPartial!.Reserved);
        Assert.Equal(7, afterPartial.Available);

        var fresh = await f.InventoryRepo.GetReservationByIdAsync(r.Id);
        Assert.NotNull(fresh);
        Assert.Equal(3, fresh!.Quantity);
        Assert.Equal(ReservationStatus.Created, fresh.Status);
    }

    [Fact]
    public async Task PartialRelease_FullRemaining_Transitions_To_Released()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        var item = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 4);
        var partial = await f.Inventory.PartialReleaseAsync(r.Id, 4);

        Assert.Equal(0, partial.Quantity);
        Assert.Equal(ReservationStatus.Released, partial.Status);
        Assert.NotNull(partial.ReleasedAt);

        var afterRelease = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(0, afterRelease!.Reserved);
        Assert.Equal(10, afterRelease.Available);
    }

    [Fact]
    public async Task PartialRelease_TwoSteps_Releases_When_Second_Empties_Reservation()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        var item = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 5);
        await f.Inventory.PartialReleaseAsync(r.Id, 2);
        var second = await f.Inventory.PartialReleaseAsync(r.Id, 3);

        Assert.Equal(0, second.Quantity);
        Assert.Equal(ReservationStatus.Released, second.Status);

        var fresh = await f.InventoryRepo.GetByIdAsync(item.Id);
        Assert.Equal(0, fresh!.Reserved);
        Assert.Equal(10, fresh.Available);
    }

    [Fact]
    public async Task PartialRelease_NonPositive_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 4);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Inventory.PartialReleaseAsync(r.Id, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Inventory.PartialReleaseAsync(r.Id, -1));
    }

    [Fact]
    public async Task PartialRelease_Beyond_Reserved_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 4);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Inventory.PartialReleaseAsync(r.Id, 5));
    }

    [Fact]
    public async Task PartialRelease_On_Released_Reservation_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 4);
        await f.Inventory.ReleaseAsync(r.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Inventory.PartialReleaseAsync(r.Id, 1));
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
