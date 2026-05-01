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

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 3, 2.00m));
        await f.Orders.SubmitAsync(order.Id);
        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 3);
        await f.Inventory.ConfirmAsync(r.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(6.00m, order.Currency), "tok", "k1", "s1");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        return (f, order.Id, r.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, orderId, _) = await CreateShippedOrderAsync();
        var before = DateTimeOffset.UtcNow;

        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.NotNull(fresh.RecalledAt);
        Assert.True(fresh.RecalledAt >= before.AddMilliseconds(-100));
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

        var inv = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(10, inv!.Available);
        Assert.Equal(0, inv.Reserved);
    }

    [Fact]
    public async Task Recall_Sends_Customer_Notification()
    {
        var (f, orderId, _) = await CreateShippedOrderAsync();
        var sentBefore = f.Notifications.Sent.Count;

        await f.Orders.RecallAsync(orderId);

        var sentAfter = f.Notifications.Sent;
        Assert.Equal(sentBefore + 1, sentAfter.Count);
        var notification = sentAfter[sentAfter.Count - 1];
        Assert.Equal("order.recalled", notification.Topic);
        Assert.Contains(orderId.ToString(), notification.Message);
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
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));
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
    public async Task Recall_Skips_Already_Released_Reservations()
    {
        var (f, orderId, reservationId) = await CreateShippedOrderAsync();

        // The reservation is currently Confirmed. Recall it directly first to mark it Released.
        await f.Inventory.ReleaseAsync(reservationId);
        var preState = await f.InventoryRepo.GetReservationByIdAsync(reservationId);
        Assert.Equal(ReservationStatus.Released, preState!.Status);
        var preReleasedAt = preState.ReleasedAt;

        await f.Orders.RecallAsync(orderId);

        // The reservation should still be Released; ReleasedAt should not have changed.
        var postState = await f.InventoryRepo.GetReservationByIdAsync(reservationId);
        Assert.Equal(ReservationStatus.Released, postState!.Status);
        Assert.Equal(preReleasedAt, postState.ReleasedAt);
    }
}
