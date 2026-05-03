using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class StateTransitionTests
{
    [Fact]
    public async Task Order_HappyPath_Draft_Submitted_Paid_Shipped_Delivered()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 5.00m));
        Assert.Equal(OrderStatus.Draft, order.Status);

        await f.Orders.SubmitAsync(order.Id);
        var sub = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Submitted, sub!.Status);

        await f.Payments.ChargeAsync(order.Id, new Money(5.00m, order.Currency), "tok", "k1", "s1");
        await f.Orders.MarkPaidAsync(order.Id);

        await f.Orders.MarkShippedAsync(order.Id);
        await f.Orders.MarkDeliveredAsync(order.Id);

        var final = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Delivered, final!.Status);
    }

    [Fact]
    public async Task Order_Cancel_From_Draft_Succeeds()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.CancelAsync(order.Id);
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, fresh!.Status);
    }

    [Fact]
    public async Task Order_Cancel_From_Submitted_Succeeds()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);
        await f.Orders.CancelAsync(order.Id);
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, fresh!.Status);
    }

    [Fact]
    public async Task Order_Cannot_Skip_Submit()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.MarkPaidAsync(order.Id));
    }

    [Fact]
    public async Task Reservation_HappyPath_Created_Confirmed_Fulfilled()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 2);
        Assert.Equal(ReservationStatus.Created, r.Status);

        await f.Inventory.ConfirmAsync(r.Id);
        var fresh = await f.InventoryRepo.GetReservationByIdAsync(r.Id);
        Assert.Equal(ReservationStatus.Confirmed, fresh!.Status);

        await f.Inventory.FulfillAsync(r.Id);
        fresh = await f.InventoryRepo.GetReservationByIdAsync(r.Id);
        Assert.Equal(ReservationStatus.Fulfilled, fresh!.Status);
    }

    [Fact]
    public async Task Reservation_Released_From_Created()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);

        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 2);
        await f.Inventory.ReleaseAsync(r.Id);
        var fresh = await f.InventoryRepo.GetReservationByIdAsync(r.Id);
        Assert.Equal(ReservationStatus.Released, fresh!.Status);
    }

    [Fact]
    public async Task Reservation_Cannot_Fulfill_Without_Confirm()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Inventory.FulfillAsync(r.Id));
    }

    [Fact]
    public async Task Shipment_Cannot_Skip_To_Delivered()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(1.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);
        var shipment = await f.Shipments.CreateShipmentAsync(order.Id, "UPS");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Shipments.MarkDeliveredAsync(shipment.Id));
    }

    [Fact]
    public async Task Order_Cannot_Cancel_From_Paid_Even_If_Validator_Allows()
    {
        // Validator and service disagree on Cancel-from-Paid; service throws.
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 5);
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(1.00m, order.Currency), "tok", "k", "s");
        await f.Orders.MarkPaidAsync(order.Id);

        // Validator's view
        Assert.True(f.OrderValidator.CanTransitionToCancelled(OrderStatus.Paid));
        // But service rejects (drift).
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Orders.CancelAsync(order.Id));
    }

    [Fact]
    public async Task Order_Cannot_Add_LineItems_After_Submit()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1.00m));
        await f.Orders.SubmitAsync(order.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.AddLineItemAsync(order.Id, Sku.Parse("WIDGET-B"), 1, 1m));
    }
}
