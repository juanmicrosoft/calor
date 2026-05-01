using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Guid orderId, Guid reservationId)> SetupShippedOrderAsync(int onHand = 10, int qty = 3)
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: onHand, unitPrice: 5m);

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", qty, 5m));
        await f.Orders.SubmitAsync(order.Id);

        var reservation = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), qty);
        await f.Inventory.ConfirmAsync(reservation.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(qty * 5m, order.Currency), "tok", $"k_{Guid.NewGuid()}", "test");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        return (f, order.Id, reservation.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, orderId, _) = await SetupShippedOrderAsync();
        var before = DateTimeOffset.UtcNow;

        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.NotNull(fresh.RecalledAt);
        Assert.True(fresh.RecalledAt >= before.AddMilliseconds(-100));
        Assert.Null(fresh.ShippedAt);
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations()
    {
        var (f, orderId, reservationId) = await SetupShippedOrderAsync(onHand: 10, qty: 3);

        await f.Orders.RecallAsync(orderId);

        var reservation = await f.InventoryRepo.GetReservationByIdAsync(reservationId);
        Assert.NotNull(reservation);
        Assert.Equal(ReservationStatus.Released, reservation!.Status);
        Assert.NotNull(reservation.ReleasedAt);
    }

    [Fact]
    public async Task Recall_Returns_Inventory_To_Available_Pool_For_Confirmed_Reservation()
    {
        var (f, orderId, _) = await SetupShippedOrderAsync(onHand: 10, qty: 3);

        // Before recall: Reserved == 3, OnHand == 10, Available == 7
        var beforeInv = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(10, beforeInv!.OnHand);
        Assert.Equal(3, beforeInv.Reserved);
        Assert.Equal(7, beforeInv.Available);

        await f.Orders.RecallAsync(orderId);

        // After recall: Reservation released, Reserved decremented; Available restored.
        var afterInv = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(10, afterInv!.OnHand);
        Assert.Equal(0, afterInv.Reserved);
        Assert.Equal(10, afterInv.Available);
    }

    [Fact]
    public async Task Recall_Notifies_Customer()
    {
        var (f, orderId, _) = await SetupShippedOrderAsync();

        await f.Orders.RecallAsync(orderId);

        var notifications = f.Notifications.Sent;
        Assert.Contains(notifications, n => n.Topic == "order.recalled");
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
        await f.Payments.ChargeAsync(order.Id, new Money(1m, order.Currency), "tok", "k_paid", "test");
        await f.Orders.MarkPaidAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Delivered_Throws()
    {
        var (f, orderId, _) = await SetupShippedOrderAsync();
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
    public async Task Recalled_Order_Not_Found_Throws()
    {
        var f = new TestFactory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(Guid.NewGuid()));
    }
}
