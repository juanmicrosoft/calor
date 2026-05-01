using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Guid orderId, Guid reservationId)> ShipOrderAsync()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 3, 5.00m));
        await f.Orders.SubmitAsync(order.Id);
        var reservation = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 3);
        await f.Inventory.ConfirmAsync(reservation.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(15.00m, order.Currency), "tok", "k_recall", "test");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        return (f, order.Id, reservation.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, orderId, _) = await ShipOrderAsync();

        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
    }

    [Fact]
    public async Task Recall_Sets_RecalledAt_Timestamp()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        var before = DateTimeOffset.UtcNow;

        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.NotNull(fresh!.RecalledAt);
        Assert.True(fresh.RecalledAt >= before.AddMilliseconds(-100));
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations()
    {
        var (f, orderId, reservationId) = await ShipOrderAsync();

        await f.Orders.RecallAsync(orderId);

        var reservations = await f.InventoryRepo.GetReservationsForOrderAsync(orderId);
        Assert.NotEmpty(reservations);
        Assert.All(reservations, r =>
            Assert.True(
                r.Status == ReservationStatus.Released || r.Status == ReservationStatus.Fulfilled,
                $"Reservation {r.Id} expected Released or Fulfilled, was {r.Status}"));

        var target = reservations.First(r => r.Id == reservationId);
        Assert.Equal(ReservationStatus.Released, target.Status);
    }

    [Fact]
    public async Task Recall_Returns_Inventory_To_Available_Pool()
    {
        var (f, orderId, _) = await ShipOrderAsync();

        var skuItemBefore = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.NotNull(skuItemBefore);
        Assert.Equal(3, skuItemBefore!.Reserved);

        await f.Orders.RecallAsync(orderId);

        var skuItemAfter = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.NotNull(skuItemAfter);
        Assert.Equal(0, skuItemAfter!.Reserved);
        Assert.Equal(10, skuItemAfter.Available);
    }

    [Fact]
    public async Task Recall_Notifies_Customer()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        var sentBefore = f.Notifications.Sent.Count;

        await f.Orders.RecallAsync(orderId);

        Assert.True(f.Notifications.Sent.Count > sentBefore);
        Assert.Contains(f.Notifications.Sent, n => n.Topic == "order.recalled");
    }

    [Fact]
    public async Task Recall_From_Delivered_Throws()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        await f.Orders.MarkDeliveredAsync(orderId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(orderId));
    }

    [Fact]
    public async Task Recall_From_Draft_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Submitted_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));
        await f.Orders.SubmitAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Paid_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(1.00m, order.Currency), "tok", "k_paid", "test");
        await f.Orders.MarkPaidAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Cancelled_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));
        await f.Orders.CancelAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recalled_Order_Cannot_Be_Recalled_Again()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        await f.Orders.RecallAsync(orderId);

        // After recall the order is back to Submitted, so a second recall must fail.
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(orderId));
    }
}
