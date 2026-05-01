using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Domain.Entities.Order order, Domain.Entities.StockReservation reservation)> SetupShippedOrderAsync()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 3, 5.00m));
        await f.Orders.SubmitAsync(order.Id);
        var reservation = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 3);
        await f.Inventory.ConfirmAsync(reservation.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(15.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        return (f, order, reservation);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, order, _) = await SetupShippedOrderAsync();
        var before = DateTimeOffset.UtcNow;

        await f.Orders.RecallAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Null(fresh.ShippedAt);
        Assert.NotNull(fresh.RecalledAt);
        Assert.True(fresh.RecalledAt >= before.AddMilliseconds(-100));
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations_And_Returns_Inventory()
    {
        var (f, order, reservation) = await SetupShippedOrderAsync();

        await f.Orders.RecallAsync(order.Id);

        var freshReservation = await f.InventoryRepo.GetReservationByIdAsync(reservation.Id);
        Assert.NotNull(freshReservation);
        Assert.Equal(ReservationStatus.Released, freshReservation!.Status);
        Assert.NotNull(freshReservation.ReleasedAt);

        var inv = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.NotNull(inv);
        Assert.Equal(10, inv!.OnHand);
        Assert.Equal(0, inv.Reserved);
        Assert.Equal(10, inv.Available);
    }

    [Fact]
    public async Task Recall_Notifies_Customer()
    {
        var (f, order, _) = await SetupShippedOrderAsync();

        await f.Orders.RecallAsync(order.Id);

        var sent = f.Notifications.Sent;
        Assert.Contains(sent, n => n.Topic == "order.recalled" && n.Message.Contains(order.Id.ToString()));
    }

    [Fact]
    public async Task Recall_From_Delivered_Throws()
    {
        var (f, order, _) = await SetupShippedOrderAsync();
        await f.Orders.MarkDeliveredAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Paid_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(1.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Submitted_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Draft_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_Missing_Order_Throws()
    {
        var f = new TestFactory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Recall_Then_Resubmit_Forward_Lifecycle_Works()
    {
        // After a recall, the order is back in Submitted; the standard forward flow
        // (charge + mark paid + ship) should still work.
        var (f, order, _) = await SetupShippedOrderAsync();
        await f.Orders.RecallAsync(order.Id);

        // Re-reserve since the prior reservations were released.
        var r2 = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 3);
        await f.Inventory.ConfirmAsync(r2.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(15.00m, order.Currency), "tok", "k2", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Shipped, fresh!.Status);
        Assert.NotNull(fresh.ShippedAt);
        // RecalledAt remains as a historical record.
        Assert.NotNull(fresh.RecalledAt);
    }
}
