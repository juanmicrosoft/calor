using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Guid orderId)> ShipOrderAsync(int onHand = 5, int qty = 2, decimal price = 5m)
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: onHand);

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", qty, price));
        await f.Orders.SubmitAsync(order.Id);

        var reservation = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), qty);
        await f.Inventory.ConfirmAsync(reservation.Id);

        await f.Payments.ChargeAsync(order.Id, new Money(qty * price, order.Currency), "tok", $"k_{order.Id}", "test");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        return (f, order.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, orderId) = await ShipOrderAsync();
        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
    }

    [Fact]
    public async Task Recall_Sets_RecalledAt_Timestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var (f, orderId) = await ShipOrderAsync();
        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.NotNull(fresh!.RecalledAt);
        Assert.True(fresh.RecalledAt >= before.AddMilliseconds(-100));
    }

    [Fact]
    public async Task Recall_Clears_ShippedAt()
    {
        var (f, orderId) = await ShipOrderAsync();
        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.Null(fresh!.ShippedAt);
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations()
    {
        var (f, orderId) = await ShipOrderAsync();
        await f.Orders.RecallAsync(orderId);

        var reservations = await f.InventoryRepo.GetReservationsForOrderAsync(orderId);
        Assert.NotEmpty(reservations);
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Released, r.Status));
    }

    [Fact]
    public async Task Recall_Returns_Inventory_To_Available_Pool()
    {
        var (f, orderId) = await ShipOrderAsync(onHand: 10, qty: 3);
        var skuValue = "WIDGET-A";
        var beforeRecall = await f.InventoryRepo.GetBySkuAsync(Sku.Parse(skuValue));
        Assert.NotNull(beforeRecall);
        Assert.Equal(3, beforeRecall!.Reserved);
        Assert.Equal(7, beforeRecall.Available);

        await f.Orders.RecallAsync(orderId);

        var afterRecall = await f.InventoryRepo.GetBySkuAsync(Sku.Parse(skuValue));
        Assert.NotNull(afterRecall);
        Assert.Equal(0, afterRecall!.Reserved);
        Assert.Equal(10, afterRecall.Available);
    }

    [Fact]
    public async Task Recall_Notifies_Customer()
    {
        var (f, orderId) = await ShipOrderAsync();
        var sentBefore = f.Notifications.Sent.Count;

        await f.Orders.RecallAsync(orderId);

        var sentAfter = f.Notifications.Sent;
        Assert.True(sentAfter.Count > sentBefore);
        Assert.Contains(sentAfter, n => n.Topic == "order.recalled" && n.Message.Contains(orderId.ToString()));
    }

    [Fact]
    public async Task Recall_From_Delivered_Throws()
    {
        var (f, orderId) = await ShipOrderAsync();
        await f.Orders.MarkDeliveredAsync(orderId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(orderId));
    }

    [Fact]
    public async Task Recall_From_Submitted_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
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
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(1m, order.Currency), "tok", "k_paid", "test");
        await f.Orders.MarkPaidAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
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
    public async Task Recall_From_Cancelled_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));
        await f.Orders.CancelAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }
}
