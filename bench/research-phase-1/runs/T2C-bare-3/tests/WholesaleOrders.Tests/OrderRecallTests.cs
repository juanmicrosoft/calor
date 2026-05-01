using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class OrderRecallTests
{
    private static async Task<(TestFactory f, Guid orderId, Guid reservationId)> ShipOrderAsync()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 5.00m));
        await f.Orders.SubmitAsync(order.Id);
        var reservation = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 2);
        await f.Inventory.ConfirmAsync(reservation.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(10.00m, order.Currency), "tok", "k1", "s1");
        await f.Orders.MarkPaidAsync(order.Id);
        await f.Orders.MarkShippedAsync(order.Id);
        return (f, order.Id, reservation.Id);
    }

    [Fact]
    public async Task Recall_From_Shipped_Reverts_Status_To_Submitted()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        var before = DateTimeOffset.UtcNow;

        await f.Orders.RecallAsync(orderId);

        var fresh = await f.OrderRepo.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.NotNull(fresh.RecalledAt);
        Assert.True(fresh.RecalledAt >= before.AddMilliseconds(-100));
    }

    [Fact]
    public async Task Recall_Releases_All_Reservations()
    {
        var (f, orderId, reservationId) = await ShipOrderAsync();

        await f.Orders.RecallAsync(orderId);

        var reservation = await f.InventoryRepo.GetReservationByIdAsync(reservationId);
        Assert.Equal(ReservationStatus.Released, reservation!.Status);
        Assert.NotNull(reservation.ReleasedAt);
    }

    [Fact]
    public async Task Recall_Returns_Inventory_To_Available_Pool()
    {
        var (f, orderId, _) = await ShipOrderAsync();
        var skuValue = Sku.Parse("WIDGET-A");
        var itemBefore = await f.InventoryRepo.GetBySkuAsync(skuValue);
        Assert.Equal(2, itemBefore!.Reserved);

        await f.Orders.RecallAsync(orderId);

        var itemAfter = await f.InventoryRepo.GetBySkuAsync(skuValue);
        Assert.Equal(0, itemAfter!.Reserved);
    }

    [Fact]
    public async Task Recall_Sends_Customer_Notification()
    {
        var (f, orderId, _) = await ShipOrderAsync();

        await f.Orders.RecallAsync(orderId);

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
    public async Task Recall_From_Paid_Throws()
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
    public async Task Recall_From_Submitted_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
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
}
