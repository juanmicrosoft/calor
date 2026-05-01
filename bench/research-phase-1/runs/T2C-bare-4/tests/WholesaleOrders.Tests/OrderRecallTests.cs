using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Domain.Entities.Order order, Domain.Entities.StockReservation reservation)>
        SetupShippedOrderAsync()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 5.00m));
        var reservation = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 2);
        await f.Inventory.ConfirmAsync(reservation.Id);
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(10.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);
        return (f, order, reservation);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, order, _) = await SetupShippedOrderAsync();

        await f.Orders.RecallAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
    }

    [Fact]
    public async Task Recall_Sets_RecalledAt_Timestamp()
    {
        var (f, order, _) = await SetupShippedOrderAsync();
        var before = DateTimeOffset.UtcNow;

        await f.Orders.RecallAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh!.RecalledAt);
        Assert.True(fresh.RecalledAt >= before.AddMilliseconds(-100));
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations_For_Order()
    {
        var (f, order, reservation) = await SetupShippedOrderAsync();

        await f.Orders.RecallAsync(order.Id);

        var reservations = await f.InventoryRepo.GetReservationsForOrderAsync(order.Id);
        Assert.NotEmpty(reservations);
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Released, r.Status));

        var fresh = await f.InventoryRepo.GetReservationByIdAsync(reservation.Id);
        Assert.Equal(ReservationStatus.Released, fresh!.Status);
        Assert.NotNull(fresh.ReleasedAt);
    }

    [Fact]
    public async Task Recall_Returns_Reserved_Inventory_To_Available_Pool()
    {
        var (f, order, _) = await SetupShippedOrderAsync();

        // Before recall: reservation was Confirmed (not Fulfilled), so OnHand still 5, Reserved 2.
        var beforeItem = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.NotNull(beforeItem);
        Assert.Equal(5, beforeItem!.OnHand);
        Assert.Equal(2, beforeItem.Reserved);

        await f.Orders.RecallAsync(order.Id);

        var afterItem = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.NotNull(afterItem);
        Assert.Equal(0, afterItem!.Reserved);
        // Available = OnHand - Reserved = 5 - 0 = 5.
        Assert.Equal(5, afterItem.OnHand - afterItem.Reserved);
    }

    [Fact]
    public async Task Recall_Notifies_Customer_Via_NotificationService()
    {
        var (f, order, _) = await SetupShippedOrderAsync();
        var beforeCount = f.Notifications.Sent.Count;

        await f.Orders.RecallAsync(order.Id);

        var sent = f.Notifications.Sent;
        Assert.True(sent.Count > beforeCount, "Expected a notification to have been sent.");
        Assert.Contains(sent, n => n.Topic == "order.recalled");
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
        await f.Payments.ChargeAsync(order.Id, new Money(1.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(order.Id));
    }

    [Fact]
    public async Task Recall_From_Delivered_Throws()
    {
        var (f, order, _) = await SetupShippedOrderAsync();
        await f.Orders.MarkDeliveredAsync(order.Id);
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
    public async Task Recall_NonExistent_Order_Throws()
    {
        var f = new TestFactory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(Guid.NewGuid()));
    }
}
