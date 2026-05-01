using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateDraft_Sets_Status_Draft()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.Orders.CreateDraftAsync(c.Id);
        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(c.Id, order.CustomerId);
        Assert.Equal("USD", order.Currency);
    }

    [Fact]
    public async Task AddLineItem_Updates_Total()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 4.50m));
        Assert.Equal(9.00m, order.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_Sets_SubmittedAt()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));
        var before = DateTimeOffset.UtcNow;
        await f.Orders.SubmitAsync(order.Id);
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh!.SubmittedAt);
        Assert.True(fresh.SubmittedAt >= before.AddMilliseconds(-100));
    }

    [Fact]
    public async Task EstimateTotal_Approximates_RecalculateTotal()
    {
        // EstimateTotal uses double precision; RecalculateTotal uses decimal.
        // Both should land within a cent for non-pathological inputs.
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 7, 0.1m));
        var estimate = ((OrderService)f.Orders).EstimateTotal(order);
        var recalc = ((OrderService)f.Orders).RecalculateTotal(order);
        // Both should be ~0.7. They may differ at the float-binary edge.
        Assert.InRange(estimate, 0.69m, 0.71m);
        Assert.Equal(0.7m, recalc.Amount);
    }

    [Fact]
    public async Task Submit_Without_PromoCode_Keeps_Full_Total()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 10m));
        await f.Orders.SubmitAsync(order.Id);
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(40.00m, fresh!.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Submit_With_WELCOME10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 10m));
        await f.Orders.SubmitAsync(order.Id, "WELCOME10");
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(36.00m, fresh!.TotalAmount.Amount);
        Assert.Equal("WELCOME10", fresh.PromoCode);
        Assert.Equal(0.10m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Submit_With_BULK25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 10m));
        await f.Orders.SubmitAsync(order.Id, "BULK25");
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(30.00m, fresh!.TotalAmount.Amount);
        Assert.Equal("BULK25", fresh.PromoCode);
        Assert.Equal(0.25m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Submit_With_Unknown_PromoCode_Throws_ArgumentException()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10m));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            f.Orders.SubmitAsync(order.Id, "NOTREAL"));
    }

    [Fact]
    public async Task PromoCode_Lookup_Is_Case_Insensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 10m));
        await f.Orders.SubmitAsync(order.Id, "welcome10");
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(36.00m, fresh!.TotalAmount.Amount);
        Assert.Equal(0.10m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Cancel_From_Draft_Sets_CancelledAt()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 1m));
        await f.Orders.CancelAsync(order.Id);
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, fresh!.Status);
        Assert.NotNull(fresh.CancelledAt);
    }
}
