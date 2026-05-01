using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task Full_Pipeline_Draft_To_Delivered()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("PALLET-100", "Pallet 100", onHand: 100, unitPrice: 12.50m);

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("PALLET-100", 4, 12.50m));
        Assert.Equal(50.00m, order.TotalAmount.Amount);

        await f.Orders.SubmitAsync(order.Id);
        var reservation = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("PALLET-100"), 4);
        await f.Inventory.ConfirmAsync(reservation.Id);
        await f.Payments.ChargeAsync(order.Id, new Money(50m, order.Currency), "cust_int", "idem_int", "test");
        await f.Orders.MarkPaidAsync(order.Id);

        var shipment = await f.Shipments.CreateShipmentAsync(order.Id, "FedEx");
        await f.Shipments.MarkInTransitAsync(shipment.Id, "TRACK-INT");
        await f.Orders.MarkShippedAsync(order.Id);
        await f.Inventory.FulfillAsync(reservation.Id);
        await f.Shipments.MarkDeliveredAsync(shipment.Id);
        await f.Orders.MarkDeliveredAsync(order.Id);

        var final = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Delivered, final!.Status);

        var inv = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("PALLET-100"));
        Assert.Equal(96, inv!.OnHand);
        Assert.Equal(0, inv.Reserved);
    }

    [Fact]
    public async Task Cancelled_Order_Releases_Inventory()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        await f.SeedInventoryAsync("WIDGET-A", "Widget A", onHand: 10);

        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 5, 1m));
        await f.Orders.SubmitAsync(order.Id);
        var r = await f.Inventory.ReserveAsync(order.Id, Sku.Parse("WIDGET-A"), 5);

        await f.Inventory.ReleaseAsync(r.Id);
        await f.Orders.CancelAsync(order.Id);

        var inv = await f.InventoryRepo.GetBySkuAsync(Sku.Parse("WIDGET-A"));
        Assert.Equal(10, inv!.Available);
    }

    [Fact]
    public async Task Multiple_Orders_Same_Customer_Independent()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var o1 = await f.CreateOrderWithItemsAsync(c.Id, ("X", 1, 1m));
        var o2 = await f.CreateOrderWithItemsAsync(c.Id, ("Y", 2, 2m));

        await f.Orders.SubmitAsync(o1.Id);
        // o2 still draft
        var fresh1 = await f.OrderRepo.GetByIdAsync(o1.Id);
        var fresh2 = await f.OrderRepo.GetByIdAsync(o2.Id);
        Assert.Equal(OrderStatus.Submitted, fresh1!.Status);
        Assert.Equal(OrderStatus.Draft, fresh2!.Status);

        var byCustomer = await f.OrderRepo.GetByCustomerAsync(c.Id);
        Assert.Equal(2, byCustomer.Count);
    }
}
