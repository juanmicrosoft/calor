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
