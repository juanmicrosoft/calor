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
        await f.Payments.ChargeAsync(order.Id, new Money(15.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        return (f, order.Id, reservation.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_To_Submitted()
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
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Released, r.Status));
        Assert.All(reservations, r => Assert.NotNull(r.ReleasedAt));

        var freshReservation = await f.InventoryRepo.GetReservationByIdAsync(reservationId);
        Assert.Equal(ReservationStatus.Released, freshReservation!.Status);
    }

    [Fact]
    public async Task Recall_Notifies_Customer()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        var beforeCount = f.Notifications.Sent.Count;

        await f.Orders.RecallAsync(orderId);

        Assert.True(f.Notifications.Sent.Count > beforeCount);
        Assert.Contains(f.Notifications.Sent, n => n.Topic == "order.recalled");
    }

    [Fact]
    public async Task Recall_Cannot_Recall_Draft()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_Cannot_Recall_Submitted()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_Cannot_Recall_Paid()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(1.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_Cannot_Recall_Delivered()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        await f.Orders.MarkDeliveredAsync(orderId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(orderId));
    }

    [Fact]
    public async Task Recall_Cannot_Recall_Cancelled()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.CancelAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_Of_Missing_Order_Throws()
    {
        var f = new TestFactory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Recall_Allows_Re_Progression_To_Shipped()
    {
        // After recall, order is back to Submitted. The customer can be paid again and shipped again.
        var (f, orderId, _) = await ShipOrderAsync();
        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);

        // Already paid; need to mark paid would fail because already not Submitted... wait, we just set it back.
        // ChargeAsync produces a separate payment row; MarkPaidAsync requires Status==Submitted.
        await f.Payments.ChargeAsync(orderId, new Money(15.00m, fresh.Currency), "tok2", "k2", "s2");
        await f.Orders.MarkPaidAsync(orderId);
        await f.Orders.MarkShippedAsync(orderId);

        fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Shipped, fresh!.Status);
        Assert.NotNull(fresh.RecalledAt);
    }
}
