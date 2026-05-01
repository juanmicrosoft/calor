using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Guid customerId, Guid orderId)> SetupShippedOrderAsync(int onHand = 5, int qty = 2, decimal price = 5m)
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: onHand);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", qty, price));
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(qty * price, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);
        return (f, c.Id, order.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Returns_Order_To_Submitted()
    {
        var (f, _, orderId) = await SetupShippedOrderAsync();

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
    public async Task Recall_Sends_Notification()
    {
        var (f, _, orderId) = await SetupShippedOrderAsync();
        var sentBefore = f.Notifications.Sent.Count;

        await f.Orders.RecallAsync(orderId);

        var sentAfter = f.Notifications.Sent;
        Assert.True(sentAfter.Count > sentBefore);
        Assert.Contains(sentAfter, n => n.Topic == "order.recalled");
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations_Returning_Inventory()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var inv = await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 3, 5m));
        await f.Orders.SubmitAsync(order.Id);

        // Reserve inventory and tie it to this order.
        var r1 = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 2);
        var r2 = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 1);

        var afterReserve = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(3, afterReserve!.Reserved);

        await f.Payments.ChargeAsync(order.Id, new Money(15m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        await f.Orders.RecallAsync(order.Id);

        // Both reservations should be Released.
        var fresh1 = await f.InventoryRepo.GetReservationByIdAsync(r1.Id);
        var fresh2 = await f.InventoryRepo.GetReservationByIdAsync(r2.Id);
        Assert.Equal(ReservationStatus.Released, fresh1!.Status);
        Assert.Equal(ReservationStatus.Released, fresh2!.Status);

        // Inventory pool: Reserved should be back to 0.
        var afterRecall = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(0, afterRecall!.Reserved);
    }

    [Fact]
    public async Task Recall_Releases_Fulfilled_Reservations_And_Restores_OnHand()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 3, 5m));
        await f.Orders.SubmitAsync(order.Id);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 4);
        await f.Inventory.ConfirmAsync(r.Id);

        await f.Payments.ChargeAsync(order.Id, new Money(15m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);

        // Simulate fulfillment having consumed inventory.
        await f.Inventory.FulfillAsync(r.Id);
        var afterFulfill = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(6, afterFulfill!.OnHand);

        await f.Orders.RecallAsync(order.Id);

        var freshR = await f.InventoryRepo.GetReservationByIdAsync(r.Id);
        Assert.Equal(ReservationStatus.Released, freshR!.Status);

        var afterRecall = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(10, afterRecall!.OnHand);
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
        var (f, _, orderId) = await SetupShippedOrderAsync();
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
    public async Task Recalled_Order_Cannot_Be_Recalled_Again()
    {
        var (f, _, orderId) = await SetupShippedOrderAsync();
        await f.Orders.RecallAsync(orderId);
        // Now in Submitted; recall must reject.
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.RecallAsync(orderId));
    }
}
