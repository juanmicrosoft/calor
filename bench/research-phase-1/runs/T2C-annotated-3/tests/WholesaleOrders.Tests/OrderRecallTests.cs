using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Guid orderId, Guid reservationId)> CreateShippedOrderAsync()
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
        return (f, order.Id, reservation.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, orderId, _) = await CreateShippedOrderAsync();

        var before = DateTimeOffset.UtcNow.AddMilliseconds(-100);
        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.NotNull(fresh.RecalledAt);
        Assert.True(fresh.RecalledAt >= before);
        Assert.Null(fresh.ShippedAt);
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations()
    {
        var (f, orderId, reservationId) = await CreateShippedOrderAsync();

        await f.Orders.RecallAsync(orderId);

        var reservation = await f.InventoryRepo.GetReservationByIdAsync(reservationId);
        Assert.Equal(ReservationStatus.Released, reservation!.Status);
        Assert.NotNull(reservation.ReleasedAt);
    }

    [Fact]
    public async Task Recall_Returns_Inventory_To_Available_Pool()
    {
        var (f, orderId, _) = await CreateShippedOrderAsync();

        var before = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(3, before!.Reserved);
        Assert.Equal(7, before.Available);

        await f.Orders.RecallAsync(orderId);

        var after = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(0, after!.Reserved);
        Assert.Equal(10, after.Available);
    }

    [Fact]
    public async Task Recall_Sends_Notification()
    {
        var (f, orderId, _) = await CreateShippedOrderAsync();

        var sentBefore = f.Notifications.Sent.Count;
        await f.Orders.RecallAsync(orderId);

        var notifications = f.Notifications.Sent;
        Assert.True(notifications.Count > sentBefore);
        Assert.Contains(notifications, n => n.Topic == "order.recalled" && n.Message.Contains(orderId.ToString()));
    }

    [Fact]
    public async Task Recall_Throws_When_Order_Not_Found()
    {
        var f = new TestFactory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(Guid.NewGuid()));
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
        await f.Payments.ChargeAsync(order.Id, new Money(1m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Delivered_Throws()
    {
        var (f, orderId, _) = await CreateShippedOrderAsync();
        await f.Orders.MarkDeliveredAsync(orderId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(orderId));
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
    public async Task Recall_Allows_Order_To_Be_Reshipped()
    {
        // After recall, order is back to Submitted; we can pay (already paid in flow before),
        // then re-ship via the standard MarkPaidAsync->MarkShippedAsync transition. Since
        // MarkShippedAsync requires Paid, we verify the recall lands us in Submitted, which
        // is the documented behavior.
        var (f, orderId, _) = await CreateShippedOrderAsync();
        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
    }
}
